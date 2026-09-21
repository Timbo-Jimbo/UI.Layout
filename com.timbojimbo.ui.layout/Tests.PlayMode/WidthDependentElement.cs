using UnityEngine;
using UnityEngine.UI;

namespace TimboJimboTests.UI.Layout.PlayMode
{
    /// <summary>A UGUI layout element shaped like a text: a fixed unconstrained width, and a height that follows the width its transform currently has, as TMP's preferred height does.</summary>
    public sealed class WidthDependentElement : MonoBehaviour, ILayoutElement
    {
        public float UnconstrainedWidth = 100f;
        public float Area = 1000f;
        public float MinimumWidth = 20f;

        public float minWidth => MinimumWidth;
        public float preferredWidth => UnconstrainedWidth;
        public float minHeight => 0f;
        public float preferredHeight
        {
            get
            {
                float width = ((RectTransform)transform).rect.width;
                return width > 0f ? Area / width : Area;
            }
        }
        public float flexibleWidth => -1f;
        public float flexibleHeight => -1f;
        public int layoutPriority => 0;

        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical() { }
    }
}
