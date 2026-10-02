using System;
using System.Collections.Generic;
using TimboJimbo.Motion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static TimboJimbo.Motion.MotionSystem;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// Lays out every <see cref="LayoutNode"/> each frame and moves them to where they go. A change made in
    /// <see cref="MotionSystem.Animate(MotionAnimation, Action, string[])"/> moves the nodes it gives somewhere new on
    /// springs, from where they are drawn and at the velocity they have, on the change's animation or a node's own
    /// (<see cref="LayoutNode.Animation"/>, which covers what is inside it); any other change puts them there at once. A
    /// node that scrolls moves its children together by its scroll offset, which drags, flicks, the wheel and ScrollTo
    /// drive.
    /// </summary>
    public static partial class LayoutSystem
    {
        // Once a frame, just before canvases are drawn (play mode and edit mode alike), Motion's frame (MotionSystem)
        // has every tree laid out from its root: the solver says where each node goes, which becomes its targets; the
        // springs of what is moving are stepped; and where each node is drawn is written into its RectTransform and
        // CanvasGroup. Outside Animate a target that changes is taken up at once, so a drag moving a node's Offset
        // follows the pointer exactly; Animate has the system lay out before and after its update, and what the update
        // changed sets off on springs, held by the change (the Driver below). A node moves on its own Animation, or on
        // that of the nearest node above it with one (PassAnimation, handed down as a pass visits), or else on the
        // change's (AnimationOf). In edit mode nothing animates.
        //
        // An outermost root keeps its content clear of the screen's safe area on the edges its SafeArea names, read
        // each pass: the solver adds the part of the unsafe area it covers to its padding. A node ignoring the safe area
        // on an edge (IgnoresSafeArea) reaches back out past it to the root's edge where it lies against it, its padding
        // growing by as much, so what is inside stays clear. That reach (a root's being its safe area) is kept on the
        // node, and what it scrolls comes to rest, snaps and draws its indicators clear of it, as a UIScrollView adjusts
        // its insets for the safe area.
        //
        // A node whose Scroll is not None is a scroll container, as a Clay scroll container or a UIScrollView: the
        // solver lets its children run past its edge the ways it scrolls, a RectMask2D clips them, and its LayoutNode
        // children (in its flow or floating) are drawn moved together by its scroll offset. The offset only moves
        // where they are drawn: their springs, targets and layout never see it, and anything that goes through where
        // they are drawn (reparenting, a node first met out of layout) takes it off and puts it back. Children that are
        // plain objects are not moved by it. The offset is the engine's, with a velocity, in phases: held by a press
        // (moved by its share of the drag, rubber-banding past the ends), gliding once flicked (at UIScrollView's
        // deceleration rate), or springing (back to an end, to a wheel's target, where ScrollTo, ScrollIntoView or
        // ScrollOffset sent it, or on to its end as its content grows under a ScrollAnchor of End, on the node's own
        // spring inside Animate); it is stepped with the springs, once a frame in play mode. One that snaps (ScrollSnap)
        // springs to a page or a child rather than gliding when it is let go of. While it scrolls it draws thin indicators
        // along its edges, on objects of their own inside it. A LayoutScroller the system adds takes the pointer for it,
        // and for a node with a drag owner (ILayoutDraggable: a sheet's height, a card's pull), and a drag is shared among
        // the containers and owners up the hierarchy from the press (see Dragging). In edit mode the offset is always at
        // the start.
        //
        // A node floating against an element (Floating.Element, in the same tree) is sized and placed against the
        // element's laid-out rect by the solver; its target then also takes in how the element is drawn apart from it:
        // the Offsets of the element and of what the element is inside, and the scroll offsets of the containers the
        // element is inside, bar those the node is inside too (and the other way round), and how far any of those that
        // floats against an element in turn is moved on by that. It is the one target that takes scroll offsets in, so
        // that a change of element springs it from where it is drawn inside Animate (and outside, puts it there at
        // rest) as any change does, while scrolling moves it each frame, the offsets stepped that frame taken up before
        // it is drawn.
        //
        // Showing and hiding is retargeting too. Each node has a shown spring, 1 while its own Display is Visible and 0
        // otherwise, which only how it is drawn goes by: its DisplayEffect shrinks it by it, and slides it by its away
        // move (just past its parent's rect as drawn, on the effect's edge) times 1 - shown; its opacity spring fades
        // it, and never bounces. Scale and Opacity are targets, as Offset is one of its position. Inside Animate the
        // topmost node that starts being drawn is put at its away pose and sets off from there, and the topmost that
        // stops is drawn, on its way out (Leaving), until its springs have come to rest, the change holding them all,
        // so it finishes only once the node has gone; what is inside either comes with it. A node hidden while moving
        // from a fling is thrown on the way it was going. The away move is never a target: when it changes while a
        // node is partly shown, the drawn part of the change goes into its position's presentation, so where it is
        // drawn never jumps.
        //
        // A node moved to another layout parent inside Animate flies (LayoutSystem.Flight.cs): from the moment it sets
        // off until its position and size come to rest, it is drawn above everything in its root canvas and out of
        // every clip above it, so neither the clip it leaves nor the one it goes into cuts it on its way, and it is cut
        // again only as it lands. Only how it is drawn changes: its springs stay in its parent's space, so it moves as
        // it would anyway, and what is inside it comes with it. A change's flights board together once its pass is
        // over, ranked by where they drew before it, above what is flying already; one inside another is drawn above
        // that one. A flight hidden, or under something hidden, drops out at once and goes with what hid it.
        //
        // Names hand a presentation over (LayoutSystem.Match.cs). A node that starts being shown inside Animate under
        // the MatchName and id another stops being shown under takes over from where that one is drawn, and at the
        // velocity it is drawn moving at, then moves to its own place on its own springs, fading in. The other follows
        // it, drawn at its rect beneath it and fading out from halfway, until the pair lands. Both fly, ignoring their
        // parent groups. When the other stays shown instead, the one shown or hidden grows out of it or shrinks back into
        // it, that one's rect its away pose, flying until its effect has played.

        // What the system drives on a node's RectTransform, which the editor then shows as driven and does not save.
        private const DrivenTransformProperties Driven = DrivenTransformProperties.Anchors | DrivenTransformProperties.Pivot
            | DrivenTransformProperties.AnchoredPosition | DrivenTransformProperties.SizeDelta | DrivenTransformProperties.Scale;

        // What the components the system adds to a scroll container are: not shown in the inspector, and never saved
        // (in the scene or a build), so a scene is the same with or without them.
        private const HideFlags AddedFlags = HideFlags.DontSave | HideFlags.HideInInspector;

        // Every enabled node, and what is kept for it.
        private static readonly Dictionary<LayoutNode, NodeState> s_states = new();

        // Scroll containers whose offset has changed since Scrolled was last raised for them, raised once the frame is
        // drawn (a handler may scroll or lay out something else).
        private static readonly List<NodeState> s_scrolled = new();

        // Nodes whose shown value as drawn has changed since ShownChanged was last raised for them, raised once the
        // frame is laid out, in the order they were written (a handler may call Animate). A node unregistered while it
        // waits leaves a null in its place.
        private static readonly List<NodeState> s_shown = new();

        // ScrollTo or ScrollIntoView was asked for something not laid out inside the container: said once.
        private static bool s_warnedScrollTo;

        // Nodes whose Floating.Element is a mistake (in another tree, or circular), each said once.
        private static readonly HashSet<LayoutNode> s_warnedElement = new();

        // A pass's scratch: the roots, outer before inner; one tree's nodes in pre-order, out of layout ones included;
        // and its solver nodes, the first s_count of them in use, with the node each is (to find an element's).
        private static readonly List<NodeState> s_roots = new();
        private static readonly List<NodeState> s_visit = new();
        private static SolverNode[] s_solver = new SolverNode[64];
        private static NodeState[] s_solved = new NodeState[64];
        private static int s_count;
        private static readonly Vector3[] s_corners = new Vector3[4];

        // A pass is under way (a canvas update forced from inside one must not start another).
        private static bool s_passing;
        // Whether the system is Motion's driver yet.
        private static bool s_hooked;

        // Statics survive play mode sessions when domain reload is off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_passing = false;
            foreach (var state in s_scrolled)
                state.Scroll.Queued = false;
            s_scrolled.Clear();
            foreach (var state in s_shown)
            {
                if (state != null)
                    state.ShownQueued = false;
            }
            s_shown.Clear();
            s_warnedScrollTo = false;
            s_warnedElement.Clear();
            s_drags.Clear();
            s_dragPool.Clear();
            s_partPool.Clear();
            s_chain.Clear();
            s_left.Clear();
            s_reach.Clear();
            s_handlers.Clear();
            s_impacts.Clear();
            ResetFlights();
            ResetMatch();
            s_roots.Clear();
            s_visit.Clear();
            s_count = 0;
            Array.Clear(s_solver, 0, s_solver.Length);
            Array.Clear(s_solved, 0, s_solved.Length);
            // Nodes register and unregister themselves as they are enabled and disabled, so the set keeps itself; any
            // an earlier session left behind that are gone are dropped. Motion keeps its drivers as long as the statics
            // last, so the system stays one.
            var gone = new List<LayoutNode>();
            foreach (var node in s_states.Keys)
            {
                if (node == null)
                    gone.Add(node);
            }
            foreach (var node in gone)
                s_states.Remove(node);
        }

        // ── Motion ───────────────────────────────────────────────────────────────

        /// <summary>
        /// The animation <paramref name="node"/> moves on in the change being made (<see cref="MotionSystem.Current"/>):
        /// its own <see cref="LayoutNode.Animation"/>, or else that of the nearest node above it with one, or else the
        /// change's (<see cref="MotionAnimation.Default"/> outside one). For moving something drawn in the node with it
        /// (<see cref="MotionSystem.AnimateValue"/>), so it keeps time with the node.
        /// </summary>
        public static MotionAnimation AnimationOf(LayoutNode node)
        {
            if (node != null)
            {
                if (node.Animation is { } own) return own;
                // Up the enabled nodes above it, its layout parents and on through the node above each root, as a pass
                // hands PassAnimation down.
                for (var parent = node.transform.parent; parent != null; parent = parent.parent)
                {
                    if (parent.TryGetComponent(out LayoutNode above) && above.isActiveAndEnabled && above.Animation is { } animation)
                        return animation;
                }
            }
            return Current?.Animation ?? MotionAnimation.Default;
        }

        // The animation a node moves on for `transition` (null for none, as a fling): its own, or that of the nearest node
        // above it with one, as the pass found them (PassAnimation), or else the change's, or else the default.
        private static MotionAnimation AnimationOf(NodeState state, MotionTransition transition) =>
            state.PassAnimation ?? transition?.Animation ?? MotionAnimation.Default;

        // The system's part in Motion's changes and frame (MotionSystem.Animate, and Motion's frame just before canvases
        // are drawn). Before a change's update it lays out, so what changed before goes where it goes at once and only
        // what the update changes animates; once the update has run it lays out again, what changed setting off on
        // springs held by the change. Each frame it lays out, steps and draws every node; and it puts what a skipped
        // change moves where it is going.
        private sealed class Driver : IMotionDriver
        {
            public static readonly Driver Instance = new();

            public bool Busy => s_passing;

            public void BeforeChange() => Pass(null);

            public void AfterChange(MotionTransition transition)
            {
                Pass(transition);

                // What the change started and stopped showing under one name pairs up, before anything boards: a pair
                // boards with the change's other flights, and a node made a follower here is then not dropped out below
                // for being hidden.
                MatchPairs(transition);

                // What the change moved to another parent flies, a flight it hid drops out, and a follower it showed
                // again without a new pair stops following, coming back with the change.
                Embark();
                DropOut(transition);
            }

            // A drag whose LayoutScroller went hears nothing more from UGUI: it lets go before the frame is laid out.
            public void BeginFrame(bool playing)
            {
                if (playing)
                    EndLostDrags();
            }

            public void Frame(bool playing, bool step, float dt) => LayoutSystem.Frame(playing, step, dt);

            // A glide that ran into an end with something above it to take its speed hands it on now the frame is laid
            // out (an owner's code may start a change); what moved is then laid out and drawn this frame too.
            public bool HandOn() => LayoutSystem.HandOn();

            // ShownChanged and then Scrolled come last, with how each node is drawn this frame. What their handlers do to
            // graphics, or to transforms that are not nodes, is drawn this frame, canvases not being drawn yet; what they
            // change in layout (a label showing the offset) is laid out with the next frame: these move every frame, and
            // laying everything out twice for each of those is not worth one frame sooner.
            public void EndFrame()
            {
                RaiseShownChanged();
                RaiseScrolled();
            }

            public void Skip(MotionTransition transition) => SkipNodes(transition);
        }

        // UIScrollView's rubber band coefficient: how much of a pull past an end is drawn at first, before the band
        // stiffens (RubberBand).
        internal const float RubberBandCoefficient = 0.55f;

        /// <summary>
        /// How far something pulled <paramref name="pull"/> past an end is drawn past it: UIScrollView's rubber band,
        /// (1 - 1 / (pull x 0.55 / limit + 1)) x limit, which gives less the further it is pulled and never reaches
        /// <paramref name="limit"/>. Scroll containers band by it past their ends, their size that way the limit; a drag
        /// owner that bands itself (<see cref="ILayoutDraggable"/>: a sheet past its highest detent) can take it for the
        /// same feel. The pull's sign is kept, and with no limit nothing gives.
        /// </summary>
        public static float RubberBand(float pull, float limit)
        {
            if (limit <= 0f) return 0f;
            float over = Mathf.Abs(pull);
            return Mathf.Sign(pull) * (1f - 1f / (over * RubberBandCoefficient / limit + 1f)) * limit;
        }

        /// <summary>
        /// <see cref="RubberBand"/> undone: the pull that draws something <paramref name="stretched"/> past an end, for a
        /// drag that takes hold of it there to carry on from that pull without a jump, limit / 0.55 x s / (limit - s). A
        /// spring can carry something further past an end than a pull could draw it (which never reaches the limit), so s
        /// is kept just short of the limit. The sign is kept, and with no limit there is no pull.
        /// </summary>
        public static float RubberBandInverse(float stretched, float limit)
        {
            if (limit <= 0f) return 0f;
            float over = Mathf.Min(Mathf.Abs(stretched), limit * 0.99f);
            return Mathf.Sign(stretched) * (limit / RubberBandCoefficient * over / (limit - over));
        }

        // ── For LayoutNode ───────────────────────────────────────────────────────

        internal static void Register(LayoutNode node)
        {
            if (!s_states.ContainsKey(node))
                s_states.Add(node, new NodeState(node));
            Hook();
            Changed(node);
        }

        // From the first node registered on, the system lays out in Motion's frame and takes part in its changes.
        private static void Hook()
        {
            if (s_hooked) return;
            s_hooked = true;
            AddDriver(Driver.Instance);
        }

        // Disabled or destroyed, a node is not drawn, so nothing waits for it: it lets go of the transitions it was
        // moving for (which did not complete), of its RectTransform, of the node it follows or that follow it, and of
        // the flight layer. Enabled again, it is met afresh, as a node seen for the first time.
        internal static void Unregister(LayoutNode node)
        {
            if (s_states.TryGetValue(node, out var state))
            {
                s_states.Remove(node);
                Stop(state.Position);
                Stop(state.Size);
                Stop(state.Opacity);
                Stop(state.Scale);
                Stop(state.Shown);
                if (state.ShownQueued)
                {
                    state.ShownQueued = false;
                    s_shown[s_shown.IndexOf(state)] = null;
                }
                Disown(state);
                Unpair(state);
                StandDown(state);
                // No longer sized by layout: its content is drawn at its rect again.
                if (node.TryGetComponent(out ILayoutMeasurable content))
                    content.Arrange(Unarranged);
                // No longer shown or hidden by the system: drawn and clickable, as it would be without it.
                if (state.Group != null)
                {
                    if (state.Group.alpha != 1f) state.Group.alpha = 1f;
                    if (!state.Group.blocksRaycasts) state.Group.blocksRaycasts = true;
                }
                // Nor scrolled nor dragged: what it added stops clipping and taking the pointer (met again, it takes
                // them back), and its indicators go. A drag it was taking part in passes it over from now on; one whose
                // LayoutScroller this was ends in the next frame.
                if (state.Scroller != null) state.Scroller.enabled = false;
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
                    HideIndicators(scroll);
                }
            }
            Changed(node);
        }

        // Something about the node changed; outside play mode, the editor should draw it again.
        internal static void Changed(LayoutNode node)
        {
            // Changed inside Animate, a caught node is let go of: the change moves it on from where it was stopped (a
            // drag letting go puts its Offset back, and whatever else it held sets off again with it).
            if (Current != null && s_states.TryGetValue(node, out var state))
                LetGoOfHolds(state);
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

        // Its position's velocity with its slide's, from its parent's layout space (y down) into world units a second.
        internal static Vector3 VelocityOf(LayoutNode node)
        {
            if (!s_states.TryGetValue(node, out var state) || (!state.Position.Moving && !state.Shown.Moving))
                return Vector3.zero;
            var space = SpaceOf(state);
            if (space == null) return Vector3.zero;
            var velocity = (state.Position.Moving ? state.Position.Velocity : Vector2.zero) + SlideVelocityOf(state);
            return space.TransformVector(new Vector3(velocity.x, -velocity.y, 0f));
        }

        // How fast its width and height are growing, from its parent's layout units into world units a second.
        internal static Vector2 SizeVelocityOf(LayoutNode node)
        {
            if (!s_states.TryGetValue(node, out var state) || !state.Size.Moving) return Vector2.zero;
            var space = SpaceOf(state);
            return space != null ? Vector2.Scale(state.Size.Velocity, UnitOf(space)) : Vector2.zero;
        }

        // Sets its position moving at `velocity` (world units a second) towards where layout puts it, on its animation
        // (its own or one it inherits, the default without either: there is no change); not for any transition, and
        // with no sideways kick. Given a `sizeVelocity` (how fast its width and
        // height are growing, world units a second), its size is set moving at it in the same way; left at zero, its
        // size is left as it is. An Animate after it sets off at those velocities.
        internal static void Fling(LayoutNode node, Vector3 velocity, Vector2 sizeVelocity)
        {
            if (!Application.isPlaying || !s_states.TryGetValue(node, out var state)) return;

            // What moved it since the last pass (a drag's last move, most likely in this same frame) goes where it
            // goes at once first, as the frame's own pass would, rather than stopping the fling there. Inside
            // Animate's update, that update's pass takes it up, keeping the velocity.
            if (Current == null)
                Pass(null);

            var space = SpaceOf(state);
            if (space == null) return;
            var spring = state.Position;
            Vector2 local = space.InverseTransformVector(velocity);
            Spring.Parameters(AnimationOf(state, null), out spring.Omega, out spring.Zeta);
            spring.Velocity = new Vector2(local.x, -local.y);
            spring.Delay = 0f;
            Hold(spring, null);
            state.Flung = true;

            if (sizeVelocity != Vector2.zero)
            {
                var size = state.Size;
                var unit = UnitOf(space);
                Spring.Parameters(AnimationOf(state, null), out size.Omega, out size.Zeta);
                size.Velocity = new Vector2(unit.x > 0f ? sizeVelocity.x / unit.x : 0f, unit.y > 0f ? sizeVelocity.y / unit.y : 0f);
                size.Delay = 0f;
                Hold(size, null);
            }
            FlushIfIdle();
        }

        // Stops it where it is drawn. Its position stops by its Offset: that moves by how far it is drawn from where it
        // was going (y up), so its target is where it is, and the next pass reads that back as no change. Its size,
        // opacity, scale and shown value stop too, each held where it stopped (below), and with any of them everything
        // inside it; its away move stays as it is while its shown value is held, so a drag on a node caught sliding in
        // follows the pointer exactly. What they were moving for is let go of on its way, and does not complete. A node
        // on its way out is not caught (it takes no pointer, and carries on leaving), nor is one following the node that
        // took over from it by name (it is drawn at that one). Returns whether any of them was moving.
        internal static bool Catch(LayoutNode node)
        {
            if (!s_states.TryGetValue(node, out var state) || state.Parent == null || state.Leaving || state.Follows != null)
                return false;
            // Taking over by name, it lands first, and so does every pair taking over inside it, each cross-fade
            // finished: held part-faded, a destination would stay so over its source, which is outside what is caught
            // and would fade out from under it.
            EndPairsInside(state);
            var position = state.Position;
            bool caught = position.Moving;
            if (caught)
            {
                var off = position.Value - position.Target;
                position.Target = position.Value;
                Stop(position);
                node.Offset += new Vector2(off.x, -off.y);
            }
            // The rest have no Offset to take up where they were stopped: each is held there instead (Place leaves it)
            // until the node is moved on, or given something new for that one alone. Caught again while held, what it
            // was given is still what it was.
            bool held = HoldWhereDrawn(state.Size, ref state.HoldSize, ref state.HeldSize);
            held |= HoldWhereDrawn(state.Opacity, ref state.HoldOpacity, ref state.HeldOpacity);
            held |= HoldWhereDrawn(state.Scale, ref state.HoldScale, ref state.HeldScale);
            held |= HoldWhereDrawn(state.Shown, ref state.HoldShown, ref state.HeldShown);
            if (held)
            {
                StopInside(state);
                caught = true;
            }
            if (caught)
                FlushIfIdle();
            return caught;
        }

        // Stops a moving spring where it is drawn and holds it there, noting what it was given (its target) unless it
        // is held already. Returns whether it was moving.
        private static bool HoldWhereDrawn(Spring spring, ref bool hold, ref Vector2 held)
        {
            if (!spring.Moving) return false;
            if (!hold)
            {
                hold = true;
                held = spring.Target;
            }
            StopWhereDrawn(spring);
            return true;
        }

        // Lets go of every value a catch held.
        private static void LetGoOfHolds(NodeState state)
        {
            state.HoldSize = false;
            state.HoldOpacity = false;
            state.HoldScale = false;
            state.HoldShown = false;
        }

        // Stops everything inside a node being held where it is drawn, as pausing an animation pauses all it moves: laid
        // out for the size the node was going to, it would otherwise carry on there, out of the node held short of it.
        // Place leaves it there (PassFrozen) until the node is let go of, when it moves on with it.
        private static void StopInside(NodeState state)
        {
            var transform = state.RectTransform;
            for (int i = 0; i < transform.childCount; i++)
            {
                if (!transform.GetChild(i).TryGetComponent(out LayoutNode child) || !child.isActiveAndEnabled
                    || !s_states.TryGetValue(child, out var childState))
                    continue;
                StopWhereDrawn(childState.Position);
                StopWhereDrawn(childState.Size);
                StopWhereDrawn(childState.Opacity);
                StopWhereDrawn(childState.Scale);
                StopWhereDrawn(childState.Shown);
                StopInside(childState);
            }
        }

        // Stops a moving spring where it is, which is now where it goes.
        private static void StopWhereDrawn(Spring spring)
        {
            if (!spring.Moving) return;
            spring.Target = spring.Value;
            Stop(spring);
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
            if (Current == null && scroll.Measured && scroll.Axis != ScrollAxis.None)
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

        // Scrolls so that `descendant`, with the space around it, sits `anchor` of the way along what it shows, within
        // range.
        internal static void ScrollTo(LayoutNode node, LayoutNode descendant, float anchor) =>
            ScrollToDescendant(node, descendant, ScrollRequest.To, anchor);

        // Scrolls only as far as brings `descendant`, with the space around it, into view, to the nearer end.
        internal static void ScrollIntoView(LayoutNode node, LayoutNode descendant) =>
            ScrollToDescendant(node, descendant, ScrollRequest.IntoView, 0f);

        // A ScrollTo (`kind` To, at `anchor`) or ScrollIntoView. Inside Animate's update, that change's pass does it (the
        // descendant may be added in the same update), on the node's spring for it. Otherwise at once when the
        // descendant has been laid out inside it, and at the end of the next pass, at once, when it has not yet.
        // Something not inside it is not scrolled to (and said so, once).
        private static void ScrollToDescendant(LayoutNode node, LayoutNode descendant, ScrollRequest kind, float anchor)
        {
            if (descendant == null) throw new ArgumentNullException(nameof(descendant));
            if (!Application.isPlaying || node.Scroll == ScrollAxis.None || !s_states.TryGetValue(node, out var state)) return;
            if (descendant == node || !descendant.transform.IsChildOf(node.transform))
            {
                WarnScrollTo(node, descendant, kind);
                return;
            }
            var scroll = state.Scroll ??= new ScrollState();
            if (Current == null && scroll.Measured && scroll.Axis != ScrollAxis.None
                && TryScrollRect(state, descendant, out var min, out var max))
            {
                ClearRequest(scroll);
                if (TryScrollOffset(scroll, state.Reach, kind, anchor, min, max, out var offset))
                {
                    StopScrollAt(scroll, offset);
                    FlushIfIdle();
                }
                return;
            }
            RequestScroll(scroll, kind, Vector2.zero, descendant, anchor);
        }

        // Puts everything the transition is moving on nodes, and the scrolls it is moving, where they are going, as if
        // they had got there (MotionTransition.Skip).
        private static void SkipNodes(MotionTransition transition)
        {
            foreach (var state in s_states.Values)
            {
                StopIfHeld(state.Position, transition);
                StopIfHeld(state.Size, transition);
                StopIfHeld(state.Opacity, transition);
                StopIfHeld(state.Scale, transition);
                StopIfHeld(state.Shown, transition);
                var scroll = state.Scroll;
                if (scroll != null && scroll.Offset.Transition == transition)
                    StopScrollAt(scroll, scroll.Offset.Target, arrived: true);
            }
        }

        // ── The frame ────────────────────────────────────────────────────────────

        // Each tree in turn, outer roots first, is laid out, stepped and drawn before the next is laid out, so a root
        // inside a node (under a plain object) lays out in that node's rect as drawn this frame. Motion's frame runs it
        // once a frame, and again without stepping when a Finished handler may have shown, made or moved nodes, or a
        // glide handed its speed on (Driver): what moved is then laid out and drawn this frame too, from where it is,
        // rather than drawn once where it was first.
        private static void Frame(bool playing, bool step, float dt)
        {
            s_passing = true;
            try
            {
                s_following.Clear();
                CollectRoots();
                for (int r = 0; r < s_roots.Count; r++)
                {
                    LayOut(s_roots[r], null);
                    // The whole tree is stepped before any of it is drawn: a scroll container's offset before its
                    // children are drawn by it, and every offset before a node floating against an element takes up
                    // those the element is drawn by, wherever they are in the tree. In pre-order, so each node's parent
                    // is stepped first: its away move follows what the parent now shows (a scroll, or its size
                    // springing) as it is, in the frame it moves, and whether it has finished leaving is known before
                    // what rides out inside it asks. A flight that has come to rest lands before it is drawn; a pair
                    // half lands with its whole pair, whichever half is found at rest last, wherever the other is.
                    for (int i = 0; i < s_visit.Count; i++)
                    {
                        var state = s_visit[i];
                        Advance(state.Position, playing, step, dt);
                        Advance(state.Size, playing, step, dt);
                        Advance(state.Opacity, playing, step, dt);
                        Advance(state.Scale, playing, step, dt);
                        Advance(state.Shown, playing, step, dt);
                        if (state.Scroll != null && state.Scroll.Axis != ScrollAxis.None)
                            StepScroll(state, playing, step, dt);
                        UpdateAway(state, false, null);
                        if (state.Leaving)
                            SettleLeaving(state);
                        state.PassLeaving = state.Leaving || (state.PassParent != null && state.PassParent.PassLeaving);
                        if (state.Flight != null && HasLanded(state))
                        {
                            if (state.Flight.PairHalf)
                                LandPair(state);
                            else
                                Land(state);
                        }
                        // Its effect has played out against the node it grew out of or shrank into: drawn by itself again.
                        if (state.Anchor != null && !state.Leaving && AnchorPlayed(state))
                            state.Anchor = null;
                        // Its size at rest where it is laid out, after taking over from a node by name: drawn as itself
                        // again, which is the picture of itself at a scale of 1.
                        if (state.Fitting && !state.Size.Moving && state.Size.Value == state.LaidOutSize)
                            state.Fitting = false;
                    }
                    for (int i = 0; i < s_visit.Count; i++)
                    {
                        var state = s_visit[i];
                        FollowElement(state);
                        // A follower is drawn at the rect of the node it follows, and an anchored node from the rect of its
                        // anchor, which may be in a tree laid out after this one: each is written once every tree has been.
                        if (state.Follows != null || state.Anchor != null)
                            s_following.Add(state);
                        else
                            Write(state);
                        if (playing)
                            QueueShownChanged(state);
                    }
                }
                // In the order they were met, outer trees first and parents before children, so one inside another
                // (an avatar in a card, each following its own) is drawn once that one has been. What they follow is
                // never a follower itself, and what they grow out of stays shown, so it has been drawn already.
                for (int i = 0; i < s_following.Count; i++)
                    Write(s_following[i]);
                s_following.Clear();
            }
            finally
            {
                s_passing = false;
            }
        }

        // Lays out every tree and gives each node its targets: at once without a transition, and with one, what
        // changed sets off on springs held by it, and who holds each name before and after the change is noted for
        // MatchPairs. Nothing is stepped or drawn: the frame does that.
        private static void Pass(MotionTransition transition)
        {
            if (s_passing) return;
            s_passing = true;
            // Noted afresh for each change: MatchPairs forgets them once it has read them, but not if this pass threw.
            s_matching = transition != null;
            if (s_matching)
                ClearKeys();
            try
            {
                CollectRoots();
                for (int r = 0; r < s_roots.Count; r++)
                    LayOut(s_roots[r], transition);
            }
            finally
            {
                s_passing = false;
                s_matching = false;
            }
        }

        // Every root, outer before inner (by depth, so an ancestor always comes first), each with the nearest enabled
        // node above it (Above), which is in a tree laid out before it.
        private static void CollectRoots()
        {
            s_roots.Clear();
            foreach (var pair in s_states)
            {
                var node = pair.Key;
                if (node == null || !IsRoot(node)) continue;
                var state = pair.Value;
                state.Depth = 0;
                state.Above = null;
                for (var t = node.transform.parent; t != null; t = t.parent)
                {
                    state.Depth++;
                    if (state.Above == null && t.TryGetComponent(out LayoutNode above) && s_states.TryGetValue(above, out var aboveState))
                        state.Above = aboveState;
                }
                int at = s_roots.Count;
                while (at > 0 && s_roots[at - 1].Depth > state.Depth)
                    at--;
                s_roots.Insert(at, state);
            }
        }

        // Solves one tree, in its root's rect, its content kept clear of the safe area, and places each of its nodes;
        // then, the whole tree placed (a ScrollTo reads where its descendant goes), each scroll container in it takes up
        // its new range and what was asked of it. Last, a flight no longer shown drops out and a follower shown again
        // stops following, bar in a change's own pass, where that waits for the change's pairs to form and its flights to
        // board (Animate).
        private static void LayOut(NodeState root, MotionTransition transition)
        {
            s_visit.Clear();
            s_count = 0;
            Visit(root, null, true, -1, root.RectTransform.GetSiblingIndex());
            if (s_count > 0)
            {
                bool attached = FindElements(root);
                LayoutSolver.Solve(s_solver, s_count, root.RectTransform.rect.size, SafeAreaOf(root));
                if (attached)
                    WarnCircular();
            }
            for (int i = 0; i < s_visit.Count; i++)
                Place(s_visit[i], transition);
            for (int i = 0; i < s_visit.Count; i++)
            {
                var state = s_visit[i];
                if (state.Scroll != null && state.Scroll.Axis != ScrollAxis.None)
                    SettleScroll(state, transition);
            }
            if (transition == null)
                DropOut(null);
        }

        // How far a root keeps its content in from each edge its SafeArea names, in its units, so that it is clear of the
        // screen's unsafe area (outside Screen.safeArea: a notch, rounded corners, the home bar), as UIKit's safe area
        // insets are: from its edge to where the safe area starts, on a side where the screen has something unsafe, and
        // nothing elsewhere. Only an outermost root keeps it: one inside another tree (under a plain object in a node) is
        // where that tree put it, which kept clear of it or chose not to. Nothing on a world space canvas, or on none.
        // Read every pass, as Unity says nothing when it changes (a rotation, the simulator's device).
        private static Insets SafeAreaOf(NodeState root)
        {
            var edges = root.Node.SafeArea;
            if (edges == Edges.None || root.Above != null) return default;
            var area = Screen.safeArea;
            bool left = area.xMin > 0.5f, right = area.xMax < Screen.width - 0.5f;
            bool bottom = area.yMin > 0.5f, top = area.yMax < Screen.height - 0.5f;
            if (!left && !right && !bottom && !top) return default;

            var rt = root.RectTransform;
            var canvas = rt.GetComponentInParent<Canvas>();
            if (canvas == null) return default;
            canvas = canvas.rootCanvas;
            if (canvas.renderMode == RenderMode.WorldSpace) return default;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceCamera ? canvas.worldCamera : null;

            // Its rect on screen, in pixels (y up), and how many of its units a pixel is each way.
            rt.GetWorldCorners(s_corners);
            var min = RectTransformUtility.WorldToScreenPoint(camera, s_corners[0]);
            var max = RectTransformUtility.WorldToScreenPoint(camera, s_corners[2]);
            var pixels = max - min;
            if (pixels.x <= 0f || pixels.y <= 0f) return default;
            var size = rt.rect.size;
            var unit = new Vector2(size.x / pixels.x, size.y / pixels.y);

            var safe = default(Insets);
            if (left && (edges & Edges.Left) != 0)
                safe.Left = Mathf.Clamp(area.xMin - min.x, 0f, pixels.x) * unit.x;
            if (right && (edges & Edges.Right) != 0)
                safe.Right = Mathf.Clamp(max.x - area.xMax, 0f, pixels.x) * unit.x;
            if (bottom && (edges & Edges.Bottom) != 0)
                safe.Bottom = Mathf.Clamp(area.yMin - min.y, 0f, pixels.y) * unit.y;
            if (top && (edges & Edges.Top) != 0)
                safe.Top = Mathf.Clamp(max.y - area.yMax, 0f, pixels.y) * unit.y;
            return safe;
        }

        // Adds a node and everything under it to the pass, in pre-order: to the solver while it and everything above
        // it are in layout (Display not None), and to the visit either way, so what has left layout still fades and is
        // drawn. Its children are the LayoutNodes directly under it, in sibling order; a plain object in between
        // makes the node under it a root of its own. `sibling` is its transform's sibling index. Returns its solver
        // index, or -1.
        private static int Visit(NodeState state, NodeState parent, bool inLayout, int parentIndex, int sibling)
        {
            var node = state.Node;
            state.PassParent = parent;
            // Where it is, and whether it is shown down to here, for the flight layer and names; a root's place follows
            // the node above it, whose tree is laid out first. The animation it inherits, handed down the same way. Then
            // its name and id, which it holds while shown.
            state.WasSibling = state.PassSibling;
            state.PassSibling = sibling;
            var above = parent ?? state.Above;
            state.WasShown = state.PassShown;
            state.PassShown = node.Display == DisplayMode.Visible && (above == null || above.PassShown);
            state.PassAnimation = node.Animation ?? above?.PassAnimation;
            RecordKey(state, above);
            // Its drag owner, looked up for every node met as its content is, before its scroll is set up: either one
            // wants the LayoutScroller that takes the pointer for it.
            node.TryGetComponent(out state.Draggable);
            if (node.Scroll != ScrollAxis.None || state.Scroll != null)
                SetUpScroll(state);
            SetUpScroller(state);
            // Not drawn as the pass starts, with nothing it is seen to move from: never placed, back in layout from
            // None having left (the targets it kept are stale), or under something that is. A Hidden node stays laid
            // out, so its targets are current: what an Animate gives it, it moves to, even from no opacity (a toast
            // shown and slid in at once). One still on its way out, or inside one that is, is drawn, and turns back
            // from where it is when shown again. PassIndex and PassLeaving are still the last pass's here, which is
            // what was drawn: one moved out from under a leaving node moves on from where it is, rather than being
            // put at its new place at once as though it had not been drawn.
            bool cameBack = state.PassIndex < 0 && !state.PassLeaving;
            state.PassLeaving = state.Leaving || (parent != null && parent.PassLeaving);
            state.PassUnseen = !state.Seen || cameBack || (parent != null && parent.PassUnseen);

            int index = -1;
            if (inLayout && node.Display != DisplayMode.None)
            {
                index = s_count++;
                if (index == s_solver.Length)
                {
                    Array.Resize(ref s_solver, index * 2);
                    Array.Resize(ref s_solved, index * 2);
                }
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
                    Wrap = node.Wrap,
                    AlignX = node.ChildAlignX,
                    AlignY = node.ChildAlignY,
                    AspectRatio = node.AspectRatio,
                    Floating = node.Floating,
                    Element = -1,
                    Ignores = node.IgnoresSafeArea,
                    Scroll = node.Scroll,
                    Content = content,
                };
                s_solved[index] = state;
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
                int childIndex = Visit(childState, state, index >= 0, index, i);
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

        // Gives each node of the tree just visited that floats against an element the element's index there, for the
        // solver. One whose element is not laid out in this tree (none, disabled or inactive, Display None or under a
        // node that is, or in another tree) is attached to its parent instead, as the solver attaches one whose element
        // it cannot place before it; another tree is a mistake, said once for the node (the rest are states a node
        // passes through). Returns whether any has an element.
        private static bool FindElements(NodeState root)
        {
            bool any = false;
            for (int i = 1; i < s_count; i++)
            {
                ref var solver = ref s_solver[i];
                if (solver.Floating.AttachTo != FloatingAttach.Element) continue;
                var element = solver.Floating.Element;
                if (element != null && s_states.TryGetValue(element, out var state))
                {
                    // Laid out in this pass, unless its index is -1 or another tree's.
                    int index = state.PassIndex;
                    if (index >= 0 && index < s_count && s_solved[index] == state)
                    {
                        solver.Element = index;
                        any = true;
                        continue;
                    }
                    var node = s_solved[i].Node;
                    if (element.Display != DisplayMode.None && !s_warnedElement.Contains(node) && RootOf(element) != root.Node)
                        WarnElement(node, $"({element.name}) is in another layout tree");
                }
                solver.Floating.AttachTo = FloatingAttach.Parent;
            }
            return any;
        }

        // Says once for each node the solver attached to its parent because it could not place its element before it.
        private static void WarnCircular()
        {
            for (int i = 1; i < s_count; i++)
            {
                ref var solver = ref s_solver[i];
                if (solver.Element < 0 || solver.Floating.AttachTo == FloatingAttach.Element) continue;
                var node = s_solved[i].Node;
                if (s_warnedElement.Contains(node)) continue;
                var element = s_solved[solver.Element].Node;
                WarnElement(node, element == node ? "is itself"
                    : element.transform.IsChildOf(node.transform) ? $"({element.name}) is inside it"
                    : $"({element.name}) cannot be placed before it, the elements they float against going round in a circle");
            }
        }

        private static void WarnElement(LayoutNode node, string why)
        {
            s_warnedElement.Add(node);
            Debug.LogWarning($"{node.name}.Floating.Element {why}, so {node.name} is placed against its parent instead. (Said once for it.)", node);
        }

        // The root of the layout tree a node is in.
        private static LayoutNode RootOf(LayoutNode node)
        {
            while (!IsRoot(node))
                node = node.transform.parent.GetComponent<LayoutNode>();
            return node;
        }

        // Gives a node its targets: from the solved layout its centre (moved by its Offset, y up) and size where it is
        // laid out, kept where they were when it has left layout; from its properties its scale, its shown value (1
        // while its own Display is Visible, 0 otherwise) and its opacity. Inside Animate a change of its Display plays
        // its DisplayEffect: in from its away pose when it starts being drawn, out to it (Leaving) when it stops. Only
        // the topmost node that changes plays it, both ways: what is shown inside it in the same change comes with it,
        // and what is hidden inside it, in the same change or while it is leaving, rides it out. Parents come before
        // their children, so what is above it in this pass is known, a change made to it in this pass included.
        private static void Place(NodeState state, MotionTransition transition)
        {
            var node = state.Node;
            var parent = state.PassParent;
            if (state.PassIndex >= 0)
            {
                ref var solved = ref s_solver[state.PassIndex];
                state.Reach = solved.Reach;
                state.Line = solved.Line;
                state.Lines = solved.Lines;
            }
            bool visible = node.Display == DisplayMode.Visible;
            var shown = new Vector2(visible ? 1f : 0f, 0f);
            var opacity = new Vector2(OpacityTargetOf(state), 0f);
            // A root keeps the rect it is given, so it is not scaled.
            var scale = new Vector2(parent != null ? node.Scale : 1f, 0f);
            // Inside a held node it stays where it was stopped with it; what was not drawn has nothing to hold.
            state.PassFrozen = parent != null && (parent.Held || parent.PassFrozen) && !state.PassUnseen;
            bool aboveLeaving = parent != null && parent.PassLeaving;
            state.PassLeaving = state.Leaving || aboveLeaving;
            state.PassAppearing = parent != null && parent.PassAppearing;
            bool topmost = parent == null || (!parent.PassUnseen && !parent.PassAppearing);

            // Met for the first time, it starts where it goes, shown or not as its Display says; out of layout, where
            // its rect is. Inside Animate the topmost of what appears plays its entrance from its away pose.
            if (!state.Seen)
            {
                state.Seen = true;
                state.Parent = parent;
                state.Visible = visible;
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
                    state.LaidOutSize = size;
                    Retarget(state, state.Position, centre, null);
                    Retarget(state, state.Size, size, null);
                }
                Retarget(state, state.Scale, scale, null);
                Retarget(state, state.Shown, shown, null);
                Retarget(state, state.Opacity, opacity, null);
                if (transition != null && visible && topmost)
                    Appear(state, opacity, transition);
                UpdateAway(state, false, null);
                Arrange(state);
                return;
            }

            if (parent != state.Parent)
                Reparent(state, parent, state.PassUnseen ? null : transition);

            // Inside a held node, laid out for the size it was going to: it keeps where it was stopped with it until
            // the node is let go of (parents are placed first, so that is seen before it is). A change of its Display
            // waits for that too.
            if (state.PassFrozen)
            {
                Arrange(state);
                return;
            }

            // In a block of its own: the first meeting above has a centre and size of its own.
            {
                bool laidOut = parent != null && state.PassIndex >= 0;
                Vector2 centre = default, size = default;
                if (laidOut)
                {
                    TargetOf(state, out centre, out size);
                    state.LaidOutSize = size;
                }
                // Caught, each value it was stopped with stays where it was stopped while it is still given what it was
                // then; given something new for it, that one is let go of and goes there as any node's does (a drag
                // setting its Scale leaves its opacity held). Not drawn (something above it left layout and came back),
                // it has nothing to hold, and goes where it is given at once.
                if (state.PassUnseen)
                {
                    LetGoOfHolds(state);
                }
                else
                {
                    if (state.HoldSize && laidOut && size != state.HeldSize)
                        state.HoldSize = false;
                    if (state.HoldOpacity && opacity != state.HeldOpacity)
                        state.HoldOpacity = false;
                    if (state.HoldScale && scale != state.HeldScale)
                        state.HoldScale = false;
                    if (state.HoldShown && shown != state.HeldShown)
                        state.HoldShown = false;
                }

                // Its Display changed. Shown again, it turns back from wherever it was going out; shown inside a change
                // while it was not drawn, it starts being drawn, and what is shown inside it in the same change comes
                // with it. Hidden inside a change while it is drawn, it is on its way out: riding what above it is
                // leaving, or playing its own effect, thrown on the way a fling was taking it. Outside a change it goes
                // at once (Write stops drawing it).
                bool cameIn = false, goes = false;
                if (visible != state.Visible)
                {
                    state.Visible = visible;
                    if (visible)
                    {
                        cameIn = transition != null && !state.PassUnseen && !state.Leaving;
                        if (cameIn)
                            state.PassAppearing = true;
                        state.Leaving = state.Riding = state.Thrown = state.EffectOff = false;
                    }
                    else if (transition != null && !state.PassUnseen)
                    {
                        state.Leaving = true;
                        if (aboveLeaving)
                        {
                            state.Riding = true;
                        }
                        else
                        {
                            goes = true;
                            Throw(state, transition);
                        }
                    }
                    state.PassLeaving = state.Leaving || aboveLeaving;
                }

                // What is not drawn has nowhere it is seen to move from: what it is given, it goes to at once (a page
                // coming back after its layout changed while it was gone appears where it now goes, and plays its
                // entrance from there). A thrown node carries on the way it was thrown until it is shown again, at the
                // size it has: one left where it was drawn as its pair ended early (Hidden, so still laid out) would
                // otherwise be put back at its layout size there while it finishes fading, its position not.
                var move = state.PassUnseen ? null : transition;
                if (laidOut)
                {
                    if (!state.Thrown && centre != state.Position.Target)
                        Retarget(state, state.Position, centre, move);
                    if (!state.Thrown && !state.HoldSize && size != state.Size.Target)
                        Retarget(state, state.Size, size, move);
                }
                if (!state.HoldScale && scale != state.Scale.Target)
                    Retarget(state, state.Scale, scale, move);

                // The topmost of what was not drawn plays its entrance inside a change; the rest of what was not drawn,
                // and what is shown inside something starting to be drawn in the same change, is shown or not at once;
                // what rides out keeps how it is shown until what it rides has gone (SettleLeaving). A follower keeps
                // its fade: it is drawn at the node it follows, whatever has become of what it is inside (a page that
                // has finished leaving).
                if (state.PassUnseen && transition != null && visible && topmost)
                {
                    Appear(state, opacity, transition);
                }
                else if (state.PassUnseen || (cameIn && !topmost))
                {
                    PutAt(state.Shown, shown);
                    if (state.Follows == null)
                        PutAt(state.Opacity, opacity);
                }
                else if (!state.Riding)
                {
                    if (!state.HoldShown && shown != state.Shown.Target)
                        Retarget(state, state.Shown, shown, transition);
                    if (!state.HoldOpacity && opacity != state.Opacity.Target)
                        Retarget(state, state.Opacity, opacity, transition);
                }

                // Playing its way out, every value of it still moving is the change's, taken over from what it was
                // moving for (which is let go of on its way), so the change finishes only once the node has gone.
                if (goes)
                {
                    HoldIfMoving(state.Position, transition);
                    HoldIfMoving(state.Size, transition);
                    HoldIfMoving(state.Opacity, transition);
                    HoldIfMoving(state.Scale, transition);
                    HoldIfMoving(state.Shown, transition);
                }

                UpdateAway(state, !state.PassUnseen, move);
                Arrange(state);
            }
        }

        // What Arrange tells content that layout does not size.
        private static readonly Vector2 Unarranged = new(-1f, -1f);

        // Tells a laid-out node's content the size its node is going to (Arrange), so it lays itself out there while its
        // rect springs there. A root keeps the rect it is given, which is its size; what has left layout keeps the size
        // it had, and is drawn as it was while it fades. So does a thrown node: a follower whose pair ended early is left
        // at the drawn size of the node it followed, while its content stays laid out at its own size, as it was drawn
        // while following (told the drawn size, a wrapped or centred text would re-wrap or shift once). A node drawn as a
        // picture of itself after taking over by name (Fitting) keeps it laid out at the size it is laid out at, caught
        // short of there or not, so a catch pauses the picture as drawn.
        private static void Arrange(NodeState state)
        {
            if (state.PassIndex < 0 || state.Thrown) return;
            var content = s_solver[state.PassIndex].Content;
            if (content != null)
                content.Arrange(state.PassParent == null ? Unarranged : state.Fitting ? state.LaidOutSize : state.Size.Target);
        }

        // Where the solver put a node: its rect's centre, moved by its Offset (y up), and its size. One floating against
        // an element is moved on from there to where the element is placed and drawn (both are its ShiftOf).
        private static void TargetOf(NodeState state, out Vector2 centre, out Vector2 size)
        {
            var rect = s_solver[state.PassIndex].Rect;
            centre = rect.center + ShiftOf(state);
            size = rect.size;
        }

        // A node floating against an element takes up the scroll offsets stepped this frame, which its target takes in,
        // so it is drawn on the element as the element is drawn this frame rather than a frame behind a glide: at rest
        // it is put there, and on its way it heads there, as with any change outside Animate; its away move follows,
        // the drawn part of that handed over as in Place. Inside a held node it stays where it was stopped, and thrown
        // it carries on the way it was thrown, as Place leaves them.
        private static void FollowElement(NodeState state)
        {
            if (state.PassParent == null || state.PassIndex < 0 || state.PassFrozen || state.Thrown
                || s_solver[state.PassIndex].Floating.AttachTo != FloatingAttach.Element)
                return;
            TargetOf(state, out var centre, out _);
            if (centre == state.Position.Target) return;
            Retarget(state, state.Position, centre, null);
            UpdateAway(state, true, null);
        }

        // A node found under a different layout parent carries where it is, how fast it is going and where it was going
        // over into the new one's layout space, through world space, so it moves on from where it was drawn. Out from
        // under the old one (a root now) its rect is its own again, and what it was moving for there is let go of on
        // its way; in from being a root (or from a parent that is gone) it starts from where its rect is drawn. Going
        // through where it is drawn, a scroll container's offset comes off on the way out (drawn = centre - offset) and
        // goes back on on the way in, so it does not jump by it; a velocity, a difference, is the same either way, and
        // so is its away move, which the pass's UpdateAway then brings up to date for the new parent's rect, handing
        // over what is drawn of the difference. Moved from one parent to another in `transition`'s pass, it boards the
        // flight layer, flying above both parents' clips to its new place; to or from being a root, it does not fly.
        private static void Reparent(NodeState state, NodeState parent, MotionTransition transition)
        {
            var old = state.Parent;
            state.Parent = parent;
            if (parent == null)
            {
                Stop(state.Position);
                Stop(state.Size);
                Stop(state.Scale);
                LetGoOfHolds(state);
                state.Away = Vector2.zero;
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
            state.Away = VectorTo(from, to, state.Away);
            if (transition != null)
                Board(state, old);
        }

        // ── Showing and hiding ───────────────────────────────────────────────────

        // Where a node's opacity goes: its Opacity, or nothing while it is away with its effect fading, or while it
        // follows the node that took over from it by name, drawn at that one's rect as it fades in over it (otherwise
        // every pass would put a source whose effect does not fade back to its Opacity).
        private static float OpacityTargetOf(NodeState state)
        {
            var node = state.Node;
            if (state.Follows != null) return 0f;
            return node.Display == DisplayMode.Visible || !node.DisplayEffect.Fade ? node.Opacity : 0f;
        }

        // Puts a node that starts being drawn inside a change at its away pose at once (its position and size already
        // where they go): not shown, and faded out if its effect fades. It then sets off from there to shown and to its
        // opacity on the change. What is shown inside it in the same change comes with it.
        private static void Appear(NodeState state, Vector2 opacity, MotionTransition transition)
        {
            PutAt(state.Shown, Vector2.zero);
            PutAt(state.Opacity, state.Node.DisplayEffect.Fade ? Vector2.zero : opacity);
            Retarget(state, state.Shown, new Vector2(1f, 0f), transition);
            Retarget(state, state.Opacity, opacity, transition);
            state.PassAppearing = true;
        }

        // A node hidden while its position is moving from a fling, on no change, is thrown away: its position is sent
        // on, once, to where the velocity it has carries it on its spring (value + velocity / omega), gliding there with
        // no bounce and swinging slightly past with some. It goes on the node's spring for the change with no delay, as
        // Fling sets it, since a delayed spring is held still and would stop dead mid-throw, held by the change. Only once: sent
        // on again every pass, it would be pushed as it slowed, which runs away on a bouncy spring. Place leaves it
        // there until it is shown again, when it turns back from where it is drawn. A position moving on no change only
        // because its away move was handed over outside a change (its Edge set there while it was partly shown) was
        // never flung, and heads home with the change as any other value of it does.
        private static void Throw(NodeState state, MotionTransition transition)
        {
            var position = state.Position;
            if (state.Parent == null || !position.Moving || position.Transition != null || !state.Flung) return;
            state.Thrown = true;
            Spring.Parameters(AnimationOf(state, transition), out position.Omega, out position.Zeta);
            position.Delay = 0f;
            position.Target = position.Value + position.Velocity / position.Omega;
            Hold(position, transition);
        }

        private static void HoldIfMoving(Spring spring, MotionTransition transition)
        {
            if (spring.Moving)
                Hold(spring, transition);
        }

        // Puts a spring at `value` at once, at rest, letting go of what it was moving for on its way.
        private static void PutAt(Spring spring, Vector2 value)
        {
            spring.Target = value;
            Stop(spring);
        }

        // Brings a node's away move up to date (AwayOf). While a catch holds its shown value it stays as it is, frozen
        // with that, so a drag on a node caught sliding in follows the pointer exactly. With `handOver`, while the node
        // is drawn at its away pose partly shown, the drawn part of the change goes into its position's presentation,
        // which is what carries on from where it was drawn: its value by d x (1 - shown) and its velocity by d x the
        // shown value's, so neither where it is drawn nor how fast it moves there jumps, and the rest rides home on its
        // own position spring, the target (and so layout) untouched. A position at rest that this sets moving goes on
        // the node's spring for the change that holds it, with no delay (one only ever put where it goes has no spring,
        // and stepping it on none would divide by nothing), held by `transition`, the pass's change; outside one, a node
        // playing its way out has it held by the change it is leaving on, so that change still finishes only once the
        // node has gone.
        private static void UpdateAway(NodeState state, bool handOver, MotionTransition transition)
        {
            if (state.HoldShown) return;
            var away = AwayOf(state);
            var d = away - state.Away;
            if (d == Vector2.zero) return;
            state.Away = away;
            if (!handOver || !Posed(state)) return;

            var shown = state.Shown;
            var drawn = d * (1f - shown.Value.x);
            var speed = d * shown.Velocity.x;
            if (drawn == Vector2.zero && speed == Vector2.zero) return;
            var position = state.Position;
            if (!position.Moving)
            {
                var holder = transition ?? (state.Leaving && !state.Riding ? state.Shown.Transition : null);
                Spring.Parameters(AnimationOf(state, holder), out position.Omega, out position.Zeta);
                position.Delay = 0f;
                Hold(position, holder);
                // Set off by this, not by a fling: hidden while it moves only from this, it is not thrown.
                state.Flung = false;
            }
            position.Value -= drawn;
            position.Velocity += speed;
        }

        // The smallest move that takes a node's target rect (its position and size targets) just past what its parent
        // shows, on its DisplayEffect's edge, in its parent's layout space: nothing on an axis it is past already, with
        // no edge, or for a root, whose rect is its own. What its parent shows is its whole rect as drawn (its drawn
        // size, padding and all; a root's own rect), moved by its scroll, whose clip is what hides the node there. For
        // a node floating against its root it is the root's rect, where the solver puts that in the parent's space: a
        // toast floating against the screen inside a padded page slides past the screen, rather than stopping in the
        // padding in view.
        private static Vector2 AwayOf(NodeState state)
        {
            var parent = state.PassParent;
            var edge = state.Node.DisplayEffect.Edge;
            if (parent == null || edge == DisplayEdge.None || state.Anchor != null) return Vector2.zero;
            var min = ScrolledBy(parent);
            Vector2 size;
            if (state.Node.Floating.AttachTo == FloatingAttach.Root && parent.PassIndex >= 0)
            {
                min -= s_solver[parent.PassIndex].RootRect.position;
                size = s_solver[0].Rect.size;
            }
            else
            {
                size = parent.PassParent != null
                    ? Vector2.Max(parent.Size.Value, Vector2.zero)
                    : parent.RectTransform.rect.size;
            }

            var centre = state.Position.Target;
            var half = state.Size.Target * 0.5f;
            switch (edge)
            {
                case DisplayEdge.Left:
                    return new Vector2(Mathf.Min(0f, min.x - (centre.x + half.x)), 0f);
                case DisplayEdge.Right:
                    return new Vector2(Mathf.Max(0f, min.x + size.x - (centre.x - half.x)), 0f);
                case DisplayEdge.Top:
                    return new Vector2(0f, Mathf.Min(0f, min.y - (centre.y + half.y)));
                default:
                    return new Vector2(0f, Mathf.Max(0f, min.y + size.y - (centre.y - half.y)));
            }
        }

        // Whether a node is drawn at its away pose (its slide and its effect's scale): while it is shown or on its way
        // out, and not while it is drawn with its effect off (a follower whose pair ended early). A node hidden at rest
        // is drawn where it is laid out, unseen, so nothing (a child met out of layout, a node reparented into it, a
        // fling) converts into a space its effect has scaled to nothing.
        private static bool Posed(NodeState state) =>
            (state.Node.Display == DisplayMode.Visible || state.Leaving) && !state.EffectOff;

        // How far its effect's edge moves where it is drawn, in its parent's layout space: its away move, as far as it
        // is not shown; with an anchor, the way from its position to where that is drawn, as far as it is not shown.
        private static Vector2 SlideOf(NodeState state)
        {
            if (!Posed(state)) return Vector2.zero;
            float away = 1f - state.Shown.Value.x;
            return state.Anchor != null ? (state.AnchorCentre - state.Position.Value) * away : state.Away * away;
        }

        // How fast that is moving, in its parent's layout units a second (an anchor's own motion left out).
        private static Vector2 SlideVelocityOf(NodeState state)
        {
            if (!Posed(state)) return Vector2.zero;
            if (state.Anchor == null) return -state.Away * state.Shown.Velocity.x;
            return -(state.AnchorCentre - state.Position.Value) * state.Shown.Velocity.x
                - state.Position.Velocity * (1f - state.Shown.Value.x);
        }

        // How much its effect's shrink scales it: 1 - Shrink away, 1 shown, unclamped, so a bouncy entrance overshoots.
        // With an anchor it is sized from that instead (DrawnSizeOf).
        private static float EffectScaleOf(NodeState state) =>
            Posed(state) && state.Anchor == null
                ? Mathf.LerpUnclamped(1f - state.Node.DisplayEffect.Shrink, 1f, state.Shown.Value.x)
                : 1f;

        // The size it is drawn at before its scale: its size; with an anchor, from the anchor's drawn size (over its own
        // scale, which it is drawn at) to its own, as far as it is shown, unclamped as the shrink is. What is inside it is
        // laid out at its own size throughout, pinned at its top left.
        private static Vector2 DrawnSizeOf(NodeState state)
        {
            if (state.Anchor == null || !Posed(state)) return state.Size.Value;
            float scale = state.Scale.Value.x;
            var from = scale > 1e-4f ? state.AnchorSize / scale : state.AnchorSize;
            return Vector2.LerpUnclamped(from, state.Size.Value, state.Shown.Value.x);
        }

        // Reads where a node's anchor is drawn this frame into its parent's layout space (the parent's scroll put back
        // on, as its position is). An anchor that has gone, or a parent drawn at a scale of about nothing, leaves the
        // last reading: its effect plays out against where the anchor was.
        private static void ReadAnchor(NodeState state)
        {
            var space = SpaceOf(state);
            if (!Live(state.Anchor) || space == null || Degenerate(space)) return;
            DrawnIn(state.Anchor.RectTransform, space, out var centre, out state.AnchorSize);
            state.AnchorCentre = centre + ScrolledBy(state.Parent);
        }

        // Whether a node's effect has played out against its anchor: its shown value and its fade at rest, and not held
        // there by a catch.
        private static bool AnchorPlayed(NodeState state) =>
            !state.Shown.Moving && !state.Opacity.Moving && !state.HoldShown;

        // The scale it is drawn at around its centre: its Scale's times its effect's, never below nothing, so a bouncy
        // exit shrinking to nothing stops there rather than turning inside out.
        private static float DrawnScaleOf(NodeState state) => Mathf.Max(0f, state.Scale.Value.x * EffectScaleOf(state));

        // After a node on its way out is stepped: playing its own effect, it has gone once every value of it has come
        // to rest, unless something above it is held, which pauses it there; riding out inside something leaving, once
        // nothing above it is, when it is put away at once, not shown and at its away opacity. From then on it is not
        // drawn, and a thrown one is put back at its place, unseen, by the next pass (a frame's, or an Animate's before
        // its update, so that move is no change's). While it follows the node that took over from it by name, it has
        // not gone: it is drawn at that one, fading out beneath it, until their pair lands.
        private static void SettleLeaving(NodeState state)
        {
            if (state.Follows != null) return;
            if (state.Riding)
            {
                if (state.PassParent != null && state.PassParent.PassLeaving) return;
                PutAt(state.Shown, Vector2.zero);
                PutAt(state.Opacity, new Vector2(OpacityTargetOf(state), 0f));
            }
            else if (state.PassFrozen || state.Position.Moving || state.Size.Moving || state.Opacity.Moving
                     || state.Scale.Moving || state.Shown.Moving)
            {
                return;
            }
            state.Leaving = state.Riding = state.Thrown = state.EffectOff = false;
        }

        // Queues ShownChanged for a node whose shown value as drawn is not what it was last raised with.
        private static void QueueShownChanged(NodeState state)
        {
            if (state.ShownQueued || state.Shown.Value.x == state.RaisedShown) return;
            state.ShownQueued = true;
            s_shown.Add(state);
        }

        // Raises ShownChanged for each node queued, in the order they were written, bar those whose value has come back
        // to what was last raised. By index, rather than taking each off the front as Scrolled is, since the frame a
        // node is first placed queues it, and a scene's first frame queues every node. Outside play mode nothing is
        // raised.
        private static void RaiseShownChanged()
        {
            for (int i = 0; i < s_shown.Count; i++)
            {
                var state = s_shown[i];
                if (state == null) continue;
                state.ShownQueued = false;
                float shown = state.Shown.Value.x;
                if (shown == state.RaisedShown) continue;
                state.RaisedShown = shown;
                if (Application.isPlaying && state.Node != null)
                    state.Node.RaiseShownChanged(shown);
            }
            s_shown.Clear();
        }

        // ── Retargeting ──────────────────────────────────────────────────────────

        // The one place a spring is given somewhere to go. Without a transition, one at rest is put there at once (a
        // caught node dragged by its Offset follows the pointer exactly), while one already on its way heads there
        // instead, keeping its speed, its spring and the change it is moving for: a box changing under it because
        // something outside it is animating (a nested root's rect) does not cut its motion off. With a transition, it
        // sets off from where it is at the velocity it has, on its node's spring for the change (its own animation, one
        // it inherits, or the change's: AnimationOf) after its delay, held by that transition until it comes to rest or
        // is taken over in turn; a position bows out sideways by the animation's curvature. On an animation with no duration (None), it is put there at once, held by nothing, so it is drawn
        // there in the frame of the change; with a delay as well, it waits that out and is put there then (Spring.Step).
        private static void Retarget(NodeState state, Spring spring, Vector2 target, MotionTransition transition)
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

            var animation = AnimationOf(state, transition);
            if (animation.AtOnce)
            {
                Snap(spring, target, transition);
                return;
            }
            Spring.Parameters(animation, out spring.Omega, out spring.Zeta);
            // An opacity never bounces, on the animation's duration and delay: past 1 it would only clip flat, and past
            // 0 it would come back into view (to about half, at the most bounce) as it goes.
            if (spring == state.Opacity)
                spring.Zeta = 1f;
            spring.Delay = Mathf.Max(0f, animation.Delay);
            // No sideways kick on no spring: it is scaled by omega, infinite there.
            if (spring == state.Position && animation.Curvature > 0f && animation.Duration > 0f)
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

        // Finishes what a gesture or a scroll let go of, outside a pass or a change (which finish theirs once over).
        private static void FlushIfIdle()
        {
            if (!s_passing && Current == null)
                Flush();
        }

        // ── Drawing ──────────────────────────────────────────────────────────────

        // Writes where a node is drawn into what it owns, each value only when it differs from what is there, so an
        // unchanged scene is not dirtied in edit mode and UGUI rebuilds nothing for nothing. A non-root's RectTransform
        // is anchored at its parent's top-left corner, pivoted on its centre, at its centre (y up; moved by its slide,
        // and back by its parent's scroll offset, when its parent scrolls) and size, scaled around it by its Scale and
        // its effect's shrink. Its CanvasGroup takes its opacity while it is Visible or on its way out (none
        // otherwise), and blocks raycasts only while it is Visible, its Opacity is above 0 (UIKit does not hit-test a
        // view whose alpha is below 0.01, nor SwiftUI one of opacity 0, while a CanvasGroup's alpha alone does not stop
        // UGUI's raycasts), and it is not moving for a change that is not interactive; not blocking them takes the
        // pointer from everything inside it too. A flight's, which the groups above it do not reach, also stops blocking
        // them while anything above it takes no pointer. One is added only once it is needed (faded, or not to be
        // clicked). A node following the one that took over from it by name is drawn at that one's rect instead, at its
        // own opacity, and takes no pointer; a destination flying, which ignores its parent groups, is drawn at its
        // opacity times what theirs are going to. Taking over by name or growing out of an anchor, it fills the rect it
        // moves through by its MatchFit, and while it flies with MatchClip it is cut to it (LayoutSystem.Match.cs).
        private static void Write(NodeState state)
        {
            var node = state.Node;
            if (state.Follows != null)
            {
                DrawOver(state);
            }
            else if (state.Parent != null)
            {
                if (state.Anchor != null)
                    ReadAnchor(state);
                var centre = state.Position.Value + SlideOf(state) - ScrolledBy(state.Parent);
                // A bouncy spring shrinking to nothing swings past it: it stops at nothing rather than turning inside out.
                var size = Vector2.Max(DrawnSizeOf(state), Vector2.zero);
                if (Fitted(state))
                    WriteFitted(state, centre, size, DrawnScaleOf(state), node.MatchFit);
                else
                    WriteRect(state.RectTransform, centre, size, DrawnScaleOf(state));
            }
            if (state.Flight != null)
                CutToMatch(state);

            bool visible = node.Display == DisplayMode.Visible;
            // Drawn by its Display as it was last placed: inside a held node that waits until the node is let go of
            // (Place), so a node hidden in there meanwhile is not cut at once only to reappear and play its way out.
            float alpha = state.Visible || state.Leaving ? Mathf.Clamp01(state.Opacity.Value.x) : 0f;
            // Its parent groups reach it again as it lands, by then at rest where they were going, so its alpha does
            // not change then; a page fading in over it meanwhile does not reach it, its fade going to 1.
            if (state.Flight != null && state.Flight.PairHalf && state.Follows == null)
                alpha *= OpacityAbove(state);
            bool clickable = BlocksRaycasts(state, visible && node.Opacity > 0f && state.Follows == null
                && !Uninteractive(state.Position) && !Uninteractive(state.Size) && !Uninteractive(state.Opacity)
                && !Uninteractive(state.Scale) && !Uninteractive(state.Shown));
            if (state.Group == null && !node.TryGetComponent(out state.Group))
            {
                if (alpha >= 1f && clickable) return;
                state.Group = node.gameObject.AddComponent<CanvasGroup>();
            }
            if (state.Group.alpha != alpha) state.Group.alpha = alpha;
            if (state.Group.blocksRaycasts != clickable) state.Group.blocksRaycasts = clickable;
        }

        // Puts a node's RectTransform at `centre` and `size` in its parent's layout space (y down, from its top-left
        // corner, as drawn: any scroll already taken off), scaled around its centre: anchored at that corner and
        // pivoted on its centre.
        private static void WriteRect(RectTransform rt, Vector2 centre, Vector2 size, float scale) =>
            WriteRect(rt, centre, size, new Vector2(scale, scale));

        // The same, scaled by as much as `scale` says along each of its axes (a picture of a node fitted to a rect).
        private static void WriteRect(RectTransform rt, Vector2 centre, Vector2 size, Vector2 scale)
        {
            var anchor = new Vector2(0f, 1f);
            if (rt.anchorMin != anchor) rt.anchorMin = anchor;
            if (rt.anchorMax != anchor) rt.anchorMax = anchor;
            var pivot = new Vector2(0.5f, 0.5f);
            if (rt.pivot != pivot) rt.pivot = pivot;
            var position = new Vector2(centre.x, -centre.y);
            if (rt.anchoredPosition != position) rt.anchoredPosition = position;
            if (rt.sizeDelta != size) rt.sizeDelta = size;
            var scaled = new Vector3(scale.x, scale.y, 1f);
            if (rt.localScale != scaled) rt.localScale = scaled;
        }

        // Whether a spring is moving for a change made with interactive false: until it comes to rest, or is taken
        // over by an interactive change, a fling or a catch (each of which lets go of that change), its node takes no
        // pointer. Scroll offsets are not asked: a scroll springing does not move the node.
        private static bool Uninteractive(Spring spring) => spring.Transition != null && !spring.Transition.Interactive;

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
        // scroll container clips what is in it with a RectMask2D, added hidden and never saved (in edit mode too, so the
        // clip shows there), or found on it already (after a domain reload, say): a RectMask2D of its own, not hidden,
        // clips anyway and is never touched. One that stops scrolling turns it off, keeping it for when it scrolls
        // again. Whatever it was doing stops where it is drawn on the axes it still scrolls, at the start on the others;
        // what was asked of it waits for the pass, unless it scrolls no more. (The pointer is SetUpScroller's.)
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
                HideIndicators(scroll);
                // Its children are drawn where they are laid out again: it is scrolled by nothing.
                if (scroll.Raised != Vector2.zero) QueueScrolled(state);
                return;
            }

            if (scroll.Clip == null && !node.TryGetComponent(out scroll.Clip))
                scroll.Clip = Hide(node.gameObject.AddComponent<RectMask2D>());
            if (scroll.Clip != null && IsHidden(scroll.Clip) && !scroll.Clip.enabled) scroll.Clip.enabled = true;
        }

        // Gives a node the LayoutScroller that takes the pointer for it while it scrolls or has an enabled drag owner,
        // added hidden and never saved (in edit mode too) or found on it already, and turns it off while it has neither,
        // keeping it for when it does again: a node that neither scrolls nor has an owner never takes a drag from what is
        // under it. An owner's node gets no clip.
        private static void SetUpScroller(NodeState state)
        {
            var node = state.Node;
            if (node.Scroll != ScrollAxis.None || IsLive(state.Draggable))
            {
                if (state.Scroller == null && !node.TryGetComponent(out state.Scroller))
                    state.Scroller = Hide(node.gameObject.AddComponent<LayoutScroller>());
                if (state.Scroller != null && !state.Scroller.enabled) state.Scroller.enabled = true;
            }
            else if (state.Scroller != null && state.Scroller.enabled)
            {
                state.Scroller.enabled = false;
            }
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
        // content runs past its size, on the axes it scrolls). Held by a press, it is kept within its new range unless it
        // is stretched past an end (by the drag, or caught there by the press), and drawn banded against it if it is: an
        // owner moving in the same drag can change its range (a sheet growing past full resizes its list), and one left
        // drawn past its end there would spring back short of it once let go. Then, in play mode: what is moving it heads
        // for where it can now go, keeping its speed; a request made of it is resolved; with its ScrollAnchor at End, it
        // is kept at its end as its range grows (KeepAtEnd), unless a request sent it somewhere, which wins (a
        // ScrollIntoView that finds its descendant in view sends it nowhere); and, at rest, it comes back into range if
        // its content shrank under it, on its spring for the change inside Animate and at once outside. Gliding or
        // springing, it is left to its motion, which settles in range.
        private static void SettleScroll(NodeState state, MotionTransition transition)
        {
            var scroll = state.Scroll;
            // Whether it is at its end on each axis, and its range, before this pass changes that range; and whether it
            // has been laid out before.
            bool first = !scroll.Measured;
            var was = scroll.Range;
            bool endX = false, endY = false;
            if (state.Node.ScrollAnchor == ScrollAnchor.End)
            {
                endX = scroll.Scrolls(0) && scroll.StaysAtEnd(0);
                endY = scroll.Scrolls(1) && scroll.StaysAtEnd(1);
            }
            if (state.PassIndex >= 0)
            {
                ref var solved = ref s_solver[state.PassIndex];
                var size = solved.Rect.size;
                bool held = scroll.Phase == ScrollPhase.Dragging;
                bool stretched = held && scroll.Raw != scroll.Clamp(scroll.Raw);
                scroll.Viewport = size;
                // Overflow within the solver's epsilon is float rounding, not something to scroll to.
                var overflow = solved.ContentSize - size;
                scroll.Range = scroll.OnAxes(new Vector2(overflow.x > 0.01f ? overflow.x : 0f, overflow.y > 0.01f ? overflow.y : 0f));
                scroll.Measured = true;
                if (held)
                {
                    if (!stretched)
                        scroll.Raw = scroll.Clamp(scroll.Raw);
                    scroll.Offset.Value = scroll.Band(scroll.Raw);
                }
            }
            if (!Application.isPlaying || !scroll.Measured) return;

            if (scroll.Phase == ScrollPhase.Gliding || scroll.Phase == ScrollPhase.Springing)
            {
                scroll.Offset.Target = scroll.Clamp(scroll.Offset.Target);
                scroll.WheelTarget = scroll.Clamp(scroll.WheelTarget);
            }
            if (scroll.Request != ScrollRequest.None && state.PassIndex >= 0 && ResolveRequest(state, transition))
                return;
            if ((endX || endY) && KeepAtEnd(state, endX, endY, was, first, transition))
                return;
            if (scroll.Phase == ScrollPhase.Idle)
            {
                var value = scroll.Offset.Value;
                var clamped = scroll.Clamp(value);
                if (clamped != value)
                    MoveScroll(state, clamped, transition);
            }
        }

        // Keeps a scroll container whose ScrollAnchor is End at its end on the axes it was at its end on as the pass
        // started (`x`, `y`: StaysAtEnd), where its range has grown past `was`, as SwiftUI's defaultScrollAnchor(.bottom)
        // for size changes, or a chat pinned to its latest message. The first time it is laid out, at once, so it starts
        // there; inside Animate, on its spring for the change, from where it is and at its speed; outside, at once, or,
        // springing already, only where it springs to moves, so its motion carries on unbroken. Its other axis stays
        // where it is or goes where it was going. A range that shrank needs nothing: at rest it is brought back into
        // range anyway, and what it springs to is kept within range. Returns whether it moved it.
        private static bool KeepAtEnd(NodeState state, bool x, bool y, Vector2 was, bool first, MotionTransition transition)
        {
            var scroll = state.Scroll;
            var range = scroll.Range;
            x &= range.x > was.x;
            y &= range.y > was.y;
            if (!x && !y) return false;

            bool springing = scroll.Phase == ScrollPhase.Springing;
            var to = springing ? scroll.Offset.Target : scroll.Offset.Value;
            if (x) to.x = range.x;
            if (y) to.y = range.y;
            to = scroll.Clamp(to);
            if (springing && transition == null)
            {
                scroll.Offset.Target = to;
                if (scroll.Wheeling)
                    scroll.WheelTarget = to;
                return true;
            }
            MoveScroll(state, to, first ? null : transition);
            return true;
        }

        // Resolves what was asked of a scroll container, at the end of a pass that laid it out: springing for the
        // change it was asked in when this is that change's pass, and at once otherwise (asked outside Animate, or in an
        // update that threw). A ScrollTo or ScrollIntoView whose descendant is not laid out inside it (Display None, or
        // under a root of its own) is dropped, and a ScrollIntoView that finds it in view already moves nothing. Returns
        // whether it sent it somewhere: true for an offset or a ScrollTo even when that is where it is already, as either
        // asked for that place; false for one dropped, or a ScrollIntoView that moves nothing.
        private static bool ResolveRequest(NodeState state, MotionTransition transition)
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
            else if (descendant == null || !TryScrollRect(state, descendant, out var min, out var max))
            {
                if (descendant != null)
                    WarnScrollTo(state.Node, descendant, kind);
                return false;
            }
            else if (!TryScrollOffset(scroll, state.Reach, kind, anchor, min, max, out offset))
            {
                return false;
            }
            MoveScroll(state, offset, move);
            return true;
        }

        // The rect a ScrollTo or ScrollIntoView of `descendant` lines up, from `min` to `max` in the container's content
        // space: the descendant's target rect there (its target top-left, centre - size / 2, and each of its layout
        // parents' up to the container's child, each in its own parent's layout space, added up), grown on each side by
        // the space around it (MarginOf). Read from the targets the last pass gave, not the solver's nodes, which the
        // next tree's pass writes over (and ScrollTo also runs between passes). False when it is not laid out inside
        // it: not placed yet, out of layout, or under a root of its own.
        private static bool TryScrollRect(NodeState container, LayoutNode descendant, out Vector2 min, out Vector2 max)
        {
            min = max = default;
            if (!s_states.TryGetValue(descendant, out var target)) return false;
            var start = Vector2.zero;
            for (var state = target; state != container; state = state.Parent)
            {
                if (state == null || !state.Seen || state.PassIndex < 0) return false;
                start += state.Position.Target - state.Size.Target * 0.5f;
            }
            var end = start + target.Size.Target;
            for (int axis = 0; axis < 2; axis++)
            {
                min[axis] = start[axis] - MarginOf(container, target, axis, false);
                max[axis] = end[axis] + MarginOf(container, target, axis, true);
            }
            return true;
        }

        // How far the rect a ScrollTo lines up runs past `target`'s own on one side of one axis (0 is x, 1 is y; `end`
        // the right or bottom side, otherwise the left or top): CSS's scroll-margin, worked out from the layout. Along
        // its parent's direction, a neighbour in the flow on that side (in its line, when its parent wraps) puts the gap
        // between them there, and that is all, so the neighbour's edge meets the view's and no sliver of it shows; across
        // it, so does another line on that side, when its parent wraps. With no neighbour there, its parent's padding
        // (and what its parent reaches past the safe area, which is padding too); and while that takes it to its parent's
        // edge, the space around its parent there too, on up to the container, whose padding is where its content
        // starts and ends. One aligned in from its parent's edge stops at the padding, the space around its parent
        // being further off. A floating node is out of the flow: it adds nothing, and nothing above it counts. The
        // chain up to the container has been checked by the caller.
        private static float MarginOf(NodeState container, NodeState target, int axis, bool end)
        {
            // Within the solver's epsilon of its parent's edge, the rest is float rounding: it is at the edge.
            const float rounding = 0.01f;
            float margin = 0f;
            for (var state = target; ; state = state.Parent)
            {
                var node = state.Node;
                if (node.Floating.IsFloating) return margin;
                var parent = state.Parent;
                var layout = parent.Node;
                bool along = (layout.Direction == LayoutDirection.LeftToRight) == (axis == 0);
                if (along ? HasFlowNeighbour(parent, state, end) : end ? state.Line < parent.Lines - 1 : state.Line > 0)
                    return margin + layout.ChildGap;
                var padding = layout.Padding + parent.Reach;
                float inset = axis == 0 ? (end ? padding.Right : padding.Left) : (end ? padding.Bottom : padding.Top);
                margin += inset;
                if (parent == container) return margin;
                // Where layout puts its edge (its target, less its own Offset, which is y up), moved out by the
                // padding, against its parent's edge.
                float centre = state.Position.Target[axis] - (axis == 0 ? node.Offset.x : -node.Offset.y);
                float half = state.Size.Target[axis] * 0.5f;
                float apart = end ? parent.Size.Target[axis] - (centre + half + inset) : centre - half - inset;
                if (Mathf.Abs(apart) > rounding) return margin;
            }
        }

        // Whether `child` has a neighbour in `parent`'s flow after it (`after`) or before it, in the same line when the
        // parent wraps its children: a sibling node the last pass laid out there, Hidden ones included, as they keep
        // their space. One out of layout (inactive, disabled or Display None, a node on its way out included, having left
        // already) or floating is not in the flow.
        private static bool HasFlowNeighbour(NodeState parent, NodeState child, bool after)
        {
            var transform = parent.RectTransform;
            int step = after ? 1 : -1;
            for (int i = child.RectTransform.GetSiblingIndex() + step; i >= 0 && i < transform.childCount; i += step)
            {
                if (transform.GetChild(i).TryGetComponent(out LayoutNode sibling) && sibling.isActiveAndEnabled
                    && s_states.TryGetValue(sibling, out var state) && state.Seen && state.PassIndex >= 0
                    && !sibling.Floating.IsFloating)
                    return state.Line == child.Line;
            }
            return false;
        }

        // Where a container scrolls to for the rect from `min` to `max` in its content (TryScrollRect), on each axis it
        // scrolls. What it shows, here, is its rect less its `reach` (the safe area it reaches under), as UIKit lines
        // things up within a scroll view's adjusted content insets. A ScrollTo (`kind` To): the rect's start less `anchor`
        // of the room around it (what it shows less the rect), within range, as it was for the descendant alone. A
        // ScrollIntoView, as CSS's 'nearest': only the axes on which the rect is not wholly in view from where the
        // container rests (where it is, or where it springs to), give or take its rest distance, move, lining its start up
        // with the view's start when it runs past the start, and its end with the view's end when it runs past the end;
        // for a rect bigger than the view, the other way round, the least move that fills the view with it. One running
        // past both edges already fills it and stays. False for a ScrollIntoView that finds nothing to move, which leaves
        // the container as it is, gliding or held by a press included.
        private static bool TryScrollOffset(ScrollState scroll, Insets reach, ScrollRequest kind, float anchor, Vector2 min, Vector2 max,
            out Vector2 offset)
        {
            var lead = new Vector2(reach.Left, reach.Top);
            var viewport = scroll.Viewport - lead - new Vector2(reach.Right, reach.Bottom);
            if (kind != ScrollRequest.IntoView)
            {
                offset = scroll.Clamp(min - lead - anchor * (viewport - (max - min)));
                return true;
            }

            var resting = scroll.Phase == ScrollPhase.Springing ? scroll.Offset.Target : scroll.Offset.Value;
            offset = resting;
            bool moves = false;
            for (int axis = 0; axis < 2; axis++)
            {
                if (!scroll.Scrolls(axis)) continue;
                float view = resting[axis] + lead[axis];
                bool before = min[axis] < view - ScrollState.Rest;
                bool past = max[axis] > view + viewport[axis] + ScrollState.Rest;
                if (before == past) continue;
                bool bigger = max[axis] - min[axis] > viewport[axis];
                offset[axis] = (before != bigger ? min[axis] : max[axis] - viewport[axis]) - lead[axis];
                moves = true;
            }
            if (!moves) return false;
            offset = scroll.Clamp(offset);
            return offset != resting;
        }

        // Keeps what was asked of a scroll container for a pass to resolve (the latest ask wins), with the change it
        // was asked in, if any.
        private static void RequestScroll(ScrollState scroll, ScrollRequest kind, Vector2 offset, LayoutNode descendant, float anchor)
        {
            scroll.Request = kind;
            scroll.RequestOffset = offset;
            scroll.RequestTarget = descendant;
            scroll.RequestAnchor = anchor;
            scroll.RequestTransition = Current;
        }

        private static void ClearRequest(ScrollState scroll)
        {
            scroll.Request = ScrollRequest.None;
            scroll.RequestTarget = null;
            scroll.RequestTransition = null;
        }

        // Says, once, that a ScrollTo or ScrollIntoView (`kind`) of `descendant` was dropped, naming the call made.
        private static void WarnScrollTo(LayoutNode node, LayoutNode descendant, ScrollRequest kind)
        {
            if (s_warnedScrollTo) return;
            s_warnedScrollTo = true;
            string call = kind == ScrollRequest.IntoView ? "ScrollIntoView" : "ScrollTo";
            Debug.LogWarning($"{node.name}.{call}({descendant.name}): {descendant.name} is not laid out inside {node.name}, so it is not scrolled to. (Said once.)", node);
        }

        // Scrolls a container to `offset`: at once without a transition, or on an animation with no duration and no delay;
        // otherwise on the node's spring for the change (AnimationOf: its duration, bounce and delay), from where it is
        // drawn at the velocity it has, held by that change.
        private static void MoveScroll(NodeState state, Vector2 offset, MotionTransition transition)
        {
            var scroll = state.Scroll;
            var animation = AnimationOf(state, transition);
            if (transition == null || animation.AtOnce)
            {
                StopScrollAt(scroll, offset);
                return;
            }
            Spring.Parameters(animation, out float omega, out float zeta);
            SpringScroll(scroll, offset, omega, zeta, Mathf.Max(0f, animation.Delay), transition);
        }

        // Sets a scroll springing to `target` on the spring given, from where it is drawn at the velocity it has, held
        // by `transition` (or by none, letting go of any it was held by), and letting go of any press, glide or wheel.
        // At rest there already, there is nothing to move.
        private static void SpringScroll(ScrollState scroll, Vector2 target, float omega, float zeta, float delay, MotionTransition transition)
        {
            var offset = scroll.Offset;
            scroll.Press = null;
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
        // springing for: having `arrived` where it was springing to (stepped or skipped there), or cut short on its
        // way (sent somewhere else at once, or no longer scrolling that way).
        private static void StopScrollAt(ScrollState scroll, Vector2 offset, bool arrived = false)
        {
            var spring = scroll.Offset;
            spring.Target = scroll.OnAxes(offset);
            Stop(spring, arrived);
            scroll.Phase = ScrollPhase.Idle;
            scroll.Press = null;
            scroll.GlideX = scroll.GlideY = false;
            scroll.Wheeling = false;
        }

        // A press takes hold of a scroll: it stops where it is drawn (letting go of the change it was springing for, on
        // its way) and is held, its raw offset where it is drawn with the rubber band undone, moved by its shares of the
        // press's drag once one sets off.
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
            scroll.Raw = scroll.Unband(spring.Value);
            scroll.GlideX = scroll.GlideY = false;
            scroll.Wheeling = false;
        }

        // The press holding a scroll lets go of it at `velocity`. One that snaps (ScrollSnap) settles on the page or child
        // it goes to (SettleSnap). Otherwise, out of range it springs back to the end it is past (0.4 seconds, no
        // bounce), carrying that velocity; in range it glides on at it, to a stop, or on into an end, where it springs
        // back, or stops for what is above it to take its speed (NotePassOn). Let go of at a standstill in range, it
        // stays where it is.
        private static void LetGo(NodeState state, Vector2 velocity)
        {
            var scroll = state.Scroll;
            var spring = scroll.Offset;
            scroll.Press = null;
            if (state.Node.ScrollSnap != ScrollSnap.None)
            {
                velocity = scroll.OnAxes(velocity);
                SettleSnap(state, SnapTarget(state, spring.Value, velocity), velocity);
                return;
            }
            spring.Velocity = scroll.OnAxes(velocity);
            spring.Target = scroll.Clamp(spring.Value);
            spring.Omega = ScrollState.BounceOmega;
            spring.Zeta = 1f;
            spring.Delay = 0f;
            scroll.GlideX = scroll.Scrolls(0) && spring.Target.x == spring.Value.x;
            scroll.GlideY = scroll.Scrolls(1) && spring.Target.y == spring.Value.y;
            scroll.Phase = scroll.GlideX || scroll.GlideY ? ScrollPhase.Gliding : ScrollPhase.Springing;
            NotePassOn(state);
        }

        // Moves a scroll container's offset on, once a frame in play mode, as Advance does a spring: a glide or a spring
        // is stepped (bar the frame it set off from rest in) and put where it is going, at rest, once it is there. A glide
        // that ran into an end for what is above it to take its speed is queued for HandOn. A press lets go of it as its
        // drag ends or its pointer comes up (EndDrag), or in the next frame when its LayoutScroller has gone. The press
        // itself is not read for whether it is over: the Input System's UI module shares one event among a mouse's
        // buttons and overwrites it every frame the mouse moves. Outside play mode it is at the start. A changed offset is
        // queued for Scrolled, and its indicators are drawn for where it is now, their fades moving on only in the frame's
        // step.
        private static void StepScroll(NodeState state, bool playing, bool step, float dt)
        {
            var scroll = state.Scroll;
            if (!playing)
            {
                if (scroll.Phase != ScrollPhase.Idle || scroll.Offset.Value != Vector2.zero)
                    StopScrollAt(scroll, Vector2.zero);
            }
            else if ((scroll.Phase == ScrollPhase.Gliding || scroll.Phase == ScrollPhase.Springing)
                     && step && scroll.Offset.SetOff != Time.frameCount)
            {
                if (scroll.Step(dt))
                    StopScrollAt(scroll, scroll.Offset.Target, arrived: true);
                if (scroll.Impacted)
                    s_impacts.Add(state);
            }
            if (scroll.Offset.Value != scroll.Raised)
                QueueScrolled(state);
            DrawIndicators(state, playing, step ? dt : 0f);
        }

        // ── Snapping ─────────────────────────────────────────────────────────────

        // Scratch: one axis's snap points, in order.
        private static readonly List<float> s_snaps = new();

        // A container that snaps (ScrollSnap), let go of or handed a glide at `velocity` (its units a second, the way its
        // offset moves), settles on `target` (SnapTarget) on the spring it springs back from an end on, carrying that
        // velocity, bar as much of it towards the target as would swing it past (more than omega times the way left, for
        // a critically damped spring), so it never overshoots a page. It is Springing after; the caller holds its offset.
        private static void SettleSnap(NodeState state, Vector2 target, Vector2 velocity)
        {
            var scroll = state.Scroll;
            var spring = scroll.Offset;
            spring.Target = target;
            spring.Omega = ScrollState.BounceOmega;
            spring.Zeta = 1f;
            spring.Delay = 0f;
            for (int axis = 0; axis < 2; axis++)
            {
                float way = target[axis] - spring.Value[axis], v = velocity[axis];
                if (v * way > 0f)
                    velocity[axis] = Mathf.Sign(way) * Mathf.Min(Mathf.Abs(v), spring.Omega * Mathf.Abs(way));
            }
            spring.Velocity = scroll.OnAxes(velocity);
            scroll.GlideX = scroll.GlideY = false;
            scroll.Wheeling = false;
            scroll.Phase = ScrollPhase.Springing;
        }

        // Where a container that snaps settles from `from` (its offset as drawn) moving at `velocity`: on each axis it
        // scrolls, the snap point nearest it (SnapPoints), or, going faster than ScrollState.SnapFlickSpeed, the first
        // one past it the way it goes (the nearest when there is none that way), so a flick moves it on one page or child
        // and no further, as UIKit's paging and SwiftUI's viewAligned on a phone do. Past either end, that end.
        private static Vector2 SnapTarget(NodeState state, Vector2 from, Vector2 velocity)
        {
            var scroll = state.Scroll;
            var target = scroll.Clamp(from);
            for (int axis = 0; axis < 2; axis++)
            {
                if (!scroll.Scrolls(axis)) continue;
                SnapPoints(state, axis, s_snaps);
                float at = from[axis], v = velocity[axis];
                float nearest = s_snaps[0];
                for (int i = 1; i < s_snaps.Count; i++)
                {
                    if (Mathf.Abs(s_snaps[i] - at) < Mathf.Abs(nearest - at))
                        nearest = s_snaps[i];
                }
                target[axis] = Mathf.Abs(v) >= ScrollState.SnapFlickSpeed && NextSnap(at, v, out float next) ? next : nearest;
            }
            s_snaps.Clear();
            return target;
        }

        // The first snap point in s_snaps past `at` by more than the rest distance, the way `way` goes (by its sign).
        private static bool NextSnap(float at, float way, out float next)
        {
            next = at;
            if (way > 0f)
            {
                for (int i = 0; i < s_snaps.Count; i++)
                {
                    if (s_snaps[i] <= at + ScrollState.Rest) continue;
                    next = s_snaps[i];
                    return true;
                }
            }
            else if (way < 0f)
            {
                for (int i = s_snaps.Count - 1; i >= 0; i--)
                {
                    if (s_snaps[i] >= at - ScrollState.Rest) continue;
                    next = s_snaps[i];
                    return true;
                }
            }
            return false;
        }

        // The offsets a container that snaps rests at along one axis (0 is x, 1 is y), in order, into `points`: its start,
        // its end, and between them each whole page of what it shows (Pages), or where each child in its flow (laid out,
        // not floating) starts less its padding there (with what it reaches past the safe area), which lines that child
        // up where its content starts (Children), from the targets the last pass gave, a child's own Offset left out.
        private static void SnapPoints(NodeState state, int axis, List<float> points)
        {
            var scroll = state.Scroll;
            var node = state.Node;
            float range = scroll.Range[axis];
            points.Clear();
            points.Add(0f);
            if (node.ScrollSnap == ScrollSnap.Pages)
            {
                float page = scroll.Viewport[axis];
                if (page > ScrollState.Rest)
                {
                    for (float at = page; at < range - ScrollState.Rest; at += page)
                        points.Add(at);
                }
            }
            else
            {
                var inset = node.Padding + state.Reach;
                float padding = axis == 0 ? inset.Left : inset.Top;
                var transform = state.RectTransform;
                for (int i = 0; i < transform.childCount; i++)
                {
                    if (!transform.GetChild(i).TryGetComponent(out LayoutNode child) || !child.isActiveAndEnabled
                        || child.Floating.IsFloating || !s_states.TryGetValue(child, out var placed) || !placed.Seen
                        || placed.PassIndex < 0)
                        continue;
                    float shift = axis == 0 ? child.Offset.x : -child.Offset.y;
                    float at = placed.Position.Target[axis] - shift - placed.Size.Target[axis] * 0.5f - padding;
                    if (at > ScrollState.Rest && at < range - ScrollState.Rest)
                        points.Add(at);
                }
                points.Sort();
            }
            if (range > ScrollState.Rest)
                points.Add(range);
        }

        // ── Indicators ───────────────────────────────────────────────────────────

        // The indicators' shape, in the container's units: how thick each is, how far in from the edge it runs along,
        // how far in from the container's corners its track stops, and how short it gets on its own (rubber-banding it
        // gets shorter still, down to a dot as long as it is thick).
        private const float IndicatorThickness = 5f;
        private const float IndicatorInset = 3f;
        private const float IndicatorEnds = 8f;
        private const float IndicatorMinLength = 36f;

        // How fast an indicator fades in and out (all the way in a tenth and a quarter of a second), and how long its axis
        // stays still before it starts to fade out.
        private const float IndicatorFadeIn = 10f;
        private const float IndicatorFadeOut = 4f;
        private const float IndicatorHold = 0.5f;

        // Draws a scroll container's indicators where it is scrolled this frame (`dt` the time the frame stepped, 0 when
        // it is laid out again), as UIScrollView's: each axis's shows while that axis moves or a press holds the
        // container, and fades out once it has been still for IndicatorHold. It is as long against its track as what the
        // container shows is against what it scrolls, as far along as it is scrolled, and shorter by as far as it is
        // drawn past an end, staying at that end. Its track runs along the container's right edge (y) or bottom edge (x)
        // as drawn this frame, short of the corners, and of the other's track while both show, and clear of the safe area
        // the container reaches under (Reach), as iOS insets its indicators by it. None shows outside play mode, with
        // ShowsScrollIndicators off, or on an axis it cannot scroll along (its content fits).
        private static void DrawIndicators(NodeState state, bool playing, float dt)
        {
            var scroll = state.Scroll;
            var node = state.Node;
            bool shows = playing && node.ShowsScrollIndicators && scroll.Measured;
            bool x = shows && scroll.Scrolls(0) && scroll.Range.x > 0f;
            bool y = shows && scroll.Scrolls(1) && scroll.Range.y > 0f;
            var size = state.Parent != null ? Vector2.Max(state.Size.Value, Vector2.zero) : state.RectTransform.rect.size;
            var reach = state.Reach;
            var at = scroll.Offset.Value;
            for (int axis = 0; axis < 2; axis++)
            {
                ref var indicator = ref (axis == 0 ? ref scroll.IndicatorX : ref scroll.IndicatorY);
                bool moved = at[axis] != scroll.IndicatorAt[axis];
                scroll.IndicatorAt[axis] = at[axis];
                if (!(axis == 0 ? x : y))
                {
                    HideIndicator(indicator);
                    scroll.IndicatorShown[axis] = 0f;
                    continue;
                }

                float shown = scroll.IndicatorShown[axis];
                if (moved || scroll.Phase == ScrollPhase.Dragging)
                {
                    scroll.IndicatorStill[axis] = 0f;
                    shown = Mathf.Min(1f, shown + dt * IndicatorFadeIn);
                }
                else
                {
                    scroll.IndicatorStill[axis] += dt;
                    if (scroll.IndicatorStill[axis] > IndicatorHold)
                        shown = Mathf.Max(0f, shown - dt * IndicatorFadeOut);
                }
                scroll.IndicatorShown[axis] = shown;

                // The safe area it reaches under at the start and end of the track, and along the track's edge.
                float lead = axis == 0 ? reach.Left : reach.Top;
                float trail = axis == 0 ? reach.Right : reach.Bottom;
                float side = axis == 0 ? reach.Bottom : reach.Right;
                float extent = size[axis];
                float track = extent - lead - trail - 2f * IndicatorEnds
                    - ((axis == 0 ? y : x) ? IndicatorThickness + IndicatorInset : 0f);
                if (shown <= 0f || track <= IndicatorThickness)
                {
                    HideIndicator(indicator);
                    continue;
                }
                float range = scroll.Range[axis];
                float length = Mathf.Clamp(track * extent / (extent + range), Mathf.Min(IndicatorMinLength, track), track);
                float along;
                if (at[axis] < 0f)
                {
                    length = Mathf.Max(IndicatorThickness, length + at[axis]);
                    along = 0f;
                }
                else if (at[axis] > range)
                {
                    length = Mathf.Max(IndicatorThickness, length - (at[axis] - range));
                    along = track - length;
                }
                else
                {
                    along = (track - length) * at[axis] / range;
                }

                if (indicator == null)
                    indicator = NewIndicator(state, axis);
                if (!indicator.gameObject.activeSelf)
                    indicator.gameObject.SetActive(true);
                var rt = indicator.rectTransform;
                KeepLast(rt);
                // y's from the top right corner down its right edge; x's from the bottom left corner along its bottom edge.
                var corner = axis == 1 ? Vector2.one : Vector2.zero;
                if (rt.anchorMin != corner) rt.anchorMin = corner;
                if (rt.anchorMax != corner) rt.anchorMax = corner;
                if (rt.pivot != corner) rt.pivot = corner;
                var position = axis == 1
                    ? new Vector2(-(IndicatorInset + side), -(IndicatorEnds + lead + along))
                    : new Vector2(IndicatorEnds + lead + along, IndicatorInset + side);
                if (rt.anchoredPosition != position) rt.anchoredPosition = position;
                var sized = axis == 1 ? new Vector2(IndicatorThickness, length) : new Vector2(length, IndicatorThickness);
                if (rt.sizeDelta != sized) rt.sizeDelta = sized;
                if (indicator.color != node.ScrollIndicatorColor) indicator.color = node.ScrollIndicatorColor;
                float alpha = shown * shown * (3f - 2f * shown);
                if (indicator.canvasRenderer.GetAlpha() != alpha) indicator.canvasRenderer.SetAlpha(alpha);
            }
        }

        // A container's indicator for one axis (0 is x, 1 is y): an object of its own inside it, hidden and never saved,
        // on its layer, taking no pointer.
        private static LayoutScrollIndicator NewIndicator(NodeState state, int axis)
        {
            var go = new GameObject(axis == 0 ? "Scroll Indicator X" : "Scroll Indicator Y", typeof(RectTransform));
            go.hideFlags = HideFlags.HideAndDontSave;
            go.layer = state.Node.gameObject.layer;
            go.transform.SetParent(state.RectTransform, false);
            var indicator = go.AddComponent<LayoutScrollIndicator>();
            indicator.raycastTarget = false;
            return indicator;
        }

        // Keeps an indicator drawn over what its container scrolls: after every sibling that is not an indicator, where a
        // child added or moved to the end since (a new row, a card dropped in) would otherwise be drawn over it.
        private static void KeepLast(Transform transform)
        {
            var parent = transform.parent;
            for (int i = transform.GetSiblingIndex() + 1; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).TryGetComponent(out LayoutScrollIndicator _)) continue;
                transform.SetAsLastSibling();
                return;
            }
        }

        // Hides a container's indicators at once (it stops scrolling, or is disabled), to fade in afresh when they next show.
        private static void HideIndicators(ScrollState scroll)
        {
            HideIndicator(scroll.IndicatorX);
            HideIndicator(scroll.IndicatorY);
            scroll.IndicatorShown = Vector2.zero;
        }

        private static void HideIndicator(LayoutScrollIndicator indicator)
        {
            if (indicator != null && indicator.gameObject.activeSelf)
                indicator.gameObject.SetActive(false);
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

        // ── Dragging ─────────────────────────────────────────────────────────────

        // A drag is shared by participants: scroll containers (a node whose Scroll is not None; they need no code) and
        // drag owners (an ILayoutDraggable on the node it moves: a sheet's height, a card's pull), UIKit's behaviour on
        // Android's nested scrolling. UGUI gives all of a drag to the innermost drag handler under the press, which is the
        // LayoutScroller the system puts on each container and on each owner's node, and that hands it here. As it sets
        // off it is locked to the axis it set off along (both, when the first participant up from the press takes both),
        // and its chain is built: the participants up the hierarchy from the press that take that axis, innermost first (a
        // node's container, then its owner, which sits just outside its own scroll), as far as the first object holding a
        // drag handler that is not the system's, which UGUI gives the drags that start below it. With none, the drag goes
        // to that handler, or else to the participants that take the other axis. An owner in the chain whose
        // PassOnMidDrag is false makes the drag one participant's, the innermost with room to move the way it sets off
        // (Decide); otherwise each move is shared out (Share). Let go, whatever moved last takes the pointer's velocity
        // (EndDrag), and a glide that then runs into an end hands its speed on to what is above it (HandOn). The system
        // never catches or flings an owner: its own code does, as it is told OnBeginDrag and OnRelease. The wheel goes
        // to containers alone (OnWheel).

        // How a scroll container takes its share of a move (Share): what brings it back to the end it is stretched past;
        // what keeps it within range; or all of it, past an end.
        private enum Taking
        {
            Relax,
            Within,
            Stretch,
        }

        // One participant in a drag: State's scroll container, or its drag owner (Owner).
        private sealed class Participant
        {
            public NodeState State;

            // The owner as it was when it joined; null for a scroll container.
            public ILayoutDraggable Owner;

            // The axes it takes (its Scroll or its DragAxis), and those of them it moves along in this drag.
            public bool TakesX;
            public bool TakesY;
            public bool X;
            public bool Y;

            // A container: whether this drag holds its offset (a move has reached it, or the press stopped it), and
            // whether something has let it go of it since (a ScrollTo, it stopping scrolling), after which it is passed
            // over for the rest of the drag.
            public bool Held;
            public bool Dropped;

            // An owner: whether it has been told OnBeginDrag, and so is told OnRelease.
            public bool Begun;

            // Whether it took some of the move being shared.
            public bool Took;
        }

        // A press that stopped or caught something, or a drag the system took, until it lets go.
        private sealed class Drag
        {
            public PointerEventData Press;

            // The LayoutScroller UGUI sends it to, and the node that is on (the one the press reached).
            public LayoutScroller Scroller;
            public NodeState Origin;

            // Before it sets off, what the press stopped or took hold of; after, its chain, innermost first.
            public readonly List<Participant> Parts = new();
            public bool SetOff;

            // When it is one participant's, that one (null when it is shared): the chain is then that one alone.
            public Participant Sole;

            // The axes it moves along.
            public bool X;
            public bool Y;

            // What took the last part of the last move anything took (at first, when it is one participant's, that one).
            public Participant Last;

            // Where the pointer was as it set off, and whether no move has come since: the move UGUI sends with the set-off
            // is the one that crossed its threshold, and it is not shared, so nothing the drag moves jumps by it.
            public Vector2 SetOffAt;
            public bool Fresh;

            // The pointer's velocity in world units a second, smoothed over the last few moves as DragGesture's is, and
            // when it last moved (unscaled seconds).
            public Vector3 Velocity;
            public bool Sampled;
            public float LastMoved;
        }

        // What is left of a move smaller than this much of it is rounding from turning it into a participant's units and
        // back, not something left over for the next participant.
        private const float Rounding = 1e-4f;

        // The drags and presses under way, and spare ones and participants to reuse, so a drag allocates nothing.
        private static readonly List<Drag> s_drags = new();
        private static readonly Stack<Drag> s_dragPool = new();
        private static readonly Stack<Participant> s_partPool = new();

        // Scratch: a chain being built, what a press stopped or caught that is not in it, the nodes a press, a drag or a
        // wheel reaches (Reach), and one object's event handlers.
        private static readonly List<Participant> s_chain = new();
        private static readonly List<Participant> s_left = new();
        private static readonly List<NodeState> s_reach = new();
        private static readonly List<IEventSystemHandler> s_handlers = new();

        // Scroll containers whose glide ran into an end in this frame's step, for what is above them to take its speed.
        private static readonly List<NodeState> s_impacts = new();

        // A press on a node with a LayoutScroller, or on anything in it that does not take drags itself, sent before any
        // drag. Every scroll container it reaches (Reach) that is gliding from a flick, or springing back from an end,
        // stops where it is drawn, whichever way it scrolls, as touching a UIScrollView, or one inside it, stops it. A
        // wheel's step or a scroll sent by ScrollTo is left to finish, and the press clicks as any would: stopping it would
        // only swallow a click made just after. Then every drag owner it reaches whose node is moving is asked whether it
        // takes hold of the press (TakesHold), where it landed being the owner's to judge, and one that does is told at once
        // (OnBeginDrag), to stop where it is drawn: a sheet takes hold of a press on its header and not of one on its list,
        // so a row tapped while the sheet settles still clicks, for the same reason. The containers go first, the system's
        // own work, so an owner's code runs once they are all stopped. A press that stops anything, or that anything takes
        // hold of, is only that: a button under it does not click, and it becomes the LayoutScroller's own, so its release
        // comes back to it (UGUI sends a pointer-up only to what took the press), and whatever took it first is let go of
        // at once. What it stopped or caught is held until a drag sets off from it or it lets go (OnPointerUp). Only the
        // left button (and touch) drags, as with ScrollRect.
        internal static void OnPointerPress(LayoutScroller scroller, LayoutNode node, PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !TryPointer(node, out var origin)) return;
            // A press of the same pointer that was never let go of (its release went elsewhere) lets go first.
            var stale = FindDrag(eventData);
            if (stale != null)
                EndDrag(stale, Vector3.zero);

            Drag drag = null;
            Reach(origin, false);
            for (int i = 0; i < s_reach.Count; i++)
            {
                var state = s_reach[i];
                var scroll = state.Scroll;
                if (scroll == null || scroll.Axis == ScrollAxis.None) continue;
                bool flicked = scroll.Phase == ScrollPhase.Gliding
                    || (scroll.Phase == ScrollPhase.Springing && !scroll.Wheeling && scroll.Offset.Transition == null);
                if (!flicked) continue;
                drag ??= StartDrag(eventData, scroller, origin);
                TakeHold(scroll, eventData);
                var part = NewPart(state, null, scroll.Scrolls(0), scroll.Scrolls(1));
                part.Held = true;
                drag.Parts.Add(part);
            }
            for (int i = 0; i < s_reach.Count; i++)
            {
                var state = s_reach[i];
                var owner = state.Draggable;
                if (!Live(state) || !IsLive(owner) || owner.DragAxis == ScrollAxis.None || !IsMoving(state)
                    || OwnerHeld(state, null) || !owner.TakesHold(eventData))
                    continue;
                drag ??= StartDrag(eventData, scroller, origin);
                var axis = owner.DragAxis;
                var part = NewPart(state, owner, Along(axis, 0), Along(axis, 1));
                part.Begun = true;
                drag.Parts.Add(part);
                owner.OnBeginDrag();
            }
            if (drag == null) return;

            eventData.eligibleForClick = false;
            var self = scroller.gameObject;
            if (eventData.pointerPress != self)
            {
                if (eventData.pointerPress != null)
                    ExecuteEvents.Execute(eventData.pointerPress, eventData, ExecuteEvents.pointerUpHandler);
                eventData.pointerPress = self;
            }
            FlushIfIdle();
        }

        // The pointer let go. A press that stopped or caught something and never dragged lets go of it here, at a
        // standstill: a container in range stays, one past an end springs back, and an owner is told OnRelease(zero). One
        // that dragged lets go in OnDragEnd, which UGUI sends after this; and a press on a Button that shares a
        // LayoutScroller's object comes here too, with nothing to let go of. Only the LayoutScroller the press became
        // (`scroller`) lets it go: the one on a pressed Button's object above it (a sheet's, which a tap on its list
        // reaches) hears the pointer-up that OnPointerPress sends that Button as the press is taken over, which is not the
        // pointer letting go.
        internal static void OnPointerUp(LayoutScroller scroller, PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !Application.isPlaying) return;
            var drag = FindDrag(eventData);
            if (drag != null && !drag.SetOff && drag.Scroller == scroller)
                EndDrag(drag, Vector3.zero);
        }

        // A drag setting off past UGUI's drag threshold from a press on `node` (or handed to its LayoutScroller by a drag
        // handler below that did not want it). Its axis is the one it set off along, in the node's units, or both when the
        // first participant up from the press takes both, and its chain is the participants up from the press that take
        // one of its axes (Collect, Lock). With none, it goes to the first drag handler above that is not the system's, as
        // it sets off: passed on part way through, that handler would start mid-gesture with none of the drag's velocity.
        // With none of those either the lock gives way, and the participants that take the other axis take it, by how far
        // it goes that way; with none of those, it is dropped. Then the hand-off switch (Decide); what the press stopped
        // or caught that is not in the chain is let go of at a standstill (Join); each owner in the chain not begun yet is
        // begun; and the press stops being a click: once a drag sets off, the Input System's UI module cancels a click
        // only when what was pressed is not the drag handler's object, and a Button can share one with a LayoutScroller
        // (an App Store card does), which would otherwise click after dragging the list under it. A container takes hold
        // of its offset only as a move first reaches it (Share), so an outer list springing for a ScrollTo is left alone
        // by a drag that never reaches it.
        internal static void OnDragSetOff(LayoutScroller scroller, LayoutNode node, PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !TryPointer(node, out var origin)) return;
            var drag = FindDrag(eventData);
            if (drag != null && drag.SetOff)
            {
                EndDrag(drag, Vector3.zero);
                drag = null;
            }
            drag ??= StartDrag(eventData, scroller, origin);
            if (!WorldMove(origin.RectTransform, eventData.pressPosition, eventData.position, eventData.pressEventCamera, out var setOff))
            {
                EndDrag(drag, Vector3.zero);
                return;
            }

            var foreign = Reach(origin, false);
            var chain = s_chain;
            Collect(drag, chain);
            Vector2 local = origin.RectTransform.InverseTransformVector(setOff);
            bool both = chain.Count > 0 && chain[0].TakesX && chain[0].TakesY;
            drag.X = both || Mathf.Abs(local.x) > Mathf.Abs(local.y);
            drag.Y = both || !drag.X;
            bool taken = Lock(drag, chain);
            if (!taken && foreign != null)
            {
                RecycleAll(chain);
                EndDrag(drag, Vector3.zero);
                eventData.pointerDrag = foreign;
                ExecuteEvents.Execute(foreign, eventData, ExecuteEvents.beginDragHandler);
                return;
            }
            if (!taken && !both)
            {
                drag.X = !drag.X;
                drag.Y = !drag.Y;
                taken = Lock(drag, chain);
            }
            if (!taken)
            {
                RecycleAll(chain);
                EndDrag(drag, Vector3.zero);
                return;
            }
            // Only what moves along one of its axes takes part.
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                if (chain[i].X || chain[i].Y) continue;
                Recycle(chain[i]);
                chain.RemoveAt(i);
            }
            Decide(drag, chain, setOff);

            drag.SetOff = true;
            drag.SetOffAt = eventData.position;
            drag.Fresh = true;
            drag.Velocity = Vector3.zero;
            drag.Sampled = false;
            drag.LastMoved = Time.unscaledTime;
            eventData.eligibleForClick = false;
            Join(drag, chain);
            drag.Last = drag.Sole;
            var parts = drag.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.Owner == null || part.Begun || !IsLive(part.Owner)) continue;
                part.Begun = true;
                part.Owner.OnBeginDrag();
            }
            FlushIfIdle();
        }

        // A move of a drag the system took: the pointer's velocity is sampled from it, over the time since the pointer last
        // moved (which can be several frames, when the pointer reports less often than the game draws), smoothed over the
        // last few moves, and it is shared out among the drag's participants (Share). A move is UGUI's pointer delta
        // (which add up exactly to where the pointer went), taken onto the plane of the node pressed in world units.
        internal static void OnDragMove(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !Application.isPlaying) return;
            var drag = FindDrag(eventData);
            if (drag == null || !drag.SetOff) return;
            if (drag.Fresh)
            {
                drag.Fresh = false;
                if (eventData.position == drag.SetOffAt) return;
            }
            var plane = drag.Origin.RectTransform;
            if (plane == null || !WorldMove(plane, eventData.position - eventData.delta, eventData.position,
                    eventData.pressEventCamera, out var move))
                return;
            float now = Time.unscaledTime, dt = now - drag.LastMoved;
            if (dt > 1e-4f)
            {
                var sample = move / dt;
                drag.Velocity = drag.Sampled ? Vector3.Lerp(drag.Velocity, sample, 0.5f) : sample;
                drag.Sampled = true;
            }
            drag.LastMoved = now;
            Share(drag, move);
            FlushIfIdle();
        }

        // A drag the system took let go: at the pointer's velocity, or at a standstill if it was held still first.
        internal static void OnDragEnd(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !Application.isPlaying) return;
            var drag = FindDrag(eventData);
            if (drag == null || !drag.SetOff) return;
            EndDrag(drag, Time.unscaledTime - drag.LastMoved > ScrollState.StillFor ? Vector3.zero : drag.Velocity);
        }

        // Every participant a drag from the press can reach (Reach, just made), innermost first: a node's scroll
        // container, then its drag owner, which sits just outside its own scroll. A container another press holds, and an
        // owner another drag has begun, are left out (a participant already held takes no part in a new drag), as is an
        // owner that is disabled or says DragAxis None.
        private static void Collect(Drag drag, List<Participant> into)
        {
            for (int i = 0; i < s_reach.Count; i++)
            {
                var state = s_reach[i];
                var scroll = state.Scroll;
                if (scroll != null && scroll.Axis != ScrollAxis.None
                    && (scroll.Phase != ScrollPhase.Dragging || scroll.Press == drag.Press))
                    into.Add(NewPart(state, null, scroll.Scrolls(0), scroll.Scrolls(1)));
                var owner = state.Draggable;
                if (!IsLive(owner) || OwnerHeld(state, drag)) continue;
                var axis = owner.DragAxis;
                if (axis != ScrollAxis.None)
                    into.Add(NewPart(state, owner, Along(axis, 0), Along(axis, 1)));
            }
        }

        // Gives each participant the drag's axes it takes, which it moves along; returns whether any moves along one.
        private static bool Lock(Drag drag, List<Participant> chain)
        {
            bool any = false;
            for (int i = 0; i < chain.Count; i++)
            {
                var part = chain[i];
                part.X = drag.X && part.TakesX;
                part.Y = drag.Y && part.TakesY;
                any |= part.X || part.Y;
            }
            return any;
        }

        // The hand-off switch. With no owner in the chain whose PassOnMidDrag is false, the drag is shared (Share). With
        // one, the drag is one participant's until it lets go (Sole), chosen as it sets off, as nested UIScrollViews and
        // Android's nested scrolling give a drag to the innermost view that can scroll the way it goes. An owner this
        // press took hold of (TakesHold) has it, the innermost if more than one did. Otherwise the innermost such owner
        // decides over everything inside it, nothing outside it taking part: the drag is the first participant from the
        // press up to it, innermost first, with room to move the way the drag set off within its own range (HasRoom), a
        // container then scrolling, rubber-banding at either end, and an owner banding itself; and with nothing that has
        // room, it is the innermost participant's, as a UIScrollView bounces the drag nothing outside it can take: a list
        // at its bottom bounces in a sheet at full, and a drag on the sheet's own header stretches it. Whether anything
        // is moving plays no part: a sheet settling to a detent does not take a drag its list has room for. What is not
        // chosen is not begun, and does not move until the next drag.
        private static void Decide(Drag drag, List<Participant> chain, Vector3 setOff)
        {
            int decider = -1;
            for (int i = 0; i < chain.Count && decider < 0; i++)
            {
                if (chain[i].Owner != null && !chain[i].Owner.PassOnMidDrag)
                    decider = i;
            }
            if (decider < 0) return;

            int sole = -1;
            for (int i = 0; i < chain.Count && sole < 0; i++)
            {
                if (chain[i].Owner != null && Caught(drag, chain[i].State))
                    sole = i;
            }
            for (int i = 0; i <= decider && sole < 0; i++)
            {
                if (HasRoom(chain[i], setOff))
                    sole = i;
            }
            if (sole < 0)
                sole = 0;
            drag.Sole = chain[sole];
            for (int i = 0; i < chain.Count; i++)
            {
                if (i != sole)
                    Recycle(chain[i]);
            }
            chain.Clear();
            chain.Add(drag.Sole);
        }

        // Whether a participant has room to move the way a drag set off (`setOff`, world units) within its own range: a
        // scroll container that is not at its end that way on one of the drag's axes it moves along (a list at its top
        // has none for a drag pulling down, one held past an end none further past it, and one not laid out yet none at
        // all), or an owner that says so (ILayoutDraggable.HasRoom), asked in its node's parent's units as OnDrag is.
        private static bool HasRoom(Participant part, Vector3 setOff)
        {
            var state = part.State;
            if (part.Owner == null)
            {
                var scroll = state.Scroll;
                var way = OffsetMoveOf(state, setOff);
                return (part.X && !scroll.AtEnd(0, way.x)) || (part.Y && !scroll.AtEnd(1, way.y));
            }
            var space = state.RectTransform.parent;
            if (space == null) return false;
            var direction = Mask(space.InverseTransformVector(setOff), part.X, part.Y);
            return direction != Vector2.zero && part.Owner.HasRoom(direction);
        }

        // Whether the owner on a node took hold of a drag's press (it is begun already), read before the chain takes over.
        private static bool Caught(Drag drag, NodeState state)
        {
            var parts = drag.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].Owner != null && parts[i].Begun && parts[i].State == state)
                    return true;
            }
            return false;
        }

        // The chain takes over from what the press stopped or caught: each carries on in it as the same participant
        // (held, or begun and not begun again), and what is not in it is let go of at a standstill there and then,
        // containers first, as EndDrag does.
        private static void Join(Drag drag, List<Participant> chain)
        {
            var parts = drag.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                var pressed = parts[i];
                Participant same = null;
                for (int j = 0; j < chain.Count && same == null; j++)
                {
                    var part = chain[j];
                    if (part.State == pressed.State && (part.Owner == null) == (pressed.Owner == null))
                        same = part;
                }
                if (same == null)
                {
                    s_left.Add(pressed);
                    continue;
                }
                same.Held = pressed.Held;
                same.Begun = pressed.Begun;
                Recycle(pressed);
            }
            parts.Clear();
            parts.AddRange(chain);
            chain.Clear();

            for (int i = 0; i < s_left.Count; i++)
            {
                var part = s_left[i];
                if (part.Owner == null && Holds(drag, part))
                    LetGo(part.State, Vector2.zero);
            }
            for (int i = 0; i < s_left.Count; i++)
            {
                var part = s_left[i];
                if (part.Owner != null && part.Begun && Live(part.State) && IsLive(part.Owner))
                    part.Owner.OnRelease(Vector2.zero);
            }
            RecycleAll(s_left);
        }

        // Shares one move of a drag (world units) out among its participants, UIKit's order on Android's mechanism. Each
        // takes its share in its own units, through its RectTransform as drawn (a container its own, an owner its node's
        // parent's, where its Offset and Height are), and what it leaves goes on in world units. A drag that is one
        // participant's goes to it alone: a container takes all of it, rubber-banding past its ends, and an owner is
        // offered it first and then what it left, and what it leaves is dropped. Otherwise: a container stretched past an
        // end takes first what brings it back to that end; then the owners, outermost first, are offered it before
        // anything inside them scrolls (a sheet below full grows, a pulled card comes back); then up the chain, innermost
        // first, each container takes what its range allows and each owner is offered what is left (a list scrolls to its
        // top, then the sheet shrinks); and what is still left stretches the outermost container on its axis past its end.
        // Owners never get the system's rubber band: one that takes a move it has no room for bands it itself, and as it
        // is outside the containers it owns, that keeps the stretch at the outermost. Each container the drag holds then
        // moves at the pointer's velocity if it took some of the move (times its rubber band's slope), and is held still
        // if it took none, so a change that retargets it sets off at the speed it is moving.
        private static void Share(Drag drag, Vector3 move)
        {
            var parts = drag.Parts;
            float tiny = move.magnitude * Rounding;
            for (int i = 0; i < parts.Count; i++)
                parts[i].Took = false;

            if (drag.Sole != null)
            {
                var part = drag.Sole;
                if (part.Owner == null)
                {
                    ScrollShare(drag, part, move, Taking.Stretch, true, true);
                }
                else
                {
                    var took = Offer(part, move, true);
                    Offer(part, WithoutRounding(move - took, tiny), false);
                }
            }
            else
            {
                Participant last = null;
                for (int i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    if (part.Owner == null && Stretched(drag, part))
                        Deduct(ref move, ref last, part, ScrollShare(drag, part, move, Taking.Relax, true, true), tiny);
                }
                for (int i = parts.Count - 1; i >= 0; i--)
                {
                    var part = parts[i];
                    if (part.Owner != null)
                        Deduct(ref move, ref last, part, Offer(part, move, true), tiny);
                }
                for (int i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    var taken = part.Owner == null
                        ? ScrollShare(drag, part, move, Taking.Within, true, true)
                        : Offer(part, move, false);
                    Deduct(ref move, ref last, part, taken, tiny);
                }
                for (int axis = 0; axis < 2; axis++)
                {
                    for (int i = parts.Count - 1; i >= 0; i--)
                    {
                        var part = parts[i];
                        if (part.Owner != null || !(axis == 0 ? part.X : part.Y) || !Usable(drag, part)) continue;
                        Deduct(ref move, ref last, part, ScrollShare(drag, part, move, Taking.Stretch, axis == 0, axis == 1), tiny);
                        break;
                    }
                }
                if (last != null)
                    drag.Last = last;
            }

            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.Owner != null || !Holds(drag, part)) continue;
                var scroll = part.State.Scroll;
                scroll.Offset.Velocity = part.Took ? Vector2.Scale(OffsetShare(part, drag.Velocity), scroll.Slope()) : Vector2.zero;
            }
        }

        // What a participant took comes off the move; one that took some is what moved last, so far.
        private static void Deduct(ref Vector3 move, ref Participant last, Participant part, Vector3 taken, float tiny)
        {
            if (taken == Vector3.zero) return;
            move = WithoutRounding(move - taken, tiny);
            last = part;
        }

        // A scroll container's share of a move (world units), in its offset's units on the drag's axes it moves along (of
        // those, `x` and `y`), taken as `taking` says once the drag holds it (it takes hold as the first share reaches it,
        // stopping whatever it was doing and letting go of any change it was springing for). Returns what it took, in
        // world units.
        private static Vector3 ScrollShare(Drag drag, Participant part, Vector3 move, Taking taking, bool x, bool y)
        {
            if (!Usable(drag, part)) return Vector3.zero;
            var share = OffsetShare(part, move, x, y);
            if (share == Vector2.zero) return Vector3.zero;
            var scroll = part.State.Scroll;
            if (!part.Held)
            {
                TakeHold(scroll, drag.Press);
                part.Held = true;
            }
            var taken = taking == Taking.Relax ? scroll.Relax(share)
                : taking == Taking.Within ? scroll.Take(share)
                : scroll.Stretch(share);
            if (taken == Vector2.zero) return Vector3.zero;
            part.Took = true;
            return WorldOfOffsetMove(part.State, taken);
        }

        // Offers a drag owner a move (world units) on the drag's axes it moves along, in its node's parent's units, before
        // the containers inside it take any (`first`) or with what they left, and returns what it took, in world units:
        // never more than it was offered, nor the other way. One that has gone, or whose node takes no pointer now (moving
        // for a change that is not interactive), is passed over.
        private static Vector3 Offer(Participant part, Vector3 move, bool first)
        {
            var state = part.State;
            if (!part.Begun || !Live(state) || !IsLive(part.Owner) || state.PassBlocked) return Vector3.zero;
            var space = state.RectTransform.parent;
            if (space == null) return Vector3.zero;
            var offered = Mask(space.InverseTransformVector(move), part.X, part.Y);
            if (offered == Vector2.zero) return Vector3.zero;
            var took = part.Owner.OnDrag(offered, first);
            took = new Vector2(Within(took.x, offered.x), Within(took.y, offered.y));
            if (took == Vector2.zero) return Vector3.zero;
            part.Took = true;
            return space.TransformVector(took);
        }

        // What an owner says it took of what it was offered on one axis, kept between nothing and all of it.
        private static float Within(float took, float offered) =>
            offered >= 0f ? Mathf.Clamp(took, 0f, offered) : Mathf.Clamp(took, offered, 0f);

        // Whether a scroll container can take a share of a drag's move now: the drag holds it, or it is free to take hold
        // of (no other press holds it). One that something let go of since the drag held it (a ScrollTo or ScrollOffset,
        // it stopping scrolling, its node going) is passed over from then on; one that takes no pointer now (moving for a
        // change that is not interactive), only for now.
        private static bool Usable(Drag drag, Participant part)
        {
            if (part.Dropped) return false;
            if (part.Held && !Holds(drag, part))
            {
                part.Dropped = true;
                return false;
            }
            var state = part.State;
            if (!part.Held && (!Live(state) || state.Scroll.Axis == ScrollAxis.None || state.Scroll.Phase == ScrollPhase.Dragging))
                return false;
            return !state.PassBlocked;
        }

        // Whether a drag holds a scroll container it took hold of: still held by its press, and still scrolling.
        private static bool Holds(Drag drag, Participant part)
        {
            if (!part.Held || part.Dropped || !Live(part.State)) return false;
            var scroll = part.State.Scroll;
            return scroll.Axis != ScrollAxis.None && scroll.Phase == ScrollPhase.Dragging && scroll.Press == drag.Press;
        }

        // Whether a scroll container is stretched past an end: by its raw offset while the drag holds it, and where it is
        // drawn otherwise (springing back from past one).
        private static bool Stretched(Drag drag, Participant part)
        {
            var scroll = part.State.Scroll;
            var raw = Holds(drag, part) ? scroll.Raw : scroll.Offset.Value;
            return raw != scroll.Clamp(raw);
        }

        // A drag or a press lets go, at `velocity` (world units a second; zero for a standstill), which goes to what moved
        // last: a container glides at it (in its units, times its rubber band's slope if it is stretched, so one past an
        // end springs back carrying what is drawn), and an owner is told OnRelease(velocity). Everything else is let go of
        // at a standstill: a container stays where it is (or springs back from past an end), and an owner is told
        // OnRelease(zero) and settles. Every container first, then the owners, innermost first: an owner's OnRelease may
        // start a change that moves a container (a card closing scrolls its story back to the top inside Animate), which
        // letting that container go after would undo. An owner that does not take the velocity passes it on up the chain:
        // a container outside it glides on at it if it can (GlideOn), and an owner outside it is offered it in place of
        // zero. What has gone since (a node disabled, an owner disabled or destroyed) is passed over.
        private static void EndDrag(Drag drag, Vector3 velocity)
        {
            s_drags.Remove(drag);
            var parts = drag.Parts;
            var last = drag.SetOff ? drag.Last : null;
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.Owner != null || !Holds(drag, part)) continue;
                var scroll = part.State.Scroll;
                LetGo(part.State, part == last ? Vector2.Scale(OffsetShare(part, velocity), scroll.Slope()) : Vector2.zero);
            }
            var carry = Vector3.zero;
            float tiny = velocity.magnitude * Rounding;
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part.Owner == null)
                {
                    if (carry != Vector3.zero && Live(part.State))
                        carry = WithoutRounding(carry - GlideOn(part.State, carry, part.X, part.Y), tiny);
                    continue;
                }
                if (!part.Begun || !Live(part.State) || !IsLive(part.Owner)) continue;
                if (part == last)
                    carry = part.Owner.OnRelease(velocity) ? Vector3.zero : velocity;
                else if (carry != Vector3.zero)
                    carry = part.Owner.OnRelease(carry) ? Vector3.zero : carry;
                else
                    part.Owner.OnRelease(Vector2.zero);
            }
            Recycle(drag);
            FlushIfIdle();
        }

        // A drag whose LayoutScroller has gone (disabled or destroyed) never hears UGUI's OnEndDrag, nor a press its
        // pointer-up: it lets go here, at a standstill, owners told OnRelease(zero) too, so a sheet is never left between
        // detents.
        private static void EndLostDrags()
        {
            for (int i = s_drags.Count - 1; i >= 0; i--)
            {
                if (i >= s_drags.Count) continue;
                var drag = s_drags[i];
                if (drag.Scroller == null || !drag.Scroller.isActiveAndEnabled)
                    EndDrag(drag, Vector3.zero);
            }
        }

        // Hands a scroll container a glide at `speed` (world units a second, the way its content moves, as a finger moving
        // it would), on the axes it scrolls (of `x` and `y`) where it is at rest or gliding and not at its end the way the
        // speed goes: it glides on at it from where it is drawn, and notes what above it could take the glide on in turn.
        // One that snaps (ScrollSnap) moves on to the page or child it goes to instead, taking all of the speed, or none
        // when that is where it is. Held by a press, or springing, it takes none. Returns what it took, in world units a
        // second.
        private static Vector3 GlideOn(NodeState state, Vector3 speed, bool x, bool y)
        {
            var scroll = state.Scroll;
            if (scroll == null || scroll.Axis == ScrollAxis.None
                || (scroll.Phase != ScrollPhase.Idle && scroll.Phase != ScrollPhase.Gliding))
                return Vector3.zero;
            var velocity = OffsetMoveOf(state, speed);
            var value = scroll.Offset.Value;
            var taken = Vector2.zero;
            for (int axis = 0; axis < 2; axis++)
            {
                float v = velocity[axis];
                if (!(axis == 0 ? x : y) || !scroll.Scrolls(axis) || v == 0f || scroll.AtEnd(axis, v)) continue;
                // An axis springing back from past an end while the other glides carries on back.
                if (value[axis] < 0f || value[axis] > scroll.Range[axis]) continue;
                taken[axis] = v;
            }
            if (taken == Vector2.zero) return Vector3.zero;
            if (state.Node.ScrollSnap != ScrollSnap.None)
            {
                var target = SnapTarget(state, value, taken);
                if ((target - value).sqrMagnitude < ScrollState.Rest * ScrollState.Rest) return Vector3.zero;
                SettleSnap(state, target, taken);
                Hold(scroll.Offset, null);
                return WorldOfOffsetMove(state, taken);
            }
            scroll.Glide(taken);
            Hold(scroll.Offset, null);
            NotePassOn(state);
            return WorldOfOffsetMove(state, taken);
        }

        // Notes, as a scroll container is let go of or handed a glide, whether anything above it on each axis could take
        // the speed of a glide of it that runs into an end (ScrollState.PassX and PassY): a scroll container above it that
        // scrolls that way, or a drag owner that moves that way with PassOnMidDrag true (its own first, as it sits just
        // outside its scroll). Inside an owner with PassOnMidDrag false that moves that way nothing is, on that axis,
        // wherever it is: a drag on that axis inside such an owner is one participant's, so a glide from it bounces at the
        // end and passes nothing on, not even to a container between it and that owner. With nothing, the glide bounces
        // at the end, as a lone container's does.
        private static void NotePassOn(NodeState state)
        {
            bool x = false, y = false, soleX = false, soleY = false;
            Transform own = state.RectTransform;
            for (var t = own; t != null && !(soleX && soleY); t = t.parent)
            {
                if (!t.TryGetComponent(out LayoutNode node) || !s_states.TryGetValue(node, out var above)) continue;
                if (t != own && above.Scroll != null)
                {
                    x |= above.Scroll.Scrolls(0);
                    y |= above.Scroll.Scrolls(1);
                }
                var owner = above.Draggable;
                if (!IsLive(owner)) continue;
                var axis = owner.DragAxis;
                if (axis == ScrollAxis.None) continue;
                if (owner.PassOnMidDrag)
                {
                    x |= Along(axis, 0);
                    y |= Along(axis, 1);
                }
                else
                {
                    soleX |= Along(axis, 0);
                    soleY |= Along(axis, 1);
                }
            }
            var scroll = state.Scroll;
            scroll.PassX = x && !soleX && scroll.Scrolls(0);
            scroll.PassY = y && !soleY && scroll.Scrolls(1);
        }

        // Hands on the speed of each glide that ran into an end in this frame's step with something above it to take it,
        // once the frame is laid out: owners' code may start changes, and Animate cannot run inside a pass. Up the
        // hierarchy from the container, walked again now (its own owner first, as it sits just outside its scroll), each
        // scroll container that can glide on at it does (GlideOn), and each owner with PassOnMidDrag true is offered the
        // part along its DragAxis (OnRelease) and takes it by returning true, until it is all taken or an owner with
        // PassOnMidDrag false that moves that way is met; an owner another press is dragging is passed over. What nothing
        // takes bounces the container at the end it ran into, at that speed, as it would with nothing above it: not the
        // outermost on that axis, since one above that declined may be nowhere near its own end. Returns whether anything
        // took any, for the frame to be laid out again so that the taker is drawn this frame from where it is (only the
        // rest of this one frame's motion is lost).
        private static bool HandOn()
        {
            bool took = false;
            while (s_impacts.Count > 0)
            {
                var state = s_impacts[0];
                s_impacts.RemoveAt(0);
                var scroll = state.Scroll;
                if (!Live(state) || scroll == null || !scroll.Impacted) continue;
                var impact = scroll.Impact;
                scroll.Impacted = false;
                scroll.Impact = Vector2.zero;
                var left = WorldOfOffsetMove(state, impact);
                float tiny = left.magnitude * Rounding;
                Transform own = state.RectTransform;
                for (var t = own; t != null && left != Vector3.zero; t = t.parent)
                {
                    if (!t.TryGetComponent(out LayoutNode node) || !s_states.TryGetValue(node, out var above)) continue;
                    if (t != own)
                    {
                        var glided = GlideOn(above, left, true, true);
                        if (glided != Vector3.zero)
                        {
                            left = WithoutRounding(left - glided, tiny);
                            took = true;
                        }
                    }
                    var owner = above.Draggable;
                    if (left == Vector3.zero || !IsLive(owner) || owner.DragAxis == ScrollAxis.None) continue;
                    // One that does not move the way the speed goes is passed over; one with PassOnMidDrag false that
                    // does ends the walk (the switch changed since the glide set off), and the rest bounces.
                    var offered = OwnerShare(above, owner.DragAxis, left);
                    if (offered == Vector3.zero) continue;
                    if (!owner.PassOnMidDrag) break;
                    if (OwnerHeld(above, null)) continue;
                    if (owner.OnRelease(offered))
                    {
                        left = WithoutRounding(left - offered, tiny);
                        took = true;
                    }
                }

                // Still at that end, and not sent anywhere since by what took part of it.
                bool free = scroll.Phase == ScrollPhase.Idle || scroll.Phase == ScrollPhase.Gliding
                    || (scroll.Phase == ScrollPhase.Springing && !scroll.Wheeling && scroll.Offset.Transition == null);
                if (left == Vector3.zero || !Live(state) || !free) continue;
                var rest = OffsetMoveOf(state, left);
                var bounce = new Vector2(
                    impact.x != 0f && scroll.AtEnd(0, impact.x) ? rest.x : 0f,
                    impact.y != 0f && scroll.AtEnd(1, impact.y) ? rest.y : 0f);
                if (bounce == Vector2.zero) continue;
                scroll.Bounce(bounce);
                Hold(scroll.Offset, null);
            }
            return took;
        }

        // A mouse wheel or trackpad over a node with a LayoutScroller. Only scroll containers take it (a wheel has no
        // release to settle an owner's detent on, and macOS sheets do not resize to it), and a notch goes along its own
        // axis: up the containers it reaches (Reach), innermost first, each takes the wheel step (positive y towards the
        // start, as ScrollRect reads it) as far as its range allows, and what it leaves goes on up. A plain wheel (no x)
        // with no vertical container to take it scrolls the horizontal ones instead, the nearest first. Containers a press
        // holds are passed over, and what nothing takes goes on to the next scroll handler above that is not the system's.
        // There is no latching a gesture to one container (Unity reports no trackpad phases), so a trackpad's momentum
        // that reaches an inner container's end carries on into an outer one on the same axis. The Input System's UI
        // module scales scrollDelta by its scrollDeltaPerTick (6 a notch by default) and the legacy one does not (1 a
        // notch), so it is brought back to notches by the module's own ConvertPointerEventScrollDeltaToTicks: a notch is
        // the same step whichever is in use.
        internal static void OnWheel(LayoutNode node, PointerEventData eventData)
        {
            if (!TryPointer(node, out var origin)) return;
            var module = eventData.currentInputModule;
            var ticks = module != null ? module.ConvertPointerEventScrollDeltaToTicks(eventData.scrollDelta) : eventData.scrollDelta;
            var left = -ticks * ScrollState.WheelStep;
            if (left == Vector2.zero) return;

            var foreign = Reach(origin, true);
            bool vertical = false;
            for (int i = 0; i < s_reach.Count; i++)
            {
                var scroll = WheelScroll(s_reach[i]);
                if (scroll == null) continue;
                vertical |= scroll.Scrolls(1);
                left -= Wheel(s_reach[i], left);
            }
            if (!vertical && ticks.x == 0f && left.y != 0f)
            {
                var across = new Vector2(left.y, 0f);
                for (int i = 0; i < s_reach.Count; i++)
                {
                    if (WheelScroll(s_reach[i]) != null)
                        across -= Wheel(s_reach[i], across);
                }
                left.y = across.x;
            }
            FlushIfIdle();
            if (left == Vector2.zero || foreign == null) return;

            // What is left, in the event's own units (the module's conversion undone axis by axis), for as long as that
            // handler reads it.
            var delta = eventData.scrollDelta;
            eventData.scrollDelta = new Vector2(
                ticks.x != 0f ? -left.x / ScrollState.WheelStep * delta.x / ticks.x : 0f,
                ticks.y != 0f ? -left.y / ScrollState.WheelStep * delta.y / ticks.y : 0f);
            ExecuteEvents.ExecuteHierarchy(foreign, eventData, ExecuteEvents.scrollHandler);
            eventData.scrollDelta = delta;
        }

        // A node's scroll state when the wheel can move it: scrolling, laid out, and not held by a press. Null otherwise.
        private static ScrollState WheelScroll(NodeState state)
        {
            var scroll = state.Scroll;
            return scroll != null && scroll.Axis != ScrollAxis.None && scroll.Measured && scroll.Phase != ScrollPhase.Dragging
                ? scroll
                : null;
        }

        // A scroll container takes as much of a wheel's `step` (its units, the way its offset moves) as its range allows
        // on the axes it scrolls, added to its wheel's own target while it is still springing there (so notches add up
        // rather than each starting from where it is drawn), and springs there quickly from where it is at the speed it
        // has. One that snaps (ScrollSnap) takes all of a notch on each axis with a page or child past where it is headed
        // (its wheel's target, where it springs to, or where it is) that way, and springs on to that one. Returns what it
        // took.
        private static Vector2 Wheel(NodeState state, Vector2 step)
        {
            var scroll = state.Scroll;
            bool snaps = state.Node.ScrollSnap != ScrollSnap.None;
            var origin = scroll.Phase != ScrollPhase.Springing ? scroll.Offset.Value
                : scroll.Wheeling ? scroll.WheelTarget
                : snaps ? scroll.Offset.Target
                : scroll.Offset.Value;
            var target = origin;
            var taken = Vector2.zero;
            for (int axis = 0; axis < 2; axis++)
            {
                float m = step[axis];
                if (!scroll.Scrolls(axis) || m == 0f) continue;
                if (snaps)
                {
                    SnapPoints(state, axis, s_snaps);
                    if (!NextSnap(origin[axis], m, out float next)) continue;
                    target[axis] = next;
                    taken[axis] = m;
                    continue;
                }
                float to = Mathf.Clamp(origin[axis] + m, 0f, scroll.Range[axis]) - origin[axis];
                taken[axis] = m > 0f ? Mathf.Clamp(to, 0f, m) : Mathf.Clamp(to, m, 0f);
                target[axis] = origin[axis] + taken[axis];
            }
            s_snaps.Clear();
            if (taken == Vector2.zero) return Vector2.zero;
            target = scroll.Clamp(target);
            SpringScroll(scroll, target, ScrollState.WheelOmega, 1f, 0f, null);
            if (scroll.Phase == ScrollPhase.Springing)
            {
                scroll.Wheeling = true;
                scroll.WheelTarget = target;
            }
            return taken;
        }

        // Collects the nodes up the hierarchy from `origin` (it first) that a press, a drag or a wheel on it reaches, into
        // s_reach, as far as the first object above it holding a handler for it (a drag handler, or for the wheel a
        // scroll handler) that is not the system's, which it returns (null for none). UGUI gives what starts below such a
        // handler to the innermost handler, so what is above it never hears it, and takes nothing from under it.
        private static GameObject Reach(NodeState origin, bool wheel)
        {
            s_reach.Clear();
            Transform start = origin.RectTransform;
            for (var t = start; t != null; t = t.parent)
            {
                if (t != start && (wheel ? HoldsForeign<IScrollHandler>(t) : HoldsForeign<IDragHandler>(t)))
                    return t.gameObject;
                if (t.TryGetComponent(out LayoutNode node) && s_states.TryGetValue(node, out var state))
                    s_reach.Add(state);
            }
            return null;
        }

        // Whether an object holds an enabled handler of T that is not a LayoutScroller (a DragGesture, a ScrollRect), as
        // UGUI's ExecuteEvents would send one to.
        private static bool HoldsForeign<T>(Transform transform) where T : IEventSystemHandler
        {
            transform.GetComponents(s_handlers);
            bool holds = false;
            for (int i = 0; i < s_handlers.Count && !holds; i++)
            {
                var handler = s_handlers[i];
                holds = handler is T && !(handler is LayoutScroller) && (!(handler is Behaviour behaviour) || behaviour.isActiveAndEnabled);
            }
            s_handlers.Clear();
            return holds;
        }

        // Whether a drag other than `except` has begun the owner on a node: one held by another press takes no part in a
        // new drag, nor is it handed a glide.
        private static bool OwnerHeld(NodeState state, Drag except)
        {
            for (int i = 0; i < s_drags.Count; i++)
            {
                var drag = s_drags[i];
                if (drag == except) continue;
                var parts = drag.Parts;
                for (int j = 0; j < parts.Count; j++)
                {
                    if (parts[j].Owner != null && parts[j].Begun && parts[j].State == state)
                        return true;
                }
            }
            return false;
        }

        // Whether a drag owner takes part at all: there, and enabled (a component that is not a Behaviour always is).
        private static bool IsLive(ILayoutDraggable owner) =>
            owner is Behaviour behaviour ? behaviour != null && behaviour.isActiveAndEnabled : owner is Component component && component != null;

        // Whether a node's state is still the one kept for it (it has not been disabled or destroyed since).
        private static bool Live(NodeState state) =>
            state.Node != null && s_states.TryGetValue(state.Node, out var current) && current == state;

        // Whether a node is moving: its position, size or scale on its way somewhere.
        private static bool IsMoving(NodeState state) => state.Position.Moving || state.Size.Moving || state.Scale.Moving;

        // The state of the node a LayoutScroller is on, in play mode, for the pointer.
        private static bool TryPointer(LayoutNode node, out NodeState state)
        {
            state = null;
            return Application.isPlaying && node != null && s_states.TryGetValue(node, out state);
        }

        // How far the pointer went from `from` to `to` (screen points) on a node's plane, in world units, through the
        // camera the press was seen by (none for a Screen Space Overlay canvas, whose world units are screen pixels).
        // False when either misses the plane.
        private static bool WorldMove(RectTransform plane, Vector2 from, Vector2 to, Camera camera, out Vector3 world)
        {
            world = Vector3.zero;
            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(plane, from, camera, out var start)
                || !RectTransformUtility.ScreenPointToWorldPointInRectangle(plane, to, camera, out var end))
                return false;
            world = end - start;
            return true;
        }

        // A world move (or velocity) as it moves a scroll container's offset, in the container's own units: dragging
        // right scrolls it left and dragging up scrolls it down (its offset is y down), so x turns round and y does not.
        private static Vector2 OffsetMoveOf(NodeState state, Vector3 world)
        {
            Vector2 local = state.RectTransform.InverseTransformVector(world);
            return new Vector2(-local.x, local.y);
        }

        // The world move that moves a scroll container's offset by `move`: OffsetMoveOf undone.
        private static Vector3 WorldOfOffsetMove(NodeState state, Vector2 move) =>
            state.RectTransform.TransformVector(new Vector3(-move.x, move.y, 0f));

        // A participant's share of a world move (or velocity) as its container's offset moves, on the drag's axes it moves
        // along (of those, `x` and `y`).
        private static Vector2 OffsetShare(Participant part, Vector3 world, bool x = true, bool y = true) =>
            part.State.Scroll.OnAxes(Mask(OffsetMoveOf(part.State, world), part.X && x, part.Y && y));

        // The part of a world vector along a drag owner's axis, taken in its node's parent's units and back, in world
        // units; nothing for a root.
        private static Vector3 OwnerShare(NodeState state, ScrollAxis axis, Vector3 world)
        {
            var space = state.RectTransform.parent;
            if (space == null) return Vector3.zero;
            var local = Mask(space.InverseTransformVector(world), Along(axis, 0), Along(axis, 1));
            return local == Vector2.zero ? Vector3.zero : space.TransformVector(local);
        }

        private static Vector2 Mask(Vector2 vector, bool x, bool y) => new(x ? vector.x : 0f, y ? vector.y : 0f);

        // Whether a drag owner's axis includes x (0) or y (1).
        private static bool Along(ScrollAxis axis, int i) => i == 0
            ? axis == ScrollAxis.Horizontal || axis == ScrollAxis.Both
            : axis == ScrollAxis.Vertical || axis == ScrollAxis.Both;

        // A vector with its components no bigger than `tiny` put to 0: what is left of a move a participant took all of
        // along an axis, turned into its units and back, is rounding, and must not reach the next as a move of its own
        // (an owner offered it would count as what moved last).
        private static Vector3 WithoutRounding(Vector3 vector, float tiny) => new(
            Mathf.Abs(vector.x) <= tiny ? 0f : vector.x,
            Mathf.Abs(vector.y) <= tiny ? 0f : vector.y,
            Mathf.Abs(vector.z) <= tiny ? 0f : vector.z);

        // The drag or press of a pointer under way, or null.
        private static Drag FindDrag(PointerEventData press)
        {
            for (int i = 0; i < s_drags.Count; i++)
            {
                if (s_drags[i].Press == press)
                    return s_drags[i];
            }
            return null;
        }

        private static Drag StartDrag(PointerEventData press, LayoutScroller scroller, NodeState origin)
        {
            var drag = s_dragPool.Count > 0 ? s_dragPool.Pop() : new Drag();
            drag.Press = press;
            drag.Scroller = scroller;
            drag.Origin = origin;
            drag.SetOff = drag.X = drag.Y = drag.Fresh = drag.Sampled = false;
            drag.Sole = drag.Last = null;
            drag.Velocity = Vector3.zero;
            drag.LastMoved = Time.unscaledTime;
            s_drags.Add(drag);
            return drag;
        }

        private static Participant NewPart(NodeState state, ILayoutDraggable owner, bool takesX, bool takesY)
        {
            var part = s_partPool.Count > 0 ? s_partPool.Pop() : new Participant();
            part.State = state;
            part.Owner = owner;
            part.TakesX = takesX;
            part.TakesY = takesY;
            part.X = part.Y = part.Held = part.Dropped = part.Begun = part.Took = false;
            return part;
        }

        private static void Recycle(Participant part)
        {
            part.State = null;
            part.Owner = null;
            s_partPool.Push(part);
        }

        private static void RecycleAll(List<Participant> parts)
        {
            for (int i = 0; i < parts.Count; i++)
                Recycle(parts[i]);
            parts.Clear();
        }

        private static void Recycle(Drag drag)
        {
            RecycleAll(drag.Parts);
            drag.Press = null;
            drag.Scroller = null;
            drag.Origin = null;
            drag.Sole = drag.Last = null;
            s_dragPool.Push(drag);
        }

        // ── Spaces ───────────────────────────────────────────────────────────────

        // How far a node scrolls what is in it: its scroll offset as drawn, or nothing for one that does not scroll
        // (or for no node, a root's parent).
        private static Vector2 ScrolledBy(NodeState state)
        {
            var scroll = state?.Scroll;
            return scroll != null && scroll.Axis != ScrollAxis.None ? scroll.Offset.Value : Vector2.zero;
        }

        // How far a node floating against an element goes from where the solver put it (against the element as laid
        // out) to be on the element as it is placed and drawn: on by how far the element is drawn from where it was
        // laid out by itself (ShiftOf) and by how far each node the element is inside and it is not moves what is
        // inside (MovedBy), and back by the same for each node it is inside and the element is not. What both are
        // inside moves both alike and is left out, so a scroll they share adds nothing, not even rounding, as it
        // glides. When the node is inside the element, the element is one of those: its shift moves both, and its
        // scroll only the node. Both were laid out in this pass, in one tree, whose units the solver takes to be one.
        // How far the element and what it is inside are drawn from there takes in their DisplayEffect slides too, so
        // a tooltip on a toast rides the toast's slide; not their Scale or shrink, which it does not follow.
        private static Vector2 OnElement(NodeState node, NodeState element)
        {
            var shift = ShiftOf(element) + SlideOf(element);
            var moved = shift;
            NodeState a = node.PassParent, b = element.PassParent;
            int depthA = DepthOf(a), depthB = DepthOf(b);
            // Going up from the node, the element is met when the node is inside it: its shift is known already, and
            // working it out again would double the work at each such node floating inside another.
            for (; depthA > depthB; depthA--, a = a.PassParent)
                moved -= a == element ? shift - ScrolledBy(a) : MovedBy(a);
            for (; depthB > depthA; depthB--, b = b.PassParent)
                moved += MovedBy(b);
            for (; a != b; a = a.PassParent, b = b.PassParent)
                moved += MovedBy(b) - MovedBy(a);
            return moved;
        }

        // How far what is inside a node is drawn from where it is laid out, by the node alone: on by its shift and its
        // slide, and back by its scroll offset.
        private static Vector2 MovedBy(NodeState state) => ShiftOf(state) + SlideOf(state) - ScrolledBy(state);

        // How far a node is drawn from where the solver put it by itself, in layout space (y down): its Offset and,
        // when it floats against an element, how far that moves it on (OnElement), so a node floating against it (or
        // against something in it, or from in it against something outside it) follows it as it follows its own
        // element. That goes back only through nodes the solver placed before it, so it comes to an end. A root's
        // moves nothing, its rect being its own. It leaves out the node's own slide, which is never a target: Write
        // adds that as it draws it.
        private static Vector2 ShiftOf(NodeState state)
        {
            if (state.PassParent == null) return Vector2.zero;
            var offset = state.Node.Offset;
            var shift = new Vector2(offset.x, -offset.y);
            ref var solved = ref s_solver[state.PassIndex];
            if (solved.Floating.AttachTo == FloatingAttach.Element)
                shift += OnElement(state, s_solved[solved.Element]);
            return shift;
        }

        // How many nodes up from it its layout tree's root is, counting both.
        private static int DepthOf(NodeState state)
        {
            int depth = 0;
            for (; state != null; state = state.PassParent)
                depth++;
            return depth;
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
            CornersIn(s_corners[0], s_corners[2], space, out centre, out size);
        }

        // Where a rect drawn between two opposite world corners is, as a centre and size in `space`'s layout space: a
        // rect read before something moved it, a source matched by name read before its destination's ancestors are
        // written.
        private static void CornersIn(Vector3 bottomLeft, Vector3 topRight, RectTransform space, out Vector2 centre, out Vector2 size)
        {
            Vector2 min = space.InverseTransformPoint(bottomLeft);
            Vector2 max = space.InverseTransformPoint(topRight);
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

        // How long a unit of `space`'s layout space is in world units, along its x and along its y. A size grows along
        // those axes whichever way they point on screen, so a size's rate is scaled by their lengths into world units
        // and back, where a position's velocity is turned (and flipped from y down).
        private static Vector2 UnitOf(RectTransform space) =>
            new(space.TransformVector(Vector3.right).magnitude, space.TransformVector(Vector3.up).magnitude);

        // A velocity in one layout space (y down), in another, through world space.
        private static Vector2 VectorTo(RectTransform from, RectTransform to, Vector2 vector)
        {
            Vector2 local = to.InverseTransformVector(from.TransformVector(new Vector3(vector.x, -vector.y, 0f)));
            return new Vector2(local.x, -local.y);
        }
    }
}
