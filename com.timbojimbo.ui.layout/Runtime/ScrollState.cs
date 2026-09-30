using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TimboJimbo.UI.Layout
{
    /// <summary>What a scroll container's offset is doing, as UIScrollView's tracking, dragging and decelerating.</summary>
    internal enum ScrollPhase
    {
        /// <summary>At rest, within range.</summary>
        Idle,

        /// <summary>Held by a press: moved by its share of the press's drag, or stopped where it was when the press caught it.</summary>
        Dragging,

        /// <summary>Let go of with a velocity that dies away at UIScrollView's normal deceleration rate.</summary>
        Gliding,

        /// <summary>
        /// On a spring: back to an end it ran past, to a wheel's target, where ScrollTo, ScrollIntoView or ScrollOffset
        /// sent it, or on to its end as its content grew (ScrollAnchor End).
        /// </summary>
        Springing,
    }

    /// <summary>What a request made of a scroll container, not resolved yet, asks for.</summary>
    internal enum ScrollRequest
    {
        None,

        /// <summary>An offset, set through ScrollOffset.</summary>
        Offset,

        /// <summary>A descendant to line up at an anchor, through ScrollTo.</summary>
        To,

        /// <summary>A descendant to bring into view only as far as it needs, through ScrollIntoView.</summary>
        IntoView,
    }

    /// <summary>
    /// What the layout system keeps for a scroll container (a node whose Scroll is not None): its scroll offset, drawn
    /// with a velocity, as UIScrollView's contentOffset; what moves it (a press, a glide, a spring); how far it can go;
    /// and the requests made of it that wait for a pass. The offset is x right and y down, in the container's own units,
    /// 0 at the start; it only moves where the container's LayoutNode children are drawn, never their layout. Made the
    /// first time a node scrolls and kept while its NodeState is, so a node that stops and starts scrolling again
    /// allocates nothing more.
    /// </summary>
    internal sealed class ScrollState
    {
        // How near where it springs to it has to be (in its units), and how slow (that times its spring's omega a
        // second), to count as there: as a node's position.
        public const float Rest = 0.5f;

        // A glide stops once it is this slow on an axis, in units a second.
        public const float StopSpeed = 5f;

        // Held still this long before letting go, a drag was not flicked (as DragToThrow).
        public const float StillFor = 0.08f;

        // How far a notch of a mouse wheel scrolls, in its units.
        public const float WheelStep = 60f;

        // Let go faster than this (its units a second), a container that snaps (ScrollSnap) moves on to the next page or
        // child the way it was flicked, rather than to the nearest.
        public const float SnapFlickSpeed = 300f;

        // UIScrollView's normal deceleration rate, 0.998 of its speed kept each millisecond, as a rate a second:
        // v(t) = v0 e^(-k t), k = 1000 ln(1 / 0.998), about 2.002.
        public static readonly float Deceleration = 1000f * Mathf.Log(1f / 0.998f);

        // The springs it goes on by itself, critically damped: back to an end it ran past (0.4 seconds), and to a
        // wheel's target (0.2 seconds, so a notch is quick but not a jump).
        public static readonly float BounceOmega = 2f * Mathf.PI / 0.4f;
        public static readonly float WheelOmega = 2f * Mathf.PI / 0.2f;

        // Which way it scrolls, as the last pass set it up: None when it does not (any more), its offset then 0.
        public ScrollAxis Axis;

        // The clip on its object, which the system added (hidden and never saved) or found there. A RectMask2D of its
        // own, not hidden, is left alone: it clips anyway. (The LayoutScroller that takes the pointer for it is kept on
        // its NodeState, as a drag owner's node has one too.)
        public RectMask2D Clip;

        public ScrollPhase Phase;

        // Its offset as drawn (Value, the presentation) and how fast it is moving (its units a second, the same way);
        // while it springs, where to (Target), on what spring, after what delay, held by the change it springs for (if
        // any). Moving whenever it is not Idle. Axes it does not scroll stay at 0.
        public readonly Spring Offset = new(Rest);

        // While it glides, the axes still dying away; its other axes spring to the end they ran past on Offset's
        // spring, or have stopped (at their target, at rest).
        public bool GlideX;
        public bool GlideY;

        // Whether a glide that runs into an end on each axis stops there for something above it on that axis to take its
        // speed on, noted as it is let go of or handed a glide (something above it could). Step then stops it at that end
        // and records the speed it ran into it with (Impact, its units a second, the way its offset was going, and
        // Impacted) rather than bouncing, for the system to hand on once the frame is laid out, bouncing it there after
        // all if nothing takes it.
        public bool PassX;
        public bool PassY;
        public bool Impacted;
        public Vector2 Impact;

        // Its size and how far it can scroll each way (how far its content runs past it; 0 on axes it does not
        // scroll), from the last pass that laid it out, and whether one has.
        public Vector2 Viewport;
        public Vector2 Range;
        public bool Measured;

        // Springing to a wheel's target, that target: the next notch adds to it rather than to where it is drawn.
        public bool Wheeling;
        public Vector2 WheelTarget;

        // The press holding it while it is Dragging, and its raw offset meanwhile: where the shares of that press's drag
        // have taken it before the rubber band (its units, as Offset), drawn banded past the ends. Taken from where it is
        // drawn as the press takes hold of it, the rubber band undone, so a drag that catches it past an end carries on
        // from there without a jump.
        public PointerEventData Press;
        public Vector2 Raw;

        // A request not resolved yet (the latest wins): what it asks for (an offset, or a descendant, with ScrollTo's
        // anchor), and the change it was made in (null for none), which it springs for when that change's pass resolves
        // it.
        public ScrollRequest Request;
        public Vector2 RequestOffset;
        public LayoutNode RequestTarget;
        public float RequestAnchor;
        public LayoutTransition RequestTransition;

        // The offset Scrolled was last raised with, and whether it is queued to be raised again.
        public Vector2 Raised;
        public bool Queued;

        // Its scroll indicators, added the first time each shows: x's along its bottom edge, y's along its right edge.
        // For each axis, how far its indicator is faded in (0 to 1), how long that axis has been still, and where it was
        // scrolled to when last drawn, which tells it moved.
        public LayoutScrollIndicator IndicatorX;
        public LayoutScrollIndicator IndicatorY;
        public Vector2 IndicatorShown;
        public Vector2 IndicatorStill;
        public Vector2 IndicatorAt;

        /// <summary>Whether it scrolls along <paramref name="axis"/> (0 is x, 1 is y).</summary>
        public bool Scrolls(int axis) => axis == 0
            ? Axis == ScrollAxis.Horizontal || Axis == ScrollAxis.Both
            : Axis == ScrollAxis.Vertical || Axis == ScrollAxis.Both;

        /// <summary><paramref name="vector"/> on the axes it scrolls, 0 on the others.</summary>
        public Vector2 OnAxes(Vector2 vector) => new(Scrolls(0) ? vector.x : 0f, Scrolls(1) ? vector.y : 0f);

        /// <summary><paramref name="offset"/> within range: from 0 to its range on the axes it scrolls, 0 on the others.</summary>
        public Vector2 Clamp(Vector2 offset) => new(
            Scrolls(0) ? Mathf.Clamp(offset.x, 0f, Range.x) : 0f,
            Scrolls(1) ? Mathf.Clamp(offset.y, 0f, Range.y) : 0f);

        /// <summary>
        /// Where a raw drag offset is drawn: as it is within range, and past either end rubber-banded as iOS does
        /// (<see cref="LayoutSystem.RubberBand"/>), its size that way the most it ever gives.
        /// </summary>
        public Vector2 Band(Vector2 raw) => new(
            Scrolls(0) ? Band(raw.x, Range.x, Viewport.x) : 0f,
            Scrolls(1) ? Band(raw.y, Range.y, Viewport.y) : 0f);

        /// <summary>The raw drag offset that is drawn at <paramref name="drawn"/>: the rubber band undone.</summary>
        public Vector2 Unband(Vector2 drawn) => new(
            Scrolls(0) ? Unband(drawn.x, Range.x, Viewport.x) : 0f,
            Scrolls(1) ? Unband(drawn.y, Range.y, Viewport.y) : 0f);

        /// <summary>
        /// Whether it is at its end on <paramref name="axis"/> (0 is x, 1 is y) the way <paramref name="way"/> goes (by
        /// its sign, as its offset would): at its start going back, at its range going on, within its rest distance. Going
        /// nowhere, it is at its end that way.
        /// </summary>
        public bool AtEnd(int axis, float way) =>
            way > 0f ? Offset.Value[axis] >= Range[axis] - Rest : way == 0f || Offset.Value[axis] <= Rest;

        /// <summary>
        /// Whether it stays at its end on <paramref name="axis"/> (0 is x, 1 is y) as its range grows, for a ScrollAnchor
        /// of End: not laid out yet (it starts there), at rest within its rest distance of its range, or springing to
        /// there. Not while a press holds it or a glide carries it: a flick sticks once it comes to rest at the end.
        /// </summary>
        public bool StaysAtEnd(int axis) => !Measured || Phase switch
        {
            ScrollPhase.Idle => AtEnd(axis, 1f),
            ScrollPhase.Springing => Offset.Target[axis] >= Range[axis] - Rest,
            _ => false,
        };

        /// <summary>
        /// Held, takes as much of <paramref name="move"/> (its units, the way its offset moves) as brings a raw offset
        /// stretched past an end back to that end, and no further. Returns what it took.
        /// </summary>
        public Vector2 Relax(Vector2 move)
        {
            var taken = Vector2.zero;
            for (int axis = 0; axis < 2; axis++)
            {
                if (!Scrolls(axis)) continue;
                float raw = Raw[axis], m = move[axis];
                if (raw < 0f && m > 0f)
                    taken[axis] = Mathf.Min(m, -raw);
                else if (raw > Range[axis] && m < 0f)
                    taken[axis] = Mathf.Max(m, Range[axis] - raw);
            }
            MoveRaw(taken);
            return taken;
        }

        /// <summary>
        /// Held, takes as much of <paramref name="move"/> as keeps its raw offset within range: from past an end back
        /// towards range it goes as far as the other end, and it never goes further past the end it is past. Returns what
        /// it took.
        /// </summary>
        public Vector2 Take(Vector2 move)
        {
            var taken = Vector2.zero;
            for (int axis = 0; axis < 2; axis++)
            {
                if (!Scrolls(axis)) continue;
                float raw = Raw[axis], m = move[axis];
                float to = m > 0f ? Mathf.Min(raw + m, Mathf.Max(raw, Range[axis])) : Mathf.Max(raw + m, Mathf.Min(raw, 0f));
                taken[axis] = to - raw;
            }
            MoveRaw(taken);
            return taken;
        }

        /// <summary>
        /// Held, takes all of <paramref name="move"/> on the axes it scrolls, past an end as far as it goes (drawn
        /// rubber-banded). Returns what it took.
        /// </summary>
        public Vector2 Stretch(Vector2 move)
        {
            var taken = OnAxes(move);
            MoveRaw(taken);
            return taken;
        }

        /// <summary>
        /// How far it is drawn moving for each unit its raw offset moves, on each axis it scrolls: 1 within range, and past
        /// an end the rubber band's slope there, so a velocity of its raw offset times this is its drawn offset's.
        /// </summary>
        public Vector2 Slope() => new(
            Scrolls(0) ? Slope(Raw.x, Range.x, Viewport.x) : 0f,
            Scrolls(1) ? Slope(Raw.y, Range.y, Viewport.y) : 0f);

        /// <summary>
        /// Sets it gliding at <paramref name="velocity"/> (its units a second) on the axes where that is not 0, from where
        /// it is drawn, to a stop as a flick's glide goes; its other axes carry on as they were (at rest, or gliding). It
        /// is Gliding after, and the caller sets its offset moving.
        /// </summary>
        public void Glide(Vector2 velocity)
        {
            if (Phase != ScrollPhase.Gliding)
                SetOffFromRest();
            for (int axis = 0; axis < 2; axis++)
            {
                if (velocity[axis] == 0f) continue;
                Offset.Velocity[axis] = velocity[axis];
                Offset.Target[axis] = Offset.Value[axis];
                SetGliding(axis, true);
            }
            Phase = ScrollPhase.Gliding;
        }

        /// <summary>
        /// Sends it on past the end it is at, at <paramref name="velocity"/> (its units a second) on the axes where that is
        /// not 0, springing back to that end, as a glide that runs into an end with nothing to hand its speed to does; its
        /// other axes carry on as they were. At rest, it is Springing after, and the caller sets its offset moving.
        /// </summary>
        public void Bounce(Vector2 velocity)
        {
            if (Phase == ScrollPhase.Idle)
            {
                SetOffFromRest();
                Phase = ScrollPhase.Springing;
            }
            var end = Clamp(Offset.Value);
            for (int axis = 0; axis < 2; axis++)
            {
                if (velocity[axis] == 0f) continue;
                Offset.Velocity[axis] = velocity[axis];
                Offset.Target[axis] = end[axis];
                SetGliding(axis, false);
            }
        }

        // Where it is drawn becomes where it goes, still, on the spring it bounces back from an end on: what Glide and
        // Bounce set off from at rest.
        private void SetOffFromRest()
        {
            GlideX = GlideY = false;
            Wheeling = false;
            Offset.Target = Offset.Value;
            Offset.Velocity = Vector2.zero;
            Offset.Omega = BounceOmega;
            Offset.Zeta = 1f;
            Offset.Delay = 0f;
        }

        // The raw offset moves by what a share took, and it is drawn there, rubber-banded past the ends.
        private void MoveRaw(Vector2 taken)
        {
            if (taken == Vector2.zero) return;
            Raw += taken;
            Offset.Value = Band(Raw);
        }

        /// <summary>
        /// Moves it <paramref name="dt"/> seconds on while it glides or springs. True once it has come to rest (the
        /// caller puts it at its target, Idle). Springing, it goes on its spring (after any delay). Gliding, each axis
        /// still gliding advances by exactly v0 (1 - e^(-k dt)) / k as its speed dies away as e^(-k dt); one that runs
        /// past an end springs back to it from then on, keeping its velocity (running on into the overshoot, as iOS
        /// bounces), unless something above it could take its speed on that axis (PassX, PassY): then it stops at the
        /// end, and the speed it ran into it with is recorded (Impact) for the system to hand on. One slower than the stop
        /// speed stops where it is; the rest spring to their ends. Once no axis is gliding it is Springing.
        /// </summary>
        public bool Step(float dt)
        {
            if (!GlideX && !GlideY)
                return Offset.Step(dt);

            float decay = Mathf.Exp(-Deceleration * dt);
            float advance = (1f - decay) / Deceleration;
            var value = Offset.Value;
            var velocity = Offset.Velocity;
            var target = Offset.Target;
            bool settled = true;
            for (int axis = 0; axis < 2; axis++)
            {
                if (!Scrolls(axis)) continue;
                if (axis == 0 ? GlideX : GlideY)
                {
                    value[axis] += velocity[axis] * advance;
                    velocity[axis] *= decay;
                    float end = Mathf.Clamp(value[axis], 0f, Range[axis]);
                    if (end != value[axis])
                    {
                        target[axis] = end;
                        SetGliding(axis, false);
                        if (axis == 0 ? PassX : PassY)
                        {
                            // Stopped at the end, the rest of this step's motion lost, for its speed to go on up.
                            value[axis] = end;
                            Impact[axis] = velocity[axis];
                            velocity[axis] = 0f;
                            Impacted = true;
                        }
                        else
                        {
                            settled = false;
                        }
                    }
                    else if (Mathf.Abs(velocity[axis]) < StopSpeed)
                    {
                        velocity[axis] = 0f;
                        target[axis] = value[axis];
                        SetGliding(axis, false);
                    }
                    else
                    {
                        settled = false;
                    }
                }
                else
                {
                    float x = value[axis] - target[axis], v = velocity[axis];
                    Spring.Evolve(Offset.Omega, Offset.Zeta, dt, ref x, ref v);
                    value[axis] = target[axis] + x;
                    velocity[axis] = v;
                    if (Mathf.Abs(x) >= Rest || Mathf.Abs(v) >= Rest * Offset.Omega)
                        settled = false;
                }
            }
            Offset.Value = value;
            Offset.Velocity = velocity;
            Offset.Target = target;
            if (!GlideX && !GlideY)
                Phase = ScrollPhase.Springing;
            return settled;
        }

        private void SetGliding(int axis, bool gliding)
        {
            if (axis == 0) GlideX = gliding;
            else GlideY = gliding;
        }

        // One axis of Band: past an end, drawn past it by the rubber band with its size as the limit. With no size it does
        // not give at all.
        private static float Band(float raw, float range, float size)
        {
            if (raw >= 0f && raw <= range) return raw;
            float end = raw < 0f ? 0f : range;
            return end + LayoutSystem.RubberBand(raw - end, size);
        }

        // One axis of Slope: past an end by `over`, Band's slope there, 0.55 / (over x 0.55 / size + 1)^2, which dies away
        // as the band stretches. With no size it does not give at all.
        private static float Slope(float raw, float range, float size)
        {
            if (raw >= 0f && raw <= range) return 1f;
            if (size <= 0f) return 0f;
            float over = raw < 0f ? -raw : raw - range;
            float stretch = over * LayoutSystem.RubberBandCoefficient / size + 1f;
            return LayoutSystem.RubberBandCoefficient / (stretch * stretch);
        }

        // One axis of Unband: Band undone, the pull past the end that draws it where it is (RubberBandInverse).
        private static float Unband(float drawn, float range, float size)
        {
            if (drawn >= 0f && drawn <= range) return drawn;
            float end = drawn < 0f ? 0f : range;
            return end + LayoutSystem.RubberBandInverse(drawn - end, size);
        }
    }
}
