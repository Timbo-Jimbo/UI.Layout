using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// Content a node measures, as Clay measures text: a component on the node's own object (a TextBlock, an Img)
    /// that says how big it is. A node with no layout children in its flow (floating ones do not count) fits to it.
    /// Layout then tells it the size its node is given (<see cref="Arrange"/>), which it can lay itself out at while
    /// its node's rect springs there, as a node's children are.
    /// </summary>
    public interface ILayoutMeasurable
    {
        /// <summary>
        /// Its size, in the node's units, at <paramref name="availableWidth"/> (text wraps to it), or at no limit when
        /// that is negative. A pure measure: it changes nothing.
        /// </summary>
        Vector2 Measure(float availableWidth);

        /// <summary>The narrowest it can go: a wrapped text's longest word.</summary>
        float MinWidth { get; }

        /// <summary>
        /// The size layout gives its node, in the node's units, told each time layout places it (every frame, so the same
        /// size again should change nothing); negative when layout no longer sizes it (a root, or a node disabled), and it
        /// is drawn at its rect as it is. A node taken out of layout (Display None), or thrown on as it is hidden, is told
        /// nothing: it keeps the size it had, and is drawn as it was while it goes. While its node animates, its rect is on
        /// its way to this size: a text wraps at it rather than at every width its rect passes through, so it has the
        /// lines it will end with from the start, as the node's children are laid out at the node's new size from the
        /// start. Content that should stretch with its rect (an image) ignores it.
        /// </summary>
        void Arrange(Vector2 size);
    }
}
