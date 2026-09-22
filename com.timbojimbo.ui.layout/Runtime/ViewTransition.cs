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
    /// ends it at once. Only one runs at a time: starting another skips this one.
    /// </summary>
    public sealed class ViewTransition
    {
        /// <summary>The transition nodes move with unless they set their own: a quarter second, easing out.</summary>
        public static readonly LayoutTransition DefaultTransition = LayoutTransition.Over(0.25f, EaseType.OutCubic);

        internal enum GroupKind
        {
            /// <summary>A name that moved from one node to another: the new node flies in from the old one's spot and the old one, if it is leaving, flies out to it, the two cross-fading.</summary>
            Pair,
            /// <summary>A persisting pair: the old object is kept and flies into the new spot; the copy marks the spot, hidden, and the two swap places at the end.</summary>
            Persist,
            /// <summary>A node handed to Exit: it plays its animator's exit or fades out where it was.</summary>
            Exit,
            /// <summary>The topmost node of a subtree that appeared: it plays its animator's enter or fades in at its final rect.</summary>
            Enter,
        }

        /// <summary>One entry of the groups table. <see cref="Old"/> is the node in the state before the update (null for an entering group), <see cref="New"/> the node after it.</summary>
        internal sealed class Group
        {
            public GroupKind Kind;
            public LayoutNode Old;
            public LayoutNode New;
            /// <summary>Whether the old half flies along: only when it is on its way out, as on the web an element that stays in the page is not the old image.</summary>
            public bool OldFlies;
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
            public SizeRule Size;
            /// <summary>Lay the node's subtree out against its animated rect each tick: a kept object is arriving and has no final layout yet, so its content follows its rect; a pair's halves keep the layout they had, like snapshots.</summary>
            public bool LayoutEachTick;
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
            public bool Done;
        }

        internal readonly int Id;
        internal readonly LayoutTransition Default;
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

        internal ViewTransition(int id, LayoutTransition transition)
        {
            Id = id;
            Default = transition;
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

        internal LayoutTransition TransitionFor(LayoutNode node) => node != null && node.Transition.IsAnimated ? node.Transition : Default;

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
