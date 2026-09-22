using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// The content of a leaf node. Implement it on a component that sits on the same object as the
    /// <see cref="LayoutNode"/>. The engine asks for the unconstrained size first, then for the size at
    /// the width it settled on, so content that wraps (text) answers with its height for that width.
    /// </summary>
    public interface ILayoutMeasurable
    {
        /// <summary>The content size for the available width, in canvas units. A negative width is unconstrained.</summary>
        Vector2 Measure(float availableWidth);

        /// <summary>The narrowest the content can be laid out at (the longest word for text). 0 if it can vanish.</summary>
        float MinWidth { get; }

        /// <summary>
        /// True when the content can be drawn at sizes between two layouts, so a transition may animate the node's
        /// size (an image scales). False, the default, when it cannot (text re-wraps or clips), and a transition
        /// gives the node its new size at once and animates only its position.
        /// </summary>
        bool SizeIsAnimatable => false;
    }
}
