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

        /// <summary>Held by a press: following its drag, or stopped where it was when the press caught it.</summary>
        Dragging,

        /// <summary>Let go of with a velocity that dies away at UIScrollView's normal deceleration rate.</summary>
        Gliding,

        /// <summary>On a spring: back to an end it ran past, to a wheel's target, or where ScrollTo or ScrollOffset sent it.</summary>
        Springing,
    }

    /// <summary>What a request made of a scroll container, not resolved yet, asks for.</summary>
    internal enum ScrollRequest
    {
        None,

        /// <summary>An offset, set through ScrollOffset.</summary>
        Offset,

        /// <summary>A descendant to bring into view, through ScrollTo.</summary>
        To,
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

        // iOS's rubber band: past an end by o, it is drawn past it by (1 - 1 / (o x 0.55 / d + 1)) x d, d its size that
        // way, so the further it is dragged the less it gives, never as far as its own size.
        public const float RubberBand = 0.55f;

        // Held still this long before letting go, it was not flicked (as DragToThrow).
        public const float StillFor = 0.08f;

        // How far a notch of a mouse wheel scrolls, in its units.
        public const float WheelStep = 60f;

        // UIScrollView's normal deceleration rate, 0.998 of its speed kept each millisecond, as a rate a second:
        // v(t) = v0 e^(-k t), k = 1000 ln(1 / 0.998), about 2.002.
        public static readonly float Deceleration = 1000f * Mathf.Log(1f / 0.998f);

        // The springs it goes on by itself, critically damped: back to an end it ran past (0.4 seconds), and to a
        // wheel's target (0.2 seconds, so a notch is quick but not a jump).
        public static readonly float BounceOmega = 2f * Mathf.PI / 0.4f;
        public static readonly float WheelOmega = 2f * Mathf.PI / 0.2f;

        // Which way it scrolls, as the last pass set it up: None when it does not (any more), its offset then 0.
        public ScrollAxis Axis;

        // The clip and the input component on its object, which the system added (hidden and never saved) or found
        // there. A RectMask2D of its own, not hidden, is left alone: it clips anyway.
        public RectMask2D Clip;
        public LayoutScroller Scroller;

        public ScrollPhase Phase;

        // Its offset as drawn (Value, the presentation) and how fast it is moving (its units a second, the same way);
        // while it springs, where to (Target), on what spring, after what delay, held by the change it springs for (if
        // any). Moving whenever it is not Idle. Axes it does not scroll stay at 0.
        public readonly Spring Offset = new(Rest);

        // While it glides, the axes still dying away; its other axes spring to the end they ran past on Offset's
        // spring, or have stopped (at their target, at rest).
        public bool GlideX;
        public bool GlideY;

        // Its size and how far it can scroll each way (how far its content runs past it; 0 on axes it does not
        // scroll), from the last pass that laid it out, and whether one has.
        public Vector2 Viewport;
        public Vector2 Range;
        public bool Measured;

        // Springing to a wheel's target, that target: the next notch adds to it rather than to where it is drawn.
        public bool Wheeling;
        public Vector2 WheelTarget;

        // The press holding it while it is Dragging, and whether it is following that press's drag: from its cursor
        // then (in its local units, y up) and its raw offset then (before the rubber band), and how fast the drawn
        // offset was moving the last time the pointer moved, smoothed over the last few moves.
        public PointerEventData Press;
        public bool Dragged;
        public Vector2 DragCursor;
        public Vector2 DragFrom;
        public Vector2 DragLast;
        public float LastMoved;
        public bool Sampled;
        public Vector2 DragVelocity;

        // A request not resolved yet (the latest wins): what it asks for, and the change it was made in (null for none),
        // which it springs for when that change's pass resolves it.
        public ScrollRequest Request;
        public Vector2 RequestOffset;
        public LayoutNode RequestTarget;
        public float RequestAnchor;
        public LayoutTransition RequestTransition;

        // The offset Scrolled was last raised with, and whether it is queued to be raised again.
        public Vector2 Raised;
        public bool Queued;

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

        /// <summary>Where a raw drag offset is drawn: as it is within range, rubber-banded past either end.</summary>
        public Vector2 Band(Vector2 raw) => new(
            Scrolls(0) ? Band(raw.x, Range.x, Viewport.x) : 0f,
            Scrolls(1) ? Band(raw.y, Range.y, Viewport.y) : 0f);

        /// <summary>The raw drag offset that is drawn at <paramref name="drawn"/>: the rubber band undone.</summary>
        public Vector2 Unband(Vector2 drawn) => new(
            Scrolls(0) ? Unband(drawn.x, Range.x, Viewport.x) : 0f,
            Scrolls(1) ? Unband(drawn.y, Range.y, Viewport.y) : 0f);

        /// <summary>
        /// Takes hold of it for a drag from <paramref name="cursor"/> (its local point, y up) at <paramref name="now"/>
        /// (unscaled seconds). The raw offset it starts from is where it is drawn with the rubber band undone, so a drag
        /// that catches it past an end carries on from there without a jump.
        /// </summary>
        public void BeginDrag(Vector2 cursor, float now)
        {
            Dragged = true;
            DragCursor = cursor;
            DragFrom = Unband(Offset.Value);
            DragLast = Offset.Value;
            LastMoved = now;
            Sampled = false;
            DragVelocity = Vector2.zero;
        }

        /// <summary>
        /// Follows the drag to <paramref name="cursor"/> at <paramref name="now"/>: the raw offset moves with the
        /// pointer exactly (dragging up scrolls down, dragging left scrolls right), and is drawn rubber-banded past the
        /// ends. Its velocity is the drawn offset's, over the time since the pointer last moved (which can be several
        /// frames, when the pointer reports less often than the game draws), smoothed over the last few moves.
        /// </summary>
        public void Drag(Vector2 cursor, float now)
        {
            var moved = cursor - DragCursor;
            var drawn = Band(DragFrom + new Vector2(-moved.x, moved.y));
            float dt = now - LastMoved;
            if (dt > 1e-4f)
            {
                var sample = (drawn - DragLast) / dt;
                DragVelocity = Sampled ? Vector2.Lerp(DragVelocity, sample, 0.5f) : sample;
                Sampled = true;
            }
            DragLast = drawn;
            LastMoved = now;
            Offset.Value = drawn;
            Offset.Velocity = DragVelocity;
        }

        /// <summary>How fast it was going as its press let go at <paramref name="now"/>: nothing if it was not dragged, or held still first.</summary>
        public Vector2 ReleaseVelocity(float now) => Dragged && now - LastMoved <= StillFor ? OnAxes(DragVelocity) : Vector2.zero;

        /// <summary>
        /// Moves it <paramref name="dt"/> seconds on while it glides or springs. True once it has come to rest (the
        /// caller puts it at its target, Idle). Springing, it goes on its spring (after any delay). Gliding, each axis
        /// still gliding advances by exactly v0 (1 - e^(-k dt)) / k as its speed dies away as e^(-k dt); one that runs
        /// past an end springs back to it from then on, keeping its velocity (running on into the overshoot, as iOS
        /// bounces), and one slower than the stop speed stops where it is; the rest spring to their ends. Once no axis
        /// is gliding it is Springing.
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
                        settled = false;
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

        // One axis of Band: past an end by `over`, drawn past it by (1 - 1 / (over x 0.55 / size + 1)) x size. With no
        // size it does not give at all.
        private static float Band(float raw, float range, float size)
        {
            if (raw >= 0f && raw <= range) return raw;
            float end = raw < 0f ? 0f : range;
            if (size <= 0f) return end;
            float over = Mathf.Abs(raw - end);
            float drawn = (1f - 1f / (over * RubberBand / size + 1f)) * size;
            return end + Mathf.Sign(raw - end) * drawn;
        }

        // One axis of Unband, Band solved for how far past the end it was dragged: size / 0.55 x b / (size - b) for b
        // drawn past it. A spring can carry it further past an end than a drag could draw it (which never reaches its
        // size), so b is kept just short of that.
        private static float Unband(float drawn, float range, float size)
        {
            if (drawn >= 0f && drawn <= range) return drawn;
            float end = drawn < 0f ? 0f : range;
            if (size <= 0f) return end;
            float over = Mathf.Min(Mathf.Abs(drawn - end), size * 0.99f);
            return end + Mathf.Sign(drawn - end) * (size / RubberBand * over / (size - over));
        }
    }
}
