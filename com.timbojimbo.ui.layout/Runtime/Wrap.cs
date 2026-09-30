using System;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// Whether a node's children run on in one line or wrap into more, as CSS's flex-wrap and SwiftUI's lazy grids: into
    /// a new line wherever the next would run past the end (<see cref="Lines"/>), or into lines of equal cells, a set
    /// number of them (<see cref="Grid"/>) or as many at least a size as fit (<see cref="Adaptive"/>). Lines stack across
    /// the node's direction, <see cref="LayoutNode.ChildGap"/> apart, as the children in a line are. Lines and Adaptive
    /// wrap left to right only, as text does, since widths are known before heights; a Grid wraps either way. Along the
    /// way a node scrolls, nothing wraps.
    /// </summary>
    [Serializable]
    public struct Wrap
    {
        [Tooltip("None: one line. Lines: a child that would run past the end starts the next line. Grid: lines of Count equal cells. Adaptive: lines of as many equal cells at least Min Size long as fit.")]
        public LayoutWrapMode Mode;

        [Tooltip("Grid: how many cells to a line.")]
        [Min(1)] public int Count;

        [Tooltip("Adaptive: the shortest a cell may be along its line; as many as fit, stretched to fill it.")]
        [Min(0f)] public float MinSize;

        /// <summary>One line: what a node has unless told otherwise.</summary>
        public static Wrap None => default;

        /// <summary>A child that would run past the end of the line starts the next, as text wraps words.</summary>
        public static Wrap Lines => new() { Mode = LayoutWrapMode.Lines };

        /// <summary>Lines of <paramref name="count"/> equal cells, as SwiftUI's LazyVGrid with flexible columns.</summary>
        public static Wrap Grid(int count) => new() { Mode = LayoutWrapMode.Grid, Count = count };

        /// <summary>
        /// Lines of as many equal cells at least <paramref name="minSize"/> long as fit, stretched to fill the line, as
        /// SwiftUI's adaptive grid items.
        /// </summary>
        public static Wrap Adaptive(float minSize) => new() { Mode = LayoutWrapMode.Adaptive, MinSize = minSize };
    }
}
