using System;

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

    /// <summary>
    /// Whether a node's children run on in one line or wrap into more (<see cref="Wrap"/>), as CSS's flex-wrap and
    /// SwiftUI's lazy grids. Lines stack across the node's direction.
    /// </summary>
    public enum LayoutWrapMode
    {
        /// <summary>They run on in one line, as Clay's do.</summary>
        None,

        /// <summary>
        /// A child that would run past the end of the line starts the next, each keeping its own size, as text wraps
        /// words: tags, chips.
        /// </summary>
        Lines,

        /// <summary>Lines of <see cref="Wrap.Count"/> equal cells, as SwiftUI's LazyVGrid with flexible columns.</summary>
        Grid,

        /// <summary>
        /// Lines of as many equal cells at least <see cref="Wrap.MinSize"/> long as fit, stretched to fill the line, as
        /// SwiftUI's adaptive grid items: more columns the wider it is.
        /// </summary>
        Adaptive,
    }

    /// <summary>A set of a rect's edges.</summary>
    [Flags]
    public enum Edges
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8,
        All = Left | Right | Top | Bottom,
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

    /// <summary>
    /// Where a scroll container comes to rest once a drag or a flick lets go of it, as UIKit's paging and SwiftUI's
    /// scroll target behaviours. A flick moves it on one page or child at most, and a wheel notch one; either end is a
    /// place to rest too. ScrollTo, ScrollIntoView and ScrollOffset go exactly where they are told.
    /// </summary>
    public enum ScrollSnap
    {
        /// <summary>Anywhere: a flick glides on to a stop.</summary>
        None,

        /// <summary>On a whole page of what it shows, as UIKit's isPagingEnabled and SwiftUI's paging.</summary>
        Pages,

        /// <summary>
        /// With one of its children in its flow starting where its content starts, inside its padding, as SwiftUI's
        /// viewAligned: a carousel's card lined up at its leading edge.
        /// </summary>
        Children,
    }

    /// <summary>
    /// How a node matched by name fills the rect it moves through, from the other node's rect to its own, as CSS's
    /// <c>object-fit</c> fills a view transition's group with its snapshots: resized to it, or kept at its own laid-out
    /// size, as a picture of itself, and scaled onto it, centred.
    /// </summary>
    public enum MatchFit
    {
        /// <summary>Scaled evenly to the rect's width, running past it or short of it down its height, as the web scales its snapshots.</summary>
        MatchWidth,

        /// <summary>The rect itself changes size; nothing is scaled, and what is inside is laid out at its own size, pinned at its top left.</summary>
        Resize,

        /// <summary>Scaled on each axis to fill the rect exactly, its content stretched or squashed on the way.</summary>
        Fill,

        /// <summary>Scaled evenly so that all of it fits inside the rect.</summary>
        Contain,

        /// <summary>Scaled evenly so that it covers the whole rect, running past it (Match Clip crops it).</summary>
        Cover,
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
