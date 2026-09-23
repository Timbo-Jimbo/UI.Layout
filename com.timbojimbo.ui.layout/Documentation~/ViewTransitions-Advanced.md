# View Transitions in depth

[← Back to View Transitions](ViewTransitions.md)

- [How a transition runs](#how-a-transition-runs)
- [The overlay](#the-overlay)
- [Draw order and grounding a cross-fade](#draw-order-and-grounding-a-cross-fade)
- [Persisting pairs](#persisting-pairs)
- [Carried nodes](#carried-nodes)
- [When a transition takes over another](#when-a-transition-takes-over-another)
- [Scopes](#scopes)
- [Sizes that can't animate](#sizes-that-cant-animate)
- [Roots and ScrollRects](#roots-and-scrollrects)
- [Clock, finishing and skipping](#clock-finishing-and-skipping)
- [Names and warnings](#names-and-warnings)

## How a transition runs

`LayoutSystem.StartViewTransition(scope, update, transition, types)`:

1. Any transition in flight whose scope overlaps this one's [hands over](#when-a-transition-takes-over-another) to it.
2. Pending layout is settled, so what is recorded is exactly what is on screen.
3. Every enabled node in the scope has its on-screen rect recorded, in world space so a node can be matched across parents and trees, together with its parent and its name.
4. `update` runs, and the new state is laid out at once.
5. The difference is sorted the way the web sorts its captured elements: names that moved from one node to another (pairs, or persisting pairs), persisting nodes moved to a new parent (carries), exits, and the topmost nodes that appeared (enters).
6. Pairs, persisting pairs and carries are lifted into [the overlay](#the-overlay) and flown; enters and exits fade or play their animator in place; every other node shown somewhere else moves there in place, starting from its recorded rect expressed in its parent's recorded frame, so a node that only rode its parent has nothing to do.

Moves tick in the same pre-render hook as layout. A layout pass during a transition leaves moves alone, and a changed target redirects a moving node with the move's own timing.

## The overlay

Pairs, persisting pairs and carried nodes are lifted out of their trees for the length of the transition into a plain rect kept as the last child of their root canvas (the **View Transition Layer**). A placeholder of the same size keeps each lifted node's slot, so the tree lays out exactly as before, and the slot, read live every frame, is where the flight lands: scrolling or a reflow during the transition is honoured. Each flight is a straight line from where the node was to its slot, however deeply it is nested and whatever its container is doing.

A lifted container keeps the layout its content was given for its final size, clipped by its own mask, the way a snapshot would; a persisting or carried node's content is laid out against its rect as it grows. Enters and exits play in place beneath the overlay, keeping their scroll-view clipping; a leaving node lets clicks through.

Nothing is left on an object afterwards but the CanvasGroup a fade added.

## Draw order and grounding a cross-fade

Things in the overlay stack in capture order: those that existed before the update in the old state's draw order, new ones after, as the web stacks its groups. What draws over what no longer depends on where the nodes sit in their trees. A named child of a named node draws above its parent's content, which is the web's rule too.

Two alpha fades let some of what's behind show through at the crossover (the web hides this by blending its snapshots with `plus-lighter`). To ground a card-to-page transition, build the card and the page as an unnamed container holding a **persisting background** node and then a **named body** with the content: the surface itself grows from the card into the page, opaque, while the bodies cross-fade over it. The background goes *before* the body, since groups stack in capture order.

## Persisting pairs

When a node flagged **View Transition Persist** and a node in the other state share a name, the two are not cross-faded. The old object (the kept one) is lifted and flies into the other's spot with its state intact while the other copy waits there, hidden. When the transition ends the two swap places: the kept object takes the copy's slot, and the copy takes the kept object's old slot, or goes with the old tree if that tree has been destroyed.

- Flagging one copy is enough: the flag travels with the kept object, so the trip back works too.
- A persisting object keeps its own sizing, so size it by its slot: a Grow node inside a slot node that each side sizes.
- After the swap, a reference to "the page's copy" points at the object now in the card, until the trip back swaps them again. If a screen needs to drive the object, [carry it](#carried-nodes) instead, or look it up through its slot when you use it.

## Carried nodes

A persisting node that the update moves to a new parent is carried: lifted, it flies from its old slot to its new one (its placeholder holds the new slot) and lands there. It's one object with no copy and no swap, so references to it are always right. It doesn't fade, and it isn't clipped by either screen while it flies.

## When a transition takes over another

A new transition hands over from each transition in flight whose scope overlaps its own. The old one finishes (its `Finished` fires), and each of its parts goes its own way:

| Part | What happens |
|---|---|
| A move inside the new scope | Stopped where it is drawn, recorded there, and restarted from there once the update has run, with the new timing. No jump. |
| A move outside the new scope | Plays on; the new transition waits for it. |
| A fade (appearing or leaving) | Plays on; a fade the new update restarts goes on from the alpha it has reached. |
| An enter or exit effect | Plays on; the new transition waits for it. |
| A flight inside the new scope | Lands at once and comes back down (the update may move it). |
| A flight outside the new scope | Flies on under the new transition. |

A node that is leaving isn't part of the new transition's picture: `Show` brings it back, and otherwise its exit plays out.

## Scopes

`StartViewTransition(scope, update)` records and animates only the subtree under `scope` (any component: a panel script, a node, a transform). The scope's own node never moves; it's the frame the transition happens in. Names are matched within the scope, so a duplicate name elsewhere doesn't matter.

Two transitions overlap when either has no scope, or one's scope contains the other's. Overlapping ones hand over; the rest run side by side.

Changes the update makes outside the scope apply at once (roots excepted, since they never animate). The editor warns about one, which usually means the scope should be the panel rather than the list inside it. Exits started outside the scope start the way they would outside any transition.

## Sizes that can't animate

Content that can't be drawn between two sizes takes its new size at once and animates only its position: text would re-wrap or clip at sizes in between. `ILayoutMeasurable.SizeIsAnimatable` says which is which; images, raw images and plain boxes resize smoothly. See [Content and UGUI](Content-and-UGUI.md).

## Roots and ScrollRects

A root's own rect never animates. It is the tree's contract with whatever holds it (a ScrollRect, a UGUI parent), which reads the transform straight away, so a scroll position set right after `StartViewTransition` is against the final content size. The same goes for a transition's scope node.

## Clock, finishing and skipping

- Moves follow the unscaled clock, so menus keep moving while the game is paused. Set `LayoutSystem.UseScaledTime` to have them follow `Time.timeScale` instead. `LayoutSystem.DeltaTime` is the step either way, for your own animators; View Transition Sequences ticks its sequences with it.
- `ViewTransition.Finished` fires when every flight has landed, every move has settled and every effect has called `done`. A handler added afterwards runs at once.
- `SkipTransition()` ends a transition at once: moves and flights land, effects are told to `Skip`, and exits conclude.
- `LayoutSystem.CurrentViewTransition` is the one started most recently that is still running.
- Outside play mode the update is simply applied.

## Names and warnings

- The same resolved name on two nodes in one state skips the transition with a warning, as on the web.
- Deactivating a node inside the update makes it vanish at once; the editor names it and points to `Exit`.
- A node outside the scope that the update moved jumped there; the editor names it.
