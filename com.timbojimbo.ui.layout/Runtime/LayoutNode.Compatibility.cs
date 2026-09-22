using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// The UGUI compatibility path of a leaf node. Content normally comes from an <see cref="ILayoutMeasurable"/>
    /// on the node's object. When there is none but there are UGUI <c>ILayoutElement</c>s (a LayoutElement,
    /// an Image, a TextMeshPro text), the node measures them the way a UGUI group would, and every Graphic's
    /// dirty-layout callback marks the node, so a sprite or text change still reflows the tree.
    /// </summary>
    /// <remarks>
    /// This is a compromise, kept apart from the rest of the node on purpose. <c>ILayoutElement</c> only
    /// answers for the width its transform has, so the measure below writes the width it is asked about onto
    /// the transform before reading; the pass that follows writes the real rect anyway. And the minimum width
    /// is what the elements report, which for a text is 0, so such a text can be squeezed narrower than its
    /// longest word. Components that implement <see cref="ILayoutMeasurable"/> avoid both.
    /// </remarks>
    public sealed partial class LayoutNode
    {
        private const float WidthTolerance = 1e-3f;
        private static readonly List<ILayoutElement> s_elements = new();

        private static readonly List<Graphic> s_graphicsScratch = new();
        private readonly List<Graphic> _graphics = new();
        private UnityAction _onGraphicLayoutDirty;
        private UguiContent _uguiContent;

        /// <summary>The UGUI elements on this object as content, or null when there are none to measure.</summary>
        internal ILayoutMeasurable UguiContentOrNull()
        {
            GetComponents(s_elements);
            bool any = false;
            for (int i = 0; i < s_elements.Count && !any; i++)
                any = IsMeasurableElement(s_elements[i]);
            s_elements.Clear();
            if (!any)
                return null;
            // A Graphic added after the node was enabled has no callback yet; every pass that measures UGUI
            // content brings the registrations in line with the components present.
            if (_graphics.Count != CountGraphics())
            {
                UnregisterGraphicCallbacks();
                RegisterGraphicCallbacks();
            }
            return _uguiContent ??= new UguiContent(this);
        }

        private int CountGraphics()
        {
            GetComponents(s_graphicsScratch);
            int count = s_graphicsScratch.Count;
            s_graphicsScratch.Clear();
            return count;
        }

        // The root bridge is an ILayoutElement too, but it reports this tree's own size to a UGUI parent;
        // measuring it from inside the tree would recurse.
        private static bool IsMeasurableElement(ILayoutElement element)
            => element is not LayoutRootBridge && (element is not Behaviour b || b.isActiveAndEnabled);

        private void RegisterGraphicCallbacks()
        {
            _onGraphicLayoutDirty ??= () => LayoutSystem.MarkDirty(this);
            GetComponents(_graphics);
            for (int i = 0; i < _graphics.Count; i++)
                _graphics[i].RegisterDirtyLayoutCallback(_onGraphicLayoutDirty);
        }

        private void UnregisterGraphicCallbacks()
        {
            for (int i = 0; i < _graphics.Count; i++)
            {
                if (_graphics[i] != null)
                    _graphics[i].UnregisterDirtyLayoutCallback(_onGraphicLayoutDirty);
            }
            _graphics.Clear();
        }

        /// <summary>
        /// Reads a layout property the way LayoutUtility does (the highest layout priority wins, then the largest
        /// value), over the measurable elements only.
        /// </summary>
        private float UguiProperty(System.Func<ILayoutElement, float> property)
        {
            GetComponents(s_elements);
            float value = 0f;
            int priority = int.MinValue;
            for (int i = 0; i < s_elements.Count; i++)
            {
                var element = s_elements[i];
                if (!IsMeasurableElement(element))
                    continue;
                int p = element.layoutPriority;
                float v = property(element);
                if (p > priority)
                {
                    priority = p;
                    value = Mathf.Max(0f, v);
                }
                else if (p == priority)
                {
                    value = Mathf.Max(value, v);
                }
            }
            s_elements.Clear();
            return value;
        }

        private sealed class UguiContent : ILayoutMeasurable
        {
            private static readonly System.Func<ILayoutElement, float> PreferredWidth = e => e.preferredWidth;
            private static readonly System.Func<ILayoutElement, float> PreferredHeight = e => e.preferredHeight;
            private static readonly System.Func<ILayoutElement, float> MinimumWidth = e => e.minWidth;

            private readonly LayoutNode _node;

            public UguiContent(LayoutNode node) => _node = node;

            public Vector2 Measure(float availableWidth)
            {
                var rect = _node.RectTransform;
                if (availableWidth >= 0f && Mathf.Abs(rect.rect.width - availableWidth) > WidthTolerance)
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, availableWidth);
                return new Vector2(_node.UguiProperty(PreferredWidth), _node.UguiProperty(PreferredHeight));
            }

            public float MinWidth => _node.UguiProperty(MinimumWidth);
        }
    }
}
