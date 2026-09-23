using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// Schedules layout. Nodes marked dirty have their trees laid out on <see cref="Canvas.preWillRenderCanvases"/>,
    /// in edit and play mode, which is just before UGUI's own rebuild, so the graphics a commit resizes rebuild in
    /// the same frame. <see cref="ForceLayout"/> settles a tree immediately. <c>StartViewTransition</c>
    /// animates one update as a whole (see <c>LayoutSystem.ViewTransitions.cs</c>), <see cref="Exit"/> takes a
    /// node out with an animation, <see cref="Hide"/> hides one in its place and <see cref="Show"/> brings one back. The static state is reset on every play mode transition; the event subscription
    /// is made once per domain and kept.
    /// </summary>
    [AutoStaticsCleanup]
    public static partial class LayoutSystem
    {
        // Marked nodes, resolved to roots when flushed: during a scene load a child can be enabled before its
        // parent node is, so a node's root is not knowable at marking time. Keyed on managed reference identity
        // because an Undo resurrects a destroyed node with its old instance ID, which UnityEngine.Object equality
        // would treat as the dead entry already in the set.
        private static readonly HashSet<LayoutNode> s_marked = new(ReferenceComparer.Instance);
        private static readonly List<LayoutNode> s_roots = new();
        private static readonly List<LayoutNode> s_animating = new();
        private static readonly List<LayoutNode> s_exiting = new();
        private static readonly List<LayoutNode> s_arrivals = new();
        private static readonly System.Comparison<LayoutNode> s_byDepth = (a, b) => Depth(a).CompareTo(Depth(b));

        // The root whose pass or transition tick is writing transforms right now. Those writes dirty the graphics
        // they resize, which report back through the nodes' Graphic callbacks; that is our own doing, not a reason for a pass.
        private static LayoutNode s_committing;

        // While the editor tears the domain down it still flushes canvases (closing the Scene View's preview scene
        // flushes an undo record, which calls Canvas.ForceUpdateCanvases). Nothing may measure text then: the
        // native text engine and its font assets are already going away, and doing so crashes the editor.
        private static bool s_reloading;

        static LayoutSystem()
        {
            Canvas.preWillRenderCanvases += Flush;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += () => s_reloading = true;
#endif
        }

        /// <summary>Marks the tree that <paramref name="node"/> belongs to for layout before the next render.</summary>
        public static void MarkDirty(LayoutNode node)
        {
            if (node == null) return;
            if (s_committing != null && ReferenceEquals(node._passRoot, s_committing)) return;
            s_marked.Add(node);
        }

        /// <summary>True when the tree that <paramref name="node"/> belongs to is waiting for a pass. A diagnostic; it scans the marked nodes.</summary>
        public static bool IsDirty(LayoutNode node)
        {
            if (node == null) return false;
            var root = node.Root;
            foreach (var marked in s_marked)
            {
                if (marked != null && ReferenceEquals(marked.Root, root))
                    return true;
            }
            return false;
        }

        /// <summary>Lays out the tree that <paramref name="node"/> belongs to now, writing its transforms.</summary>
        public static void ForceLayout(LayoutNode node)
        {
            if (node == null) return;
            var root = node.Root;
            if (!root.isActiveAndEnabled) return;

            s_roots.Clear();
            foreach (var marked in s_marked)
            {
                if (marked == null || ReferenceEquals(marked.Root, root))
                    s_roots.Add(marked);
            }
            for (int i = 0; i < s_roots.Count; i++)
                s_marked.Remove(s_roots[i]);
            s_roots.Clear();

            Run(root);
        }

        /// <summary>
        /// Takes <paramref name="node"/> out of its tree, the way removing an element inside a web view transition
        /// animates it out: the tree reflows without it at its next pass while the node, still enabled and drawn
        /// where it was, plays its <see cref="IViewTransitionAnimator"/>'s exit, or fades out with the running
        /// view transition's timing. When that ends the object is deactivated and <paramref name="onExited"/>
        /// runs (to destroy it, pool it, or show it again). Outside a view transition a node with no animator
        /// leaves at that pass. <see cref="Show"/> calls a leaving node back. Deactivating a node inside a view
        /// transition cannot animate it (a disabled object is not drawn); use this instead.
        /// </summary>
        public static void Exit(LayoutNode node, Action onExited = null)
        {
            if (node == null || node._exiting) return;
            if (!node.isActiveAndEnabled || !Application.isPlaying)
            {
                Conclude(node, onExited);
                return;
            }
            node._exiting = true;
            node._exitStarted = false;
            node._exitThen = onExited;
            s_exiting.Add(node);
            MarkDirty(node);
        }

        /// <summary>
        /// Whether view transitions advance with <see cref="Time.deltaTime"/>, slowing and pausing with
        /// <see cref="Time.timeScale"/>, or with <see cref="Time.unscaledDeltaTime"/> (the default), so that UI
        /// keeps moving while the game is paused or in slow motion.
        /// </summary>
        public static bool UseScaledTime { get; set; }

        /// <summary>
        /// This frame's step for view transitions: <see cref="Time.deltaTime"/> or <see cref="Time.unscaledDeltaTime"/>
        /// as <see cref="UseScaledTime"/> says. An <see cref="IViewTransitionAnimator"/> that advances its effect by
        /// this stays in step with the moves, so <see cref="ViewTransition.Finished"/> never waits on a frozen clock.
        /// </summary>
        public static float DeltaTime => UseScaledTime ? Time.deltaTime : Time.unscaledDeltaTime;

        internal static bool IsCommitting(LayoutNode root) => ReferenceEquals(s_committing, root);

        /// <summary>Number of nodes currently moving through a transition; a test seam.</summary>
        internal static int TransitioningCount => s_animating.Count;

        internal static void RegisterAnimating(LayoutNode node)
        {
            for (int i = 0; i < s_animating.Count; i++)
            {
                if (ReferenceEquals(s_animating[i], node))
                    return;
            }
            s_animating.Add(node);
        }

        private static void Flush()
        {
            if (s_reloading) return;
            FlushMarked();
            float deltaTime = DeltaTime;
            TickTransitions(deltaTime);
            for (int i = 0; i < s_active.Count; i++)
                TickFlights(s_active[i], deltaTime);
            FinishSettledViewTransitions();
            // A finish brings lifted nodes back into their trees: those lay out now, not a frame later.
            if (s_marked.Count > 0)
                FlushMarked();
        }

        // Resolves the marked nodes to their roots and lays each out, outer roots first. A pass can only dirty
        // roots deeper than its own: it writes the transforms of its descendants, and the only root among those
        // is one nested under a plain rect. So whatever is marked while sweeping lies strictly below the roots
        // just run, and repeating the sweep until nothing is marked descends the hierarchy and ends.
        private static void FlushMarked()
        {
            while (s_marked.Count > 0)
            {
                s_roots.Clear();
                foreach (var node in s_marked)
                {
                    if (node == null || !node.isActiveAndEnabled) continue;
                    var root = node.Root;
                    // A lifted node's subtree keeps the layout it was given until it comes back down.
                    if (root._lifted)
                    {
                        DeferMark(node);
                        continue;
                    }
                    if (root.isActiveAndEnabled && !Contains(s_roots, root))
                        s_roots.Add(root);
                }
                s_marked.Clear();

                s_roots.Sort(s_byDepth);
                for (int i = 0; i < s_roots.Count; i++)
                    Run(s_roots[i]);
            }
            s_roots.Clear();
        }

        private static void Run(LayoutNode root)
        {
            s_committing = root;
            try
            {
                LayoutEngine.Compute(root, root.RectTransform.rect.size);
                LayoutEngine.Commit(root);
            }
            finally
            {
                s_committing = null;
            }
            // Effects are started once the pass is written, so what they set (an Offset, a padding) is theirs.
            StartArrivals();
            StartExits(root);
        }

        // Outside a view transition (or outside the scope of the one whose update is running, which starts its
        // own), nodes leaving this tree go in the pass that first reflows the tree without them: an animator on
        // the object plays the exit effect, otherwise the node leaves at once.
        private static void StartExits(LayoutNode root)
        {
            for (int i = s_exiting.Count - 1; i >= 0; i--)
            {
                if (i >= s_exiting.Count) continue;
                var node = s_exiting[i];
                if (node == null || !node._exiting)
                {
                    s_exiting.RemoveAt(i);
                    continue;
                }
                if (node._exitStarted || !ReferenceEquals(node.Root, root))
                    continue;
                if (s_updating != null && InScope(s_updating, node))
                    continue;

                node._exitStarted = true;
                if (node.TryGetComponent(out IViewTransitionAnimator animator))
                    RunExitEffect(node, animator, null);
                else
                    Conclude(node, node._exitThen);
            }
        }

        // The mirror of an exit outside a view transition: the topmost node of a subtree that has just appeared
        // under a node already shown plays its animator's enter (with no transition; without an animator it simply
        // appears). A scene's first layout arrives nowhere, since nothing was shown before it, and neither do nodes
        // enabled inside Settle. Inside a view transition's update the transition enters the nodes of its scope.
        private static void StartArrivals()
        {
            // Collected first: an animator's Enter may run another pass, which reuses the pass list.
            var nodes = LayoutEngine.PassNodes;
            for (int i = 1; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (!node._arriving) continue;
                node._arriving = false;
                if (s_updating != null && InScope(s_updating, node)) continue;
                s_arrivals.Add(node);
            }
            for (int i = 0; i < s_arrivals.Count; i++)
            {
                var node = s_arrivals[i];
                if (node != null && node.TryGetComponent(out IViewTransitionAnimator animator))
                    RunEnterEffect(node, animator, null);
            }
            s_arrivals.Clear();
        }

        // Advances every in-place move, writes the eased rect and drives the fade riding on it.
        private static void TickTransitions(float deltaTime)
        {
            if (s_animating.Count == 0) return;
            for (int i = s_animating.Count - 1; i >= 0; i--)
            {
                var node = s_animating[i];
                if (node == null || !node.isActiveAndEnabled || !node._animating)
                {
                    s_animating.RemoveAt(i);
                    continue;
                }

                node._animElapsed += deltaTime;
                if (node._animElapsed >= node._animTransition.Total)
                {
                    s_animating.RemoveAt(i);
                    CompleteMove(node);
                    continue;
                }

                s_committing = node._passRoot;
                try
                {
                    // The move's frame: where its motion has it, how big, and how it looks.
                    var frame = node.CurrentFrame();
                    LayoutEngine.SetLook(node, frame);
                    LayoutEngine.Write(node, new Rect(frame.Centre - frame.Size * 0.5f, frame.Size));
                    LayoutEngine.WriteAlpha(node, node.EasedProgress());
                }
                finally
                {
                    s_committing = null;
                }
            }
        }

        // Puts a node at the end of its move: an exiting node leaves, any other rests there with its fade ended.
        private static void CompleteMove(LayoutNode node)
        {
            LayoutEngine.ResetLook(node);
            s_committing = node._passRoot;
            try
            {
                LayoutEngine.Write(node, node._animTo);
            }
            finally
            {
                s_committing = null;
            }
            node._animating = false;
            if (node._exiting)
                Conclude(node, node._exitThen);
            else
                LayoutEngine.EndFade(node);
        }

        // The node has left: its exit state is cleared, its fade is put back for its next showing, the object is
        // deactivated (so it is off screen at once, whatever the callback does with it; a Destroy would only take
        // effect at the end of the frame) and the caller's callback runs.
        private static void Conclude(LayoutNode node, Action then)
        {
            node._exiting = false;
            node._exitStarted = false;
            node._exitToken++;
            node._exitThen = null;
            node._animating = false;
            LayoutEngine.EndFade(node);
            RemoveExiting(node);
            node.gameObject.SetActive(false);
            then?.Invoke();
        }

        private static void RemoveExiting(LayoutNode node)
        {
            for (int i = s_exiting.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(s_exiting[i], node))
                    s_exiting.RemoveAt(i);
            }
        }

        private static bool Contains(List<LayoutNode> list, LayoutNode node)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], node))
                    return true;
            }
            return false;
        }

        private static int Depth(LayoutNode node)
        {
            int depth = 0;
            for (var t = node.transform.parent; t != null; t = t.parent)
                depth++;
            return depth;
        }

        private sealed class ReferenceComparer : IEqualityComparer<LayoutNode>
        {
            public static readonly ReferenceComparer Instance = new();
            public bool Equals(LayoutNode a, LayoutNode b) => ReferenceEquals(a, b);
            public int GetHashCode(LayoutNode node) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(node);
        }
    }
}
