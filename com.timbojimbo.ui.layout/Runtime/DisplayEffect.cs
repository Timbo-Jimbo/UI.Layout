using System;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// How a node appears and disappears when its <see cref="LayoutNode.Display"/> changes inside
    /// <see cref="LayoutSystem.Animate"/>, as SwiftUI's transition: fading, shrinking, and sliding past an edge of its
    /// parent, together. It plays on the node's own <see cref="LayoutNode.Animation"/>, the same both ways, and only on
    /// the topmost node that changes: what is inside it rides along. Each part is off at its default, so
    /// <c>default(DisplayEffect)</c> plays nothing and <c>new DisplayEffect { Edge = DisplayEdge.Right }</c> is a plain
    /// slide.
    /// </summary>
    [Serializable]
    public struct DisplayEffect
    {
        /// <summary>Whether it fades to nothing while it is away.</summary>
        [Tooltip("Fades to nothing while away.")]
        public bool Fade;

        /// <summary>
        /// How much smaller it is drawn while it is away, around its centre: 0 not at all, 1 to nothing. It multiplies
        /// the node's <see cref="LayoutNode.Scale"/>.
        /// </summary>
        [Tooltip("How much smaller it is while away: 0 not at all, 1 to nothing.")]
        [Range(0f, 1f)] public float Shrink;

        /// <summary>
        /// The side it slides past while it is away, just clearing its parent's rect as drawn (the root's, for a node
        /// floating against its root). It goes out of view only where something clips at that edge, a scroll
        /// container or the screen: a node between it and the clip, such as a padded wrapper, keeps it in view.
        /// </summary>
        [Tooltip("The side it slides past, just clearing its parent's rect as drawn; it hides only where something clips there (None: it does not slide).")]
        public DisplayEdge Edge;

        /// <summary>Fade only, what a node has unless told otherwise.</summary>
        public static DisplayEffect Default => new() { Fade = true };
    }
}
