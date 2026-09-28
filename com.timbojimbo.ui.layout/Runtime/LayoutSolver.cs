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
        public AlignX AlignX;
        public AlignY AlignY;
        public float AspectRatio;
        public Floating Floating;
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
        // gaps between them, and the largest of them across, at the sizes they ended up with (or, with no flow
        // children, its own size: only children scroll). A scrolling node's scroll range is how far this runs past
        // its own size.
        public Vector2 ContentSize;

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

        /// <summary>
        /// Lays out the first <paramref name="count"/> of <paramref name="nodes"/>, the root sized
        /// <paramref name="rootSize"/> (its own sizing is not used).
        /// </summary>
        /// <remarks>
        /// Follows Clay's Clay__SizeContainersAlongAxis and Clay__CalculateFinalLayout, with these differences: content
        /// is measured through <see cref="ILayoutMeasurable"/> (unwrapped for the fit width, then wrapped to the final
        /// width for the height, which is also the least a content node's height can be shrunk to); floating nodes
        /// attach only to their parent or the root, and grow or take a percentage of its content box (inside its
        /// padding) rather than its whole rect; and a run that overflows its parent starts at the padding whatever the
        /// alignment. As in Clay, percent children along a parent's direction take their part of what its padding and
        /// the gaps between its children leave (so two halves and a gap fit), and a fitted floating node keeps its own
        /// size. A scroll container is Clay's too, along each axis it scrolls: it squeezes none of its children, which
        /// run on past its end instead (a grow child still takes the room left, and across its direction fills out to
        /// the widest child), and it can itself be squeezed to nothing that way, while it still fits to all it holds.
        /// Unlike Clay, across its direction on an axis it scrolls, its children are aligned within the widest of them
        /// when that runs past it, as a stack in SwiftUI's ScrollView is, so all of them lie within its scroll range.
        /// Where it is scrolled to is not the solver's: the system moves its children by that as it draws them.
        /// Allocation-free: all its working state lives in the nodes.
        /// </remarks>
        public static void Solve(SolverNode[] nodes, int count, Vector2 rootSize)
        {
            if (count <= 0) return;

            // Which children are in each node's flow: only they fit it and have gaps between them.
            for (int i = 0; i < count; i++)
                nodes[i].FlowCount = 0;
            for (int i = 1; i < count; i++)
                if (!nodes[i].Floating.IsFloating)
                    nodes[nodes[i].Parent].FlowCount++;

            // Widths: fitted to content up the tree, then the root's given and each parent's shared out down it.
            FitAxis(nodes, count, 0);
            nodes[0].Size.x = rootSize.x;
            DistributeAxis(nodes, count, 0);

            // Content wrapped to the widths it got, then heights the same way as widths.
            WrapContent(nodes, count);
            FitAxis(nodes, count, 1);
            nodes[0].Size.y = rootSize.y;
            DistributeAxis(nodes, count, 1);

            // What each node holds, from the sizes everything ended up with, then where it all goes.
            SizeContent(nodes, count);
            Place(nodes, count);
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

                        if (IsAlong(node, axis))
                        {
                            // Its children end to end with the gaps between them.
                            float gaps = GapsOf(node);
                            size = sum + gaps + padding;
                            min = sumMin + gaps + padding;
                        }
                        else
                        {
                            // Its largest child.
                            size = largest + padding;
                            min = largestMin + padding;
                        }
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

        // Clay's Clay__SizeContainersAlongAxis: parents before children, each parent (its own size final by then)
        // sizes its children along the axis. Percent children take their part; along the parent's direction, what is
        // left over goes to its grow children, or when there is too little room it is taken from its fit and grow
        // children; across it, grow children fill it and wider ones shrink to it. A scroll container, the way it
        // scrolls, takes nothing from its children: along it they overflow, and across it they fill (and are kept
        // within) the widest of them when that is wider than it. Floating children are sized against what they are
        // attached to.
        private static void DistributeAxis(SolverNode[] nodes, int count, int axis)
        {
            float rootContent = nodes[0].Size[axis] - PaddingOf(nodes[0], axis);

            for (int p = 0; p < count; p++)
            {
                ref var parent = ref nodes[p];
                if (parent.FirstChild < 0) continue;

                float content = parent.Size[axis] - PaddingOf(parent, axis);
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
                        if (!followsWidth)
                            SizeFloating(ref child, sizing, axis, child.Floating.AttachTo == FloatingAttach.Root ? rootContent : content);
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
                            Shrink(nodes, parent.FirstChild, axis, remaining);
                    }
                    else if (remaining > Epsilon && growing > 0)
                    {
                        Grow(nodes, parent.FirstChild, axis, remaining);
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

        // A floating child, sized against the content box of what it is attached to as a child across that node's
        // direction is: grow fills it and percent is a part of it. A fitted one keeps its own size even when wider (a
        // tooltip may be wider than its button), as Clay's floating elements do.
        private static void SizeFloating(ref SolverNode child, Sizing sizing, int axis, float against)
        {
            if (sizing.Mode == SizingMode.Percent)
                child.Size[axis] = sizing.Clamp(sizing.Value * against);
            else if (sizing.Mode == SizingMode.Grow)
                child.Size[axis] = Mathf.Max(child.MinSize[axis], Mathf.Min(against, sizing.MaxOrInfinity));
        }

        // Clay's compression along a parent's direction: round after round, the largest of the children still
        // shrinking come down together towards the next largest, sharing what has to go, each stopping at its smallest,
        // until everything fits or none can shrink (the rest then overflows). Taking from the largest first keeps the
        // small children whole.
        private static void Shrink(SolverNode[] nodes, int firstChild, int axis, float remaining)
        {
            while (remaining < -Epsilon)
            {
                float largest = float.NegativeInfinity;
                for (int c = firstChild; c >= 0; c = nodes[c].NextSibling)
                    if (nodes[c].Resizing)
                        largest = Mathf.Max(largest, nodes[c].Size[axis]);
                if (float.IsNegativeInfinity(largest)) return;

                float next = float.NegativeInfinity;
                int atLargest = 0;
                for (int c = firstChild; c >= 0; c = nodes[c].NextSibling)
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
                for (int c = firstChild; c >= 0; c = nodes[c].NextSibling)
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

        // Clay's expansion along a parent's direction, the mirror of Shrink: only the grow children take what is left
        // over, round after round the smallest of them rising together towards the next smallest, sharing it, each
        // stopping at its max, until none is left or none can grow. Filling the smallest first evens them out.
        private static void Grow(SolverNode[] nodes, int firstChild, int axis, float remaining)
        {
            // Fit children stay as their content asks.
            for (int c = firstChild; c >= 0; c = nodes[c].NextSibling)
                if (nodes[c].Resizing && SizingOf(nodes[c], axis).Mode != SizingMode.Grow)
                    nodes[c].Resizing = false;

            while (remaining > Epsilon)
            {
                float smallest = float.PositiveInfinity;
                for (int c = firstChild; c >= 0; c = nodes[c].NextSibling)
                    if (nodes[c].Resizing)
                        smallest = Mathf.Min(smallest, nodes[c].Size[axis]);
                if (float.IsPositiveInfinity(smallest)) return;

                float next = float.PositiveInfinity;
                int atSmallest = 0;
                for (int c = firstChild; c >= 0; c = nodes[c].NextSibling)
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
                for (int c = firstChild; c >= 0; c = nodes[c].NextSibling)
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
        // children end to end along its direction with the gaps between them, and the largest of them across. A node
        // with none in its flow holds nothing that scrolls (what it measures, a text say, it draws itself, where no
        // scroll offset moves it): what it holds is its own size, and it is not measured again. Floating children take
        // no space, so they count for nothing here, as they fit nothing. A scroll container scrolls as far as this runs
        // past it.
        private static void SizeContent(SolverNode[] nodes, int count)
        {
            for (int i = 0; i < count; i++)
            {
                ref var node = ref nodes[i];
                var held = Vector2.zero;
                if (node.FlowCount > 0)
                {
                    int along = node.Direction == LayoutDirection.LeftToRight ? 0 : 1;
                    int across = 1 - along;
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
                else
                {
                    node.ContentSize = node.Size;
                    continue;
                }
                node.ContentSize = held + new Vector2(node.Padding.Horizontal, node.Padding.Vertical);
            }
        }

        // ── Positions ────────────────────────────────────────────────────────────

        // Clay's Clay__CalculateFinalLayout positioning, parents before children: each parent's flow children run
        // along its direction from its padding, the run moved by its alignment when there is room to spare, each child
        // aligned across it on its own. Floating children are placed against their parent's rect or the root's, which
        // is known by then since every parent is placed before its children. Where a scroll container is scrolled to
        // plays no part: its children go where they would at 0, and the system moves them by it as it draws them.
        private static void Place(SolverNode[] nodes, int count)
        {
            ref var root = ref nodes[0];
            root.Rect = new Rect(Vector2.zero, root.Size);
            root.RootRect = root.Rect;
            Vector2 rootSize = root.Size;

            for (int p = 0; p < count; p++)
            {
                ref var parent = ref nodes[p];
                if (parent.FirstChild < 0) continue;

                int along = parent.Direction == LayoutDirection.LeftToRight ? 0 : 1;
                int across = 1 - along;
                var start = new Vector2(parent.Padding.Left, parent.Padding.Top);
                var content = new Vector2(parent.Size.x - parent.Padding.Horizontal, parent.Size.y - parent.Padding.Vertical);
                var align = new Vector2(Fraction(parent.AlignX), Fraction(parent.AlignY));

                float run = GapsOf(parent), largest = 0f;
                for (int c = parent.FirstChild; c >= 0; c = nodes[c].NextSibling)
                {
                    if (nodes[c].Floating.IsFloating) continue;
                    run += nodes[c].Size[along];
                    largest = Mathf.Max(largest, nodes[c].Size[across]);
                }
                float free = content[along] - run;
                // An overflowing run starts at the padding, whatever the alignment, so a scroll container can reach
                // all of it.
                float cursor = start[along] + (free > 0f ? free * align[along] : 0f);
                // Across the way a scroll container scrolls, its children are aligned within the widest of them when
                // that runs past it, as a stack in SwiftUI's ScrollView is: the widest starts at the padding and the
                // rest sit against it, so all of them lie within its scroll range. Otherwise each is aligned within
                // the parent, overflowing it both ways if it is wider when centred.
                float room = Scrolls(parent, across) ? Mathf.Max(content[across], largest) : content[across];

                for (int c = parent.FirstChild; c >= 0; c = nodes[c].NextSibling)
                {
                    ref var child = ref nodes[c];
                    Vector2 topLeft;
                    if (child.Floating.IsFloating)
                    {
                        topLeft = FloatingTopLeft(parent, child, rootSize);
                    }
                    else
                    {
                        topLeft = default;
                        topLeft[along] = cursor;
                        topLeft[across] = start[across] + (room - child.Size[across]) * align[across];
                        cursor += child.Size[along] + parent.ChildGap;
                    }

                    child.Rect = new Rect(topLeft, child.Size);
                    child.RootRect = new Rect(parent.RootRect.position + topLeft, child.Size);
                }
            }
        }

        // Where a floating child's top-left goes in its parent's layout space: its own point put on the target point of
        // what it is attached to (its parent's rect, or the root's rect brought into its parent's space), then moved by
        // its offset (given y up, so flipped).
        private static Vector2 FloatingTopLeft(in SolverNode parent, in SolverNode child, Vector2 rootSize)
        {
            var floating = child.Floating;
            Vector2 targetMin, targetSize;
            if (floating.AttachTo == FloatingAttach.Root)
            {
                targetMin = -parent.RootRect.position;
                targetSize = rootSize;
            }
            else
            {
                targetMin = Vector2.zero;
                targetSize = parent.Size;
            }

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
