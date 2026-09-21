using System;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>How a node is sized on one axis; the Clay sizing model.</summary>
    public enum SizingMode
    {
        /// <summary>Size to the children, or to the content of a leaf, between Min and Max.</summary>
        Fit,
        /// <summary>Fill the space the parent has left, shared with sibling Grow nodes, between Min and Max. Across the flow, fill the parent's inner size.</summary>
        Grow,
        /// <summary>An exact size (Value).</summary>
        Fixed,
        /// <summary>A fraction (Value, 0..1) of the parent's inner size, padding and gaps excluded.</summary>
        Percent,
    }

    /// <summary>A node's sizing on one axis: the mode, the Fixed size or Percent fraction, and the Fit/Grow bounds (a Max of 0 is unbounded).</summary>
    [Serializable]
    public struct Sizing : IEquatable<Sizing>
    {
        public SizingMode Mode;
        public float Value;
        public float Min;
        public float Max;

        public static Sizing Fit(float min = 0f, float max = 0f) => new() { Mode = SizingMode.Fit, Min = min, Max = max };
        public static Sizing Grow(float min = 0f, float max = 0f) => new() { Mode = SizingMode.Grow, Min = min, Max = max };
        public static Sizing Fixed(float size) => new() { Mode = SizingMode.Fixed, Value = size };
        public static Sizing Percent(float fraction) => new() { Mode = SizingMode.Percent, Value = fraction };

        public bool HasMax => Max > 0f;

        /// <summary>Clamps a Fit or Grow size to the bounds.</summary>
        public float Clamp(float size)
        {
            size = Mathf.Max(size, Min);
            return HasMax ? Mathf.Min(size, Max) : size;
        }

        public bool Equals(Sizing other) => Mode == other.Mode && Value == other.Value && Min == other.Min && Max == other.Max;
        public override bool Equals(object obj) => obj is Sizing other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((int)Mode, Value, Min, Max);
        public static bool operator ==(Sizing a, Sizing b) => a.Equals(b);
        public static bool operator !=(Sizing a, Sizing b) => !a.Equals(b);

        public override string ToString() => Mode switch
        {
            SizingMode.Fixed => $"Fixed({Value})",
            SizingMode.Percent => $"Percent({Value:P0})",
            _ => $"{Mode}({Min}, {(HasMax ? Max.ToString() : "unbounded")})",
        };
    }
}
