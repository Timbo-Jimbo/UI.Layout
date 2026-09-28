using UnityEngine;
using UnityEngine.EventSystems;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// Takes the pointer for a scroll container (a <see cref="LayoutNode"/> whose <see cref="LayoutNode.Scroll"/> is
    /// not None) and hands it to the layout system: a press that stops it gliding, drags (rubber-banding past its ends)
    /// and flicks, and the mouse wheel or a trackpad. The system adds it to scroll containers only (hidden, and never
    /// saved), so a node that does not scroll never takes a drag from what is under it; it is not added by hand.
    /// </summary>
    /// <remarks>
    /// Drags keep UGUI's drag threshold, so a tap on a button inside the container still clicks. A pointer reaches it
    /// only through something drawn under the pointer inside the container (give the container a background to catch
    /// drags in the gaps between its children), and a child that takes drags itself keeps them. A drag that sets off
    /// mostly along an axis the container does not scroll is not taken. A press that stops a glide becomes the
    /// container's own (whatever took it is let go of at once, and it does not click), so its release comes here as a
    /// pointer-up. Only the left button (and touch) scrolls, as with ScrollRect.
    /// </remarks>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class LayoutScroller : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IPointerUpHandler, IScrollHandler
    {
        private LayoutNode _node;

        // The node it scrolls: the one on its own object.
        private LayoutNode Node
        {
            get
            {
                if (_node == null)
                    TryGetComponent(out _node);
                return _node;
            }
        }

        // Sent on the press to the first drag handler above what was pressed, before any drag: touch stops it.
        void IInitializePotentialDragHandler.OnInitializePotentialDrag(PointerEventData eventData) => LayoutSystem.OnScrollPress(Node, eventData);

        void IBeginDragHandler.OnBeginDrag(PointerEventData eventData) => LayoutSystem.OnScrollBeginDrag(Node, eventData);

        void IDragHandler.OnDrag(PointerEventData eventData) => LayoutSystem.OnScrollDrag(Node, eventData);

        void IEndDragHandler.OnEndDrag(PointerEventData eventData) => LayoutSystem.OnScrollEndDrag(Node, eventData);

        // Reaches it only for a press it took over by stopping a glide.
        void IPointerUpHandler.OnPointerUp(PointerEventData eventData) => LayoutSystem.OnScrollRelease(Node, eventData);

        void IScrollHandler.OnScroll(PointerEventData eventData) => LayoutSystem.OnScrollWheel(Node, eventData);
    }
}
