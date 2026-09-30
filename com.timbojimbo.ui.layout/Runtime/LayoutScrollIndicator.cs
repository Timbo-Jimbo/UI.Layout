using UnityEngine;
using UnityEngine.UI;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// A scroll container's indicator: the thin rounded bar the layout system draws along a container's edge while it
    /// scrolls (<see cref="LayoutNode.ShowsScrollIndicators"/>), one for each way it scrolls, on an object of its own
    /// inside the container, hidden and never saved, so the container's clip cuts it as it does what it scrolls. It is
    /// not added by hand. It requires its CanvasRenderer, as every concrete uGUI graphic does (Graphic itself does not):
    /// added without one, Graphic's lazy lookup gets Unity's placeholder null in the editor and never adds it.
    /// </summary>
    [AddComponentMenu("")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LayoutScrollIndicator : MaskableGraphic
    {
        // Segments in each rounded end, and how far past its rect its edge fades to nothing (canvas units): a smooth edge
        // with no texture.
        private const int CapSegments = 8;
        private const float Feather = 1f;

        // A capsule filling its rect, its ends rounded to half its thickness: a ring of points round its edge, each drawn
        // in its colour, fanned from its centre, and again a feather further out at no alpha. The ring runs round the
        // first end's arc and then the second's (the top then the bottom when it stands upright, the right then the left
        // when it lies flat), so the straight sides join them.
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            if (radius <= 0f) return;
            bool upright = rect.height >= rect.width;
            var centre = rect.center;
            var half = upright ? new Vector2(0f, rect.height * 0.5f - radius) : new Vector2(rect.width * 0.5f - radius, 0f);
            float start = upright ? 0f : -Mathf.PI * 0.5f;
            Color32 solid = color;
            var clear = solid;
            clear.a = 0;

            vh.AddVert(centre, solid, Vector4.zero);
            int points = (CapSegments + 1) * 2;
            for (int i = 0; i < points; i++)
            {
                int end = i / (CapSegments + 1);
                float angle = start + Mathf.PI * end + Mathf.PI * (i % (CapSegments + 1)) / CapSegments;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var arc = end == 0 ? centre + half : centre - half;
                vh.AddVert(arc + direction * radius, solid, Vector4.zero);
                vh.AddVert(arc + direction * (radius + Feather), clear, Vector4.zero);
            }
            for (int i = 0; i < points; i++)
            {
                int inner = 1 + i * 2, next = 1 + (i + 1) % points * 2;
                vh.AddTriangle(0, inner, next);
                vh.AddTriangle(inner, inner + 1, next + 1);
                vh.AddTriangle(inner, next + 1, next);
            }
        }
    }
}
