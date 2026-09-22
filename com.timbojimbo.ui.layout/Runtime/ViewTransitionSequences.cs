#if TJ_LAYOUT_SEQUENCER
using System;
using TimboJimbo.Sequencer;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// Enter and exit effects for a node as two authored sequences, present while the Sequencer package is
    /// installed. Point <see cref="Enter"/> and <see cref="Exit"/> at sequences on a <see cref="SequenceProvider"/>
    /// (usually one on the same object) that tween whatever the effect needs: the node's <c>Offset</c>, a
    /// CanvasGroup's alpha, a scale. The values a sequence touched are put back when it is disposed, so an exit
    /// leaves the object as it was for its next showing and an enter ends where it began. A missing sequence is
    /// an instant enter or exit.
    /// </summary>
    [AddComponentMenu("Timbo Jimbo/UI/Layout/View Transition Sequences")]
    [DisallowMultipleComponent]
    public sealed class ViewTransitionSequences : MonoBehaviour, IViewTransitionAnimator
    {
        [Tooltip("Played when the node first appears inside a view transition.")]
        [SerializeField] private SequenceRef _enter;
        [Tooltip("Played when LayoutSystem.Exit takes the node out; the node leaves when it completes.")]
        [SerializeField] private SequenceRef _exit;

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

        void IViewTransitionAnimator.Enter(ViewTransition transition, Action done) => Play(_enter, done);

        void IViewTransitionAnimator.Exit(ViewTransition transition, Action done) => Play(_exit, done);

        void IViewTransitionAnimator.Skip() => Stop();

        private void Play(SequenceRef sequence, Action done)
        {
            Stop();
            if (!sequence.TryResolve(out var resolved))
            {
                done();
                return;
            }
            _player = resolved.Play(restoreValuesOnDispose: true);
            _player.Completed += _ =>
            {
                done();
                Stop();
            };
        }

        private void Stop()
        {
            _player?.Dispose();
            _player = null;
        }

        private void OnDisable() => Stop();
    }
}
#endif
