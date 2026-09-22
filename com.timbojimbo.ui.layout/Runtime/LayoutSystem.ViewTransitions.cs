using System;
using System.Collections.Generic;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// View transitions, after the web API. <see cref="StartViewTransition"/> captures where every node is, runs the
    /// update, lays the new state out at once, and only then animates. Named pairs and persisting pairs are lifted
    /// out of their trees into a transition layer under their canvas (a placeholder keeps each one's layout slot),
    /// stacked in capture order the way the web stacks its groups in its top layer, and flown along straight lines to
    /// where layout puts them now; entering and exiting nodes fade or play their animator in place beneath the
    /// layer; every other node that changed rect moves there in place. There are no snapshots: the objects
    /// themselves move, so anything animating on them keeps animating through the transition.
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

        // The one transition in flight, and, while its update runs, the one whose exits wait for its animation step.
        private static ViewTransition s_current;
        private static ViewTransition s_updating;
        private static int s_counter;

        /// <summary>
        /// Runs <paramref name="update"/> and animates every change it made to the layout as one transition, the
        /// way <c>document.startViewTransition</c> does. Nodes that ended up somewhere else travel there from where
        /// they were shown, whatever parent or tree they moved to; nodes that appeared enter and nodes handed to
        /// <see cref="Exit"/> inside the update leave, through their <see cref="IViewTransitionAnimator"/> or with a
        /// fade; a node that appears with a <see cref="LayoutNode.ViewTransitionName"/> another node carried before
        /// the update takes that node's place, flying in from its spot while the old one, if it is leaving, flies out
        /// to it, the two cross-fading; and a persisting pair swaps places so the kept object flies in with its state.
        /// Named groups fly in a layer above their canvas, stacked in the order they were captured, as the web's
        /// groups do in its top layer. Nodes move with <paramref name="transition"/> unless they set their own; the
        /// default is <see cref="ViewTransition.DefaultTransition"/>, and a transition with no duration applies the
        /// update at once. A transition already running is skipped first, and a name found on two nodes in one state
        /// skips this one, as on the web. Outside play mode the update is simply applied.
        /// </summary>
        public static ViewTransition StartViewTransition(Action update, LayoutTransition? transition = null)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            var vt = new ViewTransition(++s_counter, transition ?? ViewTransition.DefaultTransition);

            if (!Application.isPlaying || s_reloading || s_updating != null)
            {
                update();
                FlushMarked();
                vt.Finish();
                return vt;
            }

            s_current?.SkipTransition();
            // Settle what is pending first, so the capture is exactly what is on screen.
            FlushMarked();
            Capture(vt);

            s_current = vt;
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
                if (vt.IsSettled)
                    FinishViewTransition(vt);
            }
            else
            {
                Debug.LogWarning("[UI.Layout] A view transition name was on two nodes at once; the transition is skipped.");
                ConcludeUnstartedExits();
                SkipViewTransition(vt);
            }
            return vt;
        }

        /// <summary>The view transition in flight, or null.</summary>
        public static ViewTransition CurrentViewTransition => s_current;

        internal static void Register(LayoutNode node) => s_enabled.Add(node);

        internal static void Unregister(LayoutNode node) => s_enabled.Remove(node);

        /// <summary>
        /// A lifted node disabled or destroyed mid-flight leaves the flight: its placeholder is released and it is
        /// left where it is (moving an object from inside its own OnDisable is not safe), which is what its owner
        /// asked for.
        /// </summary>
        internal static void LiftedNodeDisabled(LayoutNode node)
        {
            if (!node._lifted) return;
            var flight = s_current?.FlightOf(node);
            if (flight != null) flight.Done = true;
            node._lifted = false;
            ReleasePlaceholder(node._placeholder);
            node._placeholder = null;
        }

        // ── Capture and classification ──────────────────────────────────────────

        // Where every enabled node is shown right now, in world space so a node can be matched across parents and
        // trees, and which node carries each name.
        private static void Capture(ViewTransition vt)
        {
            foreach (var node in s_enabled)
            {
                if (node == null || node._isPlaceholder) continue;
                node._captureId = vt.Id;
                node._capturedWorld = LayoutEngine.WorldRect(node.RectTransform);
                vt.CaptureName(node);
            }
        }

        // Sorts the update's changes into the groups table, the way the web pairs its captured elements: by name
        // across the two states, then the exits and the topmost entering nodes. A name present in only one state
        // rides whatever its tree does. False when a name is on two nodes of the new state.
        private static bool Classify(ViewTransition vt)
        {
            s_newNames.Clear();
            foreach (var node in s_enabled)
            {
                if (node == null || node._isPlaceholder || IsLeaving(node)) continue;
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
                if (node.IsRoot) continue;
                bool persist = old.isActiveAndEnabled && (old.ViewTransitionPersist || node.ViewTransitionPersist);
                if (persist)
                    vt.Groups.Add(new ViewTransition.Group { Kind = ViewTransition.GroupKind.Persist, Old = old, New = node, OldFlies = true });
                else
                    vt.Groups.Add(new ViewTransition.Group { Kind = ViewTransition.GroupKind.Pair, Old = old, New = node, OldFlies = old.isActiveAndEnabled && !old.IsRoot && IsLeaving(old) });
            }

            // Exits: every node the update handed to Exit starts its way out here (outside a transition, the pass
            // that reflows its tree does). The old half of a pair is already on its way to its counterpart.
            for (int i = 0; i < s_exiting.Count; i++)
            {
                var node = s_exiting[i];
                if (node == null || !node._exiting || node._exitStarted || !node.isActiveAndEnabled) continue;
                node._exitStarted = true;
                if (IsFlyingOldHalf(vt, node)) continue;
                vt.Groups.Add(new ViewTransition.Group { Kind = ViewTransition.GroupKind.Exit, Old = node, OldFlies = true });
            }

            // Enters: the topmost node of every subtree that appeared, and is not the new half of a pair.
            foreach (var node in s_enabled)
            {
                if (node == null || node._isPlaceholder || node._captureId == vt.Id || !node._hasCommitted || IsLeaving(node)) continue;
                if (node._parentNode != null && node._parentNode._captureId != vt.Id) continue;
                if (vt.IsGrouped(node)) continue;
                vt.Groups.Add(new ViewTransition.Group { Kind = ViewTransition.GroupKind.Enter, New = node });
            }

            vt.Groups.Sort(s_byCapture);
            return true;
        }

        // ── Animation ─────────────────────────────────────────────────────────────

        // Lifts every named group into the layer and starts its flight, starts the enters and exits in place,
        // then the movers, and writes the first frame.
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
                        break;
                    }
                }
            }

            // Movers: every captured node, not lifted and not a group, that is shown somewhere else now.
            foreach (var node in s_enabled)
            {
                if (node == null || node._isPlaceholder || node._lifted || node._captureId != vt.Id || !node._hasCommitted || vt.IsGrouped(node)) continue;
                var t = vt.TransitionFor(node);
                if (!t.IsAnimated) continue;
                var now = LayoutEngine.WorldRect(node.RectTransform);
                if (LayoutEngine.Approximately(now, node._capturedWorld)) continue;
                Rect from;
                if (node.IsRoot)
                {
                    // A root's position is not ours; only its size moves, from what it was to what it is.
                    var scale = node.RectTransform.lossyScale;
                    from = new Rect(0f, 0f, scale.x != 0f ? node._capturedWorld.width / scale.x : node._capturedWorld.width, scale.y != 0f ? node._capturedWorld.height / scale.y : node._capturedWorld.height);
                }
                else
                    from = LayoutEngine.RelFromWorld(node, node._capturedWorld);
                if (node._measurable != null && !node._measurable.SizeIsAnimatable)
                    from.size = node._committedRect.size;
                if (LayoutEngine.Approximately(from, node._committedRect)) continue;
                LayoutEngine.StartMove(node, from, node._committedRect, t);
                vt.Participants.Add(node);
                WriteNow(node);
            }

            TickFlights(vt, 0f);
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
                WriteFlight(vt, flight, eased);
                if (flight.LayoutEachTick)
                    LayoutLifted(node);
                if (node._fadeGroup != null)
                    node._fadeGroup.alpha = Mathf.Lerp(node._fadeFrom, node._fadeTo, eased);
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

        // Puts a flight at its eased point between where it started and where its marker is now, in the layer.
        private static void WriteFlight(ViewTransition vt, ViewTransition.Flight flight, float eased)
        {
            var to = Destination(vt, flight);
            var from = flight.From;
            switch (flight.Size)
            {
                case ViewTransition.SizeRule.KeepDestination: from.size = to.size; break;
                case ViewTransition.SizeRule.KeepFrom: to.size = from.size; break;
            }
            var world = new Rect(
                Vector2.LerpUnclamped(from.position, to.position, eased),
                Vector2.LerpUnclamped(from.size, to.size, eased));
            var node = flight.Node;
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
            for (var t = marker.parent; t != null; t = t.parent)
            {
                if (!t.TryGetComponent<LayoutNode>(out var ancestor) || !ancestor._lifted) continue;
                var ancestorFlight = vt.FlightOf(ancestor);
                if (ancestorFlight == null) break;
                var ancestorNow = LayoutEngine.WorldRect(ancestor.RectTransform);
                var ancestorEnd = Destination(vt, ancestorFlight);
                world.position += ancestorEnd.position - ancestorNow.position;
                break;
            }
            flight.LastDestination = world;
            return world;
        }

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

        private static LayoutNode TakePlaceholder(LayoutNode node)
        {
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
            placeholder.AttachTo = node.AttachTo;
            placeholder.AttachElement = node.AttachElement;
            placeholder.ElementPoint = node.ElementPoint;
            placeholder.ParentPoint = node.ParentPoint;
            placeholder.FloatOffset = node.FloatOffset;
            placeholder.Offset = node.Offset;
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

        private static void ReleasePlaceholder(LayoutNode placeholder)
        {
            if (placeholder == null) return;
            placeholder.enabled = false;
            placeholder.gameObject.SetActive(false);
            if (s_poolRoot == null)
            {
                var go = new GameObject("View Transition Pool");
                go.SetActive(false);
                s_poolRoot = go.transform;
            }
            placeholder.transform.SetParent(s_poolRoot, false);
            s_placeholderPool.Push(placeholder);
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

        /// <summary>Hands a node's first layout to its animator; the transition finishes only once the effect reports done.</summary>
        internal static void RunEnterEffect(LayoutNode node, IViewTransitionAnimator animator, ViewTransition vt)
        {
            var effect = vt.AddEffect(node, animator, false);
            animator.Enter(vt, () => CompleteEffect(vt, effect));
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
                    CompleteEffect(vt, effect);
            });
        }

        private static void CompleteEffect(ViewTransition vt, ViewTransition.Effect effect)
        {
            if (effect.Done) return;
            effect.Done = true;
            if (ReferenceEquals(s_current, vt))
                FinishSettledViewTransition();
        }

        // ── Finishing ─────────────────────────────────────────────────────────────

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
            for (int i = 0; i < vt.Flights.Count; i++)
            {
                var flight = vt.Flights[i];
                if (flight.Done || flight.Node == null || !flight.Node._lifted) { flight.Done = true; continue; }
                flight.Elapsed = flight.Transition.Total;
                WriteFlight(vt, flight, 1f);
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

        // The transition in flight finishes when every flight has landed, no node it set moving is still on its
        // way and every effect has reported done.
        private static void FinishSettledViewTransition()
        {
            var vt = s_current;
            if (vt == null || !vt.IsSettled) return;
            FinishViewTransition(vt);
        }

        // Every lifted node comes back down into its slot, persisting pairs swap places, fades end, an old half
        // that was itself exiting leaves, and the trees touched are laid out in this same flush.
        private static void FinishViewTransition(ViewTransition vt)
        {
            for (int i = 0; i < vt.Flights.Count; i++)
            {
                var node = vt.Flights[i].Node;
                if (node == null) continue;
                Return(node);
            }
            for (int i = 0; i < vt.Swaps.Count; i++)
            {
                var (kept, copy) = vt.Swaps[i];
                Swap(kept, copy);
                if (copy != null) LayoutEngine.EndFade(copy);
            }
            for (int i = 0; i < vt.Flights.Count; i++)
            {
                var node = vt.Flights[i].Node;
                if (node == null) continue;
                LayoutEngine.EndFade(node);
                if (node._exiting)
                    Conclude(node, node._exitThen);
            }
            ApplyDeferredMarks();
            if (ReferenceEquals(s_current, vt))
                s_current = null;
            vt.Finish();
        }

        // Exits an update asked for that no animation step will start (the transition was skipped before it began).
        private static void ConcludeUnstartedExits()
        {
            for (int i = s_exiting.Count - 1; i >= 0; i--)
            {
                var node = s_exiting[i];
                if (node != null && node._exiting && !node._exitStarted)
                    Conclude(node, node._exitThen);
            }
        }

        // ── Order ─────────────────────────────────────────────────────────────────

        /// <summary>True when the node, or a node above it, is on its way out.</summary>
        private static bool IsLeaving(LayoutNode node)
        {
            for (var t = node.transform; t != null; t = t.parent)
            {
                if (t.TryGetComponent<LayoutNode>(out var n) && n._exiting)
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
            var ta = (a.Old != null ? a.Old : a.New).transform;
            var tb = (b.Old != null ? b.Old : b.New).transform;
            return PaintOrder(ta, tb);
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
