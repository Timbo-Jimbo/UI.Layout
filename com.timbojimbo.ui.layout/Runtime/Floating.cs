using System;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// Floats a node out of its parent's flow, as Clay's floating elements: it takes no space there, and is placed so
    /// that its <see cref="Point"/> meets <see cref="TargetPoint"/> on what it is attached to, moved by
    /// <see cref="Offset"/>. It is still sized as any node is (a percentage of its parent's size, or growing to it).
    /// </summary>
    [Serializable]
    public struct Floating
    {
        [Tooltip("What it is placed against: nothing (it is in its parent's flow), its parent, or its layout root.")]
        public FloatingAttach AttachTo;

        [Tooltip("The point on this node that is placed.")]
        public AttachPoint Point;

        [Tooltip("The point on what it is attached to that it is placed at.")]
        public AttachPoint TargetPoint;

        [Tooltip("How far it is moved from there, x right and y up.")]
        public Vector2 Offset;

        /// <summary>Whether it floats at all.</summary>
        public bool IsFloating => AttachTo != FloatingAttach.None;
    }
}
