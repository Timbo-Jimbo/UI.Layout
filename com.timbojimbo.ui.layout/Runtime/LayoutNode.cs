using System;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// A node of a layout, as Clay's elements: sized along each axis (fit, grow, fixed or percent), padded, laying its
    /// children out one way with a gap between and aligned, or floating against its parent or its root. A node whose
    /// parent is not a node is a root: it keeps the rect it is given, and lays its children out inside it.
    /// The layout system owns every other node's RectTransform (anchors, pivot, position, size and scale): change
    /// where a node goes through its layout, and by <see cref="Offset"/>. A change made in
    /// <see cref="LayoutSystem.Animate"/> moves the nodes it gives somewhere new on springs, from where they are and at
    /// the velocity they have; any other change puts them there at once.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Timbo Jimbo/UI/Layout Node")]
    public sealed class LayoutNode : MonoBehaviour
    {
        [Tooltip("How wide it is.")]
        [SerializeField] private Sizing _width = Sizing.Fit();

        [Tooltip("How tall it is.")]
        [SerializeField] private Sizing _height = Sizing.Fit();

        [Tooltip("Space between its edges and its children or content.")]
        [SerializeField] private Insets _padding;

        [Tooltip("Space between one child and the next.")]
        [SerializeField] private float _childGap;

        [Tooltip("Which way it lays its children out.")]
        [SerializeField] private LayoutDirection _direction;

        [Tooltip("Where its children sit across its width.")]
        [SerializeField] private AlignX _childAlignX;

        [Tooltip("Where its children sit down its height.")]
        [SerializeField] private AlignY _childAlignY;

        [Tooltip("Width over height: when above 0, its height follows its width.")]
        [SerializeField, Min(0f)] private float _aspectRatio;

        [Tooltip("Floats it out of its parent's flow, placed against its parent or its root.")]
        [SerializeField] private Floating _floating;

        [Tooltip("Whether it is shown. Hidden keeps its space; None leaves layout, its siblings closing up.")]
        [SerializeField] private DisplayMode _display;

        [Tooltip("How far it is drawn from where layout puts it, x right and y up, taking no space: for a drag, say.")]
        [SerializeField] private Vector2 _offset;

        [Tooltip("How it moves when a change made in LayoutSystem.Animate gives it somewhere new to be.")]
        [SerializeField] private LayoutAnimation _animation = LayoutAnimation.Default;

        [Tooltip("Which way it scrolls its children: it clips them, lets them run past its edge that way, and can be dragged, flicked and wheeled, as a UIScrollView.")]
        [SerializeField] private ScrollAxis _scroll;

        /// <summary>How wide it is.</summary>
        public Sizing Width { get => _width; set { _width = value; Changed(); } }

        /// <summary>How tall it is.</summary>
        public Sizing Height { get => _height; set { _height = value; Changed(); } }

        /// <summary>Space between its edges and its children or content.</summary>
        public Insets Padding { get => _padding; set { _padding = value; Changed(); } }

        /// <summary>Space between one child and the next.</summary>
        public float ChildGap { get => _childGap; set { _childGap = value; Changed(); } }

        /// <summary>Which way it lays its children out.</summary>
        public LayoutDirection Direction { get => _direction; set { _direction = value; Changed(); } }

        /// <summary>Where its children sit across its width.</summary>
        public AlignX ChildAlignX { get => _childAlignX; set { _childAlignX = value; Changed(); } }

        /// <summary>Where its children sit down its height.</summary>
        public AlignY ChildAlignY { get => _childAlignY; set { _childAlignY = value; Changed(); } }

        /// <summary>Width over height: when above 0, its height follows its width.</summary>
        public float AspectRatio { get => _aspectRatio; set { _aspectRatio = Mathf.Max(0f, value); Changed(); } }

        /// <summary>Floats it out of its parent's flow, placed against its parent or its root.</summary>
        public Floating Floating { get => _floating; set { _floating = value; Changed(); } }

        /// <summary>Whether it is shown. Hidden keeps its space; None leaves layout, its siblings closing up.</summary>
        public DisplayMode Display { get => _display; set { _display = value; Changed(); } }

        /// <summary>How far it is drawn from where layout puts it, x right and y up, taking no space: for a drag, say.</summary>
        public Vector2 Offset { get => _offset; set { _offset = value; Changed(); } }

        /// <summary>How it moves when a change made in <see cref="LayoutSystem.Animate"/> gives it somewhere new to be.</summary>
        public LayoutAnimation Animation { get => _animation; set { _animation = value; Changed(); } }

        /// <summary>
        /// Which way it scrolls its children: it clips them, lets them run past its edge that way rather than
        /// squeezing them, and can be dragged (rubber-banding past its ends), flicked (gliding to a stop) and wheeled,
        /// as a UIScrollView.
        /// </summary>
        public ScrollAxis Scroll { get => _scroll; set { _scroll = value; Changed(); } }

        /// <summary>
        /// How far its children are scrolled, x right and y down, from 0 (the start) to <see cref="ScrollRange"/>, as
        /// UIScrollView's contentOffset. Set, it scrolls there, kept within range: at once, or inside
        /// <see cref="LayoutSystem.Animate"/> on its spring, from where it is and at the speed it is scrolling.
        /// </summary>
        public Vector2 ScrollOffset { get => LayoutSystem.ScrollOffsetOf(this); set => LayoutSystem.SetScrollOffset(this, value); }

        /// <summary>How far it can be scrolled each way: how far its content runs past its edge (0 when it fits).</summary>
        public Vector2 ScrollRange => LayoutSystem.ScrollRangeOf(this);

        /// <summary>Whether it is being dragged, gliding, or springing to where it scrolls.</summary>
        public bool IsScrolling => LayoutSystem.IsScrolling(this);

        /// <summary>Raised with the scroll offset whenever it scrolls.</summary>
        public event Action<Vector2> Scrolled;

        /// <summary>
        /// Scrolls so that <paramref name="descendant"/> (anything inside it) sits <paramref name="anchor"/> of the way
        /// down (or across) what it shows: 0 at the start, 0.5 in the middle, 1 at the end, kept within range. At once,
        /// or inside <see cref="LayoutSystem.Animate"/> on its spring, from where it is and at the speed it is
        /// scrolling, as SwiftUI's scrollTo.
        /// </summary>
        public void ScrollTo(LayoutNode descendant, float anchor = 0f) => LayoutSystem.ScrollTo(this, descendant, anchor);

        internal void RaiseScrolled(Vector2 offset) => Scrolled?.Invoke(offset);

        /// <summary>Whether it is a root: its parent is not a node, so it keeps the rect it is given.</summary>
        public bool IsRoot => LayoutSystem.IsRoot(this);

        /// <summary>How fast it is moving on screen, in world units a second (screen pixels, on a Screen Space Overlay canvas).</summary>
        public Vector3 Velocity => LayoutSystem.VelocityOf(this);

        /// <summary>
        /// Sets it moving at <paramref name="velocity"/>, in world units a second (screen pixels, on a Screen Space
        /// Overlay canvas, what pointer deltas are in), as a drag lets go of it: it springs to where layout puts it,
        /// carrying that velocity, bowing out the way it was thrown before curving round. A change animated after,
        /// such as putting its <see cref="Offset"/> back, sets off from it.
        /// </summary>
        public void Fling(Vector3 velocity) => LayoutSystem.Fling(this, velocity);

        /// <summary>
        /// Stops it where it is drawn, as a drag taking hold of it: its <see cref="Offset"/> is changed so that is where
        /// it goes, and it stops moving there (its size, opacity and scale carry on where they were going). A drag then
        /// moves its Offset, and letting go flings it. Returns whether it was moving.
        /// </summary>
        public bool Catch() => LayoutSystem.Catch(this);

        // Tells the system something about it changed; outside play mode, so the editor draws it again.
        private void Changed() => LayoutSystem.Changed(this);

        private void OnEnable() => LayoutSystem.Register(this);

        private void OnDisable() => LayoutSystem.Unregister(this);

        private void OnValidate() => Changed();

        private void OnTransformParentChanged() => Changed();

        private void OnTransformChildrenChanged() => Changed();
    }
}
