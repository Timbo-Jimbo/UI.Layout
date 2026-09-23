using System;
using System.Collections;
using UnityEngine;

namespace TimboJimbo.UI.Layout.Samples.ViewTransitions
{
    /// <summary>
    /// An <see cref="IViewTransitionAnimator"/> with no dependencies: the node slides in from <see cref="EnterFrom"/>
    /// and out to <see cref="ExitTo"/> through its <c>Offset</c>, fading through a CanvasGroup. The engine calls
    /// Enter and Exit at the right moments and waits for <c>done</c>; what happens in between is this script's.
    /// (With the Sequencer package installed, View Transition Sequences does the same with authored sequences.)
    /// </summary>
    [RequireComponent(typeof(LayoutNode))]
    public sealed class SlideFadeAnimator : MonoBehaviour, IViewTransitionAnimator
    {
        public Vector2 EnterFrom = new(0f, -40f);
        public Vector2 ExitTo = new(0f, 40f);
        [Min(0.01f)] public float Duration = 0.3f;

        private LayoutNode _node;
        private CanvasGroup _group;
        private Coroutine _running;

        private void Awake()
        {
            _node = GetComponent<LayoutNode>();
            if (!TryGetComponent(out _group))
                _group = gameObject.AddComponent<CanvasGroup>();
        }

        public void Enter(ViewTransition transition, Action done) => Play(EnterFrom, Vector2.zero, 0f, 1f, done);

        public void Exit(ViewTransition transition, Action done) => Play(Vector2.zero, ExitTo, 1f, 0f, done);

        public void Skip()
        {
            Stop();
            Rest();
        }

        private void Play(Vector2 offsetFrom, Vector2 offsetTo, float alphaFrom, float alphaTo, Action done)
        {
            Stop();
            _running = StartCoroutine(Run(offsetFrom, offsetTo, alphaFrom, alphaTo, done));
        }

        private IEnumerator Run(Vector2 offsetFrom, Vector2 offsetTo, float alphaFrom, float alphaTo, Action done)
        {
            for (float elapsed = 0f; elapsed < Duration; elapsed += LayoutSystem.DeltaTime)
            {
                float t = elapsed / Duration;
                float eased = 1f - (1f - t) * (1f - t) * (1f - t);   // ease out cubic
                _node.Offset = Vector2.Lerp(offsetFrom, offsetTo, eased);
                _group.alpha = Mathf.Lerp(alphaFrom, alphaTo, eased);
                yield return null;
            }
            _node.Offset = offsetTo;
            _group.alpha = alphaTo;
            _running = null;
            done();
            // The node is at rest for its next showing (an exit has deactivated it by now).
            Rest();
        }

        private void Rest()
        {
            _node.Offset = Vector2.zero;
            _group.alpha = 1f;
        }

        private void Stop()
        {
            if (_running != null)
                StopCoroutine(_running);
            _running = null;
        }

        private void OnDisable() => Stop();
    }
}
