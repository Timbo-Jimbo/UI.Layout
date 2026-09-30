using UnityEngine;
using UnityEngine.EventSystems;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// Takes the pointer for a scroll container (a <see cref="LayoutNode"/> whose <see cref="LayoutNode.Scroll"/> is
    /// not None) or for a drag owner's node (one with an <see cref="ILayoutDraggable"/>), and hands it to the layout
    /// system: a press that stops a glide or that a moving owner takes hold of, drags (rubber-banding past a container's
    /// ends) and flicks, and the mouse wheel or a trackpad. The system adds it to those nodes only (hidden, and never
    /// saved), so a node that neither scrolls nor has an owner never takes a drag from what is under it; it is not added by
    /// hand.
    /// </summary>
    /// <remarks>
    /// UGUI gives all of a drag to the innermost drag handler under the press, which is this one; the system shares it
    /// with the scroll containers and owners up the hierarchy from here, as far as the first drag handler that is not the
    /// system's. A drag is locked to the axis it sets off along (both, when the first of them takes both) and goes to those
    /// that take that axis: a sideways drag on a strip of tiles inside a vertical list scrolls the strip, an up-and-down
    /// one the list. When none does, it goes to that other drag handler as it sets off, or, with none, to those that take
    /// the other axis, by how far it goes that way. Inside an owner whose <see cref="ILayoutDraggable.PassOnMidDrag"/> is
    /// true, one drag scrolls a container to its end and moves the owner with the rest, and a glide that runs into the end
    /// carries on into it; with false, each drag is one participant's, chosen as it sets off: the innermost with room to
    /// move that way, or with none, the innermost of all, to rubber-band. Drags keep UGUI's drag threshold, so a tap on a
    /// button inside still clicks, and a press that dragged never does. A pointer reaches it only through something drawn
    /// under the pointer (give a container a background to catch drags in the gaps between its children), and a child
    /// that takes drags itself keeps them. A press that stops something, or that an owner takes hold of, becomes this
    /// one's own (whatever took it is let go of at once, and it does not click), so its release comes here as a
    /// pointer-up. Only the left button (and touch) drags, as with ScrollRect. The wheel goes to the containers along its
    /// own axis, innermost first, and what they leave to the next scroll handler above.
    /// </remarks>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class LayoutScroller : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IPointerUpHandler, IScrollHandler
    {
        private LayoutNode _node;

        // The node it takes the pointer for: the one on its own object.
        private LayoutNode Node
        {
            get
            {
                if (_node == null)
                    TryGetComponent(out _node);
                return _node;
            }
        }

        // Sent on the press to the innermost drag handler under it, before any drag: touch stops what glides above it, and
        // a moving owner above it may take hold of it.
        void IInitializePotentialDragHandler.OnInitializePotentialDrag(PointerEventData eventData) =>
            LayoutSystem.OnPointerPress(this, Node, eventData);

        void IBeginDragHandler.OnBeginDrag(PointerEventData eventData) => LayoutSystem.OnDragSetOff(this, Node, eventData);

        void IDragHandler.OnDrag(PointerEventData eventData) => LayoutSystem.OnDragMove(eventData);

        void IEndDragHandler.OnEndDrag(PointerEventData eventData) => LayoutSystem.OnDragEnd(eventData);

        // Reaches it for a press it took over by stopping or catching something, and for a press on a Button that shares
        // its object (which the system ignores, as it does a pointer-up sent here for a press another one took over).
        void IPointerUpHandler.OnPointerUp(PointerEventData eventData) => LayoutSystem.OnPointerUp(this, eventData);

        void IScrollHandler.OnScroll(PointerEventData eventData) => LayoutSystem.OnWheel(Node, eventData);
    }
}
