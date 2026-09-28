using System;
using System.Collections.Generic;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// A change made in <see cref="LayoutSystem.Animate"/>: its types, and when everything it set moving has come to
    /// rest or been given somewhere else to go by a later one. SwiftUI's withAnimation completion.
    /// </summary>
    public sealed class LayoutTransition
    {
        private readonly string[] _types;
        private Action _finished;

        internal LayoutTransition(string[] types)
        {
            _types = types ?? Array.Empty<string>();
        }

        /// <summary>What kind of change this is ("open", "back"), as passed to <see cref="LayoutSystem.Animate"/>.</summary>
        public IReadOnlyList<string> Types => _types;

        /// <summary>True when it was started with <paramref name="type"/>.</summary>
        public bool HasType(string type) => !string.IsNullOrEmpty(type) && Array.IndexOf(_types, type) >= 0;

        /// <summary>True once everything it set moving has come to rest or been taken over, or it was skipped.</summary>
        public bool IsFinished { get; private set; }

        /// <summary>
        /// Raised once everything it set moving has come to rest or been taken over, or it was skipped; added once it
        /// has, it runs at once.
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

        /// <summary>Ends it at once: everything it set moving is put where it was going.</summary>
        public void Skip() => LayoutSystem.Skip(this);

        // ── Engine state ─────────────────────────────────────────────────────────

        // How many of the node values it set moving are still on their way there.
        internal int Moving;

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
