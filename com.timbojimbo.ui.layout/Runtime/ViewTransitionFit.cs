namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// How a node fills the rect it has while it flies in a view transition: a half of a named pair, or a node growing
    /// out of its <see cref="LayoutNode.ViewTransitionOrigin"/> and back into it. Like the web's <c>object-fit</c> on a
    /// view transition's snapshots.
    /// </summary>
    public enum ViewTransitionFit
    {
        /// <summary>
        /// The rect changes size and the content keeps its layout, uncovered or clipped as the rect grows or shrinks.
        /// </summary>
        Resize,

        /// <summary>
        /// The node keeps the layout it has at its own size and is scaled, around its centre and by its width, to the
        /// size its flight gives it, its content growing and shrinking with it: a morph. For two halves laid out
        /// alike at different sizes. A named part inside it still flies its own way, and a kept or carried object
        /// always resizes, its content following its rect.
        /// </summary>
        Scale,

        /// <summary>
        /// Like <see cref="Scale"/>, but scaled on each axis on its own, so the node fills the rect exactly however its
        /// shape differs from the other half's, its content stretched and squashed on the way: the web's
        /// <c>object-fit: fill</c>. For two halves of different shapes where that reads as one thing growing, such as a
        /// tile opening into a page.
        /// </summary>
        Stretch,
    }
}
