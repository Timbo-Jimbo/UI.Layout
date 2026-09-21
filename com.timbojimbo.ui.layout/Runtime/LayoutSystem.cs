using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// Schedules layout. Nodes marked dirty have their trees laid out on <see cref="Canvas.preWillRenderCanvases"/>,
    /// in edit and play mode, which is just before UGUI's own rebuild, so the graphics a commit resizes rebuild in
    /// the same frame. <see cref="ForceLayout"/> settles a tree immediately. The static state is reset on every
    /// play mode transition; the event subscription is made once per domain and kept.
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

        internal static bool IsCommitting(LayoutNode root) => ReferenceEquals(s_committing, root);

        /// <summary>Number of nodes currently moving through a transition.</summary>
        public static int TransitioningCount => s_animating.Count;

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
            TickTransitions();
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
        }

        // Advances every in-flight transition and writes the eased rect.
        private static void TickTransitions()
        {
            if (s_animating.Count == 0) return;
            float deltaTime = Time.unscaledDeltaTime;
            for (int i = s_animating.Count - 1; i >= 0; i--)
            {
                var node = s_animating[i];
                if (node == null || !node.isActiveAndEnabled || !node._animating)
                {
                    s_animating.RemoveAt(i);
                    continue;
                }

                node._animElapsed += deltaTime;
                bool done = node._animElapsed >= node.Transition.Duration;
                var rect = done ? node._animTo : node.CurrentVisual();

                s_committing = node._passRoot;
                try
                {
                    LayoutEngine.Write(node, rect);
                }
                finally
                {
                    s_committing = null;
                }

                if (done)
                {
                    node._animating = false;
                    s_animating.RemoveAt(i);
                }
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
