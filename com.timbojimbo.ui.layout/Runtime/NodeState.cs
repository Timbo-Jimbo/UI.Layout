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

        // Its opacity (its Opacity, times 0 while it is away with its effect fading), and its scale around its centre
        // on top of its size (its Scale), in x.
        public readonly Spring Opacity = new(0.002f);
        public readonly Spring Scale = new(0.002f);

        // How far it is shown, in x: 1 while its own Display is Visible, 0 otherwise. Only how it is drawn goes by it,
        // through its DisplayEffect (the shrink, and the slide of Away times 1 - shown), and ShownChanged reports it.
        public readonly Spring Shown = new(0.002f);

        // Whether a pass has placed it yet: until one has, it has nowhere it is drawn to move from.
        public bool Seen;

        // Whether its own Display was Visible when it was last placed, so a pass can tell it has changed (its opacity
        // cannot say, with its effect not fading).
        public bool Visible;

        // Whether it is on its way out: its Display stopped being Visible inside Animate while it was drawn, and it is
        // drawn until it has gone (its springs all at rest, or, Riding, nothing above it leaving). Riding: it was
        // hidden in the same change as something above it, or while something above it was leaving, so it plays nothing
        // of its own and goes with that. Thrown: it was moving from a fling as it was hidden, so its position was sent
        // on the way it was going, once, and Place leaves its position and size there, and its content laid out as it
        // was, until it is shown again (as it does for a follower whose pair ended early, left where it was drawn).
        // EffectOff: it is drawn without its slide and effect scale (a follower whose pair ended early).
        public bool Leaving;
        public bool Riding;
        public bool Thrown;
        public bool EffectOff;

        // Whether what last set its position moving for no change was a fling (Fling), rather than its away move handed
        // over outside a change (UpdateAway), which sets one off for no change too. Only a fling's motion is thrown on
        // as the node is hidden: a handover's is only the rest of a slide it was drawn partway through.
        public bool Flung;

        // The move that takes it just past its parent's rect as drawn on its DisplayEffect's edge (zero with none),
        // which it is drawn moved by as much as it is not shown, in Parent's layout space. Kept rather than worked out
        // each time: a change to it while it is partly shown hands the drawn part to Position (see UpdateAway), and it
        // stays as it is while a catch holds Shown.
        public Vector2 Away;

        // Its layout parent as it was last placed: the node whose layout space Position and Size are in (null for a
        // root). When a pass finds it under another, they are carried over into that one's space first.
        public NodeState Parent;

        // For a root, the nearest enabled node above its transform (null when there is none): the root's place follows
        // that node's, so what a pass works out down the layout parents (PassShown, PassBlocked) carries on through it.
        // Found as the roots are collected; left as it was once the node is not a root, when only Parent is read.
        public NodeState Above;

        // What was changed on it to fly it above everything while it moves for a change, put back as it lands (null
        // while it is not flying); and, from its reparenting inside a change until it boards, the layout parent it was
        // moved out from under, where it drew before the change, which the flight layer is ranked by.
        public FlightState Flight;
        public NodeState FlewFrom;

        // The node that took over from it by name (MatchName and MatchId, LayoutSystem.Match.cs) and that it follows
        // until their pair lands, or until it is shown again with no new pair: it is drawn at that node's rect, fading
        // out beneath it, its own springs carrying on unseen. Null while it follows nothing. A node followed never
        // follows another itself.
        public NodeState Follows;

        // The node it grows out of or shrinks back into while its DisplayEffect plays (null otherwise): one that held its
        // name and stayed shown as it was shown or hidden inside Animate (LayoutSystem.Match.cs). That node's rect is its
        // away pose, in place of its effect's edge and shrink. AnchorCentre and AnchorSize are where that node was last
        // drawn, in Parent's layout space, read as this one is written (once every tree has been, as a follower is).
        public NodeState Anchor;
        public Vector2 AnchorCentre;
        public Vector2 AnchorSize;

        // Whether the system drives its RectTransform (every node but a root), and the tracker that tells the editor
        // so: it shows those values as driven, and does not save them into the scene.
        public bool Owned;
        public DrivenRectTransformTracker Tracker;

        // Its CanvasGroup once it has one (its own, or one the system added when it first had to fade it or stop it
        // taking clicks): the system owns its alpha and whether it blocks raycasts.
        public CanvasGroup Group;

        // Which of its values a catch (Catch) stopped while they were moving: each stays where it was stopped, rather
        // than go where layout or its properties put it, until an Animate changes the node or it is given something new
        // for that value alone (HeldSize, HeldOpacity, HeldScale and HeldShown are what it was given when it was
        // caught). Position needs no hold: Catch moves its Offset.
        public bool HoldSize;
        public bool HoldOpacity;
        public bool HoldScale;
        public bool HoldShown;
        public Vector2 HeldSize;
        public Vector2 HeldOpacity;
        public Vector2 HeldScale;
        public Vector2 HeldShown;

        // Whether it holds any of them, which holds what is inside it where it was stopped too.
        public bool Held => HoldSize || HoldOpacity || HoldScale || HoldShown;

        // The shown value ShownChanged was last raised with (none yet, so the first frame it is placed raises it), and
        // whether it is waiting to be raised this frame.
        public float RaisedShown = float.NaN;
        public bool ShownQueued;

        // Its scroll offset and what moves it, once it has scrolled (its Scroll not None), kept if it stops: its
        // LayoutNode children are drawn moved by that offset. Null for a node that never has.
        public ScrollState Scroll;

        // Its drag owner (an ILayoutDraggable on its object) as the last pass found it, looked up each pass as its content
        // is, so one added or removed is met with the next frame; null for none. It takes part in drags only while it is
        // enabled.
        public ILayoutDraggable Draggable;

        // The input component on its object that takes the pointer for it while it scrolls or has an enabled drag owner,
        // which the system added (hidden and never saved) or found there; kept, turned off, while it has neither.
        public LayoutScroller Scroller;

        // ── This pass ────────────────────────────────────────────────────────────

        // Its layout parent as the pass found it (null for a root).
        public NodeState PassParent;

        // Its index in the pass's solver nodes, or -1 when it is out of layout (it or something above it Display None).
        public int PassIndex;

        // Whether it is not drawn as the pass starts with nothing it is seen to move from: never placed, back in layout
        // from None having left, or under something that is. What it is given goes there at once (the topmost of them
        // shown inside Animate plays its entrance from there).
        public bool PassUnseen;

        // Whether something above it is held (Catch): it stays where it was stopped with it, as seen inside it.
        public bool PassFrozen;

        // Whether it or something above it is on its way out (Leaving): what is inside a leaving node is drawn, not
        // come back from nothing, and what is hidden inside it rides it out.
        public bool PassLeaving;

        // Whether it or something above it starts being drawn in this pass's change, shown while it was not drawn:
        // what is shown inside it in the same change comes with it rather than playing its own effect.
        public bool PassAppearing;

        // Whether its own Display and that of everything above it (on through Above, for a root) are Visible: a flight
        // that is not shown drops out of the flight layer.
        public bool PassShown;

        // Its name (MatchName) and effective id this pass: its own MatchId, or else the nearest one above it (on through
        // Above, for a root). While it is shown it holds them, and a node shown with them inside Animate takes over from
        // where it is drawn, or it from where that one is.
        public string PassName;
        public object PassId;

        // PassShown, PassName and PassId as the pass before found them: in Animate's pass, as they were before the
        // change, so it can tell what the change started and stopped showing under each name.
        public bool WasShown;
        public string WasName;
        public object WasId;

        // Whether it or anything above it (on through Above) was last written as taking no pointer. A flight's canvas
        // stops UGUI looking further up than it for groups that do not block raycasts, so its own group asks this.
        public bool PassBlocked;

        // Its transform's sibling index as this pass found it, and as the pass before did: in Animate's pass, where it
        // was before the change, which the flight layer is ranked by.
        public int PassSibling;
        public int WasSibling;

        // How many transforms are above it, for ordering roots (outer before inner).
        public int Depth;

        public NodeState(LayoutNode node)
        {
            Node = node;
            RectTransform = (RectTransform)node.transform;
        }
    }
}
