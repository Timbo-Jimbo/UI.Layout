using System;
using System.Collections.Generic;
using TimboJimbo.UI.Layout;
using UnityEngine;

namespace TimboJimboTests.UI.Layout.PlayMode
{
    /// <summary>Leaf content for tests: a fixed size, or a width-dependent one, recording the widths it was asked for.</summary>
    public sealed class TestContent : MonoBehaviour, ILayoutMeasurable
    {
        public Vector2 Size = new(100f, 10f);
        public float Min;
        public Func<float, Vector2> Measurer;
        public readonly List<float> Widths = new();

        public Vector2 Measure(float availableWidth)
        {
            Widths.Add(availableWidth);
            return Measurer != null ? Measurer(availableWidth) : Size;
        }

        public float MinWidth => Min;
    }
}
