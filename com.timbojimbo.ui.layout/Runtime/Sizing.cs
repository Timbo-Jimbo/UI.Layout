using System;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// How a node is sized along one axis: fit to its content, grow to fill, a fixed size or a percentage of its
    /// parent's, as Clay's sizing is given. <see cref="Min"/> and <see cref="Max"/> bound fit, grow and percent sizes;
    /// a <see cref="Max"/> of 0 or less is no maximum.
    /// </summary>
    [Serializable]
    public struct Sizing
    {
        public SizingMode Mode;

        [Tooltip("Fixed: the size. Percent: the part (0 to 1) of the parent's size inside its padding (along the parent's direction, of what its padding and gaps leave; along the way a scroll container scrolls, of what it shows).")]
        public float Value;

        [Tooltip("The smallest it is sized to (fit, grow and percent).")]
        [Min(0f)] public float Min;

        [Tooltip("The largest it is sized to (fit, grow and percent); 0 or less for no maximum.")]
        [Min(0f)] public float Max;

        public static Sizing Fit(float min = 0f, float max = 0f) => new() { Mode = SizingMode.Fit, Min = min, Max = max };
        public static Sizing Grow(float min = 0f, float max = 0f) => new() { Mode = SizingMode.Grow, Min = min, Max = max };
        public static Sizing Fixed(float size) => new() { Mode = SizingMode.Fixed, Value = size };
        public static Sizing Percent(float part, float min = 0f, float max = 0f) => new() { Mode = SizingMode.Percent, Value = part, Min = min, Max = max };

        /// <summary>The largest it is sized to: its max, or infinity with none.</summary>
        public float MaxOrInfinity => Max > 0f ? Max : float.PositiveInfinity;

        /// <summary><paramref name="size"/> within <see cref="Min"/> and <see cref="Max"/>.</summary>
        public float Clamp(float size) => Mathf.Max(Min, Mathf.Min(size, MaxOrInfinity));
    }
}
