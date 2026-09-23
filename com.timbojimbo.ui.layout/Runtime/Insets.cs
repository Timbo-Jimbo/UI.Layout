using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// Builds the Vector4 insets <see cref="LayoutNode.Padding"/> takes, in its left, right, top, bottom order
    /// (the order of RectOffset and Box.Inset), so a call site reads as what it means.
    /// </summary>
    public static class Insets
    {
        /// <summary>The same inset on every side.</summary>
        public static Vector4 All(float inset) => new(inset, inset, inset, inset);

        /// <summary><paramref name="horizontal"/> on the left and right, <paramref name="vertical"/> on the top and bottom.</summary>
        public static Vector4 Symmetric(float horizontal, float vertical) => new(horizontal, horizontal, vertical, vertical);

        /// <summary>Each side on its own, named so the order cannot be mistaken.</summary>
        public static Vector4 Of(float left = 0f, float right = 0f, float top = 0f, float bottom = 0f) => new(left, right, top, bottom);
    }

    /// <summary>Draws a Vector4 of insets with its sides labelled left, right, top and bottom.</summary>
    internal sealed class InsetsFieldAttribute : PropertyAttribute { }

    /// <summary>Draws a node's transition with the override flag beside it: Inherit, or Custom and its timing.</summary>
    internal sealed class NodeTransitionAttribute : PropertyAttribute { }
}
