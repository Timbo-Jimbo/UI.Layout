using System;
using TimboJimbo.UI.Layout;
using UnityEngine;

namespace TimboJimboTests.UI.Layout.PlayMode
{
    /// <summary>An animator for tests: records the calls and completes an effect only when told to.</summary>
    public sealed class TestAnimator : MonoBehaviour, IViewTransitionAnimator
    {
        public int Enters, Exits, Skips;
        public ViewTransition LastTransition;
        private Action _done;

        public void Enter(ViewTransition transition, Action done)
        {
            Enters++;
            LastTransition = transition;
            _done = done;
        }

        public void Exit(ViewTransition transition, Action done)
        {
            Exits++;
            LastTransition = transition;
            _done = done;
        }

        public void Skip()
        {
            Skips++;
            _done = null;
        }

        public void Complete()
        {
            var done = _done;
            _done = null;
            done?.Invoke();
        }
    }
}
