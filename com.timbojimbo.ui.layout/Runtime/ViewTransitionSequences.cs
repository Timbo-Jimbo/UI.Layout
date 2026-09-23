#if TJ_LAYOUT_SEQUENCER
using System;
using System.Collections.Generic;
using TimboJimbo.Sequencer;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// Enter and exit effects for a node as authored sequences, present while the Sequencer package is installed.
    /// Point <see cref="Enter"/> and <see cref="Exit"/> at sequences on a <see cref="SequenceProvider"/> (usually
    /// one on the same object) that tween whatever the effect needs: the node's <c>Offset</c>, a CanvasGroup's
    /// alpha, a scale. <see cref="ByType"/> picks other sequences for transitions of a given type ("forward",
    /// "back"), the first rule that matches winning; a transition with none of the types, or an enter or exit
    /// outside a transition, plays <see cref="Enter"/> or <see cref="Exit"/>. The values a sequence touched are
    /// put back when it is disposed, so an exit leaves the object as it was for its next showing and an enter
    /// ends where it began. A missing sequence is an instant enter or exit. Sequences advance with
    /// <see cref="LayoutSystem.DeltaTime"/>, the clock the moves follow.
    /// </summary>
    [AddComponentMenu("Timbo Jimbo/UI/Layout/View Transition Sequences")]
    [DisallowMultipleComponent]
    public sealed class ViewTransitionSequences : MonoBehaviour, IViewTransitionAnimator
    {
        /// <summary>The sequences to play in a transition with a given type, in place of the defaults.</summary>
        [Serializable]
        public struct TypeRule
        {
            [Tooltip("The view transition type this rule applies to, as passed to StartViewTransition.")]
            public string Type;
            [Tooltip("Played in place of the default enter; empty falls through to the next rule or the default.")]
            public SequenceRef Enter;
            [Tooltip("Played in place of the default exit; empty falls through to the next rule or the default.")]
            public SequenceRef Exit;
        }

        [Tooltip("Played when the node appears: inside a view transition, or under a node already shown.")]
        [SerializeField] private SequenceRef _enter;
        [Tooltip("Played when LayoutSystem.Exit takes the node out; the node leaves when it completes.")]
        [SerializeField] private SequenceRef _exit;
        [Tooltip("Sequences for view transitions of a given type, in place of the defaults. The first rule whose type the transition has wins.")]
        [SerializeField] private List<TypeRule> _byType = new();

        private SequencePlayer _player;

        public SequenceRef Enter
        {
            get => _enter;
            set => _enter = value;
        }

        public SequenceRef Exit
        {
            get => _exit;
            set => _exit = value;
        }

        /// <summary>The per-type rules, in priority order.</summary>
        public List<TypeRule> ByType => _byType;

        void IViewTransitionAnimator.Enter(ViewTransition transition, Action done) => Play(Pick(transition, enter: true), done);

        void IViewTransitionAnimator.Exit(ViewTransition transition, Action done) => Play(Pick(transition, enter: false), done);

        void IViewTransitionAnimator.Skip() => Stop();

        private SequenceRef Pick(ViewTransition transition, bool enter)
        {
            if (transition != null)
            {
                for (int i = 0; i < _byType.Count; i++)
                {
                    var rule = _byType[i];
                    if (!transition.HasType(rule.Type)) continue;
                    var sequence = enter ? rule.Enter : rule.Exit;
                    if (sequence.IsValid)
                        return sequence;
                }
            }
            return enter ? _enter : _exit;
        }

        private void Play(SequenceRef sequence, Action done)
        {
            Stop();
            if (!sequence.TryResolve(out var resolved))
            {
                done();
                return;
            }
            // Ticked here on the layout clock rather than by the SequenceDriver (always scaled time), so the
            // effect keeps pace with the moves whichever clock LayoutSystem.UseScaledTime picks.
            var player = _player = resolved.CreatePlayer(restoreValuesOnDispose: true);
            player.Completed += _ =>
            {
                done();
                // done may have started another effect on this object, which has replaced this player already.
                if (_player == player)
                    Stop();
            };
            player.Play();
        }

        private void Update() => _player?.Tick(LayoutSystem.DeltaTime);

        private void Stop()
        {
            _player?.Dispose();
            _player = null;
        }

        private void OnDisable() => Stop();
    }
}
#endif
