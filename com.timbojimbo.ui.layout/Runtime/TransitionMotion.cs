using System;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// How something travels in a view transition, as a pure function: given where it starts, where it is going and
    /// how far along the transition is, a <see cref="MotionFrame"/> saying where its centre is, how big it is and how
    /// it looks right now. The engine is the only thing that touches objects: it applies the frame each tick and
    /// takes the look off when the move ends, however it ends. Set one on a <see cref="LayoutTransition"/> with
    /// <see cref="LayoutTransition.With"/>, or on a node's own transition in the inspector. No motion is a straight
    /// line whose contents change with the eased progress; <see cref="ArcMotion"/> bends the line. A motion shapes
    /// only what moves as a whole: what is inside it, a node moving inside a moving parent or a part flying inside a
    /// flying container, rides it and changes from its old place to its new one with its <see cref="MotionFrame.Morph"/>. Write one as a plain [Serializable] class with a parameterless constructor so the
    /// inspector can pick it; keep it stateless, since one instance may drive many moves at once.
    /// </summary>
    public interface ITransitionMotion
    {
        /// <summary>
        /// The frame at <see cref="MotionInput.Progress"/>. At progress 1 it should be the destination at full scale
        /// and opacity, morphed all the way; the engine puts the look back when the move ends either way.
        /// The axes are the engine's (in-place moves are y down, flights y up), so a motion should not depend on
        /// which way y points. Start from <see cref="MotionFrame.Straight"/> and change what the motion is about.
        /// </summary>
        MotionFrame Evaluate(in MotionInput input);
    }

    /// <summary>Where a move starts and ends, and how far along it is.</summary>
    public readonly struct MotionInput
    {
        public readonly Vector2 FromCentre;
        public readonly Vector2 ToCentre;
        public readonly Vector2 FromSize;
        public readonly Vector2 ToSize;
        /// <summary>Time through the move, 0 to 1, after its delay, before any ease.</summary>
        public readonly float Progress;
        /// <summary><see cref="Progress"/> through the transition's ease.</summary>
        public readonly float Eased;

        public MotionInput(Vector2 fromCentre, Vector2 toCentre, Vector2 fromSize, Vector2 toSize, float progress, float eased)
        {
            FromCentre = fromCentre;
            ToCentre = toCentre;
            FromSize = fromSize;
            ToSize = toSize;
            Progress = progress;
            Eased = eased;
        }

        /// <summary>True when the move goes nowhere (an enter or exit in place), which no motion shapes.</summary>
        public bool IsStill => (ToCentre - FromCentre).sqrMagnitude < 1e-8f && (ToSize - FromSize).sqrMagnitude < 1e-8f;
    }

    /// <summary>Where something is and how it looks at one moment of a move.</summary>
    public struct MotionFrame
    {
        public Vector2 Centre;
        /// <summary>The layout size (what a container's content is laid out against).</summary>
        public Vector2 Size;
        /// <summary>A visual scale around the centre, on top of the size: 1 is none.</summary>
        public float Scale;
        /// <summary>The opacity of the whole thing: 1 is fully shown.</summary>
        public float Opacity;
        /// <summary>
        /// How far the contents have gone from the old state to the new one: 0 is the old state, 1 the new. A matched
        /// pair's halves blend by it (the old one shown at <see cref="Opacity"/> times 1 - Morph, the new one at
        /// Opacity times Morph), and what rides inside moves by it from its old place and size to its new ones: the
        /// parts flying inside a flying container, and the nodes moving inside a parent that is moving. Following the
        /// eased progress it is a cross-fade with the parts gliding into place; held at 0 and then jumped to 1 while
        /// nothing shows, the contents keep their old arrangement until the swap and appear in their new one.
        /// </summary>
        public float Morph;

        /// <summary>The straight-line frame: centre and size eased from start to end, at full scale and opacity, morphing on the eased progress.</summary>
        public static MotionFrame Straight(in MotionInput input) => new()
        {
            Centre = Vector2.LerpUnclamped(input.FromCentre, input.ToCentre, input.Eased),
            Size = Vector2.LerpUnclamped(input.FromSize, input.ToSize, input.Eased),
            Scale = 1f,
            Opacity = 1f,
            Morph = input.Eased,
        };
    }

    /// <summary>
    /// A path bent into an arc: a quadratic Bézier whose control point slides with <see cref="Curvature"/> from the
    /// midpoint (a straight line) towards the corner of the L that goes along the shorter axis first, so the path
    /// sets off along the shorter axis and arrives along the longer one. For panels that swing into place.
    /// </summary>
    [Serializable]
    public sealed class ArcMotion : ITransitionMotion
    {
        [Tooltip("How much the path bends: 0 a straight line; at 1 it bows about halfway towards the corner of the L that takes the shorter axis first.")]
        [Range(0f, 1f)] public float Curvature = 0.5f;

        public ArcMotion() { }

        public ArcMotion(float curvature) => Curvature = Mathf.Clamp01(curvature);

        public MotionFrame Evaluate(in MotionInput input)
        {
            var frame = MotionFrame.Straight(input);
            frame.Centre = PointAlongPath(input.FromCentre, input.ToCentre, input.Eased);
            return frame;
        }

        /// <summary>The point <paramref name="t"/> of the way along the arc from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public Vector2 PointAlongPath(Vector2 from, Vector2 to, float t)
        {
            if (Curvature <= 0f)
                return Vector2.LerpUnclamped(from, to, t);
            var delta = to - from;
            var corner = from;
            if (Mathf.Abs(delta.x) <= Mathf.Abs(delta.y))
                corner.x = to.x;
            else
                corner.y = to.y;
            var control = Vector2.LerpUnclamped((from + to) * 0.5f, corner, Curvature);
            float u = 1f - t;

            return (u * u * from + 2f * u * t * control + t * t * to);
        }
    }
}
