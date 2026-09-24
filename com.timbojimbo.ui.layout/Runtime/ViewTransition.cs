using System;
using System.Collections.Generic;
using TimboJimbo.Core;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// One animated update of the layout, started with <see cref="LayoutSystem.StartViewTransition"/> and modelled
    /// on the web's ViewTransition object. Before the update every enabled node's on-screen rect is captured; after
    /// it, the new state is laid out and the changes are sorted into groups the way the web sorts its captured
    /// elements. Named groups (a name that moved from one node to another, or a persisting pair) are lifted into a
    /// transition layer above their canvas, stacked in capture order like the web's groups in its top layer, and
    /// flown along straight lines to where layout puts them now. Nodes that appeared enter and nodes handed to
    /// <see cref="LayoutSystem.Exit"/> leave in place, beneath the layer; every other node that changed rect moves
    /// there in place. <see cref="Finished"/> fires when the last flight has landed; <see cref="SkipTransition"/>
    /// ends it at once. A transition started over the same nodes (an overlapping scope) takes over: this one
    /// finishes and its moves go on from where they are under the new one.
    /// </summary>
    public sealed class ViewTransition
    {
        /// <summary>The transition nodes move with unless they set their own: a quarter second, easing out.</summary>
        public static readonly LayoutTransition DefaultTransition = LayoutTransition.Over(0.25f, EaseType.OutCubic);

        internal enum GroupKind
        {
            /// <summary>A name that moved from one node to another: the new node flies in from the old one's spot and the old one, if it is leaving or hidden, flies out to it, the two cross-fading.</summary>
            Pair,
            /// <summary>A persisting pair: the old object is kept and flies into the new spot; the copy marks the spot, hidden, and the two swap places at the end.</summary>
            Persist,
            /// <summary>A node handed to Exit: it plays its animator's exit or fades out where it was.</summary>
            Exit,
            /// <summary>The topmost node of a subtree that appeared: it plays its animator's enter or fades in at its final rect.</summary>
            Enter,
            /// <summary>A persisting node the update moved to a new parent: lifted, it flies from its old slot to its new one, the same object throughout.</summary>
            Carry,
        }

        /// <summary>One entry of the groups table. <see cref="Old"/> is the node in the state before the update (null for an entering group), <see cref="New"/> the node after it.</summary>
        internal sealed class Group
        {
            public GroupKind Kind;
            public LayoutNode Old;
            public LayoutNode New;
            /// <summary>
            /// Whether the old half flies along: when it is leaving, or hidden in its place, which it goes back to
            /// unseen when the flight lands. One that stays in the page stays put and the new one grows out of it.
            /// </summary>
            public bool OldFlies;
            /// <summary>For an enter or exit, the origin the node grows out of or shrinks back into, when it has one that is active.</summary>
            public RectTransform Origin;
        }

        /// <summary>How a size-locked node (text) is sized on a flight: the new half takes its destination size at once, the old half keeps the size it has.</summary>
        internal enum SizeRule { Animate, KeepDestination, KeepFrom }

        /// <summary>
        /// A lifted node on its way from <see cref="From"/> (world) to its marker: the placeholder holding its
        /// layout slot, or the copy it will replace. The marker is read live each tick, so the flight lands where
        /// layout puts the node by then, and it is corrected for lifted ancestors so the line stays straight.
        /// </summary>
        internal sealed class Flight
        {
            public LayoutNode Node;
            public Transform Marker;
            public Rect From;
            public Rect LastDestination;
            /// <summary>The rect last written, in world space, before any scale the flight's look adds: what parts riding it measure from.</summary>
            public Rect LastRect;
            public SizeRule Size;
            /// <summary>Lay the node's subtree out against its animated rect each tick: a kept object is arriving and has no final layout yet, so its content follows its rect; a pair's halves keep the layout they had, like snapshots.</summary>
            public bool LayoutEachTick;
            /// <summary>Play the motion mirrored (<see cref="LayoutMotion.Mirrored"/>): a node leaving into its origin undoes its entrance.</summary>
            public bool Reverse;
            /// <summary>
            /// For a node scaled to fit (<see cref="ViewTransitionFit.Scale"/> or <see cref="ViewTransitionFit.Stretch"/>):
            /// the scale it has at the flight's start on each axis, its start size against its own. A part riding it
            /// measures where it started in the node's own layout by it. One for a node that resizes.
            /// </summary>
            public Vector2 FitFrom = Vector2.one;
            /// <summary>
            /// Which way the flight's path bends (see <see cref="LayoutMotion.AcrossFirst"/>), decided at its first
            /// frame and kept: its end is read live and may move, and a bend decided each frame could flip sides.
            /// </summary>
            public bool? AcrossFirst;
            public LayoutTransition Transition;
            public float Elapsed;
            public bool Done;
        }

        /// <summary>An enter or exit effect an <see cref="IViewTransitionAnimator"/> is playing for this transition.</summary>
        internal sealed class Effect
        {
            public LayoutNode Node;
            public IViewTransitionAnimator Animator;
            public bool IsExit;
            /// <summary>An exit effect played for <see cref="LayoutSystem.Hide"/>: the node rests unseen in its place when it ends.</summary>
            public bool IsHide;
            public bool Done;
        }

        internal readonly int Id;
        /// <summary>The subtree this transition captures and animates, or null for the whole UI.</summary>
        internal readonly Transform Scope;
        /// <summary>The node that carried each name when the scene was captured.</summary>
        internal readonly Dictionary<string, LayoutNode> Old = new();
        internal readonly List<Group> Groups = new();
        /// <summary>The lifted flights, in layer order (a container before the parts inside it).</summary>
        internal readonly List<Flight> Flights = new();
        /// <summary>Nodes moving in place through the scheduler: movers, entering and exiting nodes.</summary>
        internal readonly List<LayoutNode> Participants = new();
        internal readonly List<Effect> Effects = new();
        /// <summary>Persisting pairs (kept, copy) that exchange places when the transition ends.</summary>
        internal readonly List<(LayoutNode Kept, LayoutNode Copy)> Swaps = new();
#if UNITY_EDITOR
        /// <summary>Nodes outside the scope where they were captured, to name one the update moved; and the captured nodes that were shown, to name one it deactivated.</summary>
        internal readonly List<(LayoutNode Node, Rect World)> Outside = new();
        internal readonly List<LayoutNode> WasShown = new();
#endif
        private readonly string[] _types;

        internal ViewTransition(int id, Transform scope, LayoutTransition transition, string[] types)
        {
            Id = id;
            Scope = scope;
            Transition = transition;
            _types = types != null && types.Length > 0 ? (string[])types.Clone() : Array.Empty<string>();
        }

        /// <summary>The timing the transition was started with: what nodes move with unless they set their own.</summary>
        public LayoutTransition Transition { get; }

        /// <summary>What kind of change this is, as the caller said when starting it ("forward", "back"), like the web's view transition types. Empty when none were given.</summary>
        public IReadOnlyList<string> Types => _types;

        /// <summary>True when the caller gave this transition the type <paramref name="type"/> (compared exactly).</summary>
        public bool HasType(string type)
        {
            for (int i = 0; i < _types.Length; i++)
            {
                if (string.Equals(_types[i], type, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// How <paramref name="node"/> moves in this transition, each part inheriting on its own: the timing of its
        /// <see cref="LayoutNode.Transition"/> if it sets one, otherwise <see cref="Transition"/>'s, with its
        /// <see cref="LayoutNode.Motion"/> if it sets one, otherwise <see cref="Transition"/>'s motion; then
        /// <see cref="LayoutSystem.AdjustTransition"/>, if one is set. An animator that follows it stays in step
        /// with the moves.
        /// </summary>
        public LayoutTransition TransitionFor(LayoutNode node)
        {
            var resolved = node == null ? Transition : (node.Transition ?? Transition).With(node.Motion ?? Transition.Motion ?? LayoutMotion.Slide());
            return LayoutSystem.AdjustTransition is { } adjust ? adjust(node, resolved) : resolved;
        }

        /// <summary>True once every node the transition set moving has settled, or it was skipped.</summary>
        public bool IsFinished { get; private set; }

        /// <summary>Raised once when the transition finishes or is skipped. A handler added afterwards is called at once.</summary>
        public event Action Finished
        {
            add { if (IsFinished) value?.Invoke(); else _finished += value; }
            remove => _finished -= value;
        }
        private Action _finished;

        /// <summary>True when the same name was on two nodes in one state, which skips the transition as it does on the web.</summary>
        internal bool HasDuplicateName { get; set; }

        /// <summary>Ends the transition now: every node it moves jumps to its end state and exits conclude.</summary>
        public void SkipTransition() => LayoutSystem.SkipViewTransition(this);

        internal Effect AddEffect(LayoutNode node, IViewTransitionAnimator animator, bool isExit)
        {
            var effect = new Effect { Node = node, Animator = animator, IsExit = isExit };
            Effects.Add(effect);
            return effect;
        }

        /// <summary>True while a flight is still in the air, a node still moving or an effect still playing.</summary>
        internal bool IsSettled
        {
            get
            {
                for (int i = 0; i < Flights.Count; i++)
                {
                    if (!Flights[i].Done)
                        return false;
                }
                for (int i = 0; i < Participants.Count; i++)
                {
                    var node = Participants[i];
                    if (node != null && node._animating)
                        return false;
                }
                for (int i = 0; i < Effects.Count; i++)
                {
                    if (!Effects[i].Done)
                        return false;
                }
                return true;
            }
        }

        /// <summary>Records a captured node under its name; a second node with the same name invalidates the transition.</summary>
        internal void CaptureName(LayoutNode node)
        {
            var name = node.ResolvedViewTransitionName();
            if (string.IsNullOrEmpty(name)) return;
            if (!Old.TryAdd(name, node))
                HasDuplicateName = true;
        }

        /// <summary>True when the node is one half of a group.</summary>
        internal bool IsGrouped(LayoutNode node)
        {
            for (int i = 0; i < Groups.Count; i++)
            {
                var g = Groups[i];
                if (ReferenceEquals(g.Old, node) || ReferenceEquals(g.New, node))
                    return true;
            }
            return false;
        }

        /// <summary>The flight a lifted node is on, or null.</summary>
        internal Flight FlightOf(LayoutNode node)
        {
            for (int i = 0; i < Flights.Count; i++)
            {
                if (ReferenceEquals(Flights[i].Node, node))
                    return Flights[i];
            }
            return null;
        }

        internal void Finish()
        {
            if (IsFinished) return;
            IsFinished = true;
            var handlers = _finished;
            _finished = null;
            handlers?.Invoke();
        }
    }
}
