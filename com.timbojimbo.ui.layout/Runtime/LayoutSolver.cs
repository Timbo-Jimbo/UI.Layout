using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// One node as the solver sees it: its place in the tree, what it is laid out by, and, once solved, where it goes.
    /// The layout system fills in the tree and the settings; the solver fills in the rects and content sizes.
    /// </summary>
    internal struct SolverNode
    {
        // The tree, as indices into the same array (-1 for none): the root at 0 with no parent, every node after its
        // parent (pre-order), siblings in order. Nodes whose Display is None are left out, with everything inside them.
        public int Parent;
        public int FirstChild;
        public int NextSibling;

        public Sizing Width;
        public Sizing Height;
        public Insets Padding;
        public float ChildGap;
        public LayoutDirection Direction;
        // Whether its children wrap into lines, and how; the solver takes it as None where it cannot apply (WrapOf).
        public Wrap Wrap;
        public AlignX AlignX;
        public AlignY AlignY;
        public float AspectRatio;
        public Floating Floating;
        // With Floating.AttachTo Element, the index of the node it is attached to (the system finds it in the tree,
        // attaching the node to its parent instead when it is not laid out there); -1 otherwise. When the element
        // cannot be sized and placed before the node (it is the node, inside it, or attached back to it), the solver
        // sets AttachTo to Parent and leaves this, so the system can tell.
        public int Element;
        // The edges it reaches out to its root's edge on, where it lies against the safe area (IgnoresSafeArea).
        public Edges Ignores;
        // Which way it scrolls: along that axis its children are never squeezed, running past its edge instead, and it
        // can itself be squeezed to nothing that way (what it shows is not what it holds).
        public ScrollAxis Scroll;
        // What it measures, if anything (null: nothing); it is fitted to when it has no children in its flow
        // (floating ones do not count).
        public ILayoutMeasurable Content;

        // Out: where it goes, relative to its parent's top-left corner, x right and y down, in its parent's units (the
        // root's is its own rect, at 0, 0).
        public Rect Rect;

        // Out: the same, relative to the root's top-left corner.
        public Rect RootRect;

        // Out: how big what it holds is, padding included: its flow children end to end along its direction with the
        // gaps between them, and the largest of them across (or, wrapping, its longest line along and its lines end to
        // end across), at the sizes they ended up with (or, with no flow children, its own size: only children scroll).
        // A scrolling node's scroll range is how far this runs past its own size.
        public Vector2 ContentSize;

        // Out: how far it reaches past where it was laid out on each side, out to its root's edge (Ignores): its rect,
        // its padding and what it holds have grown by that, so what is inside it stays where it was. The root's is the
        // safe area it keeps its content clear of, which its padding has grown by, its rect staying as it is given.
        public Insets Reach;

        // Out: which of its parent's lines it is in (0 unless its parent wraps; 0 for a floating node, which is in
        // none), and how many lines its own flow runs to (1 unless it wraps; 0 with nothing in its flow).
        public int Line;
        public int Lines;

        // ── The solver's own scratch: set and read only inside Solve, never by the system ──────────────────────────

        // Its size along each axis as the passes work it out: fitted to its content up the tree, then taken from its
        // parent (percent), grown or shrunk down it. The rects are made from it at the end.
        internal Vector2 Size;

        // The smallest its parent may shrink it to along each axis: its content's own smallest (a text's longest word,
        // or its flow children's smallest laid end to end, or the largest of them across) with its padding, within its
        // min and max; a fixed node's size; nothing for a percent node, which is never shrunk; and only its own min the
        // way a scroll container scrolls.
        internal Vector2 MinSize;

        // Its content's height wrapped to its final width, with its padding. Set only for a node fitted to what it
        // measures in height (content, no flow children, fit or grow height, no aspect ratio), before heights are fitted.
        internal float ContentHeight;

        // How many of its children are in its flow (not floating): they, and not the floating ones, fit it and have gaps
        // between them.
        internal int FlowCount;

        // Whether its parent's pass under way is still growing or shrinking it.
        internal bool Resizing;

        // For a node wrapping its children into a grid: how long each cell is along its direction, from its final size
        // that way.
        internal float Cell;

        // The order the passes that size and place nodes go in, as a list through the nodes from the root (-1 at its
        // end): every node after its parent, and one attached to an element after the element, wherever each is in
        // the tree, so the element's size and rect are final when the node is sized and placed against them.
        internal int Next;

        // Whether it is in that order yet; and, for a node attached to an element that was not in it yet when the
        // node's parent went in, whether it is waiting for the element, in a list the element heads (the first node
        // waiting for it, and the next waiting for the same element, -1 for none).
        internal bool Ordered;
        internal bool Waiting;
        internal int FirstWaiting;
        internal int NextWaiting;
    }

    /// <summary>
    /// Clay's layout pass over one tree: widths (fitted up the tree, then grown and shrunk down it), text wrapped to
    /// them, heights the same way, then how big what each node holds is, then positions, then floating nodes. Pure:
    /// it reads settings and content measures, and writes rects and content sizes.
    /// </summary>
    internal static class LayoutSolver
    {
        // Sizes this close count as the same, and this little space left over as none, as Clay's epsilon. It is also
        // what makes the grow and shrink loops end: every round that does not use up what is left either stops a child
        // at its limit or moves the largest (smallest) set of children at least this far, onto the next.
        private const float Epsilon = 0.01f;

        // How close to the root's safe rect a node's edge lies for it to count as against it, and reach out past it: half
        // a unit, what sizes that do not divide evenly round away.
        private const float Touching = 0.5f;

        /// <summary>
        /// Lays out the first <paramref name="count"/> of <paramref name="nodes"/>, the root sized
        /// <paramref name="rootSize"/> (its own sizing is not used), keeping its content clear of
        /// <paramref name="safeArea"/>: how far the screen's unsafe area reaches into it on each side, in its units.
        /// </summary>
        /// <remarks>
        /// Follows Clay's Clay__SizeContainersAlongAxis and Clay__CalculateFinalLayout, with these differences: content
        /// is measured through <see cref="ILayoutMeasurable"/> (unwrapped for the fit width, then wrapped to the final
        /// width for the height, which is also the least a content node's height can be shrunk to); floating nodes
        /// attached to their parent or the root grow or take a percentage of its content box (inside its padding)
        /// rather than its whole rect, while one attached to an element takes the element's whole rect; the passes
        /// that size and place go parents first but put a node attached to an element after the element, wherever it
        /// is in the tree; and a run that overflows its parent starts at the padding whatever the alignment. As in
        /// Clay, percent children along a parent's direction take their part of what its padding and the gaps between
        /// its children leave (so two halves and a gap fit), and a fitted floating node keeps its own size. A scroll
        /// container is Clay's too, along each axis it scrolls: it squeezes none of its children, which run on past
        /// its end instead (a grow child still takes the room left, and across its direction fills out to the widest
        /// child), and it can itself be squeezed to nothing that way, while it still fits to all it holds.
        /// Unlike Clay, across its direction on an axis it scrolls, its children are aligned within the widest of them
        /// when that runs past it, as a stack in SwiftUI's ScrollView is, so all of them lie within its scroll range.
        /// Where it is scrolled to is not the solver's: the system moves its children by that as it draws them.
        /// Beyond Clay, a node can wrap its children into lines (<see cref="Wrap"/>), as CSS's flex-wrap and SwiftUI's
        /// lazy grids do: broken as its widths are shared out, as text is between the width and height passes, each
        /// line then shared out as a row is. And the root keeps its content clear of the safe area as SwiftUI does
        /// (its padding grows by it), while a node ignoring the safe area on an edge reaches back out past it where it
        /// lies against it, as it is placed, its padding growing by as much so what is inside it stays where it was.
        /// Allocation-free: all its working state lives in the nodes.
        /// </remarks>
        public static void Solve(SolverNode[] nodes, int count, Vector2 rootSize, Insets safeArea)
        {
            if (count <= 0) return;

            // The root keeps what it lays out clear of the safe area: its padding grows by it, its rect staying as given.
            nodes[0].Padding += safeArea;
            nodes[0].Reach = safeArea;

            // Which children are in each node's flow: only they fit it and have gaps between them.
            for (int i = 0; i < count; i++)
                nodes[i].FlowCount = 0;
            for (int i = 1; i < count; i++)
                if (!nodes[i].Floating.IsFloating)
                    nodes[nodes[i].Parent].FlowCount++;

            // Which line each child is in, where that is known before anything is sized.
            LineUp(nodes, count);

            // The order sizes are shared out and positions given in: parents first, and a node attached to an element
            // after the element.
            Order(nodes, count);

            // Widths: fitted to content up the tree, then the root's given and each parent's shared out down it (lines
            // broken as they are).
            FitAxis(nodes, count, 0);
            nodes[0].Size.x = rootSize.x;
            DistributeAxis(nodes, 0);

            // Content wrapped to the widths it got, then heights the same way as widths.
            WrapContent(nodes, count);
            FitAxis(nodes, count, 1);
            nodes[0].Size.y = rootSize.y;
            DistributeAxis(nodes, 1);

            // What each node holds, from the sizes everything ended up with, then where it all goes.
            SizeContent(nodes, count);
            Place(nodes, safeArea);
        }

        // ── Order ────────────────────────────────────────────────────────────────

        // Makes the order the passes that share out sizes and give positions go in (Next): pre-order, but a node
        // attached to an element not in the order yet waits for it, with everything inside it, and goes in straight
        // after it. Without such nodes it is pre-order itself. What is still waiting at the end waits, through the
        // elements it is attached to, on itself (it is attached to itself, to something inside it, or to something
        // attached back to it): its placement is circular, so it is attached to its parent instead and goes in after
        // it (in already, as it was met). All of it at once, so which falls back does not depend on sibling order.
        // Each node goes in once.
        private static void Order(SolverNode[] nodes, int count)
        {
            for (int i = 0; i < count; i++)
            {
                ref var node = ref nodes[i];
                node.Next = -1;
                node.FirstWaiting = -1;
                node.Ordered = false;
                node.Waiting = false;
            }

            int last = -1;
            Append(nodes, 0, ref last);
            while (true)
            {
                bool circular = false;
                for (int i = 1; i < count; i++)
                {
                    if (!nodes[i].Waiting) continue;
                    nodes[i].Floating.AttachTo = FloatingAttach.Parent;
                    circular = true;
                }
                if (!circular) return;

                // Any that start waiting as these go in are attached to elements still, and are seen to next round.
                for (int i = 1; i < count; i++)
                {
                    if (!nodes[i].Waiting || nodes[i].Floating.AttachTo != FloatingAttach.Parent) continue;
                    nodes[i].Waiting = false;
                    Append(nodes, i, ref last);
                }
            }
        }

        // Puts a node in the order, then what was waiting for it, then each of its children with what is inside it;
        // a child attached to an element not in the order yet waits for it instead.
        private static void Append(SolverNode[] nodes, int index, ref int last)
        {
            nodes[index].Ordered = true;
            if (last >= 0) nodes[last].Next = index;
            last = index;

            for (int w = nodes[index].FirstWaiting; w >= 0; w = nodes[w].NextWaiting)
            {
                // One that fell back to its parent has gone in already.
                if (!nodes[w].Waiting) continue;
                nodes[w].Waiting = false;
                Append(nodes, w, ref last);
            }

            for (int c = nodes[index].FirstChild; c >= 0; c = nodes[c].NextSibling)
            {
                ref var child = ref nodes[c];
                if (child.Floating.AttachTo == FloatingAttach.Element && !nodes[child.Element].Ordered)
                {
                    child.Waiting = true;
                    child.NextWaiting = nodes[child.Element].FirstWaiting;
                    nodes[child.Element].FirstWaiting = c;
                }
                else
                {
                    Append(nodes, c, ref last);
                }
            }
        }

        // ── Lines ────────────────────────────────────────────────────────────────

        // How a node wraps its children: its Wrap's mode, or None where that cannot apply. Nothing wraps with nothing in
        // its flow, nor along the way it scrolls, where nothing has to fit. Lines and adaptive grids wrap left to right
        // only: they break their lines by how long their children are along their direction, which for widths is known
        // before anything is sized across (as text wraps between the width and height passes), but for heights only
        // after the widths across, which depend on the lines. A grid's lines go by count, so it wraps either way.
        private static LayoutWrapMode WrapOf(in SolverNode node)
        {
            var mode = node.Wrap.Mode;
            if (mode == LayoutWrapMode.None || node.FlowCount == 0) return LayoutWrapMode.None;
            bool leftToRight = node.Direction == LayoutDirection.LeftToRight;
            if (Scrolls(node, leftToRight ? 0 : 1)) return LayoutWrapMode.None;
            return leftToRight || mode == LayoutWrapMode.Grid ? mode : LayoutWrapMode.None;
        }

        // Puts every child in its parent's first line, then every child in a grid's flow in the line its place in the
        // flow puts it in (the n-th in line n / Count), which a TopToBottom grid needs before anything is sized: its
        // lines are columns, sized across in the first pass. Lines and adaptive grids are broken into lines as their
        // widths are shared out (DistributeWrapped), before anything asks which line a child is in.
        private static void LineUp(SolverNode[] nodes, int count)
        {
            for (int i = 0; i < count; i++)
            {
                ref var node = ref nodes[i];
                node.Line = 0;
                node.Lines = node.FlowCount > 0 ? 1 : 0;
            }
            for (int p = 0; p < count; p++)
            {
                ref var parent = ref nodes[p];
                if (WrapOf(parent) == LayoutWrapMode.Grid)
                    LineUpCells(nodes, ref parent, PerLineOf(parent));
            }
        }

        // Puts a grid's flow children in lines of `perLine`, in order, and says how many lines they run to.
        private static void LineUpCells(SolverNode[] nodes, ref SolverNode parent, int perLine)
        {
            int index = 0;
            for (int c = parent.FirstChild; c >= 0; c = nodes[c].NextSibling)
            {
                if (!nodes[c].Floating.IsFloating)
                    nodes[c].Line = index++ / perLine;
            }
            parent.Lines = (index + perLine - 1) / perLine;
        }

        // How many cells a grid has to a line: its Count, at least one.
        private static int PerLineOf(in SolverNode node) => Mathf.Max(1, node.Wrap.Count);

        // How many cells at least an adaptive grid's MinSize long fit along `content` with the gaps between them: at
        // least one, and with no MinSize, one for each child in its flow.
        private static int AdaptiveCount(in SolverNode node, float content)
        {
            float min = node.Wrap.MinSize;
            if (min <= Epsilon) return Mathf.Max(1, node.FlowCount);
            return Mathf.Max(1, Mathf.FloorToInt((content + node.ChildGap + Epsilon) / (min + node.ChildGap)));
        }

        // The first child from `c` on that is in its parent's flow (not floating), or -1.
        private static int FirstInFlow(SolverNode[] nodes, int c)
        {
            while (c >= 0 && nodes[c].Floating.IsFloating)
                c = nodes[c].NextSibling;
            return c;
        }

        // The child after the last of `first`'s line, in its parent's flow: the first of the next line, or -1.
        private static int EndOfLine(SolverNode[] nodes, int first)
        {
            int line = nodes[first].Line;
            int c = nodes[first].NextSibling;
            while (c >= 0 && (nodes[c].Floating.IsFloating || nodes[c].Line == line))
                c = nodes[c].NextSibling;
            return c;
        }

        // How thick the line from `first` up to `end` is across `axis`: as its thickest child.
        private static float ThicknessOf(SolverNode[] nodes, int first, int end, int axis)
        {
            float thick = 0f;
            for (int c = first; c != end; c = nodes[c].NextSibling)
            {
                if (!nodes[c].Floating.IsFloating)
                    thick = Mathf.Max(thick, nodes[c].Size[axis]);
            }
            return thick;
        }

        // ── Sizing ───────────────────────────────────────────────────────────────

        // Clay's fit sizing (which Clay does as each element closes): every node, children before parents, gets the
        // size its content asks for along the axis, and the smallest it could be squeezed to. The root is skipped: it
        // is given its size.
        private static void FitAxis(SolverNode[] nodes, int count, int axis)
        {
            // Pre-order puts every node after its parent, so going backwards reaches children before their parents.
            for (int i = count - 1; i > 0; i--)
            {
                ref var node = ref nodes[i];
                var sizing = SizingOf(node, axis);
                float size, min;

                if (axis == 1 && node.AspectRatio > 0f)
                {
                    // Its height follows its final width, and nothing grows or shrinks it.
                    size = min = node.Size.x / node.AspectRatio;
                }
                else if (sizing.Mode == SizingMode.Fixed)
                {
                    size = min = sizing.Value;
                }
                else if (sizing.Mode == SizingMode.Percent)
                {
                    // A part of its parent's size, which is not known yet: it adds nothing to what its parent fits to.
                    size = min = 0f;
                }
                else
                {
                    float padding = PaddingOf(node, axis);
                    if (node.Content != null && node.FlowCount == 0)
                    {
                        if (axis == 0)
                        {
                            // As wide as it is unwrapped, and no narrower than its longest word (which can be no wider
                            // than the whole of it).
                            float measured = node.Content.Measure(-1f).x;
                            size = measured + padding;
                            min = Mathf.Min(node.Content.MinWidth, measured) + padding;
                        }
                        else
                        {
                            // As tall as it is wrapped to its final width, padding included; text cannot be squeezed
                            // shorter than that.
                            size = min = node.ContentHeight;
                        }
                    }
                    else
                    {
                        float sum = 0f, sumMin = 0f, largest = 0f, largestMin = 0f;
                        for (int c = node.FirstChild; c >= 0; c = nodes[c].NextSibling)
                        {
                            ref var child = ref nodes[c];
                            if (child.Floating.IsFloating) continue;
                            float childSize = child.Size[axis];
                            float childMin = child.MinSize[axis];
                            sum += childSize;
                            sumMin += childMin;
                            largest = Mathf.Max(largest, childSize);
                            largestMin = Mathf.Max(largestMin, childMin);
                        }

                        var wrap = WrapOf(node);
                        if (wrap != LayoutWrapMode.None)
                        {
                            FitWrapped(nodes, node, axis, wrap, sum, largest, largestMin, out size, out min);
                        }
                        else if (IsAlong(node, axis))
                        {
                            // Its children end to end with the gaps between them.
                            float gaps = GapsOf(node);
                            size = sum + gaps;
                            min = sumMin + gaps;
                        }
                        else
                        {
                            // Its largest child.
                            size = largest;
                            min = largestMin;
                        }
                        size += padding;
                        min += padding;
                    }

                    size = sizing.Clamp(size);
                    // A scroll container fits to all it holds, but shows only part of it the way it scrolls, so that
                    // way it can be squeezed to nothing (to its own min), as Clay's scroll containers can.
                    // What scrolls is its children: a node holding none (a text drawn by itself) is not squeezed for it.
                    min = sizing.Clamp(Scrolls(node, axis) && node.FlowCount > 0 ? 0f : min);
                }

                node.Size[axis] = size;
                node.MinSize[axis] = min;
            }
        }

        // What a node wrapping its children fits to along one axis, padding aside (its flow children adding up to `sum`
        // that way, the longest `largest`, the longest of their smallest `largestMin`), and the least it can be squeezed
        // to. Along its direction, in lines: all of them on one line, down to its longest child, wrapping more as it is
        // squeezed, as text does; in a grid: Count cells as long as its longest child, down to Count of the longest
        // smallest; adaptive: all of them in one line of cells at least MinSize long, down to one cell. Across: its
        // lines end to end with the gaps between them, each as thick as its thickest child.
        private static void FitWrapped(SolverNode[] nodes, in SolverNode node, int axis, LayoutWrapMode wrap, float sum, float largest,
            float largestMin, out float size, out float min)
        {
            float gap = node.ChildGap;
            if (IsAlong(node, axis))
            {
                if (wrap == LayoutWrapMode.Lines)
                {
                    size = sum + GapsOf(node);
                    min = largestMin;
                }
                else if (wrap == LayoutWrapMode.Grid)
                {
                    int perLine = PerLineOf(node);
                    size = perLine * largest + (perLine - 1) * gap;
                    min = perLine * largestMin + (perLine - 1) * gap;
                }
                else
                {
                    size = node.FlowCount * Mathf.Max(node.Wrap.MinSize, largest) + GapsOf(node);
                    min = Mathf.Max(node.Wrap.MinSize, largestMin);
                }
                return;
            }

            size = min = 0f;
            int lines = 0;
            for (int first = FirstInFlow(nodes, node.FirstChild); first >= 0; lines++)
            {
                int end = EndOfLine(nodes, first);
                float thick = 0f, thickMin = 0f;
                for (int c = first; c != end; c = nodes[c].NextSibling)
                {
                    ref var child = ref nodes[c];
                    if (child.Floating.IsFloating) continue;
                    thick = Mathf.Max(thick, child.Size[axis]);
                    thickMin = Mathf.Max(thickMin, child.MinSize[axis]);
                }
                size += thick;
                min += thickMin;
                first = end;
            }
            float gaps = lines > 1 ? (lines - 1) * gap : 0f;
            size += gaps;
            min += gaps;
        }

        // Clay's Clay__SizeContainersAlongAxis: parents before children, each parent (its own size final by then)
        // sizes its children along the axis. Percent children take their part; along the parent's direction, what is
        // left over goes to its grow children, or when there is too little room it is taken from its fit and grow
        // children; across it, grow children fill it and wider ones shrink to it. A scroll container, the way it
        // scrolls, takes nothing from its children: along it they overflow, and across it they fill (and are kept
        // within) the widest of them when that is wider than it. Floating children are sized against what they are
        // attached to: their parent or the root with the rest of the parent's children, and an element in their own
        // turn, which the order puts after the element's (so its size is final), before they size their children. A
        // parent that wraps its children shares its size out line by line (DistributeWrapped).
        private static void DistributeAxis(SolverNode[] nodes, int axis)
        {
            float rootContent = nodes[0].Size[axis] - PaddingOf(nodes[0], axis);

            for (int p = 0; p >= 0; p = nodes[p].Next)
            {
                ref var parent = ref nodes[p];
                if (p > 0 && parent.Floating.AttachTo == FloatingAttach.Element && !(axis == 1 && parent.AspectRatio > 0f))
                    SizeFloating(ref parent, SizingOf(parent, axis), axis, nodes[parent.Element].Size[axis]);
                if (parent.FirstChild < 0) continue;

                float content = parent.Size[axis] - PaddingOf(parent, axis);
                var wrap = WrapOf(parent);
                if (wrap != LayoutWrapMode.None)
                {
                    DistributeWrapped(nodes, ref parent, axis, wrap, content, rootContent);
                    continue;
                }

                bool along = IsAlong(parent, axis);
                bool scrolls = Scrolls(parent, axis);
                float gaps = along ? GapsOf(parent) : 0f;
                float used = 0f, largest = 0f;
                int growing = 0;

                for (int c = parent.FirstChild; c >= 0; c = nodes[c].NextSibling)
                {
                    ref var child = ref nodes[c];
                    child.Resizing = false;
                    var sizing = SizingOf(child, axis);
                    // A height that follows its width is final already, whatever its sizing says.
                    bool followsWidth = axis == 1 && child.AspectRatio > 0f;

                    if (child.Floating.IsFloating)
                    {
                        SizeFloatingChild(ref child, sizing, axis, followsWidth, content, rootContent);
                        continue;
                    }

                    if (!followsWidth)
                    {
                        if (sizing.Mode == SizingMode.Percent)
                        {
                            // Along a row that has to fit, a part of what the gaps leave, so halves and a gap fit (as
                            // Clay). Along the way a scroll container scrolls nothing has to fit: a part of what it
                            // shows, so a card of 85% stays 85% however many there are (a carousel's peek, a page).
                            child.Size[axis] = sizing.Clamp(sizing.Value * (scrolls ? content : content - gaps));
                        }
                        else if (sizing.Mode != SizingMode.Fixed)
                        {
                            child.Resizing = true;
                            if (sizing.Mode == SizingMode.Grow) growing++;
                        }
                    }

                    used += child.Size[axis];
                    largest = Mathf.Max(largest, child.Size[axis]);
                }

                if (along)
                {
                    float remaining = content - used - gaps;
                    if (remaining < -Epsilon)
                    {
                        // A scroll container's children run on past its end rather than being squeezed.
                        if (!scrolls)
                            Shrink(nodes, parent.FirstChild, -1, axis, remaining);
                    }
                    else if (remaining > Epsilon && growing > 0)
                    {
                        Grow(nodes, parent.FirstChild, -1, axis, remaining);
                    }
                }
                else
                {
                    // Across: a grow child fills the parent (within its max), and any child wider than the parent
                    // shrinks to it, though never below its smallest. A scroll container scrolling this way fills and
                    // limits its children to the widest of them when that is wider than it, which squeezes none of
                    // them, as Clay grows a scroll container's children to its content rather than to itself.
                    float room = scrolls ? Mathf.Max(content, largest) : content;
                    for (int c = parent.FirstChild; c >= 0; c = nodes[c].NextSibling)
                    {
                        ref var child = ref nodes[c];
                        if (!child.Resizing) continue;
                        var sizing = SizingOf(child, axis);
                        float size = sizing.Mode == SizingMode.Grow ? Mathf.Min(room, sizing.MaxOrInfinity) : child.Size[axis];
                        child.Size[axis] = Mathf.Max(child.MinSize[axis], Mathf.Min(size, room));
                    }
                }
            }
        }

        // A parent that wraps its children shares its size out among them along one axis, as DistributeAxis does for
        // one that does not. Along its direction, in lines: percent children take their part of all of it, fixed ones
        // are what they are and fit and grow ones what their content asks, and a child that would run past the end of a
        // line starts the next (FillLines). In a grid, the cells share it equally after the gaps, as many to a line as its
        // Count or as fit at its MinSize, and each child is sized in its cell (SizeInCell). Across, each line is as thick
        // as its thickest child, which its grow children fill, and percent children are a part of all of it. Floating
        // children are sized as DistributeAxis sizes them.
        private static void DistributeWrapped(SolverNode[] nodes, ref SolverNode parent, int axis, LayoutWrapMode wrap, float content,
            float rootContent)
        {
            bool along = IsAlong(parent, axis);
            bool cells = along && wrap != LayoutWrapMode.Lines;
            if (cells)
            {
                int perLine = wrap == LayoutWrapMode.Grid ? PerLineOf(parent) : AdaptiveCount(parent, content);
                parent.Cell = Mathf.Max(0f, (content - (perLine - 1) * parent.ChildGap) / perLine);
                LineUpCells(nodes, ref parent, perLine);
            }

            for (int c = parent.FirstChild; c >= 0; c = nodes[c].NextSibling)
            {
                ref var child = ref nodes[c];
                child.Resizing = false;
                var sizing = SizingOf(child, axis);
                bool followsWidth = axis == 1 && child.AspectRatio > 0f;
                if (child.Floating.IsFloating)
                {
                    SizeFloatingChild(ref child, sizing, axis, followsWidth, content, rootContent);
                    continue;
                }
                if (followsWidth) continue;

                if (cells)
                    SizeInCell(ref child, sizing, axis, parent.Cell);
                else if (sizing.Mode == SizingMode.Percent)
                    child.Size[axis] = sizing.Clamp(sizing.Value * content);
                else if (sizing.Mode != SizingMode.Fixed)
                    child.Resizing = true;
            }

            if (cells) return;
            if (along)
                FillLines(nodes, ref parent, axis, content);
            else
                FillAcross(nodes, parent, axis);
        }

        // Breaks a parent's flow into lines, as text breaks: a child that would run past the end of the line starts the
        // next, unless it is the first on it, each at the size it has before its line is shared out. Then each line is
        // shared out as a row is: what it has left over goes to its grow children, and a child too long for it, alone on
        // it, is shrunk, down to its smallest.
        private static void FillLines(SolverNode[] nodes, ref SolverNode parent, int axis, float content)
        {
            float gap = parent.ChildGap;
            int line = 0, onLine = 0;
            float run = 0f;
            for (int c = parent.FirstChild; c >= 0; c = nodes[c].NextSibling)
            {
                ref var child = ref nodes[c];
                if (child.Floating.IsFloating) continue;
                float size = child.Size[axis];
                if (onLine > 0 && run + gap + size > content + Epsilon)
                {
                    line++;
                    onLine = 0;
                }
                run = onLine > 0 ? run + gap + size : size;
                onLine++;
                child.Line = line;
            }
            parent.Lines = line + 1;

            for (int first = FirstInFlow(nodes, parent.FirstChild); first >= 0;)
            {
                int end = EndOfLine(nodes, first);
                float used = 0f;
                int count = 0, growing = 0;
                for (int c = first; c != end; c = nodes[c].NextSibling)
                {
                    ref var child = ref nodes[c];
                    if (child.Floating.IsFloating) continue;
                    used += child.Size[axis];
                    count++;
                    if (child.Resizing && SizingOf(child, axis).Mode == SizingMode.Grow) growing++;
                }
                float remaining = content - used - (count - 1) * gap;
                if (remaining < -Epsilon)
                    Shrink(nodes, first, end, axis, remaining);
                else if (remaining > Epsilon && growing > 0)
                    Grow(nodes, first, end, axis, remaining);
                first = end;
            }
        }

        // Across a wrapping parent, each line is as thick as its thickest child, and its grow children fill it (within
        // their max).
        private static void FillAcross(SolverNode[] nodes, in SolverNode parent, int axis)
        {
            for (int first = FirstInFlow(nodes, parent.FirstChild); first >= 0;)
            {
                int end = EndOfLine(nodes, first);
                float thick = ThicknessOf(nodes, first, end, axis);
                for (int c = first; c != end; c = nodes[c].NextSibling)
                {
                    ref var child = ref nodes[c];
                    var sizing = SizingOf(child, axis);
                    if (child.Resizing && sizing.Mode == SizingMode.Grow)
                        child.Size[axis] = Mathf.Max(child.MinSize[axis], Mathf.Min(thick, sizing.MaxOrInfinity));
                }
                first = end;
            }
        }

        // A grid's child sized along its line in its cell, as a child is across a row in its parent: grow fills the cell
        // (within its max), percent is a part of it, fit shrinks to it and fixed stays as it is, neither below its
        // smallest.
        private static void SizeInCell(ref SolverNode child, Sizing sizing, int axis, float cell)
        {
            switch (sizing.Mode)
            {
                case SizingMode.Percent:
                    child.Size[axis] = sizing.Clamp(sizing.Value * cell);
                    break;
                case SizingMode.Grow:
                    child.Size[axis] = Mathf.Max(child.MinSize[axis], Mathf.Min(cell, sizing.MaxOrInfinity));
                    break;
                case SizingMode.Fit:
                    child.Size[axis] = Mathf.Max(child.MinSize[axis], Mathf.Min(child.Size[axis], cell));
                    break;
            }
        }

        // A floating child, but for one attached to an element (sized in its own turn) or whose height follows its
        // width, sized against its parent's content box, or the root's when it floats against the root (SizeFloating).
        private static void SizeFloatingChild(ref SolverNode child, Sizing sizing, int axis, bool followsWidth, float content,
            float rootContent)
        {
            if (!followsWidth && child.Floating.AttachTo != FloatingAttach.Element)
                SizeFloating(ref child, sizing, axis, child.Floating.AttachTo == FloatingAttach.Root ? rootContent : content);
        }

        // A floating child, sized against the content box of what it is attached to (the whole rect of an element, so
        // a highlight that grows both ways fills the tab it is behind) as a child across that node's direction is: grow
        // fills it and percent is a part of it. A fitted one keeps its own size even when wider (a tooltip may be wider
        // than its button), as Clay's floating elements do.
        private static void SizeFloating(ref SolverNode child, Sizing sizing, int axis, float against)
        {
            if (sizing.Mode == SizingMode.Percent)
                child.Size[axis] = sizing.Clamp(sizing.Value * against);
            else if (sizing.Mode == SizingMode.Grow)
                child.Size[axis] = Mathf.Max(child.MinSize[axis], Mathf.Min(against, sizing.MaxOrInfinity));
        }

        // Clay's compression along a parent's direction, over its children from `firstChild` up to `stop` (-1 for all of
        // them, or the first of the next line): round after round, the largest of the children still shrinking come down
        // together towards the next largest, sharing what has to go, each stopping at its smallest, until everything fits
        // or none can shrink (the rest then overflows). Taking from the largest first keeps the small children whole.
        private static void Shrink(SolverNode[] nodes, int firstChild, int stop, int axis, float remaining)
        {
            while (remaining < -Epsilon)
            {
                float largest = float.NegativeInfinity;
                for (int c = firstChild; c != stop; c = nodes[c].NextSibling)
                    if (nodes[c].Resizing)
                        largest = Mathf.Max(largest, nodes[c].Size[axis]);
                if (float.IsNegativeInfinity(largest)) return;

                float next = float.NegativeInfinity;
                int atLargest = 0;
                for (int c = firstChild; c != stop; c = nodes[c].NextSibling)
                {
                    if (!nodes[c].Resizing) continue;
                    float size = nodes[c].Size[axis];
                    if (size > largest - Epsilon) atLargest++;
                    else next = Mathf.Max(next, size);
                }

                // Each takes its share of what has to go, but comes down no further than the next largest this round.
                float step = remaining / atLargest;
                if (!float.IsNegativeInfinity(next)) step = Mathf.Max(step, next - largest);

                bool changed = false;
                for (int c = firstChild; c != stop; c = nodes[c].NextSibling)
                {
                    ref var child = ref nodes[c];
                    if (!child.Resizing) continue;
                    float before = child.Size[axis];
                    if (before <= largest - Epsilon) continue;

                    float after = before + step;
                    if (after <= child.MinSize[axis])
                    {
                        after = child.MinSize[axis];
                        child.Resizing = false;
                        changed = true;
                    }
                    child.Size[axis] = after;
                    changed |= after != before;
                    remaining -= after - before;
                }
                // A share too small to move sizes this large (floats round it away) is as good as nothing left.
                if (!changed) return;
            }
        }

        // Clay's expansion along a parent's direction, the mirror of Shrink (over the same children): only the grow
        // children take what is left over, round after round the smallest of them rising together towards the next
        // smallest, sharing it, each stopping at its max, until none is left or none can grow. Filling the smallest first
        // evens them out.
        private static void Grow(SolverNode[] nodes, int firstChild, int stop, int axis, float remaining)
        {
            // Fit children stay as their content asks.
            for (int c = firstChild; c != stop; c = nodes[c].NextSibling)
                if (nodes[c].Resizing && SizingOf(nodes[c], axis).Mode != SizingMode.Grow)
                    nodes[c].Resizing = false;

            while (remaining > Epsilon)
            {
                float smallest = float.PositiveInfinity;
                for (int c = firstChild; c != stop; c = nodes[c].NextSibling)
                    if (nodes[c].Resizing)
                        smallest = Mathf.Min(smallest, nodes[c].Size[axis]);
                if (float.IsPositiveInfinity(smallest)) return;

                float next = float.PositiveInfinity;
                int atSmallest = 0;
                for (int c = firstChild; c != stop; c = nodes[c].NextSibling)
                {
                    if (!nodes[c].Resizing) continue;
                    float size = nodes[c].Size[axis];
                    if (size < smallest + Epsilon) atSmallest++;
                    else next = Mathf.Min(next, size);
                }

                // Each takes its share of what is left, but rises no further than the next smallest this round.
                float step = remaining / atSmallest;
                if (!float.IsPositiveInfinity(next)) step = Mathf.Min(step, next - smallest);

                bool changed = false;
                for (int c = firstChild; c != stop; c = nodes[c].NextSibling)
                {
                    ref var child = ref nodes[c];
                    if (!child.Resizing) continue;
                    float before = child.Size[axis];
                    if (before >= smallest + Epsilon) continue;

                    // The largest its sizing allows (its min wins over a smaller max, as in Sizing.Clamp).
                    float max = SizingOf(child, axis).Clamp(float.PositiveInfinity);
                    float after = before + step;
                    if (after >= max)
                    {
                        after = max;
                        child.Resizing = false;
                        changed = true;
                    }
                    child.Size[axis] = after;
                    changed |= after != before;
                    remaining -= after - before;
                }
                // A share too small to move sizes this large (floats round it away) is as good as nothing left.
                if (!changed) return;
            }
        }

        // Every node with content and no flow children, fitted to it in height, measures its content wrapped to the
        // width it got (inside its padding), as Clay wraps text between its width and height passes.
        private static void WrapContent(SolverNode[] nodes, int count)
        {
            for (int i = 1; i < count; i++)
            {
                ref var node = ref nodes[i];
                if (node.Content == null || node.FlowCount > 0 || node.AspectRatio > 0f) continue;
                if (node.Height.Mode != SizingMode.Fit && node.Height.Mode != SizingMode.Grow) continue;

                // A negative width would mean no limit to Measure, so a node narrower than its padding wraps at 0.
                float width = Mathf.Max(0f, node.Size.x - node.Padding.Horizontal);
                node.ContentHeight = node.Content.Measure(width).y + node.Padding.Vertical;
            }
        }

        // How big what each node holds is, padding included, from the sizes everything ended up with: its flow
        // children end to end along its direction with the gaps between them, and the largest of them across (wrapping,
        // its longest line along and its lines end to end across). A node with none in its flow holds nothing that
        // scrolls (what it measures, a text say, it draws itself, where no scroll offset moves it): what it holds is its
        // own size, and it is not measured again. Floating children take no space, so they count for nothing here, as
        // they fit nothing. A scroll container scrolls as far as this runs past it.
        private static void SizeContent(SolverNode[] nodes, int count)
        {
            for (int i = 0; i < count; i++)
            {
                ref var node = ref nodes[i];
                if (node.FlowCount == 0)
                {
                    node.ContentSize = node.Size;
                    continue;
                }

                int along = node.Direction == LayoutDirection.LeftToRight ? 0 : 1;
                int across = 1 - along;
                var held = Vector2.zero;
                var wrap = WrapOf(node);
                if (wrap != LayoutWrapMode.None)
                {
                    held = LinesExtent(nodes, node, wrap, along);
                }
                else
                {
                    float run = GapsOf(node), largest = 0f;
                    for (int c = node.FirstChild; c >= 0; c = nodes[c].NextSibling)
                    {
                        ref var child = ref nodes[c];
                        if (child.Floating.IsFloating) continue;
                        run += child.Size[along];
                        largest = Mathf.Max(largest, child.Size[across]);
                    }
                    held[along] = run;
                    held[across] = largest;
                }
                node.ContentSize = held + new Vector2(node.Padding.Horizontal, node.Padding.Vertical);
            }
        }

        // How far a wrapping node's lines run, padding aside: along its direction, its longest line (its children, or in
        // a grid the cells they are in, end to end with the gaps between them), and across, its lines end to end with the
        // gaps between them, each as thick as its thickest child.
        private static Vector2 LinesExtent(SolverNode[] nodes, in SolverNode node, LayoutWrapMode wrap, int along)
        {
            float gap = node.ChildGap, longest = 0f, lines = 0f;
            int count = 0;
            for (int first = FirstInFlow(nodes, node.FirstChild); first >= 0; count++)
            {
                int end = EndOfLine(nodes, first);
                float run = 0f;
                int on = 0;
                for (int c = first; c != end; c = nodes[c].NextSibling)
                {
                    if (nodes[c].Floating.IsFloating) continue;
                    run += wrap == LayoutWrapMode.Lines ? nodes[c].Size[along] : node.Cell;
                    on++;
                }
                longest = Mathf.Max(longest, run + (on - 1) * gap);
                lines += ThicknessOf(nodes, first, end, 1 - along);
                first = end;
            }
            var extent = Vector2.zero;
            extent[along] = longest;
            extent[1 - along] = lines + (count > 1 ? (count - 1) * gap : 0f);
            return extent;
        }

        // ── Positions ────────────────────────────────────────────────────────────

        // Clay's Clay__CalculateFinalLayout positioning, parents before children: each parent's flow children run
        // along its direction from its padding (PlaceRun), or in lines when it wraps them (PlaceLines). Floating children
        // are placed against their parent's rect as it was laid out, or the root's safe rect (the root's own, less the
        // safe area it keeps its content clear of), which is known by then since every parent is placed before its
        // children; one attached to an element is placed in its own turn, which the order puts after both its parent's
        // and the element's, against the element's rect. Each node placed reaches out past the safe rect where it
        // ignores the safe area and lies against it (ReachOut). Where a scroll container is scrolled to plays no part:
        // its children go where they would at 0, and the system moves them by it as it draws them (and a node attached
        // to an element inside it by the same, as it gives it its target).
        private static void Place(SolverNode[] nodes, Insets safeArea)
        {
            ref var root = ref nodes[0];
            root.Rect = new Rect(Vector2.zero, root.Size);
            root.RootRect = root.Rect;
            Vector2 rootSize = root.Size;
            var safe = Rect.MinMaxRect(safeArea.Left, safeArea.Top, rootSize.x - safeArea.Right, rootSize.y - safeArea.Bottom);

            for (int p = 0; p >= 0; p = nodes[p].Next)
            {
                ref var parent = ref nodes[p];
                if (p > 0 && parent.Floating.AttachTo == FloatingAttach.Element)
                {
                    // The element's rect, brought into its parent's space.
                    ref var up = ref nodes[parent.Parent];
                    ref var element = ref nodes[parent.Element];
                    var topLeft = FloatingTopLeft(parent, element.RootRect.position - up.RootRect.position, element.Size);
                    PutAt(ref parent, up, topLeft, rootSize, safe);
                }
                if (parent.FirstChild < 0) continue;

                var start = new Vector2(parent.Padding.Left, parent.Padding.Top);
                var content = new Vector2(parent.Size.x - parent.Padding.Horizontal, parent.Size.y - parent.Padding.Vertical);
                var align = new Vector2(Fraction(parent.AlignX), Fraction(parent.AlignY));
                var wrap = WrapOf(parent);
                if (wrap != LayoutWrapMode.None)
                    PlaceLines(nodes, parent, wrap, start, content, align, rootSize, safe);
                else if (parent.FlowCount > 0)
                    PlaceRun(nodes, parent, start, content, align, rootSize, safe);

                // Floating children, but for one attached to an element (placed in its own turn): against the root's
                // safe rect, brought into this parent's space, or against this parent's rect as laid out, before it
                // reached out past the safe area, so what floats in it stays where it was as what is in its flow does.
                for (int c = parent.FirstChild; c >= 0; c = nodes[c].NextSibling)
                {
                    ref var child = ref nodes[c];
                    var attach = child.Floating.AttachTo;
                    if (attach == FloatingAttach.None || attach == FloatingAttach.Element) continue;
                    var reach = parent.Reach;
                    var topLeft = attach == FloatingAttach.Root
                        ? FloatingTopLeft(child, safe.position - parent.RootRect.position, safe.size)
                        : FloatingTopLeft(child, new Vector2(reach.Left, reach.Top),
                            parent.Size - new Vector2(reach.Horizontal, reach.Vertical));
                    PutAt(ref child, parent, topLeft, rootSize, safe);
                }
            }
        }

        // Places a parent's flow children in one run along its direction, as Clay does: from its padding, the run moved
        // by its alignment when there is room to spare, each child aligned across it on its own. An overflowing run
        // starts at the padding, whatever the alignment, so a scroll container can reach all of it. Across the way a
        // scroll container scrolls, its children are aligned within the widest of them when that runs past it, as a
        // stack in SwiftUI's ScrollView is: the widest starts at the padding and the rest sit against it, so all of them
        // lie within its scroll range. Otherwise each is aligned within the parent, overflowing it both ways if it is
        // wider when centred.
        private static void PlaceRun(SolverNode[] nodes, in SolverNode parent, Vector2 start, Vector2 content, Vector2 align,
            Vector2 rootSize, Rect safe)
        {
            int along = parent.Direction == LayoutDirection.LeftToRight ? 0 : 1;
            int across = 1 - along;
            float run = GapsOf(parent), largest = 0f;
            for (int c = parent.FirstChild; c >= 0; c = nodes[c].NextSibling)
            {
                if (nodes[c].Floating.IsFloating) continue;
                run += nodes[c].Size[along];
                largest = Mathf.Max(largest, nodes[c].Size[across]);
            }
            float free = content[along] - run;
            float cursor = start[along] + (free > 0f ? free * align[along] : 0f);
            float room = Scrolls(parent, across) ? Mathf.Max(content[across], largest) : content[across];

            for (int c = parent.FirstChild; c >= 0; c = nodes[c].NextSibling)
            {
                ref var child = ref nodes[c];
                if (child.Floating.IsFloating) continue;
                // Read before it is put there, which may reach it out past the safe area.
                var size = child.Size;
                Vector2 topLeft = default;
                topLeft[along] = cursor;
                topLeft[across] = start[across] + (room - size[across]) * align[across];
                cursor += size[along] + parent.ChildGap;
                PutAt(ref child, parent, topLeft, rootSize, safe);
            }
        }

        // Places a wrapping parent's flow children line by line. The lines stack across its direction, ChildGap apart,
        // each as thick as its thickest child, as a block moved by its alignment when there is room to spare (an
        // overflowing block starts at the padding, as a run does). Along a line of Lines, the children run from the
        // padding, the line moved by the alignment when it has room to spare, as a run is; in a grid, each child goes in
        // its cell, the cells from the padding on, aligned in it. Across, each is aligned in its line.
        private static void PlaceLines(SolverNode[] nodes, in SolverNode parent, LayoutWrapMode wrap, Vector2 start, Vector2 content,
            Vector2 align, Vector2 rootSize, Rect safe)
        {
            int along = parent.Direction == LayoutDirection.LeftToRight ? 0 : 1;
            int across = 1 - along;
            float gap = parent.ChildGap;
            float spare = content[across] - LinesExtent(nodes, parent, wrap, along)[across];
            float lineStart = start[across] + (spare > 0f ? spare * align[across] : 0f);

            for (int first = FirstInFlow(nodes, parent.FirstChild); first >= 0;)
            {
                int end = EndOfLine(nodes, first);
                float thick = ThicknessOf(nodes, first, end, across);
                float cursor = start[along];
                if (wrap == LayoutWrapMode.Lines)
                {
                    float run = 0f;
                    int on = 0;
                    for (int c = first; c != end; c = nodes[c].NextSibling)
                    {
                        if (nodes[c].Floating.IsFloating) continue;
                        run += nodes[c].Size[along];
                        on++;
                    }
                    float free = content[along] - run - (on - 1) * gap;
                    if (free > 0f) cursor += free * align[along];
                }

                for (int c = first; c != end; c = nodes[c].NextSibling)
                {
                    ref var child = ref nodes[c];
                    if (child.Floating.IsFloating) continue;
                    // Read before it is put there, which may reach it out past the safe area.
                    var size = child.Size;
                    Vector2 topLeft = default;
                    if (wrap == LayoutWrapMode.Lines)
                    {
                        topLeft[along] = cursor;
                        cursor += size[along] + gap;
                    }
                    else
                    {
                        topLeft[along] = cursor + (parent.Cell - size[along]) * align[along];
                        cursor += parent.Cell + gap;
                    }
                    topLeft[across] = lineStart + (thick - size[across]) * align[across];
                    PutAt(ref child, parent, topLeft, rootSize, safe);
                }
                lineStart += thick + gap;
                first = end;
            }
        }

        // Puts a node at `topLeft` in its parent's layout space, at the size it has, then reaches it out past the safe
        // area where it ignores that (ReachOut).
        private static void PutAt(ref SolverNode node, in SolverNode parent, Vector2 topLeft, Vector2 rootSize, Rect safe)
        {
            node.Rect = new Rect(topLeft, node.Size);
            node.RootRect = new Rect(parent.RootRect.position + topLeft, node.Size);
            if (node.Ignores != Edges.None)
                ReachOut(ref node, rootSize, safe);
        }

        // On each edge a node ignores the safe area on (Ignores), where it lies against the root's safe rect, or past it,
        // it reaches out to the root's edge, as SwiftUI's ignoresSafeArea extends a view only where it touches the safe
        // area: its rect grows out to that edge and its padding by as much, so what is inside it stays where it was
        // (Reach says how far), and so does what it holds, so it scrolls just as far: rows scroll on under the home bar and
        // come to rest clear of it. A node laid out further in (padding, or a centred node, between it and the safe area's
        // edge) does not reach.
        private static void ReachOut(ref SolverNode node, Vector2 rootSize, Rect safe)
        {
            var at = node.RootRect;
            var edges = node.Ignores;
            var reach = default(Insets);
            if ((edges & Edges.Left) != 0 && at.xMin <= safe.xMin + Touching)
                reach.Left = Mathf.Max(0f, at.xMin);
            if ((edges & Edges.Right) != 0 && at.xMax >= safe.xMax - Touching)
                reach.Right = Mathf.Max(0f, rootSize.x - at.xMax);
            if ((edges & Edges.Top) != 0 && at.yMin <= safe.yMin + Touching)
                reach.Top = Mathf.Max(0f, at.yMin);
            if ((edges & Edges.Bottom) != 0 && at.yMax >= safe.yMax - Touching)
                reach.Bottom = Mathf.Max(0f, rootSize.y - at.yMax);
            if (reach.Horizontal <= 0f && reach.Vertical <= 0f) return;

            var grown = new Vector2(reach.Horizontal, reach.Vertical);
            var shift = new Vector2(reach.Left, reach.Top);
            node.Reach = reach;
            node.Padding += reach;
            node.Size += grown;
            node.Rect = new Rect(node.Rect.position - shift, node.Size);
            node.RootRect = new Rect(at.position - shift, node.Size);
            node.ContentSize = node.FlowCount > 0 ? node.ContentSize + grown : node.Size;
        }

        // Where a floating child's top-left goes in its parent's layout space: its own point put on the target point of
        // what it is attached to (a rect at `targetMin`, sized `targetSize`, in its parent's space), then moved by its
        // offset (given y up, so flipped).
        private static Vector2 FloatingTopLeft(in SolverNode child, Vector2 targetMin, Vector2 targetSize)
        {
            var floating = child.Floating;
            Vector2 target = targetMin + Vector2.Scale(targetSize, PointFraction(floating.TargetPoint));
            Vector2 own = Vector2.Scale(child.Size, PointFraction(floating.Point));
            return target - own + new Vector2(floating.Offset.x, -floating.Offset.y);
        }

        // ── Axis helpers (axis 0 is x, 1 is y) ───────────────────────────────────

        private static Sizing SizingOf(in SolverNode node, int axis) => axis == 0 ? node.Width : node.Height;

        private static float PaddingOf(in SolverNode node, int axis) => axis == 0 ? node.Padding.Horizontal : node.Padding.Vertical;

        // Whether the axis is the one the node lays its children out along.
        private static bool IsAlong(in SolverNode node, int axis) => (node.Direction == LayoutDirection.LeftToRight) == (axis == 0);

        // Whether it scrolls along the axis: its children run past it that way rather than being squeezed.
        private static bool Scrolls(in SolverNode node, int axis) =>
            node.Scroll == ScrollAxis.Both || node.Scroll == (axis == 0 ? ScrollAxis.Horizontal : ScrollAxis.Vertical);

        // The gaps between its flow children, all together.
        private static float GapsOf(in SolverNode node) => node.FlowCount > 1 ? (node.FlowCount - 1) * node.ChildGap : 0f;

        // Left, centre, right (top, centre, bottom) as 0, 0.5, 1 of the space to spare.
        private static float Fraction(AlignX align) => (int)align * 0.5f;

        private static float Fraction(AlignY align) => (int)align * 0.5f;

        // An attach point as fractions of a rect's width and height, from its top-left (the enum runs left to right,
        // then top to bottom, three to a row).
        private static Vector2 PointFraction(AttachPoint point) => new((int)point % 3 * 0.5f, (int)point / 3 * 0.5f);
    }
}
