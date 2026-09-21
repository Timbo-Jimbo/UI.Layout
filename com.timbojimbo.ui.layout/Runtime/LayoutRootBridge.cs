using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// Lets a root node live inside UGUI layout (a layout group, or a ScrollRect content driven by a
    /// fitter). It reports the root's Fit size to UGUI as an <c>ILayoutElement</c>, computed without a
    /// commit, and re-lays the tree out inside UGUI's pass when UGUI resizes the root, so the content is
    /// right in the same frame. Does nothing on a node that is not a root.
    /// </summary>
    [AddComponentMenu("Timbo Jimbo/UI/Layout/Layout Root Bridge")]
    [RequireComponent(typeof(LayoutNode))]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class LayoutRootBridge : UIBehaviour, ILayoutElement, ILayoutSelfController
    {
        private LayoutNode _node;

        private LayoutNode Node => _node != null ? _node : (_node = GetComponent<LayoutNode>());

        public float minWidth => -1f;
        public float minHeight => -1f;
        public float flexibleWidth => -1f;
        public float flexibleHeight => -1f;
        public int layoutPriority => 1;

        public float preferredWidth => Reports(Node.Width) ? LayoutEngine.Compute(Node, Node.RectTransform.rect.size).x : -1f;

        public float preferredHeight => Reports(Node.Height) ? LayoutEngine.Compute(Node, Node.RectTransform.rect.size).y : -1f;

        private bool Reports(Sizing sizing) => IsActive() && Node.IsRoot && sizing.Mode is SizingMode.Fit or SizingMode.Fixed;

        public void CalculateLayoutInputHorizontal() { }

        public void CalculateLayoutInputVertical() { }

        public void SetLayoutHorizontal() { }

        // UGUI has sized the root on both axes by now.
        public void SetLayoutVertical()
        {
            if (IsActive() && Node.IsRoot)
                LayoutSystem.ForceLayout(Node);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }

        protected override void OnDisable()
        {
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
            base.OnDisable();
        }
    }
}
