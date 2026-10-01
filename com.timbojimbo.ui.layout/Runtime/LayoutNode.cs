using System;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// A node of a layout, as Clay's elements: sized along each axis (fit, grow, fixed or percent), padded, laying its
    /// children out one way with a gap between and aligned (in one line, or wrapped into lines or a grid), or floating
    /// against its parent, its root or another node in its tree. A node whose parent is not a node is a root: it keeps
    /// the rect it is given, and lays its children out inside it, clear of the screen's safe area.
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

        [Tooltip("A root's: the edges it keeps its content clear of the screen's notch, rounded corners and home bar on, adding as much of that as it covers to its padding, as SwiftUI keeps views in the safe area. Its own background still fills its rect.")]
        [SerializeField] private Edges _safeArea = Edges.All;

        [Tooltip("The edges it reaches out to the screen's edge on where it lies against the safe area, its padding growing by as much so what is inside stays clear, as SwiftUI's ignoresSafeArea: a bar's colour under the notch, a list scrolling under the home bar.")]
        [SerializeField] private Edges _ignoresSafeArea;

        [Tooltip("Space between one child and the next, and between one line and the next when they wrap.")]
        [SerializeField] private float _childGap;

        [Tooltip("Which way it lays its children out.")]
        [SerializeField] private LayoutDirection _direction;

        [Tooltip("Whether its children wrap into lines: as they fill one (Lines), into lines of Count equal cells (Grid), or of as many cells at least Min Size long as fit (Adaptive). Lines stack across its direction.")]
        [SerializeField] private Wrap _wrap;

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

        [Tooltip("Its name for matching: inside LayoutSystem.Animate, a node shown with the same name (and id) as one hidden takes over from where that one is drawn; shown or hidden with one that stays shown, it grows out of that one or shrinks back into it. Empty: not matched.")]
        [SerializeField] private string _matchName;

        // Code only, and never saved: an id is usually an object of the game's (an author, an item).
        [NonSerialized] private object _matchId;

        [Tooltip("Taking over from a node by name, or growing out of one that stays shown: how it fills the rect it moves through, as CSS's object-fit. Every mode but Resize keeps it at its own size, as a picture of itself, scaled onto the rect, centred: Match Width (the default, as the web's) evenly to its width, Fill to it exactly, Contain and Cover evenly to fit inside it or to cover it. Resize changes the rect's size instead. The node taking over decides for both.")]
        [SerializeField] private MatchFit _matchFit = MatchFit.MatchWidth;

        [Tooltip("Taking over from a node by name, or growing out of one that stays shown: whether it, and the node it takes over from, are cut to the rect they move through while they fly. With Cover it crops; with Resize it uncovers what is inside, laid out at its final size.")]
        [SerializeField] private bool _matchClip;

        [Tooltip("Which way it scrolls its children: it clips them, lets them run past its edge that way, and can be dragged, flicked and wheeled, as a UIScrollView.")]
        [SerializeField] private ScrollAxis _scroll;

        [Tooltip("Which end it keeps to as what it scrolls grows. End: it starts at its end and, while it is there, stays there as its content grows, as a chat does.")]
        [SerializeField] private ScrollAnchor _scrollAnchor;

        [Tooltip("Where it comes to rest once a drag or a flick lets go: anywhere, on a whole page of what it shows, or with one of its children lined up where its content starts. A flick moves it on one page or child at most.")]
        [SerializeField] private ScrollSnap _scrollSnap;

        [Tooltip("Whether it shows a thin bar along each edge it scrolls by while it scrolls, fading out once it stops, as iOS's scroll indicators.")]
        [SerializeField] private bool _showsScrollIndicators = true;

        [Tooltip("The colour of its scroll indicators.")]
        [SerializeField] private Color _scrollIndicatorColor = new(0.5f, 0.5f, 0.5f, 0.6f);

        /// <summary>How wide it is.</summary>
        public Sizing Width { get => _width; set { _width = value; Changed(); } }

        /// <summary>How tall it is.</summary>
        public Sizing Height { get => _height; set { _height = value; Changed(); } }

        /// <summary>Space between its edges and its children or content.</summary>
        public Insets Padding { get => _padding; set { _padding = value; Changed(); } }

        /// <summary>
        /// A root's: the edges it keeps its content clear of the screen's unsafe area on (a notch, rounded corners, the
        /// home bar: outside <see cref="Screen.safeArea"/>), as SwiftUI keeps views in the safe area; all of them by
        /// default. It adds as much of that as it covers to its <see cref="Padding"/>, so a root away from the notch adds
        /// nothing, and its own background (a Box on it) still fills its rect. Read every frame, so a rotation or the
        /// Device Simulator's device is taken up at once. Only an outermost root keeps it (one inside another tree goes
        /// by where that tree puts it), and not on a world space canvas. A node that should reach under it sets
        /// <see cref="IgnoresSafeArea"/>. Unused on any other node.
        /// </summary>
        public Edges SafeArea { get => _safeArea; set { _safeArea = value; Changed(); } }

        /// <summary>
        /// The edges it reaches out past the safe area on, as SwiftUI's ignoresSafeArea: on each, where it lies against
        /// the edge of its root's safe area (or past it), it reaches out to the root's edge, and its padding grows by as
        /// much, so what is inside it stays where it was. A bar's colour goes under the notch while its title stays below
        /// it; a list goes under the home bar, its rows scrolling on under it and coming to rest clear of it, with its
        /// indicators, ScrollTo and snapping kept clear of it too, as a UIScrollView's insets are. Laid out further in
        /// (padding or centring between it and that edge), it does not reach. A floating child of it is placed against
        /// where it was laid out. A root does not reach: it keeps its rect.
        /// </summary>
        public Edges IgnoresSafeArea { get => _ignoresSafeArea; set { _ignoresSafeArea = value; Changed(); } }

        /// <summary>Space between one child and the next, and between one line and the next when they wrap (<see cref="Wrap"/>).</summary>
        public float ChildGap { get => _childGap; set { _childGap = value; Changed(); } }

        /// <summary>Which way it lays its children out.</summary>
        public LayoutDirection Direction { get => _direction; set { _direction = value; Changed(); } }

        /// <summary>
        /// Whether its children wrap into lines, as CSS's flex-wrap and SwiftUI's lazy grids, the lines stacking across
        /// its <see cref="Direction"/>, <see cref="ChildGap"/> apart. <see cref="Wrap.Lines"/>: a child that would run past
        /// the end of a line starts the next, as text wraps; each line is then a row, its grow children taking what it has
        /// left and <see cref="ChildAlignX"/> placing it, and fitted, the node is as wide as all of them on one line. A
        /// <see cref="Wrap.Grid"/> of lines of Count equal cells, or <see cref="Wrap.Adaptive"/>, as many at least MinSize
        /// long as fit: each child is sized in its cell by its own sizing (grow fills it). A line is as tall as its
        /// tallest child, which ChildAlignY places it in; the lines as a block are placed by it too. Lines and Adaptive
        /// wrap left to right only; nothing wraps along the way it scrolls. Changed inside
        /// <see cref="LayoutSystem.Animate"/> (a new count, a new width), the children fly to their new places.
        /// </summary>
        public Wrap Wrap { get => _wrap; set { _wrap = value; Changed(); } }

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
        /// root only fades. Shown or hidden with a node that stays shown under its <see cref="MatchName"/>, it grows out
        /// of that node's rect and shrinks back into it instead of sliding and shrinking, fading if it fades.
        /// </summary>
        public DisplayEffect DisplayEffect { get => _displayEffect; set { _displayEffect = value; Changed(); } }

        /// <summary>
        /// Its name for matching. Inside <see cref="LayoutSystem.Animate"/>, when a node with this name and matching id
        /// (<see cref="MatchId"/>) starts being shown and another that had them is hidden, the shown one takes over from
        /// where the other is drawn, flying above everything until it lands (a cell zooming into the page it opens).
        /// When the other stays shown, it does not move: the one shown or hidden grows out of it or shrinks back into it,
        /// the other's rect standing in for its <see cref="DisplayEffect"/>'s edge and shrink (a dropdown's list out of
        /// its button). Empty: not matched. Nodes on another root canvas never match; roots never match.
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
        /// How it fills the rect it moves through when it takes over from a node by name (<see cref="MatchName"/>), from
        /// that node's rect to its own, or grows out of one that stays shown, as CSS's <c>object-fit</c> fills a view
        /// transition's group. Every mode but Resize keeps it at its own laid-out size, as a picture of itself, and scales
        /// it onto the rect, centred: <see cref="MatchFit.MatchWidth"/>, the default as on the web, evenly to its width;
        /// <see cref="MatchFit.Fill"/> to it exactly; <see cref="MatchFit.Contain"/> and <see cref="MatchFit.Cover"/>
        /// evenly to fit inside it or to cover it. <see cref="MatchFit.Resize"/> changes the rect's size instead, what is
        /// inside it laid out at its own size and pinned at its top left. The node it takes over from is drawn the same
        /// way at its own size: the node taking over decides for both. Caught on its way, it is held as it is drawn, the
        /// picture and all, until it is let go of and its size has come to rest.
        /// </summary>
        public MatchFit MatchFit { get => _matchFit; set { _matchFit = value; Changed(); } }

        /// <summary>
        /// Whether, as it takes over from a node by name or grows out of one that stays shown, it and the node it takes over
        /// from are cut to the rect they move through while they fly (rectangular, as a RectMask2D cuts; rounded corners are
        /// a masking Box's of its own). With <see cref="MatchFit.Cover"/> it crops the picture; with
        /// <see cref="MatchFit.Resize"/> it uncovers what is inside, laid out at its final size. The node taking over
        /// decides for both. Caught on its way, it lands, and is cut no more. A node that clips by itself already (a scroll
        /// container, or one with a RectMask2D of its own) clips as it does.
        /// </summary>
        public bool MatchClip { get => _matchClip; set { _matchClip = value; Changed(); } }

        /// <summary>
        /// Which way it scrolls its children: it clips them, lets them run past its edge that way rather than
        /// squeezing them, and can be dragged (rubber-banding past its ends), flicked (gliding to a stop) and wheeled,
        /// as a UIScrollView. Nested in another that scrolls, or inside a node with an <see cref="ILayoutDraggable"/> (a
        /// sheet), it shares its drags as nested UIScrollViews do: a drag goes to those that scroll the way it sets off,
        /// innermost first, and what one cannot take (at its end) goes on to what is outside it.
        /// </summary>
        public ScrollAxis Scroll { get => _scroll; set { _scroll = value; Changed(); } }

        /// <summary>
        /// Which end it keeps to as what it scrolls grows, as SwiftUI's defaultScrollAnchor. Start (the default): its
        /// offset stays as it is as its range grows. End: it starts at its end (at once, the first time it is laid out)
        /// and, while it is there (at rest within half a unit of it, or springing to it), stays there as its range grows,
        /// on each axis it scrolls, as a chat's messages do: on its spring for the change inside
        /// <see cref="LayoutSystem.Animate"/>, and otherwise at once (or, springing there already, on that spring).
        /// Scrolled away from its end it stays where it is, and one held by a press or carried by a flick is left to it.
        /// A <see cref="ScrollTo"/> or <see cref="ScrollOffset"/> in the same change wins, as does a
        /// <see cref="ScrollIntoView"/> that moves it. Only play mode scrolls.
        /// </summary>
        public ScrollAnchor ScrollAnchor { get => _scrollAnchor; set { _scrollAnchor = value; Changed(); } }

        /// <summary>
        /// Where it comes to rest once a drag or a flick lets go of it, as UIKit's paging and SwiftUI's scroll target
        /// behaviours: anywhere (None), on a whole page of what it shows (Pages), or with one of its children in its
        /// flow starting where its content starts, inside its padding (Children). Let go, it settles on the page or
        /// child nearest where it is drawn, or, flicked, on the next one the way it was flicked and no further, on a
        /// quick spring that carries its speed without swinging past; a glide handed on to it from a list inside it
        /// moves it on one too, and a wheel notch one. Either end is a place to rest as well. <see cref="ScrollTo"/>,
        /// <see cref="ScrollIntoView"/> and <see cref="ScrollOffset"/> go exactly where they are told.
        /// </summary>
        public ScrollSnap ScrollSnap { get => _scrollSnap; set { _scrollSnap = value; Changed(); } }

        /// <summary>
        /// Whether it shows its scroll indicators, as iOS does by default (true): a thin rounded bar along its right edge
        /// for scrolling up and down, and one along its bottom edge for scrolling sideways, as long against the edge as
        /// what it shows is against what it scrolls, as far along as it is scrolled, and shortening as it rubber-bands.
        /// Each shows while its axis scrolls or a press holds it, and fades out once it has been still for half a second.
        /// They take no pointer, and only play mode scrolls.
        /// </summary>
        public bool ShowsScrollIndicators { get => _showsScrollIndicators; set { _showsScrollIndicators = value; Changed(); } }

        /// <summary>The colour of its scroll indicators: by default a grey that reads on light and dark alike.</summary>
        public Color ScrollIndicatorColor { get => _scrollIndicatorColor; set { _scrollIndicatorColor = value; Changed(); } }

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
        /// scrolling, as SwiftUI's scrollTo. What is lined up is the descendant with the space around it, as CSS's
        /// scroll-margin, worked out from the layout: on a side with a neighbour in its parent's flow, the gap between
        /// them (so no sliver of the neighbour shows); on a side where it is first or last in that flow, or across it, its
        /// parent's padding, and on up through each parent it is at the edge of, to this node's own padding, the start and
        /// end of its content. So the first child scrolls to the very start and the last to the very end. A floating
        /// descendant has no space around it, and a floating parent adds none.
        /// </summary>
        public void ScrollTo(LayoutNode descendant, float anchor = 0f) => LayoutSystem.ScrollTo(this, descendant, anchor);

        /// <summary>
        /// Scrolls only as far as brings <paramref name="descendant"/> (anything inside it), with the space around it
        /// that <see cref="ScrollTo"/> lines up, wholly into view, to whichever end of what it shows is nearer; not at all
        /// when it is in view already. For one bigger than what it shows, it moves the least that fills the view with it;
        /// one that fills the view already is left where it is. As UIKit's scrollRectToVisible and CSS's scrollIntoView
        /// with 'nearest'. At once, or inside <see cref="LayoutSystem.Animate"/> on its spring, as ScrollTo.
        /// </summary>
        public void ScrollIntoView(LayoutNode descendant) => LayoutSystem.ScrollIntoView(this, descendant);

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
