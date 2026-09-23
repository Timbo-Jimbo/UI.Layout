using System;
using TimboJimbo.Core;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// How a node moves in a view transition: after <see cref="Delay"/> seconds, over <see cref="Duration"/>
    /// seconds with <see cref="Ease"/>, the way CSS animation-delay, animation-duration and animation-timing-function
    /// shape a view transition's animations, travelling the way its <see cref="Motion"/> says (a straight line when
    /// there is none). Position and size animate together, from wherever the node is shown when the move starts.
    /// <see cref="Instant"/> (no duration and no delay) applies at once.
    /// </summary>
    [Serializable]
    public struct LayoutTransition
    {
        [Min(0f)] public float Duration;
        public EaseType Ease;
        [Min(0f)] public float Delay;
        /// <summary>
        /// How the move travels; null is a straight line. Set in code; a node keeps its own in a serialized
        /// reference beside its transition, since the struct itself is saved by value.
        /// </summary>
        [NonSerialized] public ITransitionMotion Motion;

        /// <summary>No duration and no delay: the change applies at once.</summary>
        public static LayoutTransition Instant => default;

        public static LayoutTransition Over(float duration, EaseType ease = EaseType.OutCubic) => new() { Duration = duration, Ease = ease };

        /// <summary>The same transition starting <paramref name="delay"/> seconds later.</summary>
        public LayoutTransition After(float delay)
        {
            var t = this;
            t.Delay = delay;
            return t;
        }

        /// <summary>The same transition travelling the way <paramref name="motion"/> says (null for a straight line).</summary>
        public LayoutTransition With(ITransitionMotion motion)
        {
            var t = this;
            t.Motion = motion;
            return t;
        }

        /// <summary>The same transition along an arc bent by <paramref name="curvature"/>: <c>With(new ArcMotion(curvature))</c>.</summary>
        public LayoutTransition Curved(float curvature) => With(curvature > 0f ? new ArcMotion(curvature) : null);

        public bool IsAnimated => Duration > 0f || Delay > 0f;

        /// <summary>Delay plus duration: how long a move takes from start to end.</summary>
        public float Total => Delay + Duration;

        /// <summary>The frame of a move at <paramref name="input"/>: its motion's, or the straight line's. A move that goes nowhere is never shaped.</summary>
        internal MotionFrame Evaluate(in MotionInput input) =>
            Motion != null && !input.IsStill ? Motion.Evaluate(input) : MotionFrame.Straight(input);
    }
}
