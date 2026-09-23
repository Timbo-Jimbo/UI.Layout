namespace TimboJimbo.UI.Layout
{
    /// <summary>The axis a node lays its children out along.</summary>
    public enum LayoutDirection
    {
        LeftToRight,
        TopToBottom,
    }

    /// <summary>
    /// Horizontal placement of the children within the free space. SpaceBetween applies along a left-to-right
    /// flow only: the free space is shared out between the children, the first and last touching the padding
    /// (one child, or no free space, sits at the left). Across a flow it is Left.
    /// </summary>
    public enum AlignX
    {
        Left,
        Center,
        Right,
        SpaceBetween,
    }

    /// <summary>
    /// Vertical placement of the children within the free space. SpaceBetween applies along a top-to-bottom
    /// flow only: the free space is shared out between the children, the first and last touching the padding
    /// (one child, or no free space, sits at the top). Across a flow it is Top.
    /// </summary>
    public enum AlignY
    {
        Top,
        Center,
        Bottom,
        SpaceBetween,
    }

    /// <summary>Where a node sits across its parent's flow, in place of the parent's alignment on that axis.</summary>
    public enum AlignSelf
    {
        /// <summary>The parent's alignment.</summary>
        Auto,
        /// <summary>The start of the axis: the top in a left-to-right parent, the left in a top-to-bottom one.</summary>
        Start,
        Center,
        /// <summary>The end of the axis: the bottom in a left-to-right parent, the right in a top-to-bottom one.</summary>
        End,
    }

    /// <summary>What a floating node is positioned against. None keeps the node in the flow.</summary>
    public enum AttachTo
    {
        None,
        Parent,
        Root,
        Element,
    }

    /// <summary>A point on a rect, named column then row. The order is row-major so a point's position is derived from its value.</summary>
    public enum AttachPoint
    {
        LeftTop,
        CenterTop,
        RightTop,
        LeftCenter,
        CenterCenter,
        RightCenter,
        LeftBottom,
        CenterBottom,
        RightBottom,
    }
}
