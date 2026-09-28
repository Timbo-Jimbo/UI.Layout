using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// Lays out every <see cref="LayoutNode"/> each frame and moves them to where they go. A change made in
    /// <see cref="Animate"/> moves the nodes it gives somewhere new on springs, from where they are drawn and at the
    /// velocity they have; any other change puts them there at once. A node that scrolls moves its children together by
    /// its scroll offset, which drags, flicks, the wheel and ScrollTo drive.
    /// </summary>
    public static class LayoutSystem
    {
        // Once a frame, just before canvases are drawn (play mode and edit mode alike), every tree is laid out from
        // its root: the solver says where each node goes, which becomes its targets; the springs of what is moving are
        // stepped; and where each node is drawn is written into its RectTransform and CanvasGroup. Outside Animate a
        // target that changes is taken up at once, so a drag moving a node's Offset follows the pointer exactly;
        // Animate lays out before and after its update, and what the update changed sets off on springs. In edit mode
        // nothing animates.
        //
        // A node whose Scroll is not None is a scroll container, as a Clay scroll container or a UIScrollView: the
        // solver lets its children run past its edge the ways it scrolls, a RectMask2D clips them, and its LayoutNode
        // children (in its flow or floating) are drawn moved together by its scroll offset. The offset only moves
        // where they are drawn: their springs, targets and layout never see it, and anything that goes through where
        // they are drawn (reparenting, a node first met out of layout) takes it off and puts it back. Children that are
        // plain objects are not moved by it. The offset is the engine's, with a velocity, in phases: held by a press
        // (following its drag exactly, rubber-banding past the ends), gliding once flicked (at UIScrollView's
        // deceleration rate), or springing (back to an end, to a wheel's target, or where ScrollTo or ScrollOffset sent
        // it, on the node's own spring inside Animate); it is stepped with the springs, once a frame in play mode. A
        // LayoutScroller the system adds takes the pointer for it. In edit mode it is always at the start.

        // What the system drives on a node's RectTransform, which the editor then shows as driven and does not save.
        private const DrivenTransformProperties Driven = DrivenTransformProperties.Anchors | DrivenTransformProperties.Pivot
            | DrivenTransformProperties.AnchoredPosition | DrivenTransformProperties.SizeDelta | DrivenTransformProperties.Scale;

        // What the components the system adds to a scroll container are: not shown in the inspector, and never saved
        // (in the scene or a build), so a scene is the same with or without them.
        private const HideFlags AddedFlags = HideFlags.DontSave | HideFlags.HideInInspector;

        // Every enabled node, and what is kept for it.
        private static readonly Dictionary<LayoutNode, NodeState> s_states = new();

        // Transitions that everything they set moving has let go of, finished once the pass under way is over: a
        // Finished handler may start another change.
        private static readonly List<LayoutTransition> s_finishing = new();

        // Scroll containers whose offset has changed since Scrolled was last raised for them, raised once the frame is
        // drawn (a handler may scroll or lay out something else).
        private static readonly List<NodeState> s_scrolled = new();

        // ScrollTo was asked for something not laid out inside the container: said once.
        private static bool s_warnedScrollTo;

        // A pass's scratch: the roots, outer before inner; one tree's nodes in pre-order, out of layout ones included;
        // and its solver nodes, the first s_count of them in use.
        private static readonly List<NodeState> s_roots = new();
        private static readonly List<NodeState> s_visit = new();
        private static SolverNode[] s_solver = new SolverNode[64];
        private static int s_count;
        private static readonly Vector3[] s_corners = new Vector3[4];

        // The transition whose update is running, which an Animate inside it joins.
        private static LayoutTransition s_current;
        // A pass is under way (a canvas update forced from inside one must not start another).
        private static bool s_passing;
        private static bool s_hooked;
        // The frame the springs were last stepped in: once a frame, however often canvases update.
        private static int s_steppedFrame = -1;

        // Statics survive play mode sessions when domain reload is off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_current = null;
            s_passing = false;
            s_steppedFrame = -1;
            s_finishing.Clear();
            foreach (var state in s_scrolled)
                state.Scroll.Queued = false;
            s_scrolled.Clear();
            s_warnedScrollTo = false;
            s_roots.Clear();
            s_visit.Clear();
            s_count = 0;
            Array.Clear(s_solver, 0, s_solver.Length);
            // Nodes register and unregister themselves as they are enabled and disabled, so the set keeps itself; any
            // an earlier session left behind that are gone are dropped. The canvas hook lasts as long as the statics
            // do, so it stays.
            var gone = new List<LayoutNode>();
            foreach (var node in s_states.Keys)
            {
                if (node == null)
                    gone.Add(node);
            }
            foreach (var node in gone)
                s_states.Remove(node);
        }

        /// <summary>
        /// Makes the change <paramref name="update"/> makes, and moves every node it gives somewhere new (a new place,
        /// size, or Display) on its own <see cref="LayoutNode.Animation"/>'s spring, from where it is drawn and at the
        /// velocity it has, as SwiftUI's withAnimation. Nodes already moving that it leaves alone carry on; those it
        /// changes turn from where they are. <paramref name="types"/> say what kind of change it is. Called inside
        /// another's update, the change is part of that one.
        /// </summary>
        public static LayoutTransition Animate(Action update, params string[] types)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));

            // Inside another's update, the change is part of that one.
            if (s_current != null)
            {
                update();
                return s_current;
            }

            var transition = new LayoutTransition(types);

            // Outside play mode nothing animates: the change goes where it goes at once, as any other does.
            if (!Application.isPlaying)
            {
                update();
                transition.Finish();
                return transition;
            }

            // What changed before it goes where it goes at once, so only what the update changes animates.
            Pass(null);
            s_current = transition;
            try
            {
                update();
            }
            catch
            {
                // What it changed before it threw goes where it goes at once in the next frame, as any other change;
                // the transition moved nothing, and finishes then.
                s_current = null;
                s_finishing.Add(transition);
                throw;
            }
            s_current = null;
            Pass(transition);

            // Nothing moved: it is over already (a Finished handler added later still runs, at once).
            if (transition.Moving == 0)
                transition.Finish();
            Flush();
            return transition;
        }

        // ── For LayoutNode and LayoutTransition ──────────────────────────────────

        internal static void Register(LayoutNode node)
        {
            if (!s_states.ContainsKey(node))
                s_states.Add(node, new NodeState(node));
            if (!s_hooked)
            {
                s_hooked = true;
                Canvas.preWillRenderCanvases += Tick;
            }
            Changed(node);
        }

        // Disabled or destroyed, a node is not drawn, so nothing waits for it: it lets go of the transitions it was
        // moving for, and of its RectTransform. Enabled again, it is met afresh, as a node seen for the first time.
        internal static void Unregister(LayoutNode node)
        {
            if (s_states.TryGetValue(node, out var state))
            {
                s_states.Remove(node);
                Stop(state.Position);
                Stop(state.Size);
                Stop(state.Opacity);
                Stop(state.Scale);
                Disown(state);
                // No longer shown or hidden by the system: drawn and clickable, as it would be without it.
                if (state.Group != null)
                {
                    if (state.Group.alpha != 1f) state.Group.alpha = 1f;
                    if (!state.Group.blocksRaycasts) state.Group.blocksRaycasts = true;
                }
                // Nor scrolled: what it added stops clipping and taking the pointer (met again, it takes them back).
                var scroll = state.Scroll;
                if (scroll != null)
                {
                    Stop(scroll.Offset);
                    scroll.Press = null;
                    if (scroll.Queued)
                    {
                        scroll.Queued = false;
                        s_scrolled.Remove(state);
                    }
                    if (scroll.Clip != null && IsHidden(scroll.Clip)) scroll.Clip.enabled = false;
                    if (scroll.Scroller != null) scroll.Scroller.enabled = false;
                }
            }
            Changed(node);
        }

        // Something about the node changed; outside play mode, the editor should draw it again.
        internal static void Changed(LayoutNode node)
        {
#if UNITY_EDITOR
            // In play mode the next frame lays it out anyway. In edit mode the player loop, and with it the canvas
            // update this lays out in, only runs when something asks for it.
            if (Application.isPlaying) return;
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
#endif
        }

        internal static bool IsRoot(LayoutNode node)
        {
            var parent = node.transform.parent;
            return parent == null || !parent.TryGetComponent(out LayoutNode parentNode) || !parentNode.isActiveAndEnabled;
        }

        // Its position's velocity, from its parent's layout space (y down) into world units a second.
        internal static Vector3 VelocityOf(LayoutNode node)
        {
            if (!s_states.TryGetValue(node, out var state) || !state.Position.Moving) return Vector3.zero;
            var space = SpaceOf(state);
            if (space == null) return Vector3.zero;
            var velocity = state.Position.Velocity;
            return space.TransformVector(new Vector3(velocity.x, -velocity.y, 0f));
        }

        // Sets its position moving at `velocity` (world units a second) towards where layout puts it, on its own
        // spring; not for any transition, and with no sideways kick. An Animate after it sets off at that velocity.
        internal static void Fling(LayoutNode node, Vector3 velocity)
        {
            if (!Application.isPlaying || !s_states.TryGetValue(node, out var state)) return;

            // What moved it since the last pass (a drag's last move, most likely in this same frame) goes where it
            // goes at once first, as the frame's own pass would, rather than stopping the fling there. Inside
            // Animate's update, that update's pass takes it up, keeping the velocity.
            if (s_current == null)
                Pass(null);

            var space = SpaceOf(state);
            if (space == null) return;
            var spring = state.Position;
            Vector2 local = space.InverseTransformVector(velocity);
            Spring.Parameters(state.Node.Animation, out spring.Omega, out spring.Zeta);
            spring.Velocity = new Vector2(local.x, -local.y);
            spring.Delay = 0f;
            Hold(spring, null);
            FlushIfIdle();
        }

        // Stops its position where it is drawn: its Offset moves by how far that is from where it was going (y up),
        // so its target is where it is, and the next pass reads that back as no change. Its size, opacity and scale
        // carry on.
        internal static bool Catch(LayoutNode node)
        {
            if (!s_states.TryGetValue(node, out var state) || state.Parent == null || !state.Position.Moving) return false;
            var spring = state.Position;
            var off = spring.Value - spring.Target;
            spring.Target = spring.Value;
            Stop(spring);
            node.Offset += new Vector2(off.x, -off.y);
            FlushIfIdle();
            return true;
        }

        // Its scroll offset as drawn (0 for a node that does not scroll, and outside play mode).
        internal static Vector2 ScrollOffsetOf(LayoutNode node) =>
            s_states.TryGetValue(node, out var state) ? ScrolledBy(state) : Vector2.zero;

        // Scrolls it to `offset`, within range. Inside Animate's update, that change's pass does it, on the node's
        // spring for it (the node's layout may be changing in the same update). Otherwise at once, where it stops; or,
        // for a node no pass has laid out as a scroll container yet (so it has no range to keep it within), at the end
        // of the next pass, at once. Outside play mode it stays at the start.
        internal static void SetScrollOffset(LayoutNode node, Vector2 offset)
        {
            if (!Application.isPlaying || node.Scroll == ScrollAxis.None || !s_states.TryGetValue(node, out var state)) return;
            var scroll = state.Scroll ??= new ScrollState();
            if (s_current == null && scroll.Measured && scroll.Axis != ScrollAxis.None)
            {
                ClearRequest(scroll);
                StopScrollAt(scroll, scroll.Clamp(offset));
                FlushIfIdle();
                return;
            }
            RequestScroll(scroll, ScrollRequest.Offset, offset, null, 0f);
        }

        // How far it can scroll each way (0 for a node that does not scroll).
        internal static Vector2 ScrollRangeOf(LayoutNode node) =>
            s_states.TryGetValue(node, out var state) && state.Scroll != null && state.Scroll.Axis != ScrollAxis.None
                ? state.Scroll.Range
                : Vector2.zero;

        // Whether its scroll offset is held by a press, gliding or springing.
        internal static bool IsScrolling(LayoutNode node) =>
            s_states.TryGetValue(node, out var state) && state.Scroll != null && state.Scroll.Phase != ScrollPhase.Idle;

        // Scrolls so that `descendant` sits `anchor` of the way along what it shows, within range. Inside Animate's
        // update, that change's pass does it (the descendant may be added in the same update), on the node's spring for
        // it. Otherwise at once when the descendant has been laid out inside it, and at the end of the next pass, at
        // once, when it has not yet. Something not inside it is not scrolled to (and said so, once).
        internal static void ScrollTo(LayoutNode node, LayoutNode descendant, float anchor)
        {
            if (descendant == null) throw new ArgumentNullException(nameof(descendant));
            if (!Application.isPlaying || node.Scroll == ScrollAxis.None || !s_states.TryGetValue(node, out var state)) return;
            if (descendant == node || !descendant.transform.IsChildOf(node.transform))
            {
                WarnScrollTo(node, descendant);
                return;
            }
            var scroll = state.Scroll ??= new ScrollState();
            if (s_current == null && scroll.Measured && scroll.Axis != ScrollAxis.None
                && TryScrollTarget(state, descendant, anchor, out var offset))
            {
                ClearRequest(scroll);
                StopScrollAt(scroll, offset);
                FlushIfIdle();
                return;
            }
            RequestScroll(scroll, ScrollRequest.To, Vector2.zero, descendant, anchor);
        }

        // Puts everything the transition is moving where it is going, and finishes it.
        internal static void Skip(LayoutTransition transition)
        {
            if (transition == null || transition.IsFinished) return;
            foreach (var state in s_states.Values)
            {
                StopIfHeld(state.Position, transition);
                StopIfHeld(state.Size, transition);
                StopIfHeld(state.Opacity, transition);
                StopIfHeld(state.Scale, transition);
                var scroll = state.Scroll;
                if (scroll != null && scroll.Offset.Transition == transition)
                    StopScrollAt(scroll, scroll.Offset.Target);
            }
            transition.Finish();
        }

        private static void StopIfHeld(Spring spring, LayoutTransition transition)
        {
            if (spring.Transition == transition)
                Stop(spring);
        }

        // ── The frame ────────────────────────────────────────────────────────────

        // Each tree in turn, outer roots first, is laid out, stepped and drawn before the next is laid out, so a root
        // inside a node (under a plain object) lays out in that node's rect as drawn this frame.
        private static void Tick()
        {
            // Not while Animate's update is making its change (its own pass takes that up), nor inside a pass: a
            // canvas update forced from inside either comes back here.
            if (s_current != null || s_passing) return;

            bool playing = Application.isPlaying;
            int frame = Time.frameCount;
            bool step = playing && frame != s_steppedFrame;
            if (step)
                s_steppedFrame = frame;
            // UI keeps its own time: it moves and scrolls in a pause menu (a timeScale of 0) and at the speed it was
            // dragged at whatever the game's time is doing, as UIKit and ScrollRect do.
            float dt = Time.unscaledDeltaTime;

            Frame(playing, step, dt);
            // A Finished handler may have shown, made or moved nodes: they are laid out and drawn this frame too,
            // rather than drawn once where they were first. Nothing is stepped again.
            if (Flush())
            {
                Frame(playing, false, dt);
                Flush();
            }
            // Scrolled comes last, with where each container is drawn this frame. What its handlers change (a label
            // showing the offset) is laid out with the next frame: a container scrolls every frame it moves, and laying
            // everything out twice for each of those is not worth one frame sooner.
            RaiseScrolled();
        }

        private static void Frame(bool playing, bool step, float dt)
        {
            s_passing = true;
            try
            {
                CollectRoots();
                for (int r = 0; r < s_roots.Count; r++)
                {
                    LayOut(s_roots[r], null);
                    // Pre-order: a scroll container's offset is stepped before its children are drawn by it.
                    for (int i = 0; i < s_visit.Count; i++)
                    {
                        var state = s_visit[i];
                        Advance(state.Position, playing, step, dt);
                        Advance(state.Size, playing, step, dt);
                        Advance(state.Opacity, playing, step, dt);
                        Advance(state.Scale, playing, step, dt);
                        if (state.Scroll != null && state.Scroll.Axis != ScrollAxis.None)
                            StepScroll(state, playing, step, dt);
                        Write(state);
                    }
                }
            }
            finally
            {
                s_passing = false;
            }
        }

        // Lays out every tree and gives each node its targets: at once without a transition, and with one, what
        // changed sets off on springs held by it. Nothing is stepped or drawn: the frame does that.
        private static void Pass(LayoutTransition transition)
        {
            if (s_passing) return;
            s_passing = true;
            try
            {
                CollectRoots();
                for (int r = 0; r < s_roots.Count; r++)
                    LayOut(s_roots[r], transition);
            }
            finally
            {
                s_passing = false;
            }
        }

        // Every root, outer before inner (by depth, so an ancestor always comes first).
        private static void CollectRoots()
        {
            s_roots.Clear();
            foreach (var pair in s_states)
            {
                var node = pair.Key;
                if (node == null || !IsRoot(node)) continue;
                var state = pair.Value;
                state.Depth = 0;
                for (var t = node.transform.parent; t != null; t = t.parent)
                    state.Depth++;
                int at = s_roots.Count;
                while (at > 0 && s_roots[at - 1].Depth > state.Depth)
                    at--;
                s_roots.Insert(at, state);
            }
        }

        // Solves one tree, in its root's rect, and places each of its nodes; then, the whole tree placed (a ScrollTo
        // reads where its descendant goes), each scroll container in it takes up its new range and what was asked of it.
        private static void LayOut(NodeState root, LayoutTransition transition)
        {
            s_visit.Clear();
            s_count = 0;
            Visit(root, null, true, -1);
            if (s_count > 0)
                LayoutSolver.Solve(s_solver, s_count, root.RectTransform.rect.size);
            for (int i = 0; i < s_visit.Count; i++)
                Place(s_visit[i], transition);
            for (int i = 0; i < s_visit.Count; i++)
            {
                var state = s_visit[i];
                if (state.Scroll != null && state.Scroll.Axis != ScrollAxis.None)
                    SettleScroll(state, transition);
            }
        }

        // Adds a node and everything under it to the pass, in pre-order: to the solver while it and everything above
        // it are in layout (Display not None), and to the visit either way, so what has left layout still fades and is
        // drawn. Its children are the LayoutNodes directly under it, in sibling order; a plain object in between
        // makes the node under it a root of its own. Returns its solver index, or -1.
        private static int Visit(NodeState state, NodeState parent, bool inLayout, int parentIndex)
        {
            var node = state.Node;
            state.PassParent = parent;
            if (node.Scroll != ScrollAxis.None || state.Scroll != null)
                SetUpScroll(state);
            // Not drawn as the pass starts, with nothing it is seen to move from: never placed, back in layout from None
            // having faded out (the targets it kept are stale), or under something that is. A Hidden node stays laid
            // out, so its targets are current: what an Animate gives it, it moves to, even from no opacity (a toast
            // shown and slid in at once). PassIndex is still the last pass's here.
            bool cameBack = state.PassIndex < 0 && state.Opacity.Value.x <= 0f;
            state.PassUnseen = !state.Seen || cameBack || (parent != null && parent.PassUnseen);

            int index = -1;
            if (inLayout && node.Display != DisplayMode.None)
            {
                index = s_count++;
                if (index == s_solver.Length)
                    Array.Resize(ref s_solver, index * 2);
                node.TryGetComponent(out ILayoutMeasurable content);
                s_solver[index] = new SolverNode
                {
                    Parent = parentIndex,
                    FirstChild = -1,
                    NextSibling = -1,
                    Width = node.Width,
                    Height = node.Height,
                    Padding = node.Padding,
                    ChildGap = node.ChildGap,
                    Direction = node.Direction,
                    AlignX = node.ChildAlignX,
                    AlignY = node.ChildAlignY,
                    AspectRatio = node.AspectRatio,
                    Floating = node.Floating,
                    Scroll = node.Scroll,
                    Content = content,
                };
            }
            state.PassIndex = index;
            s_visit.Add(state);

            var transform = state.RectTransform;
            int last = -1;
            for (int i = 0; i < transform.childCount; i++)
            {
                if (!transform.GetChild(i).TryGetComponent(out LayoutNode child) || !child.isActiveAndEnabled
                    || !s_states.TryGetValue(child, out var childState))
                    continue;
                int childIndex = Visit(childState, state, index >= 0, index);
                if (childIndex < 0) continue;
                // The array may have grown inside the call: index it afresh.
                if (last < 0)
                    s_solver[index].FirstChild = childIndex;
                else
                    s_solver[last].NextSibling = childIndex;
                last = childIndex;
            }
            return index;
        }

        // Gives a node its targets from the solved layout: its centre (moved by its Offset, y up) and size where it is
        // laid out, kept where they were when it has left layout; its opacity 1 when Visible and 0 otherwise; its
        // scale 1. Parents come before their children.
        private static void Place(NodeState state, LayoutTransition transition)
        {
            var node = state.Node;
            var parent = state.PassParent;
            var opacity = new Vector2(node.Display == DisplayMode.Visible ? 1f : 0f, 0f);

            // Met for the first time, it starts where it goes; out of layout, where its rect is. Inside Animate it
            // fades in, unless what it is inside is not drawn yet either (only the topmost of what appears fades, what
            // is inside it coming with it, rather than fading on top of its fade).
            if (!state.Seen)
            {
                state.Seen = true;
                state.Parent = parent;
                if (parent != null)
                {
                    Own(state);
                    Vector2 centre, size;
                    if (state.PassIndex >= 0)
                    {
                        TargetOf(state, out centre, out size);
                    }
                    else
                    {
                        // Drawn there inside a scroll container, it is that far down its content.
                        DrawnIn(state.RectTransform, parent.RectTransform, out centre, out size);
                        centre += ScrolledBy(parent);
                    }
                    Retarget(state, state.Position, centre, null);
                    Retarget(state, state.Size, size, null);
                }
                Retarget(state, state.Scale, new Vector2(1f, 0f), null);
                if (transition != null && (parent == null || !parent.PassUnseen))
                {
                    Retarget(state, state.Opacity, Vector2.zero, null);
                    Retarget(state, state.Opacity, opacity, transition);
                }
                else
                {
                    Retarget(state, state.Opacity, opacity, null);
                }
                return;
            }

            if (parent != state.Parent)
                Reparent(state, parent);

            if (parent != null && state.PassIndex >= 0)
            {
                TargetOf(state, out var centre, out var size);
                // What is not drawn has nowhere it is seen to move from: what it is given, it goes to at once (a page
                // coming back after its layout changed while it was gone appears where it now goes, and fades in).
                var move = state.PassUnseen ? null : transition;
                if (centre != state.Position.Target)
                    Retarget(state, state.Position, centre, move);
                if (size != state.Size.Target)
                    Retarget(state, state.Size, size, move);
            }
            if (opacity != state.Opacity.Target)
                Retarget(state, state.Opacity, opacity, transition);
        }

        // Where the solver put a node: its rect's centre, moved by its Offset (y up), and its size.
        private static void TargetOf(NodeState state, out Vector2 centre, out Vector2 size)
        {
            var rect = s_solver[state.PassIndex].Rect;
            var offset = state.Node.Offset;
            centre = new Vector2(rect.x + rect.width * 0.5f + offset.x, rect.y + rect.height * 0.5f - offset.y);
            size = rect.size;
        }

        // A node found under a different layout parent carries where it is, how fast it is going and where it was
        // going over into the new one's layout space, through world space, so it moves on from where it was drawn.
        // Out from under the old one (a root now) its rect is its own again; in from being a root (or from a parent
        // that is gone) it starts from where its rect is drawn. Going through where it is drawn, a scroll container's
        // offset comes off on the way out (drawn = centre - offset) and goes back on on the way in, so it does not
        // jump by it; a velocity, a difference, is the same either way.
        private static void Reparent(NodeState state, NodeState parent)
        {
            var old = state.Parent;
            state.Parent = parent;
            if (parent == null)
            {
                Stop(state.Position);
                Stop(state.Size);
                Stop(state.Scale);
                Disown(state);
                return;
            }

            Own(state);
            var from = old != null ? old.RectTransform : null;
            var to = parent.RectTransform;
            var scrolledIn = ScrolledBy(parent);
            if (from == null)
            {
                DrawnIn(state.RectTransform, to, out var centre, out var size);
                Retarget(state, state.Position, centre + scrolledIn, null);
                Retarget(state, state.Size, size, null);
                return;
            }
            var scrolledOut = ScrolledBy(old);
            var position = state.Position;
            position.Value = PointTo(from, to, position.Value - scrolledOut) + scrolledIn;
            position.Target = PointTo(from, to, position.Target - scrolledOut) + scrolledIn;
            position.Velocity = VectorTo(from, to, position.Velocity);
        }

        // ── Retargeting ──────────────────────────────────────────────────────────

        // The one place a spring is given somewhere to go. Without a transition, one at rest is put there at once (a
        // caught node dragged by its Offset follows the pointer exactly), while one already on its way heads there
        // instead, keeping its speed, its spring and the change it is moving for: a box changing under it because
        // something outside it is animating (a nested root's rect) does not cut its motion off. With a transition, it
        // sets off from where it is at the velocity it has, on its node's spring after its delay, held by that
        // transition until it comes to rest or is taken over in turn; a position bows out sideways by the animation's
        // curvature.
        private static void Retarget(NodeState state, Spring spring, Vector2 target, LayoutTransition transition)
        {
            spring.Target = target;
            if (transition == null)
            {
                if (!spring.Moving)
                    Stop(spring);
                return;
            }
            // At rest there already: there is nothing to move.
            if (!spring.Moving && spring.Value == target)
            {
                spring.Value = target;
                return;
            }

            var animation = state.Node.Animation;
            Spring.Parameters(animation, out spring.Omega, out spring.Zeta);
            spring.Delay = Mathf.Max(0f, animation.Delay);
            if (spring == state.Position && animation.Curvature > 0f)
                spring.Velocity += Bow(target - spring.Value, animation.Curvature, spring.Omega);
            Hold(spring, transition);
        }

        // Curvature, option A: a kick at right angles to the move `d`, of curvature x 0.68 x |d| x omega, which a
        // critically damped spring pulls back in, bowing out about curvature x |d| / 4 and peaking early. It bows
        // towards the corner of the L that takes the shorter axis first; on a level or upright move, where that corner
        // is on the line, to the left of the way it goes as seen on screen. Layout space is y down, so the screen's
        // left of (dx, dy) is (dy, -dx) there.
        private static Vector2 Bow(Vector2 d, float curvature, float omega)
        {
            float length = d.magnitude;
            if (length < 1e-4f) return Vector2.zero;
            var left = new Vector2(d.y, -d.x) / length;
            // The corner and the chord's midpoint, from where it sets off.
            var corner = Mathf.Abs(d.x) <= Mathf.Abs(d.y) ? new Vector2(d.x, 0f) : new Vector2(0f, d.y);
            float side = Vector2.Dot(corner - d * 0.5f, left);
            float sign = Mathf.Abs(side) > 1e-3f * length ? Mathf.Sign(side) : 1f;
            return left * (sign * curvature * 0.68f * length * omega);
        }

        // Sets a spring moving for `transition` (or for none), letting go of the one it was moving for. One setting off
        // from rest notes the frame, which does not step it.
        private static void Hold(Spring spring, LayoutTransition transition)
        {
            if (spring.Transition != transition)
            {
                Release(spring);
                spring.Transition = transition;
                if (transition != null)
                    transition.Moving++;
            }
            if (!spring.Moving)
            {
                spring.Moving = true;
                spring.SetOff = Time.frameCount;
            }
        }

        // Puts a spring where it is going, at rest, letting go of its transition.
        private static void Stop(Spring spring)
        {
            spring.Value = spring.Target;
            spring.Velocity = Vector2.zero;
            spring.Delay = 0f;
            spring.Moving = false;
            Release(spring);
        }

        // Lets go of the transition a spring was moving for; one with nothing left moving finishes once the pass under
        // way is over.
        private static void Release(Spring spring)
        {
            var transition = spring.Transition;
            if (transition == null) return;
            spring.Transition = null;
            if (--transition.Moving <= 0)
            {
                transition.Moving = 0;
                s_finishing.Add(transition);
            }
        }

        // Finishes the transitions let go of, one at a time and each taken off the list first: a Finished handler may
        // start another change, which may let go of more. Returns whether it finished any. Outside play mode there are
        // none to finish: what leaving play mode let go of belongs to play mode, and its handlers never run.
        private static bool Flush()
        {
            if (!Application.isPlaying)
            {
                s_finishing.Clear();
                return false;
            }
            bool finished = false;
            while (s_finishing.Count > 0)
            {
                var transition = s_finishing[0];
                s_finishing.RemoveAt(0);
                if (transition.Moving > 0) continue;
                transition.Finish();
                finished = true;
            }
            return finished;
        }

        private static void FlushIfIdle()
        {
            if (!s_passing && s_current == null)
                Flush();
        }

        // Steps a moving spring once a frame in play mode, bar the frame it set off from rest in, and stops it where it
        // is going once it is there. Outside play mode nothing animates: anything left moving is put where it was going.
        private static void Advance(Spring spring, bool playing, bool step, float dt)
        {
            if (!spring.Moving) return;
            if (!playing)
            {
                Stop(spring);
                return;
            }
            if (!step || spring.SetOff == Time.frameCount) return;
            if (spring.Step(dt))
                Stop(spring);
        }

        // ── Drawing ──────────────────────────────────────────────────────────────

        // Writes where a node is drawn into what it owns, each value only when it differs from what is there, so an
        // unchanged scene is not dirtied in edit mode and UGUI rebuilds nothing for nothing. A non-root's RectTransform
        // is anchored at its parent's top-left corner, pivoted on its centre, at its centre (y up; moved back by its
        // parent's scroll offset, when its parent scrolls) and size, scaled around it. Its CanvasGroup takes its
        // opacity, and blocks raycasts only while it is Visible; one is added only once it is needed (faded, or not to
        // be clicked).
        private static void Write(NodeState state)
        {
            var node = state.Node;
            if (state.Parent != null)
            {
                var rt = state.RectTransform;
                var anchor = new Vector2(0f, 1f);
                if (rt.anchorMin != anchor) rt.anchorMin = anchor;
                if (rt.anchorMax != anchor) rt.anchorMax = anchor;
                var pivot = new Vector2(0.5f, 0.5f);
                if (rt.pivot != pivot) rt.pivot = pivot;
                var centre = state.Position.Value - ScrolledBy(state.Parent);
                var position = new Vector2(centre.x, -centre.y);
                if (rt.anchoredPosition != position) rt.anchoredPosition = position;
                // A bouncy spring shrinking to nothing swings past it: it stops at nothing rather than turning inside out.
                var size = Vector2.Max(state.Size.Value, Vector2.zero);
                if (rt.sizeDelta != size) rt.sizeDelta = size;
                float s = state.Scale.Value.x;
                var scale = new Vector3(s, s, 1f);
                if (rt.localScale != scale) rt.localScale = scale;
            }

            float alpha = Mathf.Clamp01(state.Opacity.Value.x);
            bool clickable = node.Display == DisplayMode.Visible;
            if (state.Group == null && !node.TryGetComponent(out state.Group))
            {
                if (alpha >= 1f && clickable) return;
                state.Group = node.gameObject.AddComponent<CanvasGroup>();
            }
            if (state.Group.alpha != alpha) state.Group.alpha = alpha;
            if (state.Group.blocksRaycasts != clickable) state.Group.blocksRaycasts = clickable;
        }

        // Takes over a node's RectTransform (it is no longer a root), telling the editor so.
        private static void Own(NodeState state)
        {
            if (state.Owned) return;
            state.Owned = true;
            state.Tracker.Add(state.Node, state.RectTransform, Driven);
        }

        // Gives a node's RectTransform back (it is a root now, or gone from layout).
        private static void Disown(NodeState state)
        {
            if (!state.Owned) return;
            state.Owned = false;
            state.Tracker.Clear();
        }

        // ── Scrolling ────────────────────────────────────────────────────────────

        // Makes a node a scroll container, or stops it being one, when its Scroll has changed since the last pass. A
        // scroll container clips what is in it with a RectMask2D and takes the pointer with a LayoutScroller, both added
        // hidden and never saved (in edit mode too, so the clip shows there), or found on it already (after a domain
        // reload, say): a RectMask2D of its own, not hidden, clips anyway and is never touched. One that stops
        // scrolling turns them off, keeping them for when it scrolls again. Whatever it was doing stops where it is
        // drawn on the axes it still scrolls, at the start on the others; what was asked of it waits for the pass, unless
        // it scrolls no more.
        private static void SetUpScroll(NodeState state)
        {
            var node = state.Node;
            var axis = node.Scroll;
            var scroll = state.Scroll;
            if (scroll != null && scroll.Axis == axis) return;
            scroll ??= state.Scroll = new ScrollState();
            scroll.Axis = axis;
            StopScrollAt(scroll, scroll.Offset.Value);
            scroll.Range = scroll.OnAxes(scroll.Range);

            if (axis == ScrollAxis.None)
            {
                ClearRequest(scroll);
                if (scroll.Clip != null && IsHidden(scroll.Clip) && scroll.Clip.enabled) scroll.Clip.enabled = false;
                if (scroll.Scroller != null && scroll.Scroller.enabled) scroll.Scroller.enabled = false;
                // Its children are drawn where they are laid out again: it is scrolled by nothing.
                if (scroll.Raised != Vector2.zero) QueueScrolled(state);
                return;
            }

            if (scroll.Clip == null && !node.TryGetComponent(out scroll.Clip))
                scroll.Clip = Hide(node.gameObject.AddComponent<RectMask2D>());
            if (scroll.Clip != null && IsHidden(scroll.Clip) && !scroll.Clip.enabled) scroll.Clip.enabled = true;
            if (scroll.Scroller == null && !node.TryGetComponent(out scroll.Scroller))
                scroll.Scroller = Hide(node.gameObject.AddComponent<LayoutScroller>());
            if (scroll.Scroller != null && !scroll.Scroller.enabled) scroll.Scroller.enabled = true;
        }

        private static T Hide<T>(T component) where T : Component
        {
            if (component != null)
                component.hideFlags = AddedFlags;
            return component;
        }

        // Whether the system added it (a component of the user's is never hidden from the inspector).
        private static bool IsHidden(Component component) => (component.hideFlags & HideFlags.HideInInspector) != 0;

        // After a pass has placed a scroll container's tree: it takes its size and range from the solver (how far its
        // content runs past its size, on the axes it scrolls). Then, in play mode: what is moving it heads for where it
        // can now go, keeping its speed; a request made of it is resolved; and, at rest, it comes back into range if its
        // content shrank under it, on its spring for the change inside Animate and at once outside. Held, gliding or
        // springing, it is left to its motion, which settles in range.
        private static void SettleScroll(NodeState state, LayoutTransition transition)
        {
            var scroll = state.Scroll;
            if (state.PassIndex >= 0)
            {
                ref var solved = ref s_solver[state.PassIndex];
                var size = solved.Rect.size;
                scroll.Viewport = size;
                // Overflow within the solver's epsilon is float rounding, not something to scroll to.
                var overflow = solved.ContentSize - size;
                scroll.Range = scroll.OnAxes(new Vector2(overflow.x > 0.01f ? overflow.x : 0f, overflow.y > 0.01f ? overflow.y : 0f));
                scroll.Measured = true;
            }
            if (!Application.isPlaying || !scroll.Measured) return;

            if (scroll.Phase == ScrollPhase.Gliding || scroll.Phase == ScrollPhase.Springing)
            {
                scroll.Offset.Target = scroll.Clamp(scroll.Offset.Target);
                scroll.WheelTarget = scroll.Clamp(scroll.WheelTarget);
            }
            if (scroll.Request != ScrollRequest.None && state.PassIndex >= 0 && ResolveRequest(state, transition))
                return;
            if (scroll.Phase == ScrollPhase.Idle)
            {
                var value = scroll.Offset.Value;
                var clamped = scroll.Clamp(value);
                if (clamped != value)
                    MoveScroll(state, clamped, transition);
            }
        }

        // Resolves what was asked of a scroll container, at the end of a pass that laid it out: springing for the
        // change it was asked in when this is that change's pass, and at once otherwise (asked outside Animate, or in an
        // update that threw). A ScrollTo whose descendant is not laid out inside it (Display None, or under a root of
        // its own) is dropped. Returns whether it resolved one.
        private static bool ResolveRequest(NodeState state, LayoutTransition transition)
        {
            var scroll = state.Scroll;
            var kind = scroll.Request;
            var requested = scroll.RequestOffset;
            var descendant = scroll.RequestTarget;
            float anchor = scroll.RequestAnchor;
            var move = scroll.RequestTransition != null && scroll.RequestTransition == transition ? transition : null;
            ClearRequest(scroll);

            Vector2 offset;
            if (kind == ScrollRequest.Offset)
            {
                offset = scroll.Clamp(requested);
            }
            else if (descendant == null || !TryScrollTarget(state, descendant, anchor, out offset))
            {
                if (descendant != null)
                    WarnScrollTo(state.Node, descendant);
                return false;
            }
            MoveScroll(state, offset, move);
            return true;
        }

        // Where a scroll container scrolls to so that `descendant` sits `anchor` of the way along what it shows: the
        // descendant's target rect in the container's content space (its target top-left, centre - size / 2, and each
        // of its layout parents' up to the container's child, each in its own parent's layout space, added up), then
        // on each axis it scrolls, that start less anchor of the room around it (the container's size less the
        // descendant's), within range. False when it is not laid out inside it: not placed yet, out of layout, or
        // under a root of its own.
        private static bool TryScrollTarget(NodeState container, LayoutNode descendant, float anchor, out Vector2 offset)
        {
            offset = default;
            if (!s_states.TryGetValue(descendant, out var target)) return false;
            var start = Vector2.zero;
            for (var state = target; state != container; state = state.Parent)
            {
                if (state == null || !state.Seen || state.PassIndex < 0) return false;
                start += state.Position.Target - state.Size.Target * 0.5f;
            }
            var scroll = container.Scroll;
            offset = scroll.Clamp(start - anchor * (scroll.Viewport - target.Size.Target));
            return true;
        }

        // Keeps what was asked of a scroll container for a pass to resolve (the latest ask wins), with the change it
        // was asked in, if any.
        private static void RequestScroll(ScrollState scroll, ScrollRequest kind, Vector2 offset, LayoutNode descendant, float anchor)
        {
            scroll.Request = kind;
            scroll.RequestOffset = offset;
            scroll.RequestTarget = descendant;
            scroll.RequestAnchor = anchor;
            scroll.RequestTransition = s_current;
        }

        private static void ClearRequest(ScrollState scroll)
        {
            scroll.Request = ScrollRequest.None;
            scroll.RequestTarget = null;
            scroll.RequestTransition = null;
        }

        private static void WarnScrollTo(LayoutNode node, LayoutNode descendant)
        {
            if (s_warnedScrollTo) return;
            s_warnedScrollTo = true;
            Debug.LogWarning($"{node.name}.ScrollTo({descendant.name}): {descendant.name} is not laid out inside {node.name}, so it is not scrolled to. (Said once.)", node);
        }

        // Scrolls a container to `offset`: at once without a transition; with one, on the node's own spring (its
        // Animation's duration, bounce and delay), from where it is drawn at the velocity it has, held by that change.
        private static void MoveScroll(NodeState state, Vector2 offset, LayoutTransition transition)
        {
            var scroll = state.Scroll;
            if (transition == null)
            {
                StopScrollAt(scroll, offset);
                return;
            }
            var animation = state.Node.Animation;
            Spring.Parameters(animation, out float omega, out float zeta);
            SpringScroll(scroll, offset, omega, zeta, Mathf.Max(0f, animation.Delay), transition);
        }

        // Sets a scroll springing to `target` on the spring given, from where it is drawn at the velocity it has, held
        // by `transition` (or by none, letting go of any it was held by), and letting go of any press, glide or wheel.
        // At rest there already, there is nothing to move.
        private static void SpringScroll(ScrollState scroll, Vector2 target, float omega, float zeta, float delay, LayoutTransition transition)
        {
            var offset = scroll.Offset;
            scroll.Press = null;
            scroll.Dragged = false;
            scroll.GlideX = scroll.GlideY = false;
            scroll.Wheeling = false;
            offset.Target = scroll.OnAxes(target);
            if (!offset.Moving && offset.Value == offset.Target)
            {
                offset.Value = offset.Target;
                return;
            }
            offset.Omega = omega;
            offset.Zeta = zeta;
            offset.Delay = delay;
            Hold(offset, transition);
            scroll.Phase = ScrollPhase.Springing;
        }

        // Puts a scroll at `offset` at once, at rest, letting go of any press, glide or wheel, and of the change it was
        // springing for.
        private static void StopScrollAt(ScrollState scroll, Vector2 offset)
        {
            var spring = scroll.Offset;
            spring.Target = scroll.OnAxes(offset);
            Stop(spring);
            scroll.Phase = ScrollPhase.Idle;
            scroll.Press = null;
            scroll.Dragged = false;
            scroll.GlideX = scroll.GlideY = false;
            scroll.Wheeling = false;
        }

        // A press takes hold of a scroll: it stops where it is drawn (letting go of the change it was springing for)
        // and is held, following the press's drag once one sets off.
        private static void TakeHold(ScrollState scroll, PointerEventData press)
        {
            var spring = scroll.Offset;
            Release(spring);
            spring.Target = spring.Value;
            spring.Velocity = Vector2.zero;
            spring.Delay = 0f;
            spring.Moving = true;
            scroll.Phase = ScrollPhase.Dragging;
            scroll.Press = press;
            scroll.Dragged = false;
            scroll.GlideX = scroll.GlideY = false;
            scroll.Wheeling = false;
        }

        // The press holding a scroll lets go of it at `velocity`: out of range it springs back to the end it is past (0.4
        // seconds, no bounce), carrying that velocity; in range it glides on at it, to a stop, or on into an end that it
        // then springs back from. Let go of at a standstill in range, it stays where it is.
        private static void LetGo(ScrollState scroll, Vector2 velocity)
        {
            var spring = scroll.Offset;
            scroll.Press = null;
            scroll.Dragged = false;
            spring.Velocity = scroll.OnAxes(velocity);
            spring.Target = scroll.Clamp(spring.Value);
            spring.Omega = ScrollState.BounceOmega;
            spring.Zeta = 1f;
            spring.Delay = 0f;
            scroll.GlideX = scroll.Scrolls(0) && spring.Target.x == spring.Value.x;
            scroll.GlideY = scroll.Scrolls(1) && spring.Target.y == spring.Value.y;
            scroll.Phase = scroll.GlideX || scroll.GlideY ? ScrollPhase.Gliding : ScrollPhase.Springing;
        }

        // Moves a scroll container's offset on, once a frame in play mode, as Advance does a spring: a glide or a spring
        // is stepped (bar the frame it set off from rest in) and put where it is going, at rest, once it is there. A press
        // lets go of it in OnEndDrag (having dragged) or OnScrollRelease (having only stopped it); one whose node went
        // lets go here. The press itself is not read for whether it is over: the Input System's UI module shares one
        // event among a mouse's buttons and overwrites it every frame the mouse moves. Outside play mode it is at the
        // start. A changed offset is queued for Scrolled.
        private static void StepScroll(NodeState state, bool playing, bool step, float dt)
        {
            var scroll = state.Scroll;
            if (!playing)
            {
                if (scroll.Phase != ScrollPhase.Idle || scroll.Offset.Value != Vector2.zero)
                    StopScrollAt(scroll, Vector2.zero);
            }
            else
            {
                if (scroll.Phase == ScrollPhase.Dragging && scroll.Press == null)
                    LetGo(scroll, Vector2.zero);
                if ((scroll.Phase == ScrollPhase.Gliding || scroll.Phase == ScrollPhase.Springing)
                    && step && scroll.Offset.SetOff != Time.frameCount && scroll.Step(dt))
                    StopScrollAt(scroll, scroll.Offset.Target);
            }
            if (scroll.Offset.Value != scroll.Raised)
                QueueScrolled(state);
        }

        private static void QueueScrolled(NodeState state)
        {
            var scroll = state.Scroll;
            if (scroll.Queued) return;
            scroll.Queued = true;
            s_scrolled.Add(state);
        }

        // Raises Scrolled for each scroll container whose offset has changed since it was last raised, one at a time
        // and each taken off the list first (a handler may scroll another). Outside play mode nothing is raised: an
        // offset going back to the start there is play mode's leaving.
        private static void RaiseScrolled()
        {
            while (s_scrolled.Count > 0)
            {
                var state = s_scrolled[0];
                s_scrolled.RemoveAt(0);
                var scroll = state.Scroll;
                scroll.Queued = false;
                var offset = scroll.Axis != ScrollAxis.None ? scroll.Offset.Value : Vector2.zero;
                if (offset == scroll.Raised) continue;
                scroll.Raised = offset;
                if (Application.isPlaying && state.Node != null)
                    state.Node.RaiseScrolled(offset);
            }
        }

        // ── For LayoutScroller ───────────────────────────────────────────────────

        // A press on a scroll container, or on anything in it that does not take drags itself, sent before any drag:
        // gliding from a flick, or springing back from an end, it stops where it is drawn, as touching a UIScrollView
        // stops it. (A wheel's step or a scroll sent by ScrollTo is left to finish, and the press clicks as any would:
        // stopping it would only swallow a click made just after.) A press that stops it is only that: a button under
        // it does not click. The press becomes the container's own, so its release comes back to it (UGUI sends a
        // pointer-up only to what took the press), and whatever took it first is let go of at once. It then holds the
        // offset until it drags it, or lets go (OnScrollRelease): in range it stays there, past an end it springs back.
        // Only the left button (and touch) scrolls, as with ScrollRect.
        internal static void OnScrollPress(LayoutNode node, PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !TryScrolling(node, out var state)) return;
            var scroll = state.Scroll;
            bool flicked = scroll.Phase == ScrollPhase.Gliding
                || (scroll.Phase == ScrollPhase.Springing && !scroll.Wheeling && scroll.Offset.Transition == null);
            if (!flicked) return;
            TakeHold(scroll, eventData);
            eventData.eligibleForClick = false;
            var self = node.gameObject;
            if (eventData.pointerPress != self)
            {
                if (eventData.pointerPress != null)
                    ExecuteEvents.Execute(eventData.pointerPress, eventData, ExecuteEvents.pointerUpHandler);
                eventData.pointerPress = self;
            }
            FlushIfIdle();
        }

        // The press let go. One that dragged lets go in OnEndDrag, sent after this, at the velocity it was dragged at;
        // one that stopped a glide and never dragged lets go here, at a standstill: in range it stays, past an end it
        // springs back.
        internal static void OnScrollRelease(LayoutNode node, PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !TryScrolling(node, out var state)) return;
            var scroll = state.Scroll;
            if (scroll.Phase != ScrollPhase.Dragging || scroll.Press != eventData || scroll.Dragged) return;
            LetGo(scroll, Vector2.zero);
            FlushIfIdle();
        }

        // A drag setting off, past UGUI's drag threshold. Unless another press is dragging the container already, or
        // this one sets off mostly along an axis it does not scroll (left to nothing, for now), the container follows
        // it from where the pointer is now (not from the press, so it does not jump by the threshold), stopping
        // whatever else it was doing.
        internal static void OnScrollBeginDrag(LayoutNode node, PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !TryScrolling(node, out var state)) return;
            var scroll = state.Scroll;
            if (scroll.Phase == ScrollPhase.Dragging && scroll.Dragged && scroll.Press != eventData) return;
            var camera = eventData.pressEventCamera;
            if (!LocalPoint(state, eventData.position, camera, out var cursor)
                || !LocalPoint(state, eventData.pressPosition, camera, out var pressed))
                return;
            var moved = cursor - pressed;
            if (scroll.Axis == ScrollAxis.Vertical && Mathf.Abs(moved.x) > Mathf.Abs(moved.y)) return;
            if (scroll.Axis == ScrollAxis.Horizontal && Mathf.Abs(moved.y) > Mathf.Abs(moved.x)) return;
            if (scroll.Phase != ScrollPhase.Dragging || scroll.Press != eventData)
                TakeHold(scroll, eventData);
            scroll.BeginDrag(cursor, Time.unscaledTime);
            FlushIfIdle();
        }

        // The pointer dragging it moved: the offset follows, the pointer's movement brought into its local units.
        internal static void OnScrollDrag(LayoutNode node, PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !TryScrolling(node, out var state)) return;
            var scroll = state.Scroll;
            if (scroll.Phase != ScrollPhase.Dragging || !scroll.Dragged || scroll.Press != eventData) return;
            if (LocalPoint(state, eventData.position, eventData.pressEventCamera, out var cursor))
                scroll.Drag(cursor, Time.unscaledTime);
        }

        // The drag let go: it glides or springs on at the velocity it was dragged at (none if held still first).
        internal static void OnScrollEndDrag(LayoutNode node, PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !TryScrolling(node, out var state)) return;
            var scroll = state.Scroll;
            if (scroll.Phase != ScrollPhase.Dragging || scroll.Press != eventData) return;
            LetGo(scroll, scroll.ReleaseVelocity(Time.unscaledTime));
        }

        // A mouse wheel or trackpad: a notch scrolls the wheel step (positive y towards the start, as ScrollRect reads
        // it), added to the wheel's own target while it is still springing there (so notches add up rather than each
        // starting from where it is drawn), within range, on a quick spring from where it is at the speed it has. A
        // vertical container takes y; a horizontal one x, or y when there is no x (a plain wheel scrolls it too); one
        // that scrolls both takes both. The Input System's UI module scales scrollDelta by its scrollDeltaPerTick (6 a
        // notch by default) and the legacy one does not (1 a notch), so it is brought back to notches by the module's
        // own ConvertPointerEventScrollDeltaToTicks: a notch is the same step whichever is in use.
        internal static void OnScrollWheel(LayoutNode node, PointerEventData eventData)
        {
            if (!TryScrolling(node, out var state)) return;
            var scroll = state.Scroll;
            if (scroll.Phase == ScrollPhase.Dragging || !scroll.Measured) return;

            var module = eventData.currentInputModule;
            var ticks = module != null ? module.ConvertPointerEventScrollDeltaToTicks(eventData.scrollDelta) : eventData.scrollDelta;
            Vector2 delta;
            switch (scroll.Axis)
            {
                case ScrollAxis.Vertical:
                    delta = new Vector2(0f, -ticks.y);
                    break;
                case ScrollAxis.Horizontal:
                    delta = new Vector2(-(ticks.x != 0f ? ticks.x : ticks.y), 0f);
                    break;
                default:
                    delta = -ticks;
                    break;
            }
            if (delta == Vector2.zero) return;

            var origin = scroll.Phase == ScrollPhase.Springing && scroll.Wheeling ? scroll.WheelTarget : scroll.Offset.Value;
            var target = scroll.Clamp(origin + delta * ScrollState.WheelStep);
            SpringScroll(scroll, target, ScrollState.WheelOmega, 1f, 0f, null);
            if (scroll.Phase == ScrollPhase.Springing)
            {
                scroll.Wheeling = true;
                scroll.WheelTarget = target;
            }
            FlushIfIdle();
        }

        // The node's scroll state, in play mode, when it is a scroll container.
        private static bool TryScrolling(LayoutNode node, out NodeState state)
        {
            state = null;
            return Application.isPlaying && node != null && s_states.TryGetValue(node, out state)
                && state.Scroll != null && state.Scroll.Axis != ScrollAxis.None;
        }

        // A screen point in a node's local space (y up), through the camera the press was seen by (none for a Screen
        // Space Overlay canvas). False when it misses the node's plane.
        private static bool LocalPoint(NodeState state, Vector2 screen, Camera camera, out Vector2 local) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(state.RectTransform, screen, camera, out local);

        // ── Spaces ───────────────────────────────────────────────────────────────

        // How far a node scrolls what is in it: its scroll offset as drawn, or nothing for one that does not scroll
        // (or for no node, a root's parent).
        private static Vector2 ScrolledBy(NodeState state)
        {
            var scroll = state?.Scroll;
            return scroll != null && scroll.Axis != ScrollAxis.None ? scroll.Offset.Value : Vector2.zero;
        }

        // The RectTransform whose layout space a node's position is in (null for a root, or when that is gone).
        private static RectTransform SpaceOf(NodeState state)
        {
            var parent = state.Parent;
            if (parent == null) return null;
            var space = parent.RectTransform;
            return space != null ? space : null;
        }

        // Where a rect is drawn now, as a centre and size in `space`'s layout space.
        private static void DrawnIn(RectTransform rect, RectTransform space, out Vector2 centre, out Vector2 size)
        {
            rect.GetWorldCorners(s_corners);
            Vector2 min = space.InverseTransformPoint(s_corners[0]);
            Vector2 max = space.InverseTransformPoint(s_corners[2]);
            var bounds = space.rect;
            var middle = (min + max) * 0.5f;
            centre = new Vector2(middle.x - bounds.xMin, bounds.yMax - middle.y);
            size = new Vector2(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y));
        }

        // A point in one layout space (origin at the rect's top-left, y down), in another, through world space.
        private static Vector2 PointTo(RectTransform from, RectTransform to, Vector2 point)
        {
            var fromRect = from.rect;
            var world = from.TransformPoint(new Vector3(fromRect.xMin + point.x, fromRect.yMax - point.y, 0f));
            Vector2 local = to.InverseTransformPoint(world);
            var toRect = to.rect;
            return new Vector2(local.x - toRect.xMin, toRect.yMax - local.y);
        }

        // A velocity in one layout space (y down), in another, through world space.
        private static Vector2 VectorTo(RectTransform from, RectTransform to, Vector2 vector)
        {
            Vector2 local = to.InverseTransformVector(from.TransformVector(new Vector3(vector.x, -vector.y, 0f)));
            return new Vector2(local.x, -local.y);
        }
    }
}
