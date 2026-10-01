using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimboJimbo.UI.Layout
{
    public static partial class LayoutSystem
    {
        // ── Values ───────────────────────────────────────────────────────────────
        //
        // A value something draws itself (a colour a variant sets, a corner radius) moves with a change as a node's place
        // does: on a spring, from where it is drawn and at the velocity it has, held by the change until it comes to rest,
        // and put where it is going when the change is skipped. Its owner is handed it to write each frame it moves. Only
        // values on their way are kept.

        // How near its target a value has to be to count as there: an opacity's, small in any unit a value is likely to be
        // in (a colour's 0 to 1, pixels).
        private const float ValueRest = 0.002f;

        private static readonly Dictionary<(Object Owner, string Key), MovingValue> s_values = new();
        private static readonly List<(Object, string)> s_landed = new();
        private static readonly List<MovingValue> s_stepped = new();

        // A value's four components move as two springs, x and y then z and w, on the same spring, each held by the change
        // as a node's springs are.
        private sealed class MovingValue
        {
            public readonly Spring Low = new(ValueRest);
            public readonly Spring High = new(ValueRest);
            public Object Owner;
            public Action<Vector4> Write;

            public bool Moving => Low.Moving || High.Moving;
            public Vector4 Drawn => new(Low.Value.x, Low.Value.y, High.Value.x, High.Value.y);
        }

        /// <summary>
        /// Moves a value <paramref name="owner"/> draws itself (a colour, a corner radius: up to four components) to
        /// <paramref name="to"/> as a node's place moves. Inside <see cref="Animate(Action, string[])"/>'s update, it sets
        /// off on <paramref name="animation"/>'s spring after its delay, from where it is drawn
        /// (<paramref name="from"/>, when it is at rest) at the velocity it has; the change waits for it to come to rest
        /// (<see cref="LayoutTransition.Finished"/>), and skipping the change puts it there. Outside one, a value at rest
        /// goes there at once, and one on its way heads there instead, keeping its speed and the change it moves for.
        /// <paramref name="key"/> tells an owner's values apart. <paramref name="write"/> is handed the value as drawn
        /// each frame it moves, and where it lands; a value whose owner is destroyed is let go of, and the change it moved
        /// for does not complete.
        /// </summary>
        public static void AnimateValue(Object owner, string key, Vector4 from, Vector4 to, LayoutAnimation animation, Action<Vector4> write)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (write == null) throw new ArgumentNullException(nameof(write));

            var id = (owner, key);
            var transition = Application.isPlaying ? s_current : null;
            if (!s_values.TryGetValue(id, out var value))
            {
                // At rest, it is there at once without a change, or with nothing to move.
                if (transition == null || from == to)
                {
                    write(to);
                    return;
                }
                value = new MovingValue { Owner = owner };
                value.Low.Value = new Vector2(from.x, from.y);
                value.High.Value = new Vector2(from.z, from.w);
                s_values.Add(id, value);
                Hook();
            }

            value.Write = write;
            RetargetValue(value.Low, new Vector2(to.x, to.y), animation, transition);
            RetargetValue(value.High, new Vector2(to.z, to.w), animation, transition);
            if (!value.Moving)
            {
                s_values.Remove(id);
                write(to);
            }
        }

        // A value's spring given somewhere to go, as Retarget gives a node's: on an animation with no duration and no
        // delay, put there at once.
        private static void RetargetValue(Spring spring, Vector2 target, LayoutAnimation animation, LayoutTransition transition)
        {
            spring.Target = target;
            if (transition == null)
            {
                if (!spring.Moving)
                    Stop(spring);
                return;
            }
            if (!spring.Moving && spring.Value == target)
                return;
            if (animation.AtOnce)
            {
                Snap(spring, target, transition);
                return;
            }

            Spring.Parameters(animation, out spring.Omega, out spring.Zeta);
            spring.Delay = Mathf.Max(0f, animation.Delay);
            Hold(spring, transition);
        }

        // Steps every value on its way, as the frame steps nodes, and hands each to its owner as drawn: one come to rest
        // where it landed, then forgotten. One whose owner has gone is let go of. The owners write once the values are no
        // longer being walked, as a write may set off another value.
        private static void StepValues(bool playing, bool step, float dt)
        {
            if (s_values.Count == 0) return;

            s_stepped.Clear();
            foreach (var pair in s_values)
            {
                var value = pair.Value;
                if (value.Owner == null)
                {
                    Release(value.Low);
                    Release(value.High);
                    s_landed.Add(pair.Key);
                    continue;
                }
                Advance(value.Low, playing, step, dt);
                Advance(value.High, playing, step, dt);
                if (!value.Moving)
                    s_landed.Add(pair.Key);
                s_stepped.Add(value);
            }
            foreach (var id in s_landed)
                s_values.Remove(id);
            s_landed.Clear();

            foreach (var value in s_stepped)
                value.Write(value.Drawn);
            s_stepped.Clear();
        }

        // Puts every value the transition is moving where it is going, as Skip does a node's; each is written there in the
        // next frame.
        private static void SkipValues(LayoutTransition transition)
        {
            foreach (var value in s_values.Values)
            {
                StopIfHeld(value.Low, transition);
                StopIfHeld(value.High, transition);
            }
        }
    }
}
