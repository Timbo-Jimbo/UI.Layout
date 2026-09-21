using System.Collections.Generic;
using TimboJimbo.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// One element of a layout tree, modelled on Clay. The node whose parent object has no enabled node is
    /// the root; the engine lays out every node below it and leaves other children alone. A node with no
    /// child nodes is a leaf whose content comes from an <see cref="ILayoutMeasurable"/> on the same object.
    /// Every setting is a serialized field with a dirtying setter, so animation, bindings and styles drive
    /// it. Layout runs before the canvases render (see <see cref="LayoutSystem"/>); the transforms are
    /// written once per pass and never read back, which is what lets <see cref="Offset"/> sit on top.
    /// </summary>
    [AddComponentMenu("Timbo Jimbo/UI/Layout/Layout Node")]
    [RequireComponent(typeof(RectTransform))]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed partial class LayoutNode : UIBehaviour
    {
        [SerializeField] private Sizing _width = Sizing.Fit();
        [SerializeField] private Sizing _height = Sizing.Fit();
        [SerializeField] private LayoutDirection _direction = LayoutDirection.LeftToRight;
        // Left, right, top, bottom. One Vector4 so property bindings see four animatable channels (as Box.Inset).
        [SerializeField] private Vector4 _padding;
        [SerializeField, Min(0f)] private float _gap;
        [SerializeField] private AlignX _alignX = AlignX.Left;
        [SerializeField] private AlignY _alignY = AlignY.Top;
        [SerializeField, Min(0f)] private float _aspectRatio;
        [SerializeField] private Vector2 _offset;
        [SerializeField] private AttachTo _attachTo = AttachTo.None;
        [SerializeField] private RectTransform _attachElement;
        [SerializeField] private AttachPoint _elementPoint = AttachPoint.LeftTop;
        [SerializeField] private AttachPoint _parentPoint = AttachPoint.LeftTop;
        [SerializeField] private Vector2 _floatOffset;
        [SerializeField] private LayoutTransition _transition;

        // Engine scratch, valid from the owning root's Compute to its Commit. Engine space is the root's
        // top-left corner, y down; _pos is relative to the parent node's top-left, _absPos to the root's.
        internal readonly List<LayoutNode> _children = new();
        internal readonly List<LayoutNode> _floatingChildren = new();
        internal LayoutNode _parentNode;
        internal LayoutNode _passRoot;
        internal ILayoutMeasurable _measurable;
        internal bool _isLeaf;
        internal Vector2 _contentSize;
        internal float _minContentWidth;
        internal Vector2 _fit;
        internal Vector2 _minFit;
        internal Vector2 _size;
        internal Vector2 _pos;
        internal Vector2 _absPos;
        internal DrivenRectTransformTracker _tracker;

        // What the last pass decided (_committedRect, the target) and what is on the transform right now
        // (_visualRect, which lags the target while a transition plays), kept here rather than read back.
        internal Rect _committedRect;
        internal Rect _visualRect;
        internal Vector2 _committedBase;
        internal bool _hasCommitted;

        // Transition state: from the rect the node was showing when the target changed, to the target.
        internal Rect _animFrom;
        internal Rect _animTo;
        internal float _animElapsed;
        internal bool _animating;

        private RectTransform _rectTransform;

        /// <summary>Sizing on the horizontal axis.</summary>
        public Sizing Width
        {
            get => _width;
            set
            {
                if (_width == value) return;
                _width = value;
                SetDirty();
            }
        }

        /// <summary>Sizing on the vertical axis.</summary>
        public Sizing Height
        {
            get => _height;
            set
            {
                if (_height == value) return;
                _height = value;
                SetDirty();
            }
        }

        /// <summary>The axis the children are laid out along.</summary>
        public LayoutDirection Direction
        {
            get => _direction;
            set
            {
                if (_direction == value) return;
                _direction = value;
                SetDirty();
            }
        }

        /// <summary>Inset of the children from the node's edges: x left, y right, z top, w bottom.</summary>
        public Vector4 Padding
        {
            get => _padding;
            set
            {
                if (_padding == value) return;
                _padding = value;
                SetDirty();
            }
        }

        /// <summary>Space between consecutive children along the direction.</summary>
        public float Gap
        {
            get => _gap;
            set
            {
                value = Mathf.Max(0f, value);
                if (Mathf.Approximately(_gap, value)) return;
                _gap = value;
                SetDirty();
            }
        }

        /// <summary>Horizontal placement of the children within the free space.</summary>
        public AlignX AlignX
        {
            get => _alignX;
            set
            {
                if (_alignX == value) return;
                _alignX = value;
                SetDirty();
            }
        }

        /// <summary>Vertical placement of the children within the free space.</summary>
        public AlignY AlignY
        {
            get => _alignY;
            set
            {
                if (_alignY == value) return;
                _alignY = value;
                SetDirty();
            }
        }

        /// <summary>Width over height. When above 0 and the height is Fit, the height is derived from the width.</summary>
        public float AspectRatio
        {
            get => _aspectRatio;
            set
            {
                value = Mathf.Max(0f, value);
                if (Mathf.Approximately(_aspectRatio, value)) return;
                _aspectRatio = value;
                SetDirty();
            }
        }

        /// <summary>
        /// A translation applied after layout, in canvas units (y up, like anchoredPosition). It never
        /// affects siblings or the parent's size, and setting it never runs a pass: the node is moved in
        /// place, so this is the field to animate. It has no effect on a root.
        /// </summary>
        public Vector2 Offset
        {
            get => _offset;
            set
            {
                if (_offset == value) return;
                _offset = value;
                if (_hasCommitted && !IsRoot)
                    RectTransform.anchoredPosition = _committedBase + _offset;
            }
        }

        /// <summary>What this node floats against. Anything but None takes it out of the flow and out of the parent's Fit size.</summary>
        public AttachTo AttachTo
        {
            get => _attachTo;
            set
            {
                if (_attachTo == value) return;
                _attachTo = value;
                SetDirty();
            }
        }

        /// <summary>The node to float against when <see cref="AttachTo"/> is Element. Falls back to the parent when it is not in this tree.</summary>
        public RectTransform AttachElement
        {
            get => _attachElement;
            set
            {
                if (_attachElement == value) return;
                _attachElement = value;
                SetDirty();
            }
        }

        /// <summary>The point on this node that is placed at <see cref="ParentPoint"/>.</summary>
        public AttachPoint ElementPoint
        {
            get => _elementPoint;
            set
            {
                if (_elementPoint == value) return;
                _elementPoint = value;
                SetDirty();
            }
        }

        /// <summary>The point on the attach target that <see cref="ElementPoint"/> is placed at.</summary>
        public AttachPoint ParentPoint
        {
            get => _parentPoint;
            set
            {
                if (_parentPoint == value) return;
                _parentPoint = value;
                SetDirty();
            }
        }

        /// <summary>Extra offset for a floating node, in engine space (x right, y down).</summary>
        public Vector2 FloatOffset
        {
            get => _floatOffset;
            set
            {
                if (_floatOffset == value) return;
                _floatOffset = value;
                SetDirty();
            }
        }

        /// <summary>
        /// How the node moves to a new rect: over a duration with an ease, or instantly (the default).
        /// Only what a pass changes animates; a node that keeps its rect stays put, and a node's first
        /// layout is never animated. Changing this never triggers a pass.
        /// </summary>
        public LayoutTransition Transition
        {
            get => _transition;
            set => _transition = value;
        }

        /// <summary>This node's RectTransform.</summary>
        public RectTransform RectTransform => _rectTransform != null ? _rectTransform : (_rectTransform = (RectTransform)transform);

        /// <summary>The enabled node on the parent object, or null when this node is a root.</summary>
        public LayoutNode ParentNode
        {
            get
            {
                var parent = transform.parent;
                return parent != null && parent.TryGetComponent<LayoutNode>(out var node) && node.isActiveAndEnabled ? node : null;
            }
        }

        /// <summary>True when the parent object has no enabled node, so this node owns a tree.</summary>
        public bool IsRoot => ParentNode == null;

        /// <summary>The root of the tree this node belongs to.</summary>
        public LayoutNode Root
        {
            get
            {
                var node = this;
                for (var parent = node.ParentNode; parent != null; parent = parent.ParentNode)
                    node = parent;
                return node;
            }
        }

        /// <summary>The rect the last pass gave this node, in engine space: from the parent node's top-left corner, y down. A root's is (0, 0, width, height). This is the target; see <see cref="VisualRect"/> for where the node is while a transition plays.</summary>
        public Rect LayoutRect => _committedRect;

        /// <summary>The rect currently written to the transform, in engine space. Equal to <see cref="LayoutRect"/> unless a transition is in flight.</summary>
        public Rect VisualRect => _visualRect;

        /// <summary>True while the node is moving toward <see cref="LayoutRect"/>.</summary>
        public bool IsTransitioning => _animating;

        /// <summary>Marks the tree for layout before the next render.</summary>
        public void MarkDirty() => LayoutSystem.MarkDirty(this);

        /// <summary>Where the node should be shown right now: the eased point between the transition's ends, or the visual rect when nothing is in flight.</summary>
        internal Rect CurrentVisual()
        {
            if (!_animating) return _visualRect;
            float t = _transition.Duration > 0f ? Mathf.Clamp01(_animElapsed / _transition.Duration) : 1f;
            float eased = EaseUtility.Evaluate(t, _transition.Ease);
            return new Rect(
                Vector2.LerpUnclamped(_animFrom.position, _animTo.position, eased),
                Vector2.LerpUnclamped(_animFrom.size, _animTo.size, eased));
        }

        // ── Axis helpers for the engine ────────────────────────────────────────

        internal int LayoutAxis => _direction == LayoutDirection.LeftToRight ? 0 : 1;

        internal Sizing SizingOn(int axis) => axis == 0 ? _width : _height;

        internal float PaddingStart(int axis) => axis == 0 ? _padding.x : _padding.z;

        internal float PaddingSum(int axis) => axis == 0 ? _padding.x + _padding.y : _padding.z + _padding.w;

        /// <summary>Where the children sit within the free space on an axis: 0 at the start, 0.5 centred, 1 at the end.</summary>
        internal float AlignFraction(int axis)
        {
            if (axis == 0)
            {
                return _alignX switch
                {
                    AlignX.Center => 0.5f,
                    AlignX.Right => 1f,
                    _ => 0f,
                };
            }
            return _alignY switch
            {
                AlignY.Center => 0.5f,
                AlignY.Bottom => 1f,
                _ => 0f,
            };
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        protected override void OnEnable()
        {
            base.OnEnable();
            RegisterGraphicCallbacks();
            SetDirty();
        }

        // Transition state is scoped to one enabled span: the first commit after an enable snaps, so a node
        // never animates from where it was when it was last disabled. To animate a node in from its
        // laid-out spot, enable it, ForceLayout its root, then change the property.
        protected override void OnDisable()
        {
            UnregisterGraphicCallbacks();
            _tracker.Clear();
            _animating = false;
            _hasCommitted = false;
            // The tree this node leaves re-lays out without it; a disabled node cannot carry the mark itself.
            LayoutSystem.MarkDirty(ParentNode);
            base.OnDisable();
        }

        // A root's size is its input (its anchors, or a UGUI parent), so a change re-runs the pass. A
        // child's size is our own output, or someone else's write the next pass overwrites, so it is ignored.
        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if (IsRoot && !LayoutSystem.IsCommitting(this))
                SetDirty();
        }

        // The tree being left re-lays out without this node; the tree being joined is marked on arrival.
        protected override void OnBeforeTransformParentChanged()
        {
            base.OnBeforeTransformParentChanged();
            LayoutSystem.MarkDirty(ParentNode);
        }

        // Rects are parent-relative, so a node under a new parent starts fresh: its first commit there snaps.
        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            _hasCommitted = false;
            SetDirty();
        }

        private void OnTransformChildrenChanged()
        {
            SetDirty();
        }

        // A Unity Animation clip writes the serialized fields directly and says nothing about which; the
        // property bindings package goes through the setters instead (see LayoutNodeProperties).
        protected override void OnDidApplyAnimationProperties()
        {
            base.OnDidApplyAnimationProperties();
            SetDirty();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            SetDirty();
        }
#endif

        private void SetDirty()
        {
            if (!IsActive()) return;
            LayoutSystem.MarkDirty(this);
        }
    }
}
