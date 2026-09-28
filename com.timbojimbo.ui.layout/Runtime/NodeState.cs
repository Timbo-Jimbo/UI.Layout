using UnityEngine;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// What the layout system keeps for one enabled <see cref="LayoutNode"/>: a spring per channel, each holding
    /// where layout puts it (its target, Core Animation's model) and where it is drawn with its velocity (its
    /// presentation), and which space its position lives in. Made when the node is enabled and dropped when it is
    /// disabled or destroyed; a pass places it the first time it meets it.
    /// </summary>
    internal sealed class NodeState
    {
        public readonly LayoutNode Node;
        public readonly RectTransform RectTransform;

        // Its centre and its width and height, both in Parent's layout space: origin at that rect's top-left corner,
        // x right and y down, in its units. Unused on a root, whose rect is its own.
        public readonly Spring Position = new(0.5f);
        public readonly Spring Size = new(0.5f);

        // Its opacity (1 shown, 0 not), and its scale around its centre on top of its size (1 none), in x.
        public readonly Spring Opacity = new(0.002f);
        public readonly Spring Scale = new(0.002f);

        // Whether a pass has placed it yet: until one has, it has nowhere it is drawn to move from.
        public bool Seen;

        // Its layout parent as it was last placed: the node whose layout space Position and Size are in (null for a
        // root). When a pass finds it under another, they are carried over into that one's space first.
        public NodeState Parent;

        // Whether the system drives its RectTransform (every node but a root), and the tracker that tells the editor
        // so: it shows those values as driven, and does not save them into the scene.
        public bool Owned;
        public DrivenRectTransformTracker Tracker;

        // Its CanvasGroup once it has one (its own, or one the system added when it first had to fade it or stop it
        // taking clicks): the system owns its alpha and whether it blocks raycasts.
        public CanvasGroup Group;

        // Its scroll offset and what moves it, once it has scrolled (its Scroll not None), kept if it stops: its
        // LayoutNode children are drawn moved by that offset. Null for a node that never has.
        public ScrollState Scroll;

        // ── This pass ────────────────────────────────────────────────────────────

        // Its layout parent as the pass found it (null for a root).
        public NodeState PassParent;

        // Its index in the pass's solver nodes, or -1 when it is out of layout (it or something above it Display None).
        public int PassIndex;

        // Whether it is not drawn as the pass starts with nothing it is seen to move from: never placed, back in layout
        // from None having faded out, or under something that is. What it is given goes there at once.
        public bool PassUnseen;

        // How many transforms are above it, for ordering roots (outer before inner).
        public int Depth;

        public NodeState(LayoutNode node)
        {
            Node = node;
            RectTransform = (RectTransform)node.transform;
        }
    }
}
