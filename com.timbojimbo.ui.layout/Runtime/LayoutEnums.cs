namespace TimboJimbo.UI.Layout
{
    /// <summary>The axis a node lays its children out along.</summary>
    public enum LayoutDirection
    {
        LeftToRight,
        TopToBottom,
    }

    /// <summary>Horizontal placement of the children within the free space.</summary>
    public enum AlignX
    {
        Left,
        Center,
        Right,
    }

    /// <summary>Vertical placement of the children within the free space.</summary>
    public enum AlignY
    {
        Top,
        Center,
        Bottom,
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
