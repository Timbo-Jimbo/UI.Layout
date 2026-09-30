namespace TimboJimbo.UI.Layout
{
    /// <summary>How a node is sized along one axis, as Clay sizes an element.</summary>
    public enum SizingMode
    {
        /// <summary>As big as its content (its children, or what it measures), within its min and max.</summary>
        Fit,

        /// <summary>Its content's size at least, growing to fill what its parent has left over, within its min and max.</summary>
        Grow,

        /// <summary>Exactly its value.</summary>
        Fixed,

        /// <summary>
        /// Its value (0 to 1) of its parent's size inside the padding (along its parent's direction, of what the padding
        /// and the gaps between its siblings leave, so halves and a gap fit; but along the way a scroll container
        /// scrolls, of what it shows, so a card of 85% stays 85% however many there are), within its min and max.
        /// </summary>
        Percent,
    }

    /// <summary>Which way a node lays its children out.</summary>
    public enum LayoutDirection
    {
        LeftToRight,
        TopToBottom,
    }

    /// <summary>Where a node's children sit across its width.</summary>
    public enum AlignX
    {
        Left,
        Center,
        Right,
    }

    /// <summary>Where a node's children sit down its height.</summary>
    public enum AlignY
    {
        Top,
        Center,
        Bottom,
    }

    /// <summary>Whether a node is shown, as CSS's display and visibility say.</summary>
    public enum DisplayMode
    {
        /// <summary>Laid out and drawn.</summary>
        Visible,

        /// <summary>Laid out, keeping its space, but not drawn and not clickable.</summary>
        Hidden,

        /// <summary>Left out of layout, its siblings closing up, and not drawn or clickable.</summary>
        None,
    }

    /// <summary>
    /// The side a node slides past as it disappears, and in from as it appears (<see cref="DisplayEffect.Edge"/>): just
    /// clearing its parent's rect as drawn, so it goes out of view where something clips at that edge.
    /// </summary>
    public enum DisplayEdge
    {
        /// <summary>It does not slide.</summary>
        None,

        /// <summary>Past its parent's left edge.</summary>
        Left,

        /// <summary>Past its parent's right edge.</summary>
        Right,

        /// <summary>Past its parent's top edge.</summary>
        Top,

        /// <summary>Past its parent's bottom edge.</summary>
        Bottom,
    }

    /// <summary>What a floating node is placed against, rather than in its parent's flow.</summary>
    public enum FloatingAttach
    {
        /// <summary>Not floating: laid out in its parent's flow.</summary>
        None,

        /// <summary>Placed against its parent's rect.</summary>
        Parent,

        /// <summary>Placed against its layout root's rect.</summary>
        Root,

        /// <summary>
        /// Placed against another node's rect (<see cref="Floating.Element"/>) in the same layout tree, wherever it is
        /// in the tree, and kept on it as drawn while the scroll containers it is inside scroll: a highlight behind the
        /// selected tab, a tooltip on a row. Grow and percent sizes are of the element's whole size.
        /// </summary>
        Element,
    }

    /// <summary>
    /// Which way a node scrolls its children, as a Clay scroll container or a UIScrollView: it clips them, lets them run
    /// past its edge that way rather than squeezing them, and moves them all together by its scroll offset.
    /// </summary>
    public enum ScrollAxis
    {
        None,
        Vertical,
        Horizontal,
        Both,
    }

    /// <summary>
    /// Which end a scroll container keeps to as what it scrolls grows, as SwiftUI's defaultScrollAnchor does for where
    /// a scroll view starts and how it takes a change of size.
    /// </summary>
    public enum ScrollAnchor
    {
        /// <summary>It starts at its start, and its offset stays as it is as its content grows.</summary>
        Start,

        /// <summary>
        /// It starts at its end, and while it is there it stays there as its content grows, as a chat or a log does;
        /// scrolled away from its end, it stays where it is.
        /// </summary>
        End,
    }

    /// <summary>One of nine points on a rect: a corner, the middle of an edge, or its centre.</summary>
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
