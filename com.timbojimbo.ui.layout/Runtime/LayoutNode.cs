using System;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// A node of a layout, as Clay's elements: sized along each axis (fit, grow, fixed or percent), padded, laying its
    /// children out one way with a gap between and aligned, or floating against its parent, its root or another node
    /// in its tree. A node whose parent is not a node is a root: it keeps the rect it is given, and lays its children
    /// out inside it.
    /// The layout system owns every other node's RectTransform (anchors, pivot, position, size and scale): change
    /// where a node goes through its layout, and how it is drawn apart from that, taking no space, by
    /// <see cref="Offset"/>, <see cref="Scale"/> and <see cref="Opacity"/>. A change made in
    /// <see cref="LayoutSystem.Animate"/> moves the nodes it gives somewhere new on springs, from where they are and at
    /// the velocity they have, and plays the <see cref="DisplayEffect"/> of those it shows or hides; any other change
    /// puts them there at once.
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

        [Tooltip("Floats it out of its parent's flow, placed against its parent, its root or another node in its tree.")]
        [SerializeField] private Floating _floating;

        [Tooltip("Whether it is shown. Hidden keeps its space; None leaves layout, its siblings closing up.")]
        [SerializeField] private DisplayMode _display;

        [Tooltip("How far it is drawn from where layout puts it, x right and y up, taking no space: for a drag, say.")]
        [SerializeField] private Vector2 _offset;

        [Tooltip("How much bigger it is drawn around its centre (1: as laid out), taking no space: for a press or a drag. A root is not scaled.")]
        [SerializeField, Min(0f)] private float _scale = 1f;

        [Tooltip("How opaque it is drawn, 0 to 1, multiplying its fade: visual only. At 0 it takes no pointer, nor does anything inside it.")]
        [SerializeField, Range(0f, 1f)] private float _opacity = 1f;

        [Tooltip("How it moves when a change made in LayoutSystem.Animate gives it somewhere new to be.")]
        [SerializeField] private LayoutAnimation _animation = LayoutAnimation.Default;

        [Tooltip("How it appears and disappears when its Display changes inside LayoutSystem.Animate, on its Animation, the same both ways. Only the topmost node that changes plays it; what is inside rides along.")]
        [SerializeField] private DisplayEffect _displayEffect = DisplayEffect.Default;

        [Tooltip("Its name for matching: inside LayoutSystem.Animate, a node shown with the same name (and id) as one hidden takes over from where that one is drawn. Empty: not matched.")]
        [SerializeField] private string _matchName;

        // Code only, and never saved: an id is usually an object of the game's (an author, an item).
        [NonSerialized] private object _matchId;

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

        /// <summary>
        /// Floats it out of its parent's flow, placed against its parent, its root or another node in its tree
        /// (<see cref="Floating.Element"/>). Changed inside <see cref="LayoutSystem.Animate"/> (a new element, say), it
        /// springs there from where it is drawn.
        /// </summary>
        public Floating Floating { get => _floating; set { _floating = value; Changed(); } }

        /// <summary>
        /// Whether it is shown. Hidden keeps its space; None leaves layout, its siblings closing up at once. Changed
        /// inside <see cref="LayoutSystem.Animate"/>, it plays its <see cref="DisplayEffect"/> on its
        /// <see cref="Animation"/>, in from its away pose or out to it, and a node it hides is drawn, taking no
        /// pointer, until it has gone (the change finishes then). What is inside it rides along. Changed outside
        /// Animate, it shows or goes at once.
        /// </summary>
        public DisplayMode Display { get => _display; set { _display = value; Changed(); } }

        /// <summary>How far it is drawn from where layout puts it, x right and y up, taking no space: for a drag, say.</summary>
        public Vector2 Offset { get => _offset; set { _offset = value; Changed(); } }

        /// <summary>
        /// How much bigger it is drawn around its centre (1: as laid out), taking no space: for a press or a drag.
        /// Changed in <see cref="LayoutSystem.Animate"/> it springs there on its <see cref="Animation"/>; otherwise at
        /// once (a drag setting it should catch the node first, <see cref="Catch"/>). Multiplies its
        /// <see cref="DisplayEffect"/>'s shrink. Nodes attached to it follow where it is laid out, not its scale. A
        /// root is not scaled.
        /// </summary>
        public float Scale { get => _scale; set { _scale = Mathf.Max(0f, value); Changed(); } }

        /// <summary>
        /// How opaque it is drawn (0 to 1), multiplying its fade: visual only. Changed in
        /// <see cref="LayoutSystem.Animate"/> it moves there on its <see cref="Animation"/>'s duration without
        /// bouncing; otherwise at once. At 0 it takes no pointer, nor does anything inside it.
        /// </summary>
        public float Opacity { get => _opacity; set { _opacity = Mathf.Clamp01(value); Changed(); } }

        /// <summary>How it moves when a change made in <see cref="LayoutSystem.Animate"/> gives it somewhere new to be.</summary>
        public LayoutAnimation Animation { get => _animation; set { _animation = value; Changed(); } }

        /// <summary>
        /// How it appears and disappears when its <see cref="Display"/> changes inside
        /// <see cref="LayoutSystem.Animate"/>, on its own <see cref="Animation"/>, the same both ways: fading,
        /// shrinking, and sliding just past an edge of its parent's rect as drawn (the screen, for one floating against
        /// its root). A slide hides it only where something clips at that edge: a node between it and the clip (a
        /// padded wrapper) keeps it in view. Only the topmost node that changes plays it; what is inside rides along. A
        /// root only fades.
        /// </summary>
        public DisplayEffect DisplayEffect { get => _displayEffect; set { _displayEffect = value; Changed(); } }

        /// <summary>
        /// Its name for matching. Inside <see cref="LayoutSystem.Animate"/>, when a node with this name and matching id
        /// (<see cref="MatchId"/>) starts being shown and another that had them is hidden (or stays shown), the shown
        /// one takes over from where the other is drawn, flying above everything until it lands. Empty: not matched.
        /// Nodes on another root canvas never match; roots never match.
        /// </summary>
        public string MatchName { get => _matchName ?? string.Empty; set { _matchName = value; Changed(); } }

        /// <summary>
        /// Which of several nodes with the same <see cref="MatchName"/> this is (an author id), set in code; null to
        /// use the nearest ancestor's id. Compared with Equals(object) and hashed with GetHashCode: strings, numbers,
        /// Guids, enums and records by value; a class by reference unless it overrides both (IEquatable&lt;T&gt; alone
        /// is not used). Never saved.
        /// </summary>
        public object MatchId { get => _matchId; set { _matchId = value; Changed(); } }

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

        /// <summary>
        /// Raised with how far it is shown, as drawn (0 away, 1 shown, past either while its spring overshoots or turns
        /// round), whenever that changes: once a frame in play mode, after the frame is laid out and before it is
        /// drawn, for an effect of your own (a <see cref="DisplayEffect"/> with nothing on leaves it drawn as it is
        /// until its shown value comes to rest). First raised the frame it is first placed; a handler added later hears
        /// nothing until it next changes. It is not raised for the nodes inside one that plays its effect, which ride
        /// along. A layout change made in a handler is laid out next frame.
        /// </summary>
        public event Action<float> ShownChanged;

        internal void RaiseShownChanged(float shown) => ShownChanged?.Invoke(shown);

        /// <summary>Whether it is a root: its parent is not a node, so it keeps the rect it is given.</summary>
        public bool IsRoot => LayoutSystem.IsRoot(this);

        /// <summary>
        /// How fast its centre is moving within its parent, in world units a second (screen pixels, on a Screen Space
        /// Overlay canvas), its <see cref="DisplayEffect"/>'s slide included; zero when it is not moving. It is its own
        /// motion: a node riding a moving parent is at rest here, however its parent moves on screen.
        /// </summary>
        public Vector3 Velocity => LayoutSystem.VelocityOf(this);

        /// <summary>
        /// How fast its width and height are growing (negative when shrinking), in world units a second (screen pixels,
        /// on a Screen Space Overlay canvas); zero when its size is not moving.
        /// </summary>
        public Vector2 SizeVelocity => LayoutSystem.SizeVelocityOf(this);

        /// <summary>
        /// Sets it moving at <paramref name="velocity"/> within its parent, in world units a second (screen pixels, on
        /// a Screen Space Overlay canvas, what pointer deltas are in), as a drag lets go of it: it springs to where
        /// layout puts it, carrying that velocity, bowing out the way it was thrown before curving round.
        /// <paramref name="sizeVelocity"/> is how fast its width and height are growing, in the same units, given to
        /// its size as <paramref name="velocity"/> is to its centre (left at zero, its size is left as it is): a bottom
        /// sheet dragged by its height with its bottom edge pinned, its top edge moving up at v, is flung with its
        /// centre moving up at v / 2 and its size growing at (0, v). A change animated after, such as putting its
        /// <see cref="Offset"/> back or setting its height to a detent, sets off from both. Whatever change it was
        /// moving for is let go of on its way (it does not complete). Hidden (<see cref="Display"/>) inside an Animate
        /// while it is still moving from the fling, it is thrown away: it carries on the way it was flung, to a stop,
        /// while its <see cref="DisplayEffect"/> plays, rather than turning back to its place.
        /// </summary>
        public void Fling(Vector3 velocity, Vector2 sizeVelocity = default) => LayoutSystem.Fling(this, velocity, sizeVelocity);

        /// <summary>
        /// Stops it where it is drawn, as a drag taking hold of it: its <see cref="Offset"/> is changed so that is
        /// where it goes, and it stops moving there. Its size, opacity, scale and how far it is shown (its
        /// <see cref="DisplayEffect"/> part way) stop too, as does everything inside it, and each stays as it is until
        /// an Animate changes it (letting go of a drag, putting its Offset back, sets them off again from there) or it
        /// is given something new for that one alone: layout a new size, or a new <see cref="Scale"/>, so a drag
        /// setting its Scale leaves its opacity where it stopped. A drag then moves its Offset, and letting go flings
        /// it. A change it was moving for is let go of on its way, so it does not complete
        /// (<see cref="LayoutTransition.Completed"/>), and it takes the pointer again if that change had taken it away.
        /// A matched node flying to take over from another (<see cref="MatchName"/>), or with one inside it, lands at
        /// once first. Returns whether it was moving; false, and nothing is stopped, for a node on its way out (hidden
        /// inside Animate) or one following the node that took over from it.
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
