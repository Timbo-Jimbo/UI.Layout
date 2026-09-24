using System;
using TimboJimbo.Core;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// One half of a <see cref="LayoutMotion"/>: what happens on the way out of where the node was
    /// (<see cref="LayoutMotion.Depart"/>) or on the way into where it is going (<see cref="LayoutMotion.Arrive"/>).
    /// </summary>
    [Serializable]
    public struct MotionPhase
    {
        [Tooltip("Travel along the path in this phase. The phases that move share the path, half each when both do; one that does not holds still.")]
        public bool Move;
        [Tooltip("Fade out over this phase when departing, in when arriving.")]
        public bool Fade;
        [Tooltip("Shrink to nothing over this phase when departing, grow from nothing when arriving, around the centre. A fade alongside runs a little ahead of it, so it reads.")]
        public bool Scale;
        [Tooltip("The ease this phase moves, fades and scales with.")]
        public EaseType Ease;

        public MotionPhase(bool move, bool fade, bool scale, EaseType ease)
        {
            Move = move;
            Fade = fade;
            Scale = scale;
            Ease = ease;
        }
    }

    /// <summary>
    /// How something travels in a view transition, in two phases: <see cref="Depart"/> takes it out of where it was
    /// and <see cref="Arrive"/> brings it into where it is going, with an optional <see cref="Gap"/> between them.
    /// Each phase can move it along the path, fade it and scale it, with its own ease. <see cref="Midpoint"/> says
    /// where in the duration Depart ends. When both phases move they share the path half each, wherever the midpoint
    /// is, so with a midpoint of 0.25 the node is halfway there a quarter of the way through; a phase that does not
    /// move holds still and the other covers the whole path; when neither moves the node jumps across in the gap, a
    /// teleport. The path bends into an arc by <see cref="Curvature"/>. A plain slide is one phase: no departure
    /// (midpoint 0) and an arrival that moves (<see cref="Slide"/>). What rides inside a travelling node, and the two
    /// halves of a matched pair, change from their old state to their new one as the path is travelled: gradually
    /// while the node moves, at once where it jumps. A move that goes nowhere (a node entering or leaving in place) is
    /// not shaped; its fade follows the phases' eases. It is a plain struct: set its fields, or start from a preset.
    /// </summary>
    [Serializable]
    public struct LayoutMotion
    {
        [Tooltip("Where in the duration the departure ends and the gap begins: 0 has no departure, 1 no arrival.")]
        [Range(0f, 1f)] public float Midpoint;
        [Tooltip("The part of the duration spent between the phases.")]
        [Range(0f, 0.9f)] public float Gap;
        [Tooltip("How much the path bends: 0 a straight line; at 1 it bows about halfway towards the corner of the L that takes the shorter axis first. It fades out on paths close to an axis: none on a level or upright one, half at 15 degrees off, all from 30.")]
        [Range(0f, 1f)] public float Curvature;
        public MotionPhase Depart;
        public MotionPhase Arrive;

        /// <summary>A straight slide on <paramref name="ease"/>: no departure, an arrival that moves.</summary>
        public static LayoutMotion Slide(EaseType ease = EaseType.OutCubic) => new() { Arrive = new MotionPhase(true, false, false, ease) };

        /// <summary>Falls into place from where it came from, fading in as it goes: a popover opening out of its button.</summary>
        public static LayoutMotion Drop => new() { Arrive = new MotionPhase(true, true, false, EaseType.OutCubic) };

        /// <summary>Winds up and vanishes where it was, then pops out where it is going with an overshoot.</summary>
        public static LayoutMotion Pop => Teleport(0.1f, new MotionPhase(false, false, true, EaseType.InBack), new MotionPhase(false, false, true, EaseType.OutBack));

        /// <summary>Gently shrinks and fades out, and grows and fades back in with no pause.</summary>
        public static LayoutMotion Soft => Teleport(0f, new MotionPhase(false, true, true, EaseType.InCubic), new MotionPhase(false, true, true, EaseType.OutCubic));

        /// <summary>A quick fade and shrink, a noticeable pause, and a quick return.</summary>
        public static LayoutMotion Blink => Teleport(0.3f, new MotionPhase(false, true, true, EaseType.InQuad), new MotionPhase(false, true, true, EaseType.OutQuad));

        /// <summary>Gone and back almost at once, on steep curves.</summary>
        public static LayoutMotion Snap => Teleport(0.05f, new MotionPhase(false, false, true, EaseType.InExpo), new MotionPhase(false, false, true, EaseType.OutExpo));

        /// <summary>Winds up and vanishes, then lands with a springy wobble.</summary>
        public static LayoutMotion Wobble => Teleport(0.1f, new MotionPhase(false, false, true, EaseType.InBack), new MotionPhase(false, false, true, EaseType.OutElastic));

        /// <summary>The presets by name, for tools such as the inspector's.</summary>
        public static readonly (string Name, LayoutMotion Motion)[] Presets =
        {
            ("Slide", Slide()), ("Arc", Slide().Curved(0.5f)), ("Drop", Drop),
            ("Pop", Pop), ("Soft", Soft), ("Blink", Blink), ("Snap", Snap), ("Wobble", Wobble),
        };

        private static LayoutMotion Teleport(float gap, MotionPhase depart, MotionPhase arrive) =>
            new() { Midpoint = 0.5f, Gap = gap, Depart = depart, Arrive = arrive };

        /// <summary>The same motion along a path bent by <paramref name="curvature"/>.</summary>
        public LayoutMotion Curved(float curvature)
        {
            var motion = this;
            motion.Curvature = Mathf.Clamp01(curvature);
            return motion;
        }

        /// <summary>
        /// The motion undone, as a move of its own: what was the arrival departs, and what was the departure arrives,
        /// each where the other was in the duration and on its own ease. Played from where a node is to where it came
        /// from, it fades and scales out where the original faded and scaled in, and eases the way the original does
        /// in time rather than backwards (a slide that eases out still eases out); its path bends as any move setting
        /// off from there would. So it keeps pace with every other move in the same transition, such as a pair's
        /// halves going the same way. A node going back into its origin moves this way.
        /// </summary>
        internal LayoutMotion Mirrored() => new()
        {
            Midpoint = 1f - Midpoint,
            Gap = Gap,
            Curvature = Curvature,
            Depart = Arrive,
            Arrive = Depart,
        };

        // ── Evaluation ─────────────────────────────────────────────────────────

        // The timeline: Depart over [0, departEnd), the gap over [departEnd, arriveStart), Arrive over [arriveStart, 1].
        private void Timeline(out float departEnd, out float arriveStart)
        {
            float gap = Mathf.Clamp(Gap, 0f, 0.9f);
            departEnd = Mathf.Clamp01(Midpoint) * (1f - gap);
            arriveStart = departEnd + gap;
        }

        // How the path is shared: where the departure ends on it, and whether each phase travels.
        private void Shares(float departEnd, float arriveStart, out float departShare, out bool arriveMoves)
        {
            bool departMoves = Depart.Move && departEnd > 0f;
            arriveMoves = Arrive.Move && arriveStart < 1f;
            // The part of the path covered by the end of the departure: its share if it moves; otherwise nothing, or
            // everything when the arrival does not move either (the node has jumped across).
            departShare = departMoves ? (arriveMoves ? 0.5f : 1f) : (arriveMoves ? 0f : 1f);
        }

        /// <summary>The frame of a move from one rect to another at <paramref name="progress"/> (0 to 1, after the delay, before any ease).</summary>
        internal MotionFrame Evaluate(Vector2 fromCentre, Vector2 toCentre, Vector2 fromSize, Vector2 toSize, float progress, bool? acrossFirst = null)
        {
            bool still = (toCentre - fromCentre).sqrMagnitude < 1e-8f && (toSize - fromSize).sqrMagnitude < 1e-8f;
            if (still || progress >= 1f)
            {
                float t = progress >= 1f ? 1f : Progress(progress);
                return MotionFrame.Straight(fromCentre, toCentre, fromSize, toSize, t);
            }

            Timeline(out float departEnd, out float arriveStart);
            Shares(departEnd, arriveStart, out float departShare, out bool arriveMoves);
            float path, opacity = 1f, scale = 1f;
            if (progress < departEnd)
            {
                float local = progress / departEnd;
                float e = EaseUtility.Evaluate(local, Depart.Ease);
                path = Depart.Move ? departShare * e : 0f;
                if (Depart.Scale) scale = 1f - e;
                if (Depart.Fade) opacity = 1f - (Depart.Scale ? EaseUtility.Evaluate(Ahead(local), Depart.Ease) : e);
            }
            else if (progress < arriveStart)
            {
                path = departShare;
                // Between the phases the node is absent if either side takes it away.
                bool departs = departEnd > 0f, arrives = arriveStart < 1f;
                if ((departs && Depart.Scale) || (arrives && Arrive.Scale)) scale = 0f;
                if ((departs && Depart.Fade) || (arrives && Arrive.Fade)) opacity = 0f;
            }
            else
            {
                float local = (progress - arriveStart) / (1f - arriveStart);
                float e = EaseUtility.Evaluate(local, Arrive.Ease);
                path = arriveMoves ? departShare + (1f - departShare) * e : departShare;
                if (Arrive.Scale) scale = e;
                if (Arrive.Fade) opacity = Arrive.Scale ? EaseUtility.Evaluate(Ahead(local), Arrive.Ease) : e;
            }

            return new MotionFrame
            {
                Centre = PointAlongPath(fromCentre, toCentre, Curvature, path, acrossFirst),
                Size = Vector2.LerpUnclamped(fromSize, toSize, path),
                Scale = Vector2.one * Mathf.Max(0f, scale),
                Opacity = Mathf.Clamp01(opacity),
                Morph = path,
            };
        }

        /// <summary>
        /// How far along the move is, 0 to 1, through the phases' eases as though both moved: what a fade in place
        /// (a node entering or leaving where it stands) follows.
        /// </summary>
        internal float Progress(float progress)
        {
            if (progress >= 1f) return 1f;
            Timeline(out float departEnd, out float arriveStart);
            float departShare = departEnd > 0f ? (arriveStart < 1f ? 0.5f : 1f) : 0f;
            if (progress < departEnd)
                return departShare * EaseUtility.Evaluate(progress / departEnd, Depart.Ease);
            if (progress < arriveStart)
                return departShare;
            return departShare + (1f - departShare) * EaseUtility.Evaluate((progress - arriveStart) / (1f - arriveStart), Arrive.Ease);
        }

        // A fade alongside a scale runs ahead of it, twice as fast at first and meeting it at the end of the phase:
        // on the scale's own timing it would hardly show, the node being small by the time it had faded much.
        private static float Ahead(float x) => 1f - (1f - x) * (1f - x);

        /// <summary>
        /// The point <paramref name="t"/> of the way along the path from <paramref name="from"/> to <paramref name="to"/>:
        /// a quadratic Bézier whose control point slides with <paramref name="curvature"/> from the midpoint (a straight
        /// line) towards the corner of the L that goes along the shorter axis first. The curvature fades out on a path
        /// close to an axis (see below). <paramref name="acrossFirst"/> fixes which corner (see <see cref="AcrossFirst"/>),
        /// for a path whose end moves; without it the corner follows the ends.
        /// </summary>
        internal static Vector2 PointAlongPath(Vector2 from, Vector2 to, float curvature, float t, bool? acrossFirst = null)
        {
            var delta = to - from;
            // A hack, by eye: on a path that is nearly level or upright the L's corner is barely off the line, so a
            // high curvature only kinks it, which reads as a wobble rather than an arc. The curvature fades out with
            // the path's angle off its nearer axis: none at 0 degrees, half at 15, all from 30 on.
            float major = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
            float minor = Mathf.Min(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
            float angle = major > 0f ? Mathf.Atan2(minor, major) * Mathf.Rad2Deg : 0f;
            curvature *= Mathf.Clamp01(angle / 30f);
            if (curvature <= 0f)
                return Vector2.LerpUnclamped(from, to, t);
            var corner = from;
            if (acrossFirst ?? AcrossFirst(from, to))
                corner.x = to.x;
            else
                corner.y = to.y;
            var control = Vector2.LerpUnclamped((from + to) * 0.5f, corner, curvature);
            float u = 1f - t;
            return u * u * from + 2f * u * t * control + t * t * to;
        }

        /// <summary>
        /// Whether the path from <paramref name="from"/> to <paramref name="to"/> bends through the corner that goes
        /// across (along x) first, which it does when across is the shorter way; otherwise it goes up or down first. A
        /// path whose end moves decides this once, from where it starts: close to the diagonal the shorter axis can
        /// change as the end moves, and the bend would jump to the other side mid-flight.
        /// </summary>
        internal static bool AcrossFirst(Vector2 from, Vector2 to) => Mathf.Abs(to.x - from.x) <= Mathf.Abs(to.y - from.y);
    }

    /// <summary>Where something is and how it looks at one moment of a move: what the engine writes each tick.</summary>
    internal struct MotionFrame
    {
        public Vector2 Centre;
        /// <summary>The layout size (what a container's content is laid out against).</summary>
        public Vector2 Size;
        /// <summary>
        /// A visual scale around the centre, on top of the size, on each axis: one is none. A motion scales both alike;
        /// a node stretched to fit its rect scales them apart.
        /// </summary>
        public Vector2 Scale;
        /// <summary>The opacity of the whole thing: 1 is fully shown.</summary>
        public float Opacity;
        /// <summary>
        /// How far the contents have gone from the old state to the new one: 0 the old, 1 the new. A matched pair's
        /// halves blend by it, and what rides inside moves by it from its old place and size to its new ones.
        /// </summary>
        public float Morph;

        /// <summary>A straight move <paramref name="t"/> of the way along, at full scale and opacity, morphed as far.</summary>
        public static MotionFrame Straight(Vector2 fromCentre, Vector2 toCentre, Vector2 fromSize, Vector2 toSize, float t) => new()
        {
            Centre = Vector2.LerpUnclamped(fromCentre, toCentre, t),
            Size = Vector2.LerpUnclamped(fromSize, toSize, t),
            Scale = Vector2.one,
            Opacity = 1f,
            Morph = t,
        };
    }
}
