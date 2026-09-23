using System.Collections.Generic;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// The layout algorithm, a port of Clay's sizing model, as a pure computation over a
    /// <see cref="LayoutNode"/> tree. <see cref="Compute"/> reads node settings and leaf measures and writes
    /// only the nodes' engine-space scratch (the UGUI compatibility content may touch its own transform to
    /// answer); <see cref="Commit"/> is the single step that writes RectTransforms. Engine space is the root's
    /// top-left corner, y down, in canvas units.
    /// </summary>
    internal static class LayoutEngine
    {
        private const float Epsilon = 1e-4f;

        // The tree of the pass in progress, in depth-first order (a parent before its subtree).
        private static readonly List<LayoutNode> s_all = new();
        private static readonly List<LayoutNode> s_resizable = new();
        private static readonly List<ILayoutMeasurable> s_measurables = new();

        /// <summary>Number of passes computed since load; tests use it to check that clean frames do no work.</summary>
        internal static int PassCount { get; private set; }

        /// <summary>The nodes of the last computed pass, a parent before its subtree; valid until the next Compute.</summary>
        internal static List<LayoutNode> PassNodes => s_all;

        /// <summary>
        /// Lays out the tree under <paramref name="root"/>. <paramref name="available"/> is the size the root
        /// has to work with on an axis whose sizing is Grow or Percent (normally its rect size). Returns the
        /// root's resulting size.
        /// </summary>
        internal static Vector2 Compute(LayoutNode root, Vector2 available)
        {
            PassCount++;
            s_all.Clear();
            Build(root, null);
            for (int i = 0; i < s_all.Count; i++)
                s_all[i]._passRoot = root;

            for (int i = 0; i < s_all.Count; i++)
                MeasureContent(s_all[i], -1f);

            FitAxis(0);
            SizeAxis(0, root, available.x);

            // Widths are final: content that wraps answers with its height for that width (Clay wraps text here).
            for (int i = 0; i < s_all.Count; i++)
            {
                var n = s_all[i];
                if (n._measurable != null)
                    MeasureContent(n, Mathf.Max(0f, n._size.x - n.PaddingSum(0)));
            }

            FitAxis(1);
            SizeAxis(1, root, available.y);
            Position(root);
            return root._size;
        }

        /// <summary>Writes the last computed layout to the RectTransforms of the tree. Must follow <see cref="Compute"/> for the same root.</summary>
        internal static void Commit(LayoutNode root)
        {
            root._tracker.Clear();

            // A node arrives on its first layout in this enabled span when its parent was already shown: the
            // topmost node of a subtree that appeared. Read before any node of the pass is marked shown.
            for (int i = 1; i < s_all.Count; i++)
            {
                var n = s_all[i];
                n._arriving = !n._shown && n._parentNode._shown && !n._settled;
            }
            root._shown = true;

            var rootRect = root.RectTransform;
            if (root.Width.Mode is SizingMode.Fit or SizingMode.Fixed)
                root._tracker.Add(root, rootRect, DrivenTransformProperties.SizeDeltaX);
            if (root.Height.Mode is SizingMode.Fit or SizingMode.Fixed)
                root._tracker.Add(root, rootRect, DrivenTransformProperties.SizeDeltaY);
            // A lifted node's own rect is its flight's; only its subtree is laid out, against that rect.
            if (!root._lifted)
                CommitNode(root, new Rect(Vector2.zero, root._size));

            for (int i = 1; i < s_all.Count; i++)
            {
                var n = s_all[i];
                root._tracker.Add(root, n.RectTransform, DrivenTransformProperties.Anchors | DrivenTransformProperties.AnchoredPosition | DrivenTransformProperties.SizeDelta);
                CommitNode(n, new Rect(n._pos, n._size));
            }
        }

        // Records the target and writes the transform. Layout is instant: a view transition animates after its
        // update's pass, not in it (see LayoutSystem.ViewTransitions). A pass while a move is in flight leaves the
        // move alone, and only a changed target retargets it, with the move's own timing, as a page change during
        // a web transition would; the tick in LayoutSystem carries every move the rest of the way.
        private static void CommitNode(LayoutNode n, Rect target)
        {
            if (n._animating && !Approximately(target, n._committedRect))
            {
                var from = n.CurrentVisual();
                // Content that cannot be drawn between two sizes (text) takes its new size at once and only its
                // position animates; at intermediate widths it would wrap or clip.
                if (n._measurable != null && !n._measurable.SizeIsAnimatable)
                    from.size = target.size;
                if (Approximately(from, target))
                    n._animating = false;
                else
                    StartMove(n, from, target, n._animTransition);
            }
            n._committedRect = target;
            n._hasCommitted = true;
            n._shown = true;
            Write(n, n._animating ? n.CurrentVisual() : target);
        }

        /// <summary>Starts a move between two engine-space rects under the node's parent.</summary>
        internal static void StartMove(LayoutNode n, Rect from, Rect to, LayoutTransition transition)
        {
            n._animFrom = from;
            n._animTo = to;
            n._animElapsed = 0f;
            n._animTransition = transition;
            n._animating = true;
            // A move starts with its contents in their old state; what rides inside it follows its morph from here.
            n._lookMorph = 0f;
            n._ridesOn = null;
            // A fade riding on the move goes on from the alpha it has reached, rather than starting over.
            if (n._fadeGroup != null)
                n._fadeFrom = n._fadeGroup.alpha;
            LayoutSystem.RegisterAnimating(n);
        }

        internal static void StopMove(LayoutNode n)
        {
            n._animating = false;
            EndFade(n);
            ResetLook(n);
        }

        /// <summary>
        /// Sets the look a motion's frame gives a node: its scale, applied around the centre by the next
        /// <see cref="Write"/>, its opacity, applied by the next <see cref="WriteAlpha"/>, and its morph.
        /// </summary>
        internal static void SetLook(LayoutNode n, in MotionFrame frame)
        {
            n._lookScale = frame.Scale;
            n._lookOpacity = frame.Opacity;
            n._lookMorph = frame.Morph;
        }

        /// <summary>
        /// Writes the node's opacity, the one place it is written while a node moves: its fade at
        /// <paramref name="fade"/> (0 where the fade starts, 1 where it ends; for a half of a pair, the frame's
        /// morph) times its look's opacity.
        /// </summary>
        internal static void WriteAlpha(LayoutNode n, float fade)
        {
            if (n._fadeGroup != null)
            {
                n._fadeGroup.alpha = Mathf.Lerp(n._fadeFrom, n._fadeTo, fade) * n._lookOpacity;
                return;
            }
            if (n._lookOpacity >= 1f && n._lookGroup == null)
                return;
            if (n._lookGroup == null)
            {
                if (!n.TryGetComponent(out n._lookGroup))
                    n._lookGroup = n.gameObject.AddComponent<CanvasGroup>();
                n._lookRestore = n._lookGroup.alpha;
            }
            n._lookGroup.alpha = n._lookRestore * n._lookOpacity;
        }

        /// <summary>Takes a motion's look off the node when its move ends: full scale, and the opacity it had.</summary>
        internal static void ResetLook(LayoutNode n)
        {
            bool scaled = n._lookScaled;
            n._lookScale = 1f;
            n._lookOpacity = 1f;
            n._lookMorph = 1f;
            n._lookScaled = false;
            if (scaled)
            {
                var r = n.RectTransform;
                r.localScale = Vector3.one;
                if (n._hasCommitted || n._lifted)
                    r.anchoredPosition = n._committedBase + n.Offset;
            }
            if (n._lookGroup != null)
            {
                if (n._fadeGroup == null)
                    n._lookGroup.alpha = n._lookRestore;
                n._lookGroup = null;
            }
        }

        /// <summary>
        /// How far a node scaled by its look is shifted so it scales around its centre rather than its pivot, in
        /// anchoredPosition terms (y up), for a rect of <paramref name="size"/>.
        /// </summary>
        internal static Vector2 LookShift(LayoutNode n, Vector2 size)
        {
            float s = n._lookScale;
            if (Mathf.Approximately(s, 1f)) return Vector2.zero;
            var pivot = n.RectTransform.pivot;
            return new Vector2((0.5f - pivot.x) * size.x, (0.5f - pivot.y) * size.y) * (1f - s);
        }

        /// <summary>
        /// Fades the node's CanvasGroup, adding one the first time the object needs it: in, from invisible up to the
        /// alpha it has, or out, from that alpha to invisible, driven by the move's eased progress so the crossover
        /// of a pair rides the motion whichever way a transition runs. A node already fading goes on from where its
        /// fade has got to. <paramref name="passThrough"/> lets clicks through a node on its way out.
        /// <see cref="EndFade"/> puts the group back; the group stays on the object.
        /// </summary>
        internal static void StartFade(LayoutNode n, bool fadeIn, bool passThrough = false)
        {
            float? current = n._fadeGroup != null ? n._fadeGroup.alpha : null;
            var group = TakeGroup(n, passThrough);
            float shown = group.alpha;
            n._fadeFrom = current ?? (fadeIn ? 0f : shown);
            n._fadeTo = fadeIn ? shown : 0f;
            n._fadeRestore = shown;
            group.alpha = n._fadeFrom;
        }

        /// <summary>Hides the node until <see cref="EndFade"/>, letting clicks through: the copy of a persisting pair while the kept object flies to it.</summary>
        internal static void Hide(LayoutNode n)
        {
            var group = TakeGroup(n, true);
            n._fadeRestore = group.alpha;
            n._fadeFrom = n._fadeTo = 0f;
            group.alpha = 0f;
        }

        private static CanvasGroup TakeGroup(LayoutNode n, bool passThrough)
        {
            ReleaseFade(n);
            if (!n.TryGetComponent(out CanvasGroup group))
                group = n.gameObject.AddComponent<CanvasGroup>();
            n._fadeGroup = group;
            if (passThrough && group.blocksRaycasts)
            {
                group.blocksRaycasts = false;
                n._fadePassThrough = true;
            }
            return group;
        }

        /// <summary>
        /// Ends a fade or a hide: the group shows at the value it had and stays on the object for the next one. A
        /// node hidden by <see cref="LayoutSystem.Hide"/> then rests unseen (<see cref="Conceal"/>).
        /// </summary>
        internal static void EndFade(LayoutNode n)
        {
            ReleaseFade(n);
            if (n._hidden)
                Conceal(n);
        }

        /// <summary>
        /// Rests a hidden node unseen: its CanvasGroup, added if it has none, goes to 0 and lets clicks through, what
        /// it had kept for <see cref="Reveal"/>. Only while playing: the editor goes on drawing a hidden node.
        /// </summary>
        internal static void Conceal(LayoutNode n)
        {
            if (n._concealed || !Application.isPlaying) return;
            if (!n.TryGetComponent(out CanvasGroup group))
                group = n.gameObject.AddComponent<CanvasGroup>();
            n._concealed = true;
            n._concealRestore = group.alpha;
            n._concealBlocked = group.blocksRaycasts;
            group.alpha = 0f;
            group.blocksRaycasts = false;
        }

        /// <summary>Shows a concealed node again: its CanvasGroup goes back to what it had.</summary>
        internal static void Reveal(LayoutNode n)
        {
            if (!n._concealed) return;
            n._concealed = false;
            if (!n.TryGetComponent(out CanvasGroup group)) return;
            group.alpha = n._concealRestore;
            if (n._concealBlocked)
                group.blocksRaycasts = true;
        }

        // Puts a fade's group back as it was: for a fade that ends, and before one that takes over from it.
        private static void ReleaseFade(LayoutNode n)
        {
            var group = n._fadeGroup;
            n._fadeGroup = null;
            if (group == null) return;
            group.alpha = n._fadeRestore;
            if (n._fadePassThrough)
                group.blocksRaycasts = true;
            n._fadePassThrough = false;
        }

        /// <summary>An offset given like <see cref="LayoutNode.Offset"/> (y up) in engine space (y down).</summary>
        internal static Vector2 ToEngine(Vector2 offset) => new(offset.x, -offset.y);

        /// <summary>The rect a transform shows on screen: its top-left corner and size in world units.</summary>
        internal static Rect WorldRect(RectTransform rt)
        {
            var rect = rt.rect;
            var topLeft = rt.TransformPoint(new Vector3(rect.xMin, rect.yMax, 0f));
            var scale = rt.lossyScale;
            return new Rect(topLeft.x, topLeft.y, rect.width * scale.x, rect.height * scale.y);
        }

        /// <summary>A world rect as the engine-space rect that would put a (non-root) node there under its parent's current frame, its Offset taken out since the write adds it.</summary>
        internal static Rect RelFromWorld(LayoutNode n, Rect world)
        {
            var parent = (RectTransform)n.RectTransform.parent;
            var pr = parent.rect;
            var local = parent.InverseTransformPoint(new Vector3(world.x, world.y, 0f));
            var scale = parent.lossyScale;
            var size = new Vector2(scale.x != 0f ? world.width / scale.x : world.width, scale.y != 0f ? world.height / scale.y : world.height);
            var offset = ToEngine(n.Offset);
            return new Rect(local.x - pr.xMin - offset.x, pr.yMax - local.y - offset.y, size.x, size.y);
        }

        /// <summary>
        /// A world rect as the engine-space rect it is under a parent whose top-left corner is at
        /// <paramref name="parentWorld"/>, a frame given rather than read from a transform: how a captured rect is
        /// expressed in a captured parent frame. <paramref name="offset"/> is the node's Offset, taken out since the write adds it.
        /// </summary>
        internal static Rect RelIn(Rect parentWorld, Vector3 scale, Rect world, Vector2 offset)
        {
            float sx = scale.x != 0f ? scale.x : 1f, sy = scale.y != 0f ? scale.y : 1f;
            var o = ToEngine(offset);
            return new Rect((world.x - parentWorld.x) / sx - o.x, (parentWorld.y - world.y) / sy - o.y, world.width / sx, world.height / sy);
        }

        /// <summary>Where an engine-space rect of a (non-root) node lands on screen under its parent's current frame, in world units.</summary>
        internal static Rect WorldFromRel(LayoutNode n, Rect rel)
        {
            var parent = (RectTransform)n.RectTransform.parent;
            var pr = parent.rect;
            var offset = ToEngine(n.Offset);
            var world = parent.TransformPoint(new Vector3(pr.xMin + rel.x + offset.x, pr.yMax - rel.y - offset.y, 0f));
            var scale = parent.lossyScale;
            return new Rect(world.x, world.y, rel.width * scale.x, rel.height * scale.y);
        }

        internal static bool Approximately(Rect a, Rect b) =>
            Mathf.Abs(a.x - b.x) <= Epsilon && Mathf.Abs(a.y - b.y) <= Epsilon
            && Mathf.Abs(a.width - b.width) <= Epsilon && Mathf.Abs(a.height - b.height) <= Epsilon;

        /// <summary>Puts <paramref name="rect"/> (engine space) on the node's transform. The only place a node's rect is written, whether by a commit or by a transition tick.</summary>
        internal static void Write(LayoutNode n, Rect rect)
        {
            var r = n.RectTransform;
            if (n.IsRoot && !n._lifted)
            {
                // A root keeps its anchors and position; only a Fit or Fixed axis is ours to size. (A lifted node
                // is a root of nothing while it sits in the transition layer: it is placed like a child.)
                if (n.Width.Mode is SizingMode.Fit or SizingMode.Fixed)
                    r.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rect.width);
                if (n.Height.Mode is SizingMode.Fit or SizingMode.Fixed)
                    r.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rect.height);
            }
            else
            {
                // Same convention as LayoutGroup.SetChildAlongAxis: anchored to the parent's top-left, the
                // position placing the pivot, so any pivot and any later animation of anchoredPosition behave
                // as they do under a UGUI group.
                r.anchorMin = Vector2.up;
                r.anchorMax = Vector2.up;
                r.sizeDelta = rect.size;
                var pivot = r.pivot;
                n._committedBase = new Vector2(rect.x + rect.width * pivot.x, -(rect.y + rect.height * (1f - pivot.y)));
                // A motion's scale is around the centre: the transform scales around its pivot, so shift to make up.
                // The transform's scale is only touched while a motion has it, so a game's own scale is left alone.
                if (!Mathf.Approximately(n._lookScale, 1f))
                {
                    r.localScale = new Vector3(n._lookScale, n._lookScale, 1f);
                    n._lookScaled = true;
                }
                else if (n._lookScaled)
                {
                    r.localScale = Vector3.one;
                    n._lookScaled = false;
                }
                r.anchoredPosition = n._committedBase + n.Offset + LookShift(n, rect.size);
            }
            n._visualRect = rect;
        }

        // ── Tree ───────────────────────────────────────────────────────────────

        private static void Build(LayoutNode node, LayoutNode parent)
        {
            node._parentNode = parent;
            node._children.Clear();
            node._floatingChildren.Clear();
            s_all.Add(node);

            var t = node.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                // An exiting node has left the flow; it stays drawn where it was while its exit plays.
                if (!child.gameObject.activeSelf || !child.TryGetComponent<LayoutNode>(out var childNode) || !childNode.enabled || childNode._exiting)
                    continue;

                if (childNode.AttachTo != AttachTo.None)
                    node._floatingChildren.Add(childNode);
                else
                    node._children.Add(childNode);
                Build(childNode, node);
            }

            node._isLeaf = node._children.Count == 0;
            node._measurable = node._isLeaf ? FindMeasurable(node) ?? node.UguiContentOrNull() : null;
        }

        private static ILayoutMeasurable FindMeasurable(LayoutNode node)
        {
            node.GetComponents(s_measurables);
            ILayoutMeasurable found = null;
            for (int i = 0; i < s_measurables.Count; i++)
            {
                var m = s_measurables[i];
                if (m is Behaviour b && !b.isActiveAndEnabled)
                    continue;
                found = m;
                break;
            }
            s_measurables.Clear();
            return found;
        }

        private static void MeasureContent(LayoutNode n, float availableWidth)
        {
            if (n._measurable == null)
            {
                n._contentSize = Vector2.zero;
                n._minContentWidth = 0f;
                return;
            }
            n._contentSize = Vector2.Max(n._measurable.Measure(availableWidth), Vector2.zero);
            n._minContentWidth = Mathf.Max(0f, n._measurable.MinWidth);
        }

        // ── Sizing ─────────────────────────────────────────────────────────────

        // A child's contribution to its parent's Fit before the top-down pass: Fit brings its own fit,
        // Fixed its size, Grow its minimum, Percent nothing (it is resolved from the parent later).
        private static float Initial(LayoutNode n, int axis)
        {
            var s = n.SizingOn(axis);
            return s.Mode switch
            {
                SizingMode.Fit => n._fit[axis],
                SizingMode.Fixed => s.Value,
                SizingMode.Grow => s.Min,
                _ => 0f,
            };
        }

        private static float InitialMin(LayoutNode n, int axis)
        {
            var s = n.SizingOn(axis);
            return s.Mode switch
            {
                SizingMode.Fit => n._minFit[axis],
                SizingMode.Fixed => s.Value,
                SizingMode.Grow => s.Min,
                _ => 0f,
            };
        }

        // Bottom-up (children come after their parent in s_all): each node's Fit size and the smallest
        // size it can shrink to. Along the direction: sum plus gaps; across it: the max; both plus padding.
        private static void FitAxis(int axis)
        {
            for (int i = s_all.Count - 1; i >= 0; i--)
            {
                var n = s_all[i];
                float pad = n.PaddingSum(axis);
                float fit, minFit;

                if (n._isLeaf)
                {
                    fit = n._contentSize[axis] + pad;
                    minFit = (axis == 0 ? n._minContentWidth : n._contentSize.y) + pad;
                }
                else
                {
                    var children = n._children;
                    fit = 0f;
                    minFit = 0f;
                    if (n.LayoutAxis == axis)
                    {
                        for (int c = 0; c < children.Count; c++)
                        {
                            fit += Initial(children[c], axis);
                            minFit += InitialMin(children[c], axis);
                        }
                        float gaps = n.Gap * (children.Count - 1);
                        fit += gaps;
                        minFit += gaps;
                    }
                    else
                    {
                        for (int c = 0; c < children.Count; c++)
                        {
                            fit = Mathf.Max(fit, Initial(children[c], axis));
                            minFit = Mathf.Max(minFit, InitialMin(children[c], axis));
                        }
                    }
                    fit += pad;
                    minFit += pad;
                }

                // An aspect ratio derives a Fit height from the width, or, when only the height is known up front
                // (Fixed; widths are settled before heights), a Fit width from the height.
                if (n.AspectRatio > 0f)
                {
                    if (axis == 1 && n.Height.Mode == SizingMode.Fit)
                    {
                        fit = n._size.x / n.AspectRatio;
                        minFit = fit;
                    }
                    else if (axis == 0 && n.Width.Mode == SizingMode.Fit && n.Height.Mode == SizingMode.Fixed)
                    {
                        fit = n.Height.Value * n.AspectRatio;
                        minFit = fit;
                    }
                }

                var sizing = n.SizingOn(axis);
                n._fit[axis] = sizing.Clamp(fit);
                n._minFit[axis] = sizing.Clamp(minFit);
            }
        }

        // Top-down: the root takes its input size, then every container resolves its children.
        private static void SizeAxis(int axis, LayoutNode root, float available)
        {
            var rootSizing = root.SizingOn(axis);
            root._size[axis] = rootSizing.Mode switch
            {
                SizingMode.Fixed => rootSizing.Value,
                SizingMode.Fit => root._fit[axis],
                _ => Mathf.Max(0f, available),
            };

            for (int i = 0; i < s_all.Count; i++)
            {
                var n = s_all[i];
                float inner = Mathf.Max(0f, n._size[axis] - n.PaddingSum(axis));

                if (n._children.Count > 0)
                {
                    if (n.LayoutAxis == axis)
                        SizeAlong(n, axis, inner);
                    else
                        SizeAcross(n, axis, inner);
                }

                // Floating children size against their parent's whole rect, padding included (the rect their attach
                // points are placed on, as CSS sizes an absolutely positioned box against the padding box), whatever
                // they attach to; a Fit one keeps its own fit, since it is not confined to the parent.
                var floating = n._floatingChildren;
                for (int f = 0; f < floating.Count; f++)
                    floating[f]._size[axis] = ResolveFloating(floating[f], axis, n._size[axis]);
            }
        }

        // Across the direction a child has the parent's inner size to itself: Grow fills it (within its
        // bounds), Fit wraps to it but never below its minimum, Fixed and Percent are what they say.
        private static float ResolveAcross(LayoutNode c, int axis, float inner)
        {
            var s = c.SizingOn(axis);
            return s.Mode switch
            {
                SizingMode.Fixed => s.Value,
                SizingMode.Percent => inner * s.Value,
                SizingMode.Grow => s.Clamp(inner),
                _ => Mathf.Max(Mathf.Min(c._fit[axis], inner), c._minFit[axis]),
            };
        }

        private static float ResolveFloating(LayoutNode c, int axis, float parentSize)
        {
            var s = c.SizingOn(axis);
            return s.Mode switch
            {
                SizingMode.Fixed => s.Value,
                SizingMode.Percent => parentSize * s.Value,
                SizingMode.Grow => s.Clamp(parentSize),
                _ => c._fit[axis],
            };
        }

        private static void SizeAcross(LayoutNode n, int axis, float inner)
        {
            var children = n._children;
            for (int c = 0; c < children.Count; c++)
                children[c]._size[axis] = ResolveAcross(children[c], axis, inner);
        }

        private static void SizeAlong(LayoutNode n, int axis, float inner)
        {
            var children = n._children;
            float available = inner - n.Gap * (children.Count - 1);
            float used = 0f;
            for (int c = 0; c < children.Count; c++)
            {
                var child = children[c];
                var s = child.SizingOn(axis);
                child._size[axis] = s.Mode switch
                {
                    SizingMode.Fixed => s.Value,
                    SizingMode.Percent => Mathf.Max(0f, available) * s.Value,
                    SizingMode.Grow => s.Min,
                    _ => child._fit[axis],
                };
                used += child._size[axis];
            }

            float remaining = available - used;
            if (remaining > Epsilon)
                Grow(children, axis, remaining);
            else if (remaining < -Epsilon)
                Shrink(children, axis, -remaining);
        }

        // Clay's distribution: the smallest Grow children are raised together until they meet the next
        // size up, a max, or the space runs out; repeat. Equal children therefore end up equal.
        private static void Grow(List<LayoutNode> children, int axis, float remaining)
        {
            s_resizable.Clear();
            for (int c = 0; c < children.Count; c++)
            {
                var s = children[c].SizingOn(axis);
                if (s.Mode == SizingMode.Grow && (!s.HasMax || children[c]._size[axis] < s.Max - Epsilon))
                    s_resizable.Add(children[c]);
            }

            while (remaining > Epsilon && s_resizable.Count > 0)
            {
                float smallest = float.MaxValue, second = float.MaxValue;
                int count = 0;
                for (int i = 0; i < s_resizable.Count; i++)
                {
                    float v = s_resizable[i]._size[axis];
                    if (v < smallest - Epsilon) { second = smallest; smallest = v; count = 1; }
                    else if (v < smallest + Epsilon) count++;
                    else if (v < second) second = v;
                }

                float add = remaining / count;
                if (second < float.MaxValue) add = Mathf.Min(add, second - smallest);
                for (int i = 0; i < s_resizable.Count; i++)
                {
                    var r = s_resizable[i];
                    var s = r.SizingOn(axis);
                    if (s.HasMax && Mathf.Abs(r._size[axis] - smallest) <= Epsilon)
                        add = Mathf.Min(add, s.Max - r._size[axis]);
                }
                if (add <= Epsilon) break;

                for (int i = s_resizable.Count - 1; i >= 0; i--)
                {
                    var r = s_resizable[i];
                    if (Mathf.Abs(r._size[axis] - smallest) > Epsilon) continue;
                    r._size[axis] += add;
                    remaining -= add;
                    var s = r.SizingOn(axis);
                    if (s.HasMax && r._size[axis] >= s.Max - Epsilon)
                        s_resizable.RemoveAt(i);
                }
            }
        }

        // The mirror image on overflow: the largest shrinkable children come down together until they
        // meet the next size down or a floor. Grow floors at its Min, Fit at its minimum fit (a leaf's
        // content minimum, such as the longest word); Fixed and Percent never shrink.
        private static void Shrink(List<LayoutNode> children, int axis, float excess)
        {
            s_resizable.Clear();
            for (int c = 0; c < children.Count; c++)
            {
                if (Floor(children[c], axis) < children[c]._size[axis] - Epsilon)
                    s_resizable.Add(children[c]);
            }

            while (excess > Epsilon && s_resizable.Count > 0)
            {
                float largest = float.MinValue, second = float.MinValue;
                int count = 0;
                for (int i = 0; i < s_resizable.Count; i++)
                {
                    float v = s_resizable[i]._size[axis];
                    if (v > largest + Epsilon) { second = largest; largest = v; count = 1; }
                    else if (v > largest - Epsilon) count++;
                    else if (v > second) second = v;
                }

                float remove = excess / count;
                if (second > float.MinValue) remove = Mathf.Min(remove, largest - second);
                for (int i = 0; i < s_resizable.Count; i++)
                {
                    var r = s_resizable[i];
                    if (Mathf.Abs(r._size[axis] - largest) <= Epsilon)
                        remove = Mathf.Min(remove, r._size[axis] - Floor(r, axis));
                }
                if (remove <= Epsilon) break;

                for (int i = s_resizable.Count - 1; i >= 0; i--)
                {
                    var r = s_resizable[i];
                    if (Mathf.Abs(r._size[axis] - largest) > Epsilon) continue;
                    r._size[axis] -= remove;
                    excess -= remove;
                    if (Floor(r, axis) >= r._size[axis] - Epsilon)
                        s_resizable.RemoveAt(i);
                }
            }
        }

        private static float Floor(LayoutNode c, int axis)
        {
            var s = c.SizingOn(axis);
            return s.Mode switch
            {
                SizingMode.Grow => s.Min,
                SizingMode.Fit => c._minFit[axis],
                _ => c._size[axis],
            };
        }

        // ── Positions ──────────────────────────────────────────────────────────

        // Three steps. Every flow child gets its position within its parent, which needs only sizes. The
        // flow tree under the root then gets its root-relative positions, so any flow node can serve as an
        // attach target wherever it sits. Finally each floating node, in hierarchy order, is placed against
        // its target and its own flow subtree is resolved under it; a target that is itself floating is
        // therefore current only when it precedes the node in hierarchy order.
        private static void Position(LayoutNode root)
        {
            for (int i = 0; i < s_all.Count; i++)
            {
                var n = s_all[i];
                var children = n._children;
                if (children.Count == 0) continue;

                int along = n.LayoutAxis;
                int across = 1 - along;
                float innerAlong = n._size[along] - n.PaddingSum(along);
                float innerAcross = n._size[across] - n.PaddingSum(across);

                float total = n.Gap * (children.Count - 1);
                for (int c = 0; c < children.Count; c++)
                    total += children[c]._size[along];

                // SpaceBetween shares the free space out between the children, on top of the gap; with one child
                // or nothing to share it sits at the start.
                float free = innerAlong - total;
                bool spread = n.SpreadsAlong(along) && children.Count > 1 && free > Epsilon;
                float step = spread ? free / (children.Count - 1) : 0f;
                float cursor = n.PaddingStart(along) + (spread ? 0f : n.AlignFraction(along) * free);
                for (int c = 0; c < children.Count; c++)
                {
                    var child = children[c];
                    float acrossFraction = child.AlignSelf == AlignSelf.Auto ? n.AlignFraction(across) : child.AlignSelfFraction();
                    child._pos[along] = cursor;
                    child._pos[across] = n.PaddingStart(across) + acrossFraction * (innerAcross - child._size[across]);
                    cursor += child._size[along] + n.Gap + step;
                }
            }

            root._pos = Vector2.zero;
            root._absPos = Vector2.zero;
            ResolveFlowPositions(root);

            for (int i = 1; i < s_all.Count; i++)
            {
                var n = s_all[i];
                if (n.AttachTo == AttachTo.None) continue;

                var parent = n._parentNode;
                var target = parent;
                if (n.AttachTo == AttachTo.Root)
                    target = root;
                else if (n.AttachTo == AttachTo.Element && n.AttachElement != null)
                {
                    if (n.AttachElement.TryGetComponent<LayoutNode>(out var element) && ReferenceEquals(element._passRoot, root))
                        target = element;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    else if (!n._warnedAttach)
                    {
                        n._warnedAttach = true;
                        Debug.LogWarning($"[UI.Layout] '{n.name}' attaches to '{n.AttachElement.name}', which is not a node in the same layout tree, so it floats against its parent instead.", n);
                    }
#endif
                }

                var targetPoint = target._absPos + Point(n.ParentPoint, target._size);
                n._absPos = targetPoint - Point(n.ElementPoint, n._size) + n.FloatOffset;
                n._pos = n._absPos - parent._absPos;
                ResolveFlowPositions(n);
            }
        }

        // Root-relative positions of the flow children under a node whose own position is known.
        private static void ResolveFlowPositions(LayoutNode node)
        {
            var children = node._children;
            for (int c = 0; c < children.Count; c++)
            {
                var child = children[c];
                child._absPos = node._absPos + child._pos;
                ResolveFlowPositions(child);
            }
        }

        private static Vector2 Point(AttachPoint point, Vector2 size)
        {
            int p = (int)point;
            return new Vector2(size.x * ((p % 3) * 0.5f), size.y * ((p / 3) * 0.5f));
        }
    }
}
