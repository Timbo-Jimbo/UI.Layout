using System;
using System.Collections.Generic;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// View transitions, after the web API. <see cref="StartViewTransition(Action, LayoutTransition?, string[])"/>
    /// captures where every node is, runs the update, lays the new state out at once, and only then animates.
    /// Named pairs, persisting pairs and carried persisting nodes are lifted out of their trees into a transition
    /// layer under their canvas (a placeholder keeps each one's layout slot), stacked in capture order the way the
    /// web stacks its groups in its top layer, and flown along straight lines to where layout puts them now;
    /// entering and exiting nodes fade or play their animator in place beneath the layer; every other node that
    /// changed rect moves there in place. There are no snapshots: the objects themselves move, so anything
    /// animating on them keeps animating through the transition. A transition can be scoped to a subtree, like the
    /// web's element-scoped transitions, so transitions in separate parts of the UI run side by side.
    /// </summary>
    public static partial class LayoutSystem
    {
        // Every enabled node, so a transition can capture all of them whatever tree they are in.
        private static readonly HashSet<LayoutNode> s_enabled = new(ReferenceComparer.Instance);
        // Marks made inside a lifted subtree, applied when the node comes back down.
        private static readonly List<LayoutNode> s_deferred = new();
        private static readonly Dictionary<Canvas, RectTransform> s_layers = new();
        private static readonly Stack<LayoutNode> s_placeholderPool = new();
        private static Transform s_poolRoot;
        private static readonly Dictionary<string, LayoutNode> s_newNames = new();
        private static readonly List<Transform> s_chainA = new();
        private static readonly List<Transform> s_chainB = new();
        private static readonly Comparison<ViewTransition.Group> s_byCapture = CompareGroups;

        // The transitions in flight, oldest first, and, while its update runs, the one whose exits wait for its
        // animation step.
        private static readonly List<ViewTransition> s_active = new();
        private static readonly List<ViewTransition> s_finishing = new();
        private static ViewTransition s_updating;
        private static int s_counter;

        // Moves a hand-over stopped where they stand, restarted from there once the new transition has captured them;
        // nodes called back from leaving, and nodes hidden, inside the running update; and the roots of the Settle
        // scopes open now.
        private static readonly List<LayoutNode> s_frozen = new();
        private static readonly List<LayoutNode> s_recalled = new();
        private static readonly List<LayoutNode> s_hiding = new();
        private static readonly List<Transform> s_settle = new();

        /// <summary>
        /// Runs <paramref name="update"/> and animates every change it made to the layout as one transition, the
        /// way <c>document.startViewTransition</c> does. Nodes that ended up somewhere else travel there from where
        /// they were shown, whatever parent or tree they moved to; nodes that appeared enter and nodes handed to
        /// <see cref="Exit"/> inside the update leave, through their <see cref="IViewTransitionAnimator"/> or with a
        /// fade; a node that appears with a <see cref="LayoutNode.ViewTransitionName"/> another node carried before
        /// the update takes that node's place, flying in from its spot while the old one, if it is leaving or hidden, flies out
        /// to it, the two cross-fading; a persisting pair swaps places so the kept object flies in with its state;
        /// and a persisting node moved to a new parent is carried there above everything. Nodes move with
        /// <paramref name="transition"/> unless they set their own; the default is
        /// <see cref="ViewTransition.DefaultTransition"/>, and <see cref="LayoutTransition.Instant"/> applies the
        /// update at once. <paramref name="types"/> say what kind of change this is ("forward", "back") for the
        /// animators to read (<see cref="ViewTransition.HasType"/>). A transition already running hands over to this
        /// one: its moves go on from where they are. A name found on two nodes in one state skips this one, as on
        /// the web. Outside play mode the update is simply applied. Started inside another transition's update, the
        /// update simply joins that one (its timing and types are the outer transition's), so helpers that wrap
        /// their own change can be called from a bigger one.
        /// </summary>
        public static ViewTransition StartViewTransition(Action update, LayoutTransition? transition = null, params string[] types)
            => Start(null, update, transition, types);

        /// <summary>
        /// <see cref="StartViewTransition(Action, LayoutTransition?, string[])"/> for the subtree under
        /// <paramref name="scope"/> only, like the web's <c>element.startViewTransition</c>: only nodes below it are
        /// captured and animated (the scope's own node never moves), names are matched within it, and a transition
        /// running in a subtree that does not overlap this one (neither contains the other) plays on untouched.
        /// Changes the update makes outside the scope apply at once; in the editor a warning names a node that
        /// moved outside it. A null scope is the whole UI.
        /// </summary>
        public static ViewTransition StartViewTransition(Component scope, Action update, LayoutTransition? transition = null, params string[] types)
            => Start(scope != null ? scope.transform : null, update, transition, types);

        private static ViewTransition Start(Transform scope, Action update, LayoutTransition? transition, string[] types)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            var vt = new ViewTransition(++s_counter, scope, transition ?? ViewTransition.DefaultTransition, types);

            // Started inside another transition's update, this one is part of that update: laid out and animated
            // with it, not on its own. Laying out here would move the rest before that transition sees them, so a
            // node the outer update exits after this call would leave from the place its sibling left.
            if (s_updating != null)
            {
                update();
                vt.Finish();
                return vt;
            }
            if (!Application.isPlaying || s_reloading)
            {
                update();
                FlushMarked();
                vt.Finish();
                return vt;
            }

            // Every transition whose subtree overlaps this one's hands over to it; the rest play on.
            for (int i = s_active.Count - 1; i >= 0; i--)
            {
                if (i < s_active.Count && Overlaps(s_active[i], vt))
                    HandOver(s_active[i], vt);
            }
            // Settle what is pending first, so the capture is exactly what is on screen.
            FlushMarked();
            Capture(vt);
            // The moves the hand-over stopped re-lay out with the update and restart from where they were captured.
            for (int i = 0; i < s_frozen.Count; i++)
            {
                var node = s_frozen[i];
                if (node == null) continue;
                node._animating = false;
                MarkDirty(node);
            }
            s_frozen.Clear();

            s_active.Add(vt);
            s_updating = vt;
            try
            {
                update();
                // The new state is laid out at once; the animation below starts from the captured rects.
                FlushMarked();
            }
            finally
            {
                s_updating = null;
            }

            if (!vt.HasDuplicateName && Classify(vt))
            {
                Animate(vt);
                s_recalled.Clear();
                if (vt.IsSettled)
                    FinishViewTransition(vt);
            }
            else
            {
                Debug.LogWarning("[UI.Layout] A view transition name was on two nodes at once; the transition is skipped.");
                s_recalled.Clear();
                ConcludeUnstartedExits();
                ConcealPendingHides();
                SkipViewTransition(vt);
            }
            return vt;
        }

        /// <summary>
        /// Adjusts how every node moves in every view transition: called with the node and the timing and motion
        /// it resolved (its own, else the transition's), it returns what the node moves with instead. Null (the
        /// default) leaves them alone. For a setting that applies to the whole UI whoever authored the moves, such
        /// as a "reduce motion" option that drops every motion or shortens every move.
        /// </summary>
        public static Func<LayoutNode, LayoutTransition, LayoutTransition> AdjustTransition { get; set; }

        /// <summary>The view transition started most recently that is still in flight, or null.</summary>
        public static ViewTransition CurrentViewTransition => s_active.Count > 0 ? s_active[s_active.Count - 1] : null;

        /// <summary>Skips every transition in flight; a test seam.</summary>
        internal static void SkipAllViewTransitions()
        {
            for (int i = s_active.Count - 1; i >= 0; i--)
            {
                if (i < s_active.Count)
                    SkipViewTransition(s_active[i]);
            }
        }

        /// <summary>
        /// Shows <paramref name="node"/>: activates its object and shows it if it was hidden (<see cref="Hide"/>),
        /// or, if it is on its way out, calls it back, the exit cancelled (its animator is told to
        /// <see cref="IViewTransitionAnimator.Skip"/> and its <see cref="Exit"/> callback will not run). Inside a
        /// view transition a hidden node appears like any other (the new half of a pair, or entering), and a
        /// called-back node enters again from wherever its exit had got to; outside one it is simply shown.
        /// <c>SetActive(true)</c> cannot do this for a leaving node, whose object is still active.
        /// </summary>
        public static void Show(LayoutNode node)
        {
            if (node == null) return;
            if (node._hidden)
            {
                node._hidden = false;
                LayoutEngine.Reveal(node);
                // Caught fading out: inside an update it enters again from where the fade has got to; outside one
                // it is simply shown.
                if (node._animating && node._fadeGroup != null)
                {
                    if (s_updating != null)
                        s_recalled.Add(node);
                    else
                        LayoutEngine.StopMove(node);
                }
            }
            if (!node._exiting)
            {
                if (!node.gameObject.activeSelf)
                    node.gameObject.SetActive(true);
                return;
            }

            node._exiting = false;
            node._exitStarted = false;
            node._exitToken++;
            node._exitThen = null;
            RemoveExiting(node);
            for (int i = 0; i < s_active.Count; i++)
            {
                var effects = s_active[i].Effects;
                for (int e = 0; e < effects.Count; e++)
                {
                    if (effects[e].IsExit && ReferenceEquals(effects[e].Node, node))
                        effects[e].Done = true;
                }
            }
            if (node.TryGetComponent(out IViewTransitionAnimator animator))
                animator.Skip();
            // Inside an update the node enters with the transition, its fade going on from where it is; outside
            // one there is nothing to enter with.
            if (s_updating != null)
                s_recalled.Add(node);
            else
                LayoutEngine.StopMove(node);
            MarkDirty(node);
        }

        /// <summary>
        /// Hides <paramref name="node"/> in its place, as CSS <c>visibility: hidden</c> does beside
        /// <see cref="Exit"/>'s <c>display: none</c>: the tree goes on laying it out, but it is not drawn and takes
        /// no clicks, and to view transitions it is gone, its names with it. Inside a view transition it fades out
        /// where it is, or, when a node that appears takes its name, flies out into that node and then waits in its
        /// slot unseen, like a card that opens into a dialog; <see cref="Show"/> in a later transition brings it
        /// back, so a node leaving with the name lands onto it. Outside one it goes at once. Hiding is the running
        /// UI's: outside play mode the node is only marked, and still drawn. A node on its way out is left to go.
        /// </summary>
        public static void Hide(LayoutNode node)
        {
            if (node == null || node._hidden || node._exiting) return;
            node._hidden = true;
            // Inside an update the transition fades or flies it out; otherwise it goes now, or when the fade it is
            // in the middle of ends.
            if (s_updating != null && InScope(s_updating, node))
                s_hiding.Add(node);
            else if (node._fadeGroup == null)
                LayoutEngine.Conceal(node);
        }

        /// <summary>
        /// Opens a scope in which nodes enabled below <paramref name="root"/> just appear on their first layout
        /// instead of entering: for content that was already there as far as the player is concerned, such as a
        /// list filled from saved state or a feed rebuilt in a page already on screen. Dispose it (a
        /// <c>using</c> block) once the content is in place.
        /// </summary>
        public static SettleScope Settle(Component root)
        {
            var t = root != null ? root.transform : null;
            if (t != null)
                s_settle.Add(t);
            return new SettleScope(t);
        }

        /// <summary>The scope <see cref="Settle"/> opens; disposing it closes it.</summary>
        public readonly struct SettleScope : IDisposable
        {
            private readonly Transform _root;

            internal SettleScope(Transform root) => _root = root;

            public void Dispose()
            {
                if (_root == null) return;
                for (int i = s_settle.Count - 1; i >= 0; i--)
                {
                    if (s_settle[i] == _root)
                    {
                        s_settle.RemoveAt(i);
                        return;
                    }
                }
            }
        }

        internal static void Register(LayoutNode node)
        {
            s_enabled.Add(node);
            StampSettled(node);
        }

        internal static void Unregister(LayoutNode node) => s_enabled.Remove(node);

        /// <summary>A node not yet shown that turns up under an open <see cref="Settle"/> scope will appear rather than enter.</summary>
        internal static void StampSettled(LayoutNode node)
        {
            if (s_settle.Count == 0 || node._shown) return;
            for (int i = 0; i < s_settle.Count; i++)
            {
                if (s_settle[i] != null && node.transform.IsChildOf(s_settle[i]))
                {
                    node._settled = true;
                    return;
                }
            }
        }

        /// <summary>
        /// A lifted node disabled or destroyed mid-flight leaves the flight: its placeholder is released and it is
        /// left where it is (moving an object from inside its own OnDisable is not safe), which is what its owner
        /// asked for.
        /// </summary>
        internal static void LiftedNodeDisabled(LayoutNode node)
        {
            if (!node._lifted) return;
            for (int i = 0; i < s_active.Count; i++)
            {
                var flight = s_active[i].FlightOf(node);
                if (flight != null) flight.Done = true;
            }
            node._lifted = false;
            ReleasePlaceholder(node._placeholder);
            node._placeholder = null;
        }

        // ── Scopes ────────────────────────────────────────────────────────────────

        // Whether a node belongs to a transition's subtree: a lifted node by the slot its placeholder keeps.
        private static bool InScope(ViewTransition vt, LayoutNode node)
        {
            if (vt.Scope == null) return true;
            var t = node._lifted && node._placeholder != null ? node._placeholder.transform : node.transform;
            return t.IsChildOf(vt.Scope);
        }

        private static bool InScope(ViewTransition vt, Transform t) => vt.Scope == null || (t != null && t.IsChildOf(vt.Scope));

        private static bool IsScope(ViewTransition vt, LayoutNode node) => vt.Scope != null && node.transform == vt.Scope;

        // Two transitions overlap when either is unscoped or one's subtree contains the other's.
        private static bool Overlaps(ViewTransition a, ViewTransition b) =>
            a.Scope == null || b.Scope == null || a.Scope.IsChildOf(b.Scope) || b.Scope.IsChildOf(a.Scope);

        // ── Hand-over ─────────────────────────────────────────────────────────────

        // A transition in flight meets a new one over the same nodes. Each part goes its own way: a flight in the new
        // transition's subtree lands (its lifted node comes back down; the update may move it), one outside flies on
        // under the new transition; a move in the subtree is stopped where it is drawn, to be captured there and
        // restarted by the new transition once its update has run; moves outside it, fades and animator effects play
        // on, the new transition waiting for them. The old transition then finishes.
        private static void HandOver(ViewTransition old, ViewTransition next)
        {
            for (int i = 0; i < old.Flights.Count; i++)
            {
                var flight = old.Flights[i];
                if (flight.Done || flight.Node == null || !flight.Node._lifted) continue;
                if (InScope(next, flight.Node) || InScope(next, flight.Marker))
                {
                    flight.Elapsed = flight.Transition.Total;
                    WriteFlight(old, flight, 1f, 1f);
                    flight.Done = true;
                    continue;
                }
                next.Flights.Add(flight);
                for (int s = old.Swaps.Count - 1; s >= 0; s--)
                {
                    if (!ReferenceEquals(old.Swaps[s].Kept, flight.Node)) continue;
                    next.Swaps.Add(old.Swaps[s]);
                    old.Swaps.RemoveAt(s);
                }
                old.Flights.RemoveAt(i--);
            }

            for (int i = 0; i < old.Participants.Count; i++)
            {
                var node = old.Participants[i];
                if (node == null || !node._animating) continue;
                if (node._fadeGroup == null && InScope(next, node))
                {
                    // Captured as it is laid out, not as a motion scales or fades it.
                    LayoutEngine.ResetLook(node);
                    WriteNow(node);
                    s_frozen.Add(node);
                }
                else
                    next.Participants.Add(node);
            }
            old.Participants.Clear();

            for (int i = 0; i < old.Effects.Count; i++)
            {
                if (!old.Effects[i].Done)
                    next.Effects.Add(old.Effects[i]);
            }
            old.Effects.Clear();

            FinishViewTransition(old);
        }

        // ── Capture and classification ──────────────────────────────────────────

        // Where every enabled node in the scope is shown right now, in world space so a node can be matched across
        // parents and trees, the parent it has, and which node carries each name. A node already on its way out, or
        // hidden, is not part of the scene the update changes.
        private static void Capture(ViewTransition vt)
        {
            foreach (var node in s_enabled)
            {
                if (node == null || node._isPlaceholder) continue;
                if (!InScope(vt, node))
                {
#if UNITY_EDITOR
                    if (node._shown && !node._lifted)
                        vt.Outside.Add((node, LayoutEngine.WorldRect(node.RectTransform)));
#endif
                    continue;
                }
                if (IsLeaving(node)) continue;
                node._captureId = vt.Id;
                node._capturedWorld = LayoutEngine.WorldRect(node.RectTransform);
                node._capturedParent = node.transform.parent;
#if UNITY_EDITOR
                if (node._shown)
                    vt.WasShown.Add(node);
#endif
                vt.CaptureName(node);
            }
        }

        // Sorts the update's changes into the groups table, the way the web pairs its captured elements: by name
        // across the two states, then the persisting nodes moved to a new parent, the exits and the topmost
        // entering nodes. A name present in only one state rides whatever its tree does. False when a name is on
        // two nodes of the new state.
        private static bool Classify(ViewTransition vt)
        {
            s_newNames.Clear();
            foreach (var node in s_enabled)
            {
                if (node == null || node._isPlaceholder || !InScope(vt, node) || IsLeaving(node)) continue;
                var name = node.ResolvedViewTransitionName();
                if (string.IsNullOrEmpty(name)) continue;
                if (!s_newNames.TryAdd(name, node))
                {
                    vt.HasDuplicateName = true;
                    return false;
                }
            }

            foreach (var pair in s_newNames)
            {
                var node = pair.Value;
                if (!vt.Old.TryGetValue(pair.Key, out var old) || old == null || ReferenceEquals(old, node)) continue;
                if (node.IsRoot || IsScope(vt, node)) continue;
                bool persist = old.isActiveAndEnabled && (old.ViewTransitionPersist || node.ViewTransitionPersist);
                if (persist)
                    vt.Groups.Add(new ViewTransition.Group { Kind = ViewTransition.GroupKind.Persist, Old = old, New = node, OldFlies = true });
                else
                    vt.Groups.Add(new ViewTransition.Group { Kind = ViewTransition.GroupKind.Pair, Old = old, New = node, OldFlies = old.isActiveAndEnabled && !old.IsRoot && IsLeaving(old) });
            }

            // Carries: a persisting node the update moved to a new parent flies there above everything, the one
            // object from its old slot to its new one.
            foreach (var node in s_enabled)
            {
                if (node == null || !node.ViewTransitionPersist || node._captureId != vt.Id || node._isPlaceholder) continue;
                if (node.transform.parent == node._capturedParent || !node._hasCommitted || node.IsRoot || IsScope(vt, node) || IsLeaving(node) || vt.IsGrouped(node)) continue;
                vt.Groups.Add(new ViewTransition.Group { Kind = ViewTransition.GroupKind.Carry, Old = node, New = node });
            }

            // Exits: every node in the scope the update handed to Exit starts its way out here (outside a
            // transition, or outside its scope, the pass that reflows its tree does). The old half of a pair is
            // already on its way to its counterpart.
            for (int i = 0; i < s_exiting.Count; i++)
            {
                var node = s_exiting[i];
                if (node == null || !node._exiting || node._exitStarted || !node.isActiveAndEnabled || !InScope(vt, node)) continue;
                node._exitStarted = true;
                if (IsFlyingOldHalf(vt, node)) continue;
                vt.Groups.Add(new ViewTransition.Group { Kind = ViewTransition.GroupKind.Exit, Old = node, OldFlies = true });
            }

            // Enters: the topmost node of every subtree that appeared, and is not the new half of a pair; nodes
            // settled into place do not.
            foreach (var node in s_enabled)
            {
                if (node == null || node._isPlaceholder || node._captureId == vt.Id || !node._hasCommitted || node._settled || IsLeaving(node)) continue;
                if (!InScope(vt, node) || IsScope(vt, node)) continue;
                if (node._parentNode != null && node._parentNode._captureId != vt.Id) continue;
                if (vt.IsGrouped(node)) continue;
                vt.Groups.Add(new ViewTransition.Group { Kind = ViewTransition.GroupKind.Enter, New = node });
            }

            vt.Groups.Sort(s_byCapture);
#if UNITY_EDITOR
            WarnAboutVanished(vt);
#endif
            return true;
        }

        // ── Animation ─────────────────────────────────────────────────────────────

        // Lifts every named group and carry into the layer and starts its flight, starts the enters and exits in
        // place, then the movers, and writes the first frame.
        private static void Animate(ViewTransition vt)
        {
            for (int i = 0; i < vt.Groups.Count; i++)
            {
                var g = vt.Groups[i];
                switch (g.Kind)
                {
                    case ViewTransition.GroupKind.Pair:
                    {
                        var t = vt.TransitionFor(g.New);
                        if (!t.IsAnimated) break;
                        // The new node's slot is the destination of both halves; the old half is lifted first so
                        // the new one draws above it.
                        if (g.OldFlies)
                            Lift(g.Old);
                        Lift(g.New);
                        var slot = g.New._placeholder.transform;
                        if (g.OldFlies)
                        {
                            vt.Flights.Add(new ViewTransition.Flight { Node = g.Old, Marker = slot, From = g.Old._capturedWorld, Transition = t, Size = SizeRuleFor(g.Old, ViewTransition.SizeRule.KeepFrom) });
                            LayoutEngine.StartFade(g.Old, false, passThrough: true);
                        }
                        vt.Flights.Add(new ViewTransition.Flight { Node = g.New, Marker = slot, From = g.Old._capturedWorld, Transition = t, Size = SizeRuleFor(g.New, ViewTransition.SizeRule.KeepDestination) });
                        LayoutEngine.StartFade(g.New, true);
                        break;
                    }
                    case ViewTransition.GroupKind.Persist:
                    {
                        var t = vt.TransitionFor(g.Old);
                        if (!t.IsAnimated)
                        {
                            Swap(g.Old, g.New);
                            break;
                        }
                        Lift(g.Old);
                        LayoutEngine.Hide(g.New);
                        vt.Flights.Add(new ViewTransition.Flight { Node = g.Old, Marker = g.New.transform, From = g.Old._capturedWorld, Transition = t, Size = SizeRuleFor(g.Old, ViewTransition.SizeRule.KeepDestination), LayoutEachTick = true });
                        vt.Swaps.Add((g.Old, g.New));
                        break;
                    }
                    case ViewTransition.GroupKind.Carry:
                    {
                        // The node is already in its new slot; lifted, its placeholder holds that slot and is where it lands.
                        var node = g.New;
                        var t = vt.TransitionFor(node);
                        if (!t.IsAnimated) break;
                        Lift(node);
                        vt.Flights.Add(new ViewTransition.Flight { Node = node, Marker = node._placeholder.transform, From = node._capturedWorld, Transition = t, Size = SizeRuleFor(node, ViewTransition.SizeRule.KeepDestination), LayoutEachTick = true });
                        break;
                    }
                    case ViewTransition.GroupKind.Exit:
                    {
                        var node = g.Old;
                        var t = vt.TransitionFor(node);
                        if (node.TryGetComponent(out IViewTransitionAnimator animator))
                            RunExitEffect(node, animator, vt);
                        else if (t.IsAnimated)
                        {
                            var rect = node.CurrentVisual();
                            LayoutEngine.StartMove(node, rect, rect, t);
                            vt.Participants.Add(node);
                            LayoutEngine.StartFade(node, false, passThrough: true);
                        }
                        else
                            Conclude(node, node._exitThen);
                        break;
                    }
                    case ViewTransition.GroupKind.Enter:
                    {
                        var node = g.New;
                        var t = vt.TransitionFor(node);
                        if (node.TryGetComponent(out IViewTransitionAnimator animator))
                            RunEnterEffect(node, animator, vt);
                        else if (t.IsAnimated)
                        {
                            var rect = node.CurrentVisual();
                            LayoutEngine.StartMove(node, rect, rect, t);
                            vt.Participants.Add(node);
                            LayoutEngine.StartFade(node, true);
                        }
                        else
                            LayoutEngine.StopMove(node);
                        break;
                    }
                }
            }

            // A node called back from leaving that did not enter again (it was not the top of what appeared) stops fading out.
            for (int i = 0; i < s_recalled.Count; i++)
            {
                var node = s_recalled[i];
                if (node != null && !vt.IsGrouped(node))
                    LayoutEngine.StopMove(node);
            }

            // Movers: every captured node, not lifted and not a group, that is shown somewhere else now. Where a
            // move starts comes from the capture alone: the node's captured rect expressed in its parent's
            // captured frame (or the parent's settled frame when the parent is new), never read back from a
            // transform, so no order of starting matters. A child that only rode its parent then starts where
            // it ends and has nothing of its own to do; one that moved as well moves relative to that same frame.
            // A root never moves: its rect is the tree's contract with whatever holds it (a ScrollRect, a UGUI
            // parent), which reads the transform at once, as the document's own size changes at once on the web.
            // Neither does the scope's own node, the frame the transition happens in.
            foreach (var node in s_enabled)
            {
                if (node == null || node._isPlaceholder || node._lifted || node.IsRoot || node._captureId != vt.Id || !node._hasCommitted || IsScope(vt, node) || vt.IsGrouped(node)) continue;
                var t = vt.TransitionFor(node);
                if (!t.IsAnimated) continue;
                if (LayoutEngine.Approximately(LayoutEngine.WorldRect(node.RectTransform), node._capturedWorld)) continue;
                var from = CapturedStart(vt, node);
                if (node._measurable != null && !node._measurable.SizeIsAnimatable)
                    from.size = node._committedRect.size;
                if (LayoutEngine.Approximately(from, node._committedRect)) continue;
                LayoutEngine.StartMove(node, from, node._committedRect, t);
                vt.Participants.Add(node);
                WriteNow(node);
            }

            // Hides: a node the update hid fades out where layout has it (moving there with the rest if its slot
            // moved) and rests there unseen when the fade ends, as the old half of a pair does when its flight
            // lands. One the transition does not animate goes at once.
            for (int i = 0; i < s_hiding.Count; i++)
            {
                var node = s_hiding[i];
                if (node == null || !node._hidden) continue;
                var t = vt.TransitionFor(node);
                if (!node._lifted && node._captureId == vt.Id && node.isActiveAndEnabled && t.IsAnimated)
                {
                    if (!node._animating)
                    {
                        var rect = node.CurrentVisual();
                        LayoutEngine.StartMove(node, rect, rect, t);
                        vt.Participants.Add(node);
                    }
                    LayoutEngine.StartFade(node, false, passThrough: true);
                }
                else if (node._fadeGroup == null)
                    LayoutEngine.Conceal(node);
            }
            s_hiding.Clear();

            // A motion shapes what moves as a whole. A node moving inside something that travels too goes straight
            // within it (its move is relative to its parent), riding the traveller's motion, and changes from its
            // old place in it to its new one with the traveller's morph.
            for (int i = 0; i < vt.Participants.Count; i++)
            {
                var node = vt.Participants[i];
                if (node == null || !node._animating) continue;
                var traveller = TravellingAncestorOf(node);
                if (traveller == null) continue;
                node._ridesOn = traveller;
                if (node._animTransition.Motion != null)
                    node._animTransition = node._animTransition.With(null);
            }

            TickFlights(vt, 0f);
#if UNITY_EDITOR
            WarnAboutOutside(vt);
#endif
        }

        // The nearest node above this one that is on its way somewhere: moving in place (not just fading where it
        // is) or flying. Null when there is none.
        private static LayoutNode TravellingAncestorOf(LayoutNode node)
        {
            for (var p = node._parentNode; p != null; p = p._parentNode)
            {
                if (p._lifted || (p._animating && !LayoutEngine.Approximately(p._animFrom, p._animTo)))
                    return p;
            }
            return null;
        }

        // The engine-space rect a captured (non-root) node starts its move from, under the parent it has now: its
        // captured world rect in that parent's captured frame, or the parent's settled frame when the parent was not captured.
        private static Rect CapturedStart(ViewTransition vt, LayoutNode node)
        {
            var rt = node.RectTransform;
            var parent = (RectTransform)rt.parent;
            var parentStart = parent.TryGetComponent<LayoutNode>(out var parentNode) && parentNode._captureId == vt.Id
                ? parentNode._capturedWorld
                : LayoutEngine.WorldRect(parent);
            return LayoutEngine.RelIn(parentStart, parent.lossyScale, node._capturedWorld, node.Offset);
        }

        private static ViewTransition.SizeRule SizeRuleFor(LayoutNode node, ViewTransition.SizeRule locked) =>
            node._measurable != null && !node._measurable.SizeIsAnimatable ? locked : ViewTransition.SizeRule.Animate;

        // Advances every flight, in layer order so a container is written before the parts whose markers ride
        // inside it, and drives the fade riding on each.
        private static void TickFlights(ViewTransition vt, float deltaTime)
        {
            for (int i = 0; i < vt.Flights.Count; i++)
            {
                var flight = vt.Flights[i];
                if (flight.Done) continue;
                var node = flight.Node;
                if (node == null || !node._lifted)
                {
                    flight.Done = true;
                    continue;
                }
                flight.Elapsed += deltaTime;
                bool landing = flight.Elapsed >= flight.Transition.Total;
                float progress = landing ? 1f : Progress(flight);
                float eased = EaseUtilityEvaluate(progress, flight.Transition.Ease);
                WriteFlight(vt, flight, progress, eased);
                if (flight.LayoutEachTick)
                    LayoutLifted(node);
                // A flying half of a pair fades by its frame's morph; the motion decides how the two halves show.
                LayoutEngine.WriteAlpha(node, node._lookMorph);
                if (landing)
                    flight.Done = true;
            }
        }

        private static float Progress(ViewTransition.Flight flight)
        {
            float elapsed = flight.Elapsed - flight.Transition.Delay;
            return flight.Transition.Duration > 0f ? Mathf.Clamp01(elapsed / flight.Transition.Duration) : elapsed >= 0f ? 1f : 0f;
        }

        private static float EaseUtilityEvaluate(float t, TimboJimbo.Core.EaseType ease) => TimboJimbo.Core.EaseUtility.Evaluate(t, ease);

        // Puts a flight at its point between where it started and where its marker is now, in the layer. A flight
        // on its own follows its transition's motion (a straight line morphing on the eased progress without one):
        // its centre, its size, and its look (a scale around the centre, an opacity, and the morph the halves of a
        // pair blend by). A part flying inside a flying container rides the container instead: its offset from the
        // container's centre and its size go straight from what they were to what they will be by the container's
        // morph, scaled with the container, and it takes the container's look, so a panel carries its parts along
        // its path, and a panel that shrinks, fades or holds its old arrangement until it swaps takes them with it.
        private static void WriteFlight(ViewTransition vt, ViewTransition.Flight flight, float progress, float eased)
        {
            var to = Destination(vt, flight);
            var from = flight.From;
            switch (flight.Size)
            {
                case ViewTransition.SizeRule.KeepDestination: from.size = to.size; break;
                case ViewTransition.SizeRule.KeepFrom: to.size = from.size; break;
            }
            var node = flight.Node;
            MotionFrame frame;
            var ancestor = LiftedAncestorOf(vt, flight, out var ancestorFlight);
            if (ancestor != null)
            {
                var offsetFrom = Centre(from) - Centre(ancestorFlight.From);
                var offsetTo = Centre(to) - Centre(Destination(vt, ancestorFlight));
                float morph = ancestor._lookMorph;
                frame = new MotionFrame
                {
                    Centre = Centre(RidingFrame(ancestor, ancestorFlight)) + Vector2.LerpUnclamped(offsetFrom, offsetTo, morph) * ancestor._lookScale,
                    Size = Vector2.LerpUnclamped(from.size, to.size, morph),
                    Scale = ancestor._lookScale,
                    Opacity = ancestor._lookOpacity,
                    Morph = morph,
                };
            }
            else
                frame = flight.Transition.Evaluate(new MotionInput(Centre(from), Centre(to), from.size, to.size, progress, eased));
            var world = new Rect(frame.Centre.x - frame.Size.x * 0.5f, frame.Centre.y + frame.Size.y * 0.5f, frame.Size.x, frame.Size.y);
            flight.LastRect = world;
            LayoutEngine.SetLook(node, frame);
            s_committing = node._passRoot;
            try
            {
                LayoutEngine.Write(node, LayoutEngine.RelFromWorld(node, world));
            }
            finally
            {
                s_committing = null;
            }
        }

        // Lays a lifted node's subtree out against the rect its flight just wrote; the node itself is the flight's.
        private static void LayoutLifted(LayoutNode node)
        {
            s_committing = node;
            try
            {
                LayoutEngine.Compute(node, node.RectTransform.rect.size);
                LayoutEngine.Commit(node);
            }
            finally
            {
                s_committing = null;
            }
        }

        // Where a flight lands: its marker's rect on screen right now (so scrolling or a reflow during the
        // transition is honoured), corrected for any lifted ancestor of the marker so the line stays straight: a
        // lifted node's subtree is rigid, so the marker sits at a fixed offset from wherever that ancestor ends.
        private static Rect Destination(ViewTransition vt, ViewTransition.Flight flight)
        {
            var marker = flight.Marker;
            if (marker == null)
                return flight.LastDestination;
            var world = LayoutEngine.WorldRect((RectTransform)marker);
            var ancestor = LiftedAncestorOf(vt, flight, out var ancestorFlight);
            if (ancestor != null)
            {
                // The marker is drawn inside the container, so the container's look scales it; take that back out
                // (around the container's centre) to measure it against the container as laid out.
                var ancestorNow = RidingFrame(ancestor, ancestorFlight);
                float scale = ancestor._lookScale;
                if (!Mathf.Approximately(scale, 1f))
                {
                    if (scale < 1e-3f)
                        return flight.LastDestination;
                    var pivot = Centre(ancestorNow);
                    var centre = pivot + (Centre(world) - pivot) / scale;
                    var size = world.size / scale;
                    world = new Rect(centre.x - size.x * 0.5f, centre.y + size.y * 0.5f, size.x, size.y);
                }
                var ancestorEnd = Destination(vt, ancestorFlight);
                world.position += ancestorEnd.position - ancestorNow.position;
            }
            flight.LastDestination = world;
            return world;
        }

        // Where a container is this tick as laid out (before its look's scale): what the parts riding it measure from.
        private static Rect RidingFrame(LayoutNode ancestor, ViewTransition.Flight ancestorFlight) =>
            ancestorFlight.LastRect.width > 0f || ancestorFlight.LastRect.height > 0f
                ? ancestorFlight.LastRect
                : LayoutEngine.WorldRect(ancestor.RectTransform);

        // The nearest lifted node above a flight's marker that is flying in this transition: the container the
        // flight's node is a part of. Null for a flight on its own.
        private static LayoutNode LiftedAncestorOf(ViewTransition vt, ViewTransition.Flight flight, out ViewTransition.Flight ancestorFlight)
        {
            ancestorFlight = null;
            if (flight.Marker == null) return null;
            for (var t = flight.Marker.parent; t != null; t = t.parent)
            {
                if (!t.TryGetComponent<LayoutNode>(out var ancestor) || !ancestor._lifted) continue;
                ancestorFlight = vt.FlightOf(ancestor);
                return ancestorFlight != null ? ancestor : null;
            }
            return null;
        }

        // The centre of a world rect, whose y is its top edge with y up.
        private static Vector2 Centre(Rect world) => new(world.x + world.width * 0.5f, world.y - world.height * 0.5f);

        // ── Lifting ───────────────────────────────────────────────────────────────

        // Moves a node into its canvas's transition layer, leaving a placeholder of the same size in its slot so
        // the tree lays out exactly as before and the slot can be read as the flight's destination.
        private static void Lift(LayoutNode node)
        {
            if (node._lifted) return;
            var rt = node.RectTransform;
            var parent = rt.parent;
            var placeholder = TakePlaceholder(node);
            placeholder.transform.SetParent(parent, false);
            placeholder.transform.SetSiblingIndex(rt.GetSiblingIndex());
            var layer = LayerFor(node);
            node._placeholder = placeholder;
            node._lifted = true;
            node._tracker.Clear();
            rt.SetParent(layer, true);
            rt.SetAsLastSibling();
            layer.SetAsLastSibling();
        }

        // A kept object lands in its copy's slot, whatever became of the tree it left; the copy takes the slot
        // it left, and goes with that tree if the tree is gone.
        private static void ReturnKept(LayoutNode kept, LayoutNode copy)
        {
            if (!kept._lifted) return;
            kept._lifted = false;
            var placeholder = kept._placeholder;
            kept._placeholder = null;
            if (copy == null)
            {
                // No slot to land in: the new tree is gone, so is the kept object's place.
                if (placeholder != null)
                {
                    kept.transform.SetParent(placeholder.transform.parent, false);
                    kept.transform.SetSiblingIndex(placeholder.transform.GetSiblingIndex());
                }
                else
                    UnityEngine.Object.Destroy(kept.gameObject);
                ReleasePlaceholder(placeholder);
                return;
            }
            var slot = copy.transform;
            var slotParent = slot.parent;
            int slotIndex = slot.GetSiblingIndex();
            if (placeholder != null)
            {
                copy.transform.SetParent(placeholder.transform.parent, false);
                copy.transform.SetSiblingIndex(placeholder.transform.GetSiblingIndex());
            }
            else
                UnityEngine.Object.Destroy(copy.gameObject);
            kept.transform.SetParent(slotParent, false);
            kept.transform.SetSiblingIndex(slotIndex);
            LayoutEngine.EndFade(copy);
            ReleasePlaceholder(placeholder);
        }

        private static bool TryGetCopy(ViewTransition vt, LayoutNode kept, out LayoutNode copy)
        {
            for (int i = 0; i < vt.Swaps.Count; i++)
            {
                if (ReferenceEquals(vt.Swaps[i].Kept, kept))
                {
                    copy = vt.Swaps[i].Copy;
                    return true;
                }
            }
            copy = null;
            return false;
        }

        // Puts a lifted node back in its placeholder's slot; the tree is marked and laid out in the same flush.
        private static void Return(LayoutNode node)
        {
            if (!node._lifted) return;
            node._lifted = false;
            var placeholder = node._placeholder;
            node._placeholder = null;
            if (placeholder == null)
            {
                // The tree it belonged to is gone: so is its place.
                if (node != null) UnityEngine.Object.Destroy(node.gameObject);
                return;
            }
            var slot = placeholder.transform;
            node.transform.SetParent(slot.parent, false);
            node.transform.SetSiblingIndex(slot.GetSiblingIndex());
            ReleasePlaceholder(placeholder);
        }

        private static RectTransform LayerFor(LayoutNode node)
        {
            var canvas = node.GetComponentInParent<Canvas>();
            var root = canvas != null ? canvas.rootCanvas : null;
            if (root == null)
                throw new InvalidOperationException("[UI.Layout] A view transition needs the node to be under a Canvas.");
            if (s_layers.TryGetValue(root, out var layer) && layer != null)
                return layer;
            var go = new GameObject("View Transition Layer", typeof(RectTransform));
            layer = (RectTransform)go.transform;
            layer.SetParent(root.transform, false);
            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.offsetMin = Vector2.zero;
            layer.offsetMax = Vector2.zero;
            layer.SetAsLastSibling();
            s_layers[root] = layer;
            return layer;
        }

        // A placeholder stands in for the node in layout: the same slot, the same size, and out of the flow the
        // same way when the node is one layout skips because it is leaving.
        private static LayoutNode TakePlaceholder(LayoutNode node)
        {
            EnsurePool();
            LayoutNode placeholder = null;
            while (s_placeholderPool.Count > 0 && placeholder == null)
                placeholder = s_placeholderPool.Pop();
            if (placeholder == null)
            {
                var go = new GameObject("Placeholder", typeof(RectTransform), typeof(LayoutNode));
                placeholder = go.GetComponent<LayoutNode>();
                placeholder._isPlaceholder = true;
                placeholder.enabled = false;
                go.SetActive(false);
            }
            placeholder.gameObject.name = "Placeholder (" + node.name + ")";
            // The same slot: the node's size as laid out, and everything that places it.
            var size = node._committedRect.size;
            placeholder.Width = Sizing.Fixed(size.x);
            placeholder.Height = Sizing.Fixed(size.y);
            placeholder.AlignSelf = node.AlignSelf;
            placeholder.AttachTo = node.AttachTo;
            placeholder.AttachElement = node.AttachElement;
            placeholder.ElementPoint = node.ElementPoint;
            placeholder.ParentPoint = node.ParentPoint;
            placeholder.FloatOffset = node.FloatOffset;
            placeholder.Offset = node.Offset;
            placeholder._exiting = node._exiting;
            var from = node.RectTransform;
            var to = placeholder.RectTransform;
            to.anchorMin = from.anchorMin;
            to.anchorMax = from.anchorMax;
            to.pivot = from.pivot;
            to.anchoredPosition = from.anchoredPosition;
            to.sizeDelta = from.sizeDelta;
            placeholder.gameObject.SetActive(true);
            placeholder.enabled = true;
            return placeholder;
        }

        // Parks a placeholder in the pool. The pool is only ever created when a placeholder is taken, never here: a
        // release can come from a lifted node's OnDisable while its scene is closing, when the pool may already be
        // gone and nothing may be created. A placeholder with no pool to go to is destroyed instead.
        private static void ReleasePlaceholder(LayoutNode placeholder)
        {
            if (placeholder == null) return;
            placeholder.enabled = false;
            placeholder.gameObject.SetActive(false);
            if (s_poolRoot == null)
            {
                UnityEngine.Object.Destroy(placeholder.gameObject);
                return;
            }
            placeholder.transform.SetParent(s_poolRoot, false);
            s_placeholderPool.Push(placeholder);
        }

        private static void EnsurePool()
        {
            if (s_poolRoot != null) return;
            var go = new GameObject("View Transition Pool");
            go.SetActive(false);
            s_poolRoot = go.transform;
        }

        // Marks made inside a lifted subtree wait for the node to come back down.
        internal static void DeferMark(LayoutNode node) => s_deferred.Add(node);

        private static void ApplyDeferredMarks()
        {
            for (int i = 0; i < s_deferred.Count; i++)
            {
                var node = s_deferred[i];
                if (node != null && node.isActiveAndEnabled)
                    s_marked.Add(node);
            }
            s_deferred.Clear();
        }

        // Puts a node at the start of its move right away, so nothing shows at its settled rect before the first tick.
        private static void WriteNow(LayoutNode node)
        {
            s_committing = node._passRoot;
            try
            {
                LayoutEngine.Write(node, node.CurrentVisual());
            }
            finally
            {
                s_committing = null;
            }
        }

        // The kept object and its copy exchange parents and sibling indices.
        private static void Swap(LayoutNode kept, LayoutNode copy)
        {
            if (kept == null || copy == null) return;
            var keptParent = kept.transform.parent;
            int keptIndex = kept.transform.GetSiblingIndex();
            var copyParent = copy.transform.parent;
            int copyIndex = copy.transform.GetSiblingIndex();
            kept.transform.SetParent(copyParent, false);
            kept.transform.SetSiblingIndex(copyIndex);
            copy.transform.SetParent(keptParent, false);
            copy.transform.SetSiblingIndex(keptIndex);
        }

        // ── Effects ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Hands a node's first layout to its animator; a transition finishes only once the effect reports done.
        /// Outside a view transition (an arrival) the animator gets a null transition.
        /// </summary>
        internal static void RunEnterEffect(LayoutNode node, IViewTransitionAnimator animator, ViewTransition vt)
        {
            var effect = vt?.AddEffect(node, animator, false);
            animator.Enter(vt, () =>
            {
                if (effect != null)
                    CompleteEffect(effect);
            });
        }

        /// <summary>
        /// Hands an exiting node to its animator. The node leaves (its callback runs, or it is deactivated) when
        /// the effect reports done, provided it is still the same exit; outside a view transition the animator
        /// gets a null transition.
        /// </summary>
        internal static void RunExitEffect(LayoutNode node, IViewTransitionAnimator animator, ViewTransition vt)
        {
            int token = node._exitToken;
            var effect = vt?.AddEffect(node, animator, true);
            animator.Exit(vt, () =>
            {
                if (node != null && node._exiting && node._exitToken == token)
                    Conclude(node, node._exitThen);
                if (effect != null)
                    CompleteEffect(effect);
            });
        }

        // An effect may belong to a transition that has since handed it over, so every transition in flight is checked.
        private static void CompleteEffect(ViewTransition.Effect effect)
        {
            if (effect.Done) return;
            effect.Done = true;
            FinishSettledViewTransitions();
        }

        // ── Finishing ─────────────────────────────────────────────────────────────

        /// <summary>Ends the transition at once, effects included: what <see cref="ViewTransition.SkipTransition"/> does.</summary>
        internal static void SkipViewTransition(ViewTransition vt)
        {
            if (vt == null || vt.IsFinished) return;
            for (int i = 0; i < vt.Effects.Count; i++)
            {
                var effect = vt.Effects[i];
                if (effect.Done) continue;
                effect.Done = true;
                effect.Animator.Skip();
                if (effect.IsExit && effect.Node != null && effect.Node._exiting)
                    Conclude(effect.Node, effect.Node._exitThen);
            }
            CompleteViewTransition(vt);
        }

        // Lands every flight and completes every move at once, then finishes.
        private static void CompleteViewTransition(ViewTransition vt)
        {
            for (int i = 0; i < vt.Flights.Count; i++)
            {
                var flight = vt.Flights[i];
                if (flight.Done || flight.Node == null || !flight.Node._lifted) { flight.Done = true; continue; }
                flight.Elapsed = flight.Transition.Total;
                WriteFlight(vt, flight, 1f, 1f);
                flight.Done = true;
            }
            for (int i = 0; i < vt.Participants.Count; i++)
            {
                var node = vt.Participants[i];
                if (node == null || !node._animating) continue;
                for (int a = s_animating.Count - 1; a >= 0; a--)
                    if (ReferenceEquals(s_animating[a], node))
                        s_animating.RemoveAt(a);
                if (node.isActiveAndEnabled)
                    CompleteMove(node);
                else
                    node._animating = false;
            }
            FinishViewTransition(vt);
        }

        // Every transition in flight finishes when every flight it has has landed, no node it set moving is still
        // on its way and every effect it waits for has reported done. Finishing runs user code (Finished), so
        // the list is walked from a copy.
        private static void FinishSettledViewTransitions()
        {
            if (s_active.Count == 0) return;
            s_finishing.Clear();
            s_finishing.AddRange(s_active);
            for (int i = 0; i < s_finishing.Count; i++)
            {
                var vt = s_finishing[i];
                if (!vt.IsFinished && vt.IsSettled)
                    FinishViewTransition(vt);
            }
            s_finishing.Clear();
        }

        // Every lifted node comes back down: a pair half into its slot, a kept object into its copy's slot (the
        // copy taking the kept object's old slot, or dying with the tree that slot was in), a carried node into
        // its new slot; fades end, an old half that was itself exiting leaves, and the trees touched are laid out
        // in this same flush.
        private static void FinishViewTransition(ViewTransition vt)
        {
            for (int i = 0; i < vt.Flights.Count; i++)
            {
                var node = vt.Flights[i].Node;
                if (node == null) continue;
                if (TryGetCopy(vt, node, out var copy))
                    ReturnKept(node, copy);
                else
                    Return(node);
            }
            for (int i = 0; i < vt.Flights.Count; i++)
            {
                var node = vt.Flights[i].Node;
                if (node == null) continue;
                LayoutEngine.EndFade(node);
                LayoutEngine.ResetLook(node);
                if (node._exiting)
                    Conclude(node, node._exitThen);
            }
            ApplyDeferredMarks();
            s_active.Remove(vt);
            vt.Finish();
        }

        // Exits an update asked for that no animation step will start (the transition was skipped before it began).
        private static void ConcludeUnstartedExits()
        {
            for (int i = s_exiting.Count - 1; i >= 0; i--)
            {
                if (i >= s_exiting.Count) continue;
                var node = s_exiting[i];
                if (node != null && node._exiting && !node._exitStarted)
                    Conclude(node, node._exitThen);
            }
        }

        // Hides an update asked for that no animation step will fade: they go at once (one caught in a fade goes
        // when that ends).
        private static void ConcealPendingHides()
        {
            for (int i = 0; i < s_hiding.Count; i++)
            {
                var node = s_hiding[i];
                if (node != null && node._hidden && node._fadeGroup == null)
                    LayoutEngine.Conceal(node);
            }
            s_hiding.Clear();
        }

        // ── Diagnostics ───────────────────────────────────────────────────────────

#if UNITY_EDITOR
        // A node deactivated inside the update vanished at once: a disabled object cannot be drawn, so there is
        // nothing to animate out. Exit is the way to take it out with an animation. Reported for the topmost only.
        private static void WarnAboutVanished(ViewTransition vt)
        {
            LayoutNode vanished = null;
            for (int i = 0; i < vt.WasShown.Count; i++)
            {
                var node = vt.WasShown[i];
                if (node == null || node.gameObject.activeSelf) continue;
                if (vanished == null || Depth(node) < Depth(vanished))
                    vanished = node;
            }
            if (vanished != null)
                Debug.LogWarning($"[UI.Layout] '{vanished.name}' was deactivated inside a view transition, so it vanished at once. Use LayoutSystem.Exit to animate it out.", vanished);
        }

        // A node outside the transition's scope that the update moved jumped there: widen the scope to animate it.
        private static void WarnAboutOutside(ViewTransition vt)
        {
            LayoutNode moved = null;
            for (int i = 0; i < vt.Outside.Count; i++)
            {
                var (node, rect) = vt.Outside[i];
                if (node == null || !node.isActiveAndEnabled || node._lifted || node.IsRoot) continue;
                if (LayoutEngine.Approximately(LayoutEngine.WorldRect(node.RectTransform), rect)) continue;
                if (moved == null || Depth(node) < Depth(moved))
                    moved = node;
            }
            vt.Outside.Clear();
            if (moved != null)
                Debug.LogWarning($"[UI.Layout] '{moved.name}' is outside the view transition's scope '{vt.Scope.name}' but its update moved it, so it jumped. Widen the scope to animate it.", moved);
        }
#endif

        // ── Order ─────────────────────────────────────────────────────────────────

        /// <summary>True when the node, or a node above it, is on its way out or hidden: either way it is not part of what is shown.</summary>
        private static bool IsLeaving(LayoutNode node)
        {
            for (var t = node.transform; t != null; t = t.parent)
            {
                if (t.TryGetComponent<LayoutNode>(out var n) && (n._exiting || n._hidden))
                    return true;
            }
            return false;
        }

        private static bool IsFlyingOldHalf(ViewTransition vt, LayoutNode node)
        {
            for (int i = 0; i < vt.Groups.Count; i++)
            {
                var g = vt.Groups[i];
                if (ReferenceEquals(g.Old, node) && g.OldFlies && g.Kind != ViewTransition.GroupKind.Exit)
                    return true;
            }
            return false;
        }

        // Capture order, as on the web: groups that existed before the update in the old state's paint order,
        // then the new ones in the new state's paint order.
        private static int CompareGroups(ViewTransition.Group a, ViewTransition.Group b)
        {
            int ka = a.Old != null ? 0 : 1, kb = b.Old != null ? 0 : 1;
            if (ka != kb) return ka - kb;
            return PaintOrder(CapturedPlace(a), CapturedPlace(b));
        }

        // Where a group sat when the scene was captured. A carried node has been moved by the update already, so
        // the parent it was captured under stands in for it: it drew just after that parent.
        private static Transform CapturedPlace(ViewTransition.Group g)
        {
            if (g.Kind == ViewTransition.GroupKind.Carry && g.Old._capturedParent != null)
                return g.Old._capturedParent;
            return (g.Old != null ? g.Old : g.New).transform;
        }

        // UGUI draws in hierarchy order: an ancestor before its subtree, an earlier sibling's subtree before a later one's.
        private static int PaintOrder(Transform a, Transform b)
        {
            if (a == b) return 0;
            s_chainA.Clear();
            for (var t = a; t != null; t = t.parent) s_chainA.Add(t);
            s_chainB.Clear();
            for (var t = b; t != null; t = t.parent) s_chainB.Add(t);
            s_chainA.Reverse();
            s_chainB.Reverse();
            int i = 0;
            while (i < s_chainA.Count && i < s_chainB.Count && s_chainA[i] == s_chainB[i]) i++;
            int result;
            if (i == s_chainA.Count) result = -1;
            else if (i == s_chainB.Count) result = 1;
            else result = s_chainA[i].GetSiblingIndex().CompareTo(s_chainB[i].GetSiblingIndex());
            s_chainA.Clear();
            s_chainB.Clear();
            return result;
        }
    }
}
