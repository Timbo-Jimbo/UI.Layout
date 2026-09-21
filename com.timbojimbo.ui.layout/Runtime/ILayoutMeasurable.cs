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
    }
}
