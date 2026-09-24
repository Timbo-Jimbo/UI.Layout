using System;
using TimboJimbo.Core;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// How a node moves in a view transition: after <see cref="Delay"/> seconds, over <see cref="Duration"/>
    /// seconds, the way CSS animation-delay and animation-duration time a view transition's animations, travelling,
    /// fading and scaling the way its <see cref="Motion"/> says (a straight slide easing out when it has none).
    /// Position and size animate together, from wherever the node is shown when the move starts.
    /// <see cref="Instant"/> (no duration and no delay) applies at once.
    /// </summary>
    [Serializable]
    public struct LayoutTransition
    {
        [Min(0f)] public float Duration;
        [Min(0f)] public float Delay;
        /// <summary>
        /// How the move travels, fades and scales; null leaves it to whatever this transition is combined with (a
        /// node's own timing with the transition's motion), and a straight slide easing out when nothing says. Set in
        /// code; a node keeps its own beside its timing, which is saved on its own.
        /// </summary>
        [NonSerialized] public LayoutMotion? Motion;

        /// <summary>No duration and no delay: the change applies at once.</summary>
        public static LayoutTransition Instant => default;

        /// <summary>A straight slide over <paramref name="duration"/> seconds on <paramref name="ease"/>.</summary>
        public static LayoutTransition Over(float duration, EaseType ease = EaseType.OutCubic) =>
            new() { Duration = duration, Motion = LayoutMotion.Slide(ease) };

        /// <summary>The same transition starting <paramref name="delay"/> seconds later.</summary>
        public LayoutTransition After(float delay)
        {
            var t = this;
            t.Delay = delay;
            return t;
        }

        /// <summary>The same transition travelling the way <paramref name="motion"/> says (null leaves the motion unsaid).</summary>
        public LayoutTransition With(LayoutMotion? motion)
        {
            var t = this;
            t.Motion = motion;
            return t;
        }

        /// <summary>The same transition along a path bent by <paramref name="curvature"/>.</summary>
        public LayoutTransition Curved(float curvature) => With(Resolved.Curved(curvature));

        /// <summary>The same transition with its motion mirrored (see <see cref="LayoutMotion.Mirrored"/>): an entrance undone.</summary>
        internal LayoutTransition Mirrored() => With(Resolved.Mirrored());

        public bool IsAnimated => Duration > 0f || Delay > 0f;

        /// <summary>Delay plus duration: how long a move takes from start to end.</summary>
        public float Total => Delay + Duration;

        // The motion, or the straight slide easing out when there is none.
        private LayoutMotion Resolved => Motion ?? LayoutMotion.Slide();

        /// <summary>The frame of a move at <paramref name="progress"/> (0 to 1 through the duration).</summary>
        internal MotionFrame Evaluate(Vector2 fromCentre, Vector2 toCentre, Vector2 fromSize, Vector2 toSize, float progress, bool? acrossFirst = null) =>
            Resolved.Evaluate(fromCentre, toCentre, fromSize, toSize, progress, acrossFirst);

        /// <summary>How far along a move is at <paramref name="progress"/>, through the motion's eases: what a fade in place follows.</summary>
        internal float Eased(float progress) => Resolved.Progress(progress);
    }
}
