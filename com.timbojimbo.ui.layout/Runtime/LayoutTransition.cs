using System;
using TimboJimbo.Core;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// How a node moves in a view transition: after <see cref="Delay"/> seconds, over <see cref="Duration"/>
    /// seconds with <see cref="Ease"/>, the way CSS animation-delay, animation-duration and animation-timing-function
    /// shape a view transition's animations. Position and size animate together, from wherever the node is shown
    /// when the move starts. A transition with no duration and no delay applies at once.
    /// </summary>
    [Serializable]
    public struct LayoutTransition
    {
        [Min(0f)] public float Duration;
        public EaseType Ease;
        [Min(0f)] public float Delay;

        public static LayoutTransition None => default;

        public static LayoutTransition Over(float duration, EaseType ease = EaseType.OutCubic) => new() { Duration = duration, Ease = ease };

        /// <summary>The same transition starting <paramref name="delay"/> seconds later.</summary>
        public LayoutTransition After(float delay) => new() { Duration = Duration, Ease = Ease, Delay = delay };

        public bool IsAnimated => Duration > 0f || Delay > 0f;

        /// <summary>Delay plus duration: how long a move takes from start to end.</summary>
        public float Total => Delay + Duration;
    }
}
