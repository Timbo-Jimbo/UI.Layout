using System;
using TimboJimbo.Core;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// How a node moves when a pass gives it a different rect: over <see cref="Duration"/> seconds with
    /// <see cref="Ease"/>, or instantly when the duration is 0. Position and size animate together, from
    /// wherever the node is at that moment, so a pass in the middle of a move retargets smoothly. Plays in
    /// play mode only; edit mode commits instantly.
    /// </summary>
    [Serializable]
    public struct LayoutTransition
    {
        [Min(0f)] public float Duration;
        public EaseType Ease;

        public static LayoutTransition None => default;

        public static LayoutTransition Over(float duration, EaseType ease = EaseType.OutCubic) => new() { Duration = duration, Ease = ease };

        public bool IsAnimated => Duration > 0f;
    }
}
