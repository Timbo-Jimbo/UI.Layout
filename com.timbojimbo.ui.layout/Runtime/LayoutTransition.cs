using System;
using System.Collections.Generic;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// A change made in <see cref="LayoutSystem.Animate"/>: its types, whether what it moves takes the pointer, when
    /// everything it set moving has come to rest or been given somewhere else to go by a later one, and whether it got
    /// there. SwiftUI's withAnimation completion, and UIKit's completion(finished:).
    /// </summary>
    public sealed class LayoutTransition
    {
        private readonly string[] _types;
        private Action _finished;

        internal LayoutTransition(string[] types, bool interactive)
        {
            _types = types ?? Array.Empty<string>();
            Interactive = interactive;
        }

        /// <summary>What kind of change this is ("open", "back"), as passed to <see cref="LayoutSystem.Animate"/>.</summary>
        public IReadOnlyList<string> Types => _types;

        /// <summary>True when it was started with <paramref name="type"/>.</summary>
        public bool HasType(string type) => !string.IsNullOrEmpty(type) && Array.IndexOf(_types, type) >= 0;

        /// <summary>
        /// Whether what it moves takes the pointer on its way, as UIKit's .allowUserInteraction: true unless it was
        /// started with interactive false. When false, a node moving for it takes no pointer, and nor does anything
        /// inside it (the pointer goes to whatever is under it), until it comes to rest, is taken over by an interactive
        /// change, or is caught.
        /// </summary>
        public bool Interactive { get; }

        /// <summary>True once everything it set moving has come to rest or been taken over, or it was skipped.</summary>
        public bool IsFinished { get; private set; }

        /// <summary>
        /// Whether it got to the end, once it <see cref="IsFinished"/>, as the finished flag UIKit's animation
        /// completion is given: true when everything it set moving came to rest where it was going (or it was
        /// skipped, which puts everything there); false when anything it moved was let go of on its way: taken over
        /// by a later change, caught (<see cref="LayoutNode.Catch"/>), stopped because its node was disabled, destroyed
        /// or taken out from under its layout parent, or a scroll it moved taken hold of by a press. Read it in a
        /// <see cref="Finished"/> handler to tell a change that landed from one that was interrupted. False until it
        /// has finished.
        /// </summary>
        public bool Completed => IsFinished && !Interrupted;

        /// <summary>
        /// Raised once everything it set moving has come to rest or been taken over, or it was skipped
        /// (<see cref="Completed"/> says whether it got to the end); added once it has, it runs at once.
        /// </summary>
        public event Action Finished
        {
            add
            {
                if (IsFinished)
                    value?.Invoke();
                else
                    _finished += value;
            }
            remove => _finished -= value;
        }

        /// <summary>
        /// Ends it at once: everything it set moving is put where it was going, as if it had got there (it is
        /// <see cref="Completed"/> unless something it moved had already been let go of).
        /// </summary>
        public void Skip() => LayoutSystem.Skip(this);

        // ── Engine state ─────────────────────────────────────────────────────────

        // How many of the node values it set moving are still on their way there.
        internal int Moving;

        // Whether any of them was let go of before it got there (see Completed).
        internal bool Interrupted;

        internal void Finish()
        {
            if (IsFinished) return;
            IsFinished = true;
            var finished = _finished;
            _finished = null;
            finished?.Invoke();
        }
    }
}
