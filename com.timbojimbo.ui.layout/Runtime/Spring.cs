using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// One of a node's animated values (its position, size, opacity or scale) on a damped spring, as SwiftUI moves
    /// things: where it is drawn and how fast it is moving there (Core Animation's presentation), and where it is
    /// going (the model). Position and size use both components; opacity and scale use x alone, y staying 0.
    /// It is stepped exactly, in closed form from where it is and how fast, so it plays the same at any frame rate and
    /// can be given somewhere new to go at any moment, carrying on at the velocity it has.
    /// </summary>
    internal sealed class Spring
    {
        // Where it is drawn, and how fast it is moving there, in its units a second.
        public Vector2 Value;
        public Vector2 Velocity;

        // Where it is going.
        public Vector2 Target;

        // The spring it goes on: its natural angular frequency (radians a second) and its damping ratio (1 settles
        // without overshooting, less swings past).
        public float Omega;
        public float Zeta;

        // Seconds it still waits before setting off, holding where it is and the velocity it has, unstepped.
        public float Delay;

        // Whether it is on its way (waiting out its delay included).
        public bool Moving;

        // The frame it set off from rest in: it is not stepped in that frame, so the first frame drawn shows where it
        // was and the motion is continuous from there.
        public int SetOff;

        // The change that set it moving, which waits for it to come to rest or be taken over (null for none, as a
        // fling).
        public LayoutTransition Transition;

        // How near its target it has to be (in its units), and how slow (in its units a radian of its swing, so
        // omega times this a second), to count as there.
        private readonly float _rest;

        public Spring(float rest)
        {
            _rest = rest;
        }

        /// <summary>
        /// The spring of <paramref name="animation"/>, as SwiftUI gives one: from its perceptual duration, about how
        /// long it takes to settle with no bounce, and its bounce, 0 for none (critically damped) and more the further
        /// it overshoots. A duration of 0 is no spring at all: omega is infinite, and <see cref="Step"/> puts it where it
        /// is going once its delay is up. Whatever sets a spring moving gives it its omega, so none is left infinite.
        /// </summary>
        public static void Parameters(LayoutAnimation animation, out float omega, out float zeta)
        {
            omega = animation.Duration > 0f ? 2f * Mathf.PI / Mathf.Max(animation.Duration, 0.01f) : float.PositiveInfinity;
            zeta = 1f - Mathf.Clamp(animation.Bounce, 0f, 0.9f);
        }

        /// <summary>
        /// Moves it <paramref name="dt"/> seconds on: the rest of its delay first, if it is waiting, then along its
        /// spring for what is left. True once it is within its rest distance of its target on every component and
        /// slower than its rest speed, when it counts as there (the caller snaps and stops it); with no duration (an
        /// infinite omega), as soon as its delay is up.
        /// </summary>
        public bool Step(float dt)
        {
            if (Delay > 0f)
            {
                if (dt <= Delay)
                {
                    Delay -= dt;
                    return false;
                }
                dt -= Delay;
                Delay = 0f;
            }

            if (float.IsPositiveInfinity(Omega))
            {
                Value = Target;
                Velocity = Vector2.zero;
                return true;
            }

            float x = Value.x - Target.x, vx = Velocity.x;
            float y = Value.y - Target.y, vy = Velocity.y;
            Evolve(Omega, Zeta, dt, ref x, ref vx);
            Evolve(Omega, Zeta, dt, ref y, ref vy);
            Value = new Vector2(Target.x + x, Target.y + y);
            Velocity = new Vector2(vx, vy);

            float slow = _rest * Omega;
            return Mathf.Abs(x) < _rest && Mathf.Abs(y) < _rest && Mathf.Abs(vx) < slow && Mathf.Abs(vy) < slow;
        }

        // A damped spring `t` seconds after it is `x` from where it rests moving at `v`: how far it is from there and
        // how fast it is moving, worked out exactly. Critically damped (no bounce) and underdamped are different
        // curves; the damping ratio never goes below 0.1, so the underdamped one is always well defined. A scroll's
        // glide steps it one axis at a time (an axis springing back from an end while the other still glides).
        internal static void Evolve(float omega, float zeta, float t, ref float x, ref float v)
        {
            float x0 = x, v0 = v;
            if (zeta >= 0.9999f)
            {
                float decay = Mathf.Exp(-omega * t);
                float b = v0 + omega * x0;
                x = (x0 + b * t) * decay;
                v = (b - omega * (x0 + b * t)) * decay;
                return;
            }
            float damped = omega * Mathf.Sqrt(1f - zeta * zeta);
            float envelope = Mathf.Exp(-zeta * omega * t);
            float cos = Mathf.Cos(damped * t), sin = Mathf.Sin(damped * t);
            float c = (v0 + zeta * omega * x0) / damped;
            x = envelope * (x0 * cos + c * sin);
            v = envelope * ((c * damped - zeta * omega * x0) * cos - (zeta * omega * c + x0 * damped) * sin);
        }
    }
}
