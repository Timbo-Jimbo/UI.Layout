using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// Content a node measures, as Clay measures text: a component on the node's own object (a TextBlock, an Img)
    /// that says how big it is. A node with no layout children in its flow (floating ones do not count) fits to it.
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
    }
}
