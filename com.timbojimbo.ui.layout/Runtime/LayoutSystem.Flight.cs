using System.Collections.Generic;
using TimboJimbo.Motion;
using UnityEngine;
using UnityEngine.UI;

namespace TimboJimbo.UI.Layout
{
    public static partial class LayoutSystem
    {
        // ── The flight layer ─────────────────────────────────────────────────────
        //
        // A node flies by having a canvas of its own that overrides the sorting, at the root canvas's sorting layer and
        // an order above any a scene uses. That draws it above everything in its root canvas, and takes it out of every
        // RectMask2D and Mask above it, since both stop looking up at such a canvas; its own clip still cuts what is in
        // it. Nothing is reparented, so its layout parent (its transform parent) and its springs, in that parent's
        // space, are what they would be anyway. Landing puts back what flying changed, and removes what it added.

        // The sorting order of the bottom of the layer: each flight's is this plus its place in it.
        private const int FlightOrder = 30000;

        // What is flying, bottom to top.
        private static readonly List<NodeState> s_flights = new();

        // A change's new flights, which board together once its pass is over (Embark).
        private static readonly List<Boarding> s_boarding = new();

        // Scratch for Nest: the layer in its new order.
        private static readonly List<NodeState> s_nested = new();

        private static readonly System.Comparison<Boarding> s_byDrawnBefore = CompareDrawnBefore;

        // One entry waiting to board: a node moved to another layout parent in the change, a node growing out of or
        // shrinking into one that stays shown under its name (Anchored), or a pair (names), its source with its
        // destination directly above it. It is ranked by where it drew before the change (a pair by its source): the
        // canvas sorting that drew it there, then the parent it was in and its sibling index there.
        private struct Boarding
        {
            public NodeState Node;
            public NodeState Over;
            public bool Anchored;
            public int Layer;
            public int Order;
            public Transform From;
            public int Sibling;
            // Its place in the queue, which settles a tie.
            public int Index;
        }

        private static void ResetFlights()
        {
            s_flights.Clear();
            s_boarding.Clear();
            s_nested.Clear();
        }

        // A node moved from under `from` to another layout parent in a change's pass (Reparent) boards with the
        // change's other flights once the pass is over.
        private static void Board(NodeState state, NodeState from)
        {
            state.FlewFrom = from;
            s_boarding.Add(new Boarding { Node = state });
        }

        // For names: a pair boards as one entry, ranked by where its source drew, its destination directly above its
        // source, and both then ignore their parent groups while they fly. It takes the place of either half boarding
        // by itself in the same change. A half already flying keeps its place in the layer: a source, or a destination
        // turning back (TakeOver lands any other destination first, so that it boards again above its new source).
        private static void BoardPair(NodeState source, NodeState destination)
        {
            for (int i = s_boarding.Count - 1; i >= 0; i--)
            {
                var boarding = s_boarding[i];
                if (boarding.Over == null && (boarding.Node == source || boarding.Node == destination))
                    s_boarding.RemoveAt(i);
            }
            s_boarding.Add(new Boarding { Node = source, Over = destination });
        }

        // For names: a node growing out of, or shrinking back into, one that stays shown boards by itself, in place of
        // boarding for a move to another parent in the same change, and flies until its effect has played.
        private static void BoardAnchored(NodeState state)
        {
            for (int i = s_boarding.Count - 1; i >= 0; i--)
            {
                if (s_boarding[i].Over == null && s_boarding[i].Node == state)
                    s_boarding.RemoveAt(i);
            }
            s_boarding.Add(new Boarding { Node = state, Anchored = true });
        }

        // At the end of Animate, once its pass is over: the change's new flights take off together, ranked by where each
        // drew before the change, above everything flying already, which keeps its place (a node moved again while it
        // flies stays where it is in the layer). A node the change moved to another parent that is not moving (its new
        // place is where it was drawn), or that is not shown, does not board: it would only leave again at once. One
        // anchored always does: its effect moves it. Then what is inside another flight is brought above it, and the
        // layer is numbered.
        private static void Embark()
        {
            if (s_boarding.Count == 0) return;
            for (int i = 0; i < s_boarding.Count; i++)
            {
                var boarding = s_boarding[i];
                var node = boarding.Node;
                boarding.From = (node.FlewFrom ?? node.Parent).RectTransform;
                boarding.Sibling = node.WasSibling;
                boarding.Index = i;
                SortingOf(boarding.From, out boarding.Layer, out boarding.Order);
                s_boarding[i] = boarding;
            }
            s_boarding.Sort(s_byDrawnBefore);

            for (int i = 0; i < s_boarding.Count; i++)
            {
                var boarding = s_boarding[i];
                var node = boarding.Node;
                node.FlewFrom = null;
                if (boarding.Over == null)
                {
                    if (boarding.Anchored || (node.PassShown && (node.Position.Moving || node.Size.Moving)))
                        TakeOff(node);
                }
                else
                {
                    boarding.Over.FlewFrom = null;
                    TakeOff(node);
                    TakeOff(boarding.Over);
                    MarkPairHalf(node);
                    MarkPairHalf(boarding.Over);
                }
            }
            s_boarding.Clear();
            Nest();
            Number();
        }

        // Flies a node, at the top of the layer: a canvas on it overriding the sorting, and, when its root canvas takes
        // the pointer, a raycaster so that it does up there too, copying the root canvas's. Its own canvas is used if it
        // has one. Otherwise one is added, hidden and never saved, drawing with the root canvas's shader channels; a
        // hidden one there already is the system's, left by the node being disabled mid-flight, and is taken up again (a
        // hidden raycaster likewise). Either way the canvas's sorting is saved, to be put back as it leaves the layer.
        // Not under a canvas, it does not fly. Flying already, it keeps its place.
        private static void TakeOff(NodeState state)
        {
            if (state.Flight != null) return;
            var parent = state.RectTransform.parent;
            var outer = parent != null ? parent.GetComponentInParent<Canvas>() : null;
            if (outer == null) return;
            var root = outer.rootCanvas;
            var go = state.Node.gameObject;
            var flight = new FlightState();

            if (!go.TryGetComponent(out flight.Canvas))
                flight.Canvas = Hide(go.AddComponent<Canvas>());
            var canvas = flight.Canvas;
            if (IsHidden(canvas))
            {
                flight.AddedCanvas = true;
                canvas.additionalShaderChannels = root.additionalShaderChannels;
            }
            flight.SavedOverrideSorting = canvas.overrideSorting;
            flight.SavedSortingLayerID = canvas.sortingLayerID;
            flight.SavedSortingOrder = canvas.sortingOrder;

            // Added after the canvas, which it needs.
            root.TryGetComponent(out GraphicRaycaster rootRaycaster);
            if (!go.TryGetComponent(out flight.Raycaster) && rootRaycaster != null)
                flight.Raycaster = Hide(go.AddComponent<GraphicRaycaster>());
            var raycaster = flight.Raycaster;
            if (raycaster != null && IsHidden(raycaster))
            {
                flight.AddedRaycaster = true;
                if (rootRaycaster != null)
                {
                    raycaster.ignoreReversedGraphics = rootRaycaster.ignoreReversedGraphics;
                    raycaster.blockingObjects = rootRaycaster.blockingObjects;
                    raycaster.blockingMask = rootRaycaster.blockingMask;
                }
            }

            // Its order is its place in the layer, given as the layer is numbered.
            canvas.overrideSorting = true;
            canvas.sortingLayerID = root.sortingLayerID;
            state.Flight = flight;
            s_flights.Add(state);
        }

        // A pair half's CanvasGroup (added if it has none) ignores its parent groups while it flies: the pair draws its
        // own fades, which a page fading in over it would otherwise cut.
        private static void MarkPairHalf(NodeState state)
        {
            var flight = state.Flight;
            if (flight == null || flight.PairHalf) return;
            flight.PairHalf = true;
            if (state.Group == null && !state.Node.TryGetComponent(out state.Group))
                state.Group = state.Node.gameObject.AddComponent<CanvasGroup>();
            flight.SavedIgnoreParentGroups = state.Group.ignoreParentGroups;
            state.Group.ignoreParentGroups = true;
        }

        // Canvases that override sorting are drawn by their order whatever their nesting, so a flight below one it is
        // inside would be drawn under that one's own background (a pair half inside a card moved in the same change,
        // whose source drew before the card; a panel that sets off after a card inside it already has). Each flight
        // that comes before the flight it is inside is moved to just above that one, those moved keeping their order
        // and what is inside them coming with them; every other flight keeps its place.
        private static void Nest()
        {
            if (s_flights.Count < 2) return;
            for (int i = 0; i < s_flights.Count; i++)
            {
                var flight = s_flights[i].Flight;
                flight.Inside = FlightAround(s_flights[i]);
                flight.Placed = false;
            }
            s_nested.Clear();
            for (int i = 0; i < s_flights.Count; i++)
            {
                var state = s_flights[i];
                var inside = state.Flight.Inside;
                if (!state.Flight.Placed && (inside == null || inside.Flight.Placed))
                    NestNext(state, i);
            }
            s_flights.Clear();
            s_flights.AddRange(s_nested);
            s_nested.Clear();
        }

        // Places a flight next in the new order, then each flight inside it that came before `reached` in the old one
        // and so is still waiting for it, each followed by what waits for it in turn.
        private static void NestNext(NodeState state, int reached)
        {
            state.Flight.Placed = true;
            s_nested.Add(state);
            for (int i = 0; i < reached; i++)
            {
                var other = s_flights[i];
                if (!other.Flight.Placed && other.Flight.Inside == state)
                    NestNext(other, reached);
            }
        }

        // The nearest flight a node is inside, by its transform (through plain objects too, whose canvas it draws with).
        private static NodeState FlightAround(NodeState state)
        {
            for (var t = state.RectTransform.parent; t != null; t = t.parent)
            {
                if (t.TryGetComponent(out LayoutNode node) && s_states.TryGetValue(node, out var around) && around.Flight != null)
                    return around;
            }
            return null;
        }

        // Gives each flight the order of its place in the layer, bottom to top. Numbered afresh from the bottom each
        // time flights board, so the orders never run up however many come and go; those that land meanwhile leave gaps
        // that change nothing.
        private static void Number()
        {
            for (int i = 0; i < s_flights.Count; i++)
            {
                var canvas = s_flights[i].Flight.Canvas;
                int order = Mathf.Min(FlightOrder + i, short.MaxValue);
                if (canvas.sortingOrder != order) canvas.sortingOrder = order;
            }
        }

        // Whether a flight has landed: its position and size are at rest, caught, skipped or where they were going, so
        // it is drawn where it stays, and an anchored one's effect has played out (AnchorPlayed). A pair half lands
        // with its pair, once both halves and the fades of its destination's ancestors are (LayoutSystem.Match.cs).
        private static bool HasLanded(NodeState state)
        {
            if (state.Anchor != null && !AnchorPlayed(state)) return false;
            if (state.Flight.PairHalf) return PairAtRest(state);
            return !state.Position.Moving && !state.Size.Moving;
        }

        // A flight that is not shown (hidden, or under something hidden) leaves the layer at once, and is faded, clipped
        // and kept from the pointer along with what hid it (a page switched mid-flight). Out of it, the groups above it
        // reach it again: UGUI's raycasts stop looking up for them at its canvas, and a pair half ignores them. A
        // follower stays while it is hidden: it is drawn at the node it follows, and lands with it. Shown again, and not
        // made a destination by the change that showed it (`transition`, whose pairs have formed by now), it stops
        // following and leaves (ShowAgain). One shrinking back into its anchor stays too, until its effect has played. A
        // destination that drops out ends its pair first, and what followed it drops out in turn if it is not shown
        // either, so the layer is looked through again.
        private static void DropOut(MotionTransition transition)
        {
            for (int i = s_flights.Count - 1; i >= 0; i--)
            {
                var state = s_flights[i];
                if (state.Follows != null)
                {
                    if (state.PassShown && !state.WasShown)
                        ShowAgain(state, transition);
                    continue;
                }
                if (state.PassShown || (state.Anchor != null && state.Leaving)) continue;
                bool ended = state.Flight.PairHalf && Unfollow(state, true);
                Land(state);
                if (ended)
                    i = s_flights.Count;
            }
        }

        // A flight lands: out of the layer, with what flying changed put back, and what it added removed at once (the
        // raycaster first, which needs the canvas), so that nothing is left to be destroyed at the end of the frame for
        // a change later in the same frame to find and take for the node's own. Nothing moves, since nothing changed
        // space, but the clips above it cut it again.
        private static void Land(NodeState state)
        {
            var flight = LeaveLayer(state);
            if (flight == null) return;
            if (flight.AddedRaycaster && flight.Raycaster != null)
                Object.DestroyImmediate(flight.Raycaster);
            if (flight.AddedCanvas && flight.Canvas != null)
                Object.DestroyImmediate(flight.Canvas);
        }

        // A node disabled or destroyed mid-flight leaves the layer. This runs in OnDisable, where destroying at once may
        // be refused, so what flying added stays, hidden and sorting as it did before the flight, for the node's next
        // flight to take up and that one's landing to remove. It stays switched on: a disabled canvas stops drawing
        // what is in it, while UGUI still raycasts that against the canvas above, so a node enabled again (or whose
        // LayoutNode alone was disabled) would be unseen yet take presses; and what is in an enabled canvas is raycast
        // only by that canvas's own raycaster.
        private static void StandDown(NodeState state) => LeaveLayer(state);

        // Takes a node out of the layer, its canvas's sorting put back as it was before it flew, so that it no longer
        // sorts above everything, and a pair half's parent groups put back; cut to the rect it moved through, it is cut
        // no more (LayoutSystem.Match.cs). Returns what it changed to fly it, or null when it was not flying.
        private static FlightState LeaveLayer(NodeState state)
        {
            var flight = state.Flight;
            if (flight == null) return null;
            Uncut(state);
            state.Flight = null;
            s_flights.Remove(state);
            var canvas = flight.Canvas;
            if (canvas != null)
            {
                canvas.overrideSorting = flight.SavedOverrideSorting;
                canvas.sortingLayerID = flight.SavedSortingLayerID;
                canvas.sortingOrder = flight.SavedSortingOrder;
            }
            if (flight.PairHalf && state.Group != null)
                state.Group.ignoreParentGroups = flight.SavedIgnoreParentGroups;
            return flight;
        }

        // Notes whether a node, or anything above it (on through Above, for a root), is written as taking no pointer,
        // for the flights inside it to ask; and returns whether its own group blocks raycasts: whether it takes the
        // pointer and, for a flight, whether nothing above it keeps it from it. Its canvas stops UGUI's raycasts
        // looking further up for groups that do not block them, so a flight inside a page taking no pointer (hidden,
        // or moving for a change that is not interactive) would otherwise take it.
        private static bool BlocksRaycasts(NodeState state, bool clickable)
        {
            var above = state.PassParent ?? state.Above;
            state.PassBlocked = !clickable || (above != null && above.PassBlocked);
            return state.Flight != null ? !state.PassBlocked : clickable;
        }

        // ── Ranking ──────────────────────────────────────────────────────────────

        // The sorting of the canvas a transform draws with: the nearest that sorts itself (a root canvas, or one that
        // overrides the sorting), by its sorting layer's place and its order in it.
        private static void SortingOf(Transform transform, out int layer, out int order)
        {
            var canvas = transform.GetComponentInParent<Canvas>();
            while (canvas != null && !canvas.isRootCanvas && !canvas.overrideSorting)
                canvas = canvas.transform.parent != null ? canvas.transform.parent.GetComponentInParent<Canvas>() : null;
            layer = canvas != null ? SortingLayer.GetLayerValueFromID(canvas.sortingLayerID) : 0;
            order = canvas != null ? canvas.sortingOrder : 0;
        }

        // Which of two entries drew first before the change: by the sorting of the canvas that drew each, then in
        // hierarchy order, then in the order they were queued.
        private static int CompareDrawnBefore(Boarding a, Boarding b)
        {
            if (a.Layer != b.Layer) return a.Layer.CompareTo(b.Layer);
            if (a.Order != b.Order) return a.Order.CompareTo(b.Order);
            int hierarchy = CompareHierarchy(a.From, a.Sibling, b.From, b.Sibling);
            return hierarchy != 0 ? hierarchy : a.Index.CompareTo(b.Index);
        }

        // Hierarchy order of two children, each given as its parent and the sibling index it had there, which it may
        // since have left: a parent before what is in it, siblings in order, and the roots of different scenes in some
        // fixed order. That is the order of the paths of sibling indices down to each, a path before those it begins.
        private static int CompareHierarchy(Transform a, int siblingA, Transform b, int siblingB)
        {
            if (a == b) return siblingA.CompareTo(siblingB);
            int depthA = DepthOf(a), depthB = DepthOf(b);
            Transform x = a, y = b;
            // The deeper parent is taken up to the other's depth, keeping the index of the child it came up through.
            int nextA = siblingA, nextB = siblingB;
            for (int d = depthA; d > depthB; d--)
            {
                nextA = x.GetSiblingIndex();
                x = x.parent;
            }
            for (int d = depthB; d > depthA; d--)
            {
                nextB = y.GetSiblingIndex();
                y = y.parent;
            }
            // One parent is inside the other: under the outer one, by the child each goes on through; the same child,
            // and the one from the outer parent is that child itself, so it comes first.
            if (x == y)
            {
                int order = nextA.CompareTo(nextB);
                return order != 0 ? order : depthA.CompareTo(depthB);
            }
            while (x.parent != y.parent)
            {
                x = x.parent;
                y = y.parent;
            }
            if (x.parent == null && x.gameObject.scene != y.gameObject.scene)
                return x.gameObject.scene.handle.GetRawData().CompareTo(y.gameObject.scene.handle.GetRawData());
            return x.GetSiblingIndex().CompareTo(y.GetSiblingIndex());
        }

        // How many transforms are above it.
        private static int DepthOf(Transform transform)
        {
            int depth = 0;
            for (var t = transform.parent; t != null; t = t.parent)
                depth++;
            return depth;
        }
    }
}
