using UnityEngine;
using UnityEngine.EventSystems;

namespace TimboJimbo.UI.Layout
{
    /// <summary>
    /// Something a drag moves that is not a scroll offset (a sheet's height, a card's pull to dismiss), on a component on
    /// the <see cref="LayoutNode"/> it moves. It takes part in the drags on its node and on everything inside it, with the
    /// scroll containers inside it: sharing each drag and flick with them (<see cref="PassOnMidDrag"/> true, the usual
    /// choice), offered each move first, before they scroll, and then what they leave, and taking what it wants of each;
    /// or each drag going whole to the innermost of them, it included, with room to move the way the drag sets off
    /// (false). Let go, it is given the pointer's velocity if it moved last and, sharing, the speed a glide inside it runs
    /// into an end with, as UIKit hands a sheet or an interactive dismiss the rest of a scroll. The layout system never
    /// catches or flings it: that is its own code's, in <see cref="OnBeginDrag"/> and <see cref="OnRelease"/>.
    /// </summary>
    /// <remarks>
    /// A drag goes up the hierarchy from where it was pressed, as far as the first drag handler that is not the layout
    /// system's (a ScrollRect, say), and is locked to the axis it sets off along: it takes part when its
    /// <see cref="DragAxis"/> includes that axis. Several in one chain are offered each move outermost first; when one
    /// with <see cref="PassOnMidDrag"/> false is among them, the innermost such one decides over everything inside it, and
    /// the drag is not shared. Scroll containers need no code to take part.
    /// </remarks>
    public interface ILayoutDraggable
    {
        /// <summary>Which way drags move it: Vertical, Horizontal, Both, or None to take no part (now).</summary>
        ScrollAxis DragAxis { get; }

        /// <summary>
        /// Whether a drag passes between it and the scroll containers inside it part way through. True, the usual choice
        /// (an iOS sheet, the App Store's cards): one drag scrolls a list inside it to its end and moves it with the rest,
        /// and a glide that runs into the list's end hands it its speed. False: each drag is one participant's until the
        /// pointer lets go, chosen as it sets off, as nested UIScrollViews give a drag to the innermost that can scroll
        /// that way. An owner that took hold of the press it follows from (<see cref="TakesHold"/>) has it; otherwise it is
        /// the first participant, innermost first from the press up to this one, with room to move the way the drag sets
        /// off within its own range: a scroll container not at its end that way, which scrolls it, rubber-banding at its
        /// ends, or an owner whose <see cref="HasRoom"/> says so. With nothing that has room it is the innermost's, to band
        /// past its end: a list at its bottom bounces in a sheet at full, while a drag on the sheet's header, with nothing
        /// inside it, stretches the sheet. Whether it is moving plays no part. A glide inside it that runs into an end bounces there. Read as a drag sets off and as a
        /// scroll inside it is let go of, so a change takes effect with the next drag.
        /// </summary>
        bool PassOnMidDrag { get; }

        /// <summary>
        /// Whether it can move the way <paramref name="direction"/> goes within its own range, in the units and signs
        /// <see cref="OnDrag"/> is offered moves in (its node's parent's, y up, on the axes it moves along in this drag):
        /// a sheet below its highest detent has room up and one at it has none, and a card pulled smaller has room up and
        /// a whole one has none. Its rubber band is not room: past its range it has none further that way. Asked as a drag
        /// that is one participant's sets off (see <see cref="PassOnMidDrag"/>), before it is begun, for the drag to go to
        /// the innermost that has room; a drag that is shared never asks it.
        /// </summary>
        bool HasRoom(Vector2 direction);

        /// <summary>
        /// A press landed on its node or on anything inside it while its node is moving (its position, size or scale):
        /// whether it takes hold of the press, as touching a moving UIScrollView stops it. True, it is told
        /// <see cref="OnBeginDrag"/> at once, to stop where it is drawn; the press is then only a stop, as one on a gliding
        /// list is (it does not click), and a drag that sets off from it and is one participant's (see
        /// <see cref="PassOnMidDrag"/>) is this one's. False leaves the press alone, to click what is under it, and a
        /// drag from it goes where it would anyway: a sheet takes hold of a press on its header and not of one on a row
        /// of its list, so a row tapped while the sheet settles still clicks. Where the press landed is the press's
        /// rawPointerPress (the object under it). Not asked while its <see cref="DragAxis"/> is None; and a node on its
        /// way out (hidden inside Animate), or moving for an Animate made with interactive false, takes no pointer, so no
        /// press reaches it.
        /// </summary>
        bool TakesHold(PointerEventData press);

        /// <summary>
        /// A drag it takes part in set off, or it took hold of a press (<see cref="TakesHold"/>), once for both: stop what
        /// it is doing where it is drawn (<see cref="LayoutNode.Catch"/>), for the drag to move it from there.
        /// <see cref="OnRelease"/> always follows, unless it is disabled or destroyed first.
        /// </summary>
        void OnBeginDrag();

        /// <summary>
        /// Offered a move of a drag it takes part in, in its node's parent's units (y up, as an Offset), on the axes it
        /// moves along in this drag: <paramref name="first"/>, before the scroll containers inside it take any of it, and
        /// then again with what they left (back to back, when the drag is its alone). Returns how much of it it took, which
        /// is kept within what it was offered; what it leaves goes on to what is above it. The rubber band is its own: a
        /// move it takes with no room for it is for it to band (<see cref="LayoutSystem.RubberBand"/> gives the scroll
        /// containers' feel) or limit.
        /// </summary>
        Vector2 OnDrag(Vector2 move, bool first);

        /// <summary>
        /// Let go at <paramref name="velocity"/>, in world units a second (what <see cref="LayoutNode.Fling"/> takes): the
        /// drag it took part in ended (the pointer's velocity if it moved last, zero otherwise), or, with no drag and
        /// <see cref="PassOnMidDrag"/> true, a glide inside it ran into its end with that much speed left (the way its
        /// content was moving). Returns whether it took the velocity; one it does not take goes on to what is above it, or
        /// bounces the scroll.
        /// </summary>
        bool OnRelease(Vector2 velocity);
    }
}
