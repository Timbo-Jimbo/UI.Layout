# View Transitions

[← Back to the README](../../README.md)

- [The idea](#the-idea)
- [What happens to each thing](#what-happens-to-each-thing)
- [Showing and hiding](#showing-and-hiding)
- [Timing](#timing)
- [Matching by name](#matching-by-name)
- [Names in lists](#names-in-lists)
- [Custom enter and exit effects](#custom-enter-and-exit-effects)
- [Content that was already there: Settle](#content-that-was-already-there-settle)
- [Spawned pages](#spawned-pages)
- [Keeping an object alive across the jump](#keeping-an-object-alive-across-the-jump)
- [Running more than one](#running-more-than-one)
- [When to wrap](#when-to-wrap)
- [Glossary](#glossary)

## The idea

Layout is instant. A view transition makes one change animated as a whole:

```csharp
var vt = LayoutSystem.StartViewTransition(() =>
{
    // any ordinary change: add, remove, reorder, reparent, resize, show a page
});
vt.Finished += () => Debug.Log("done");
```

It notes where everything is shown, runs your change, lays the new state out at once, and then animates from the old picture to the new one. The real objects move (nothing is snapshotted), so anything animating on them keeps going. `LayoutRect` is always where a node is going; `VisualRect` and `IsTransitioning` say where it is on the way.

It's modelled on the web's [`document.startViewTransition`](https://developer.mozilla.org/docs/Web/API/View_Transition_API).

## What happens to each thing

- **Moves.** A node that ends up somewhere else travels there from where it was, whatever parent or tree it moved to. A node that only rode its parent has nothing of its own to do.
- **Appears.** A node that wasn't shown before fades in where it lands (only the topmost node of what appeared; the nodes inside it ride with it).
- **Leaves.** A node handed to `LayoutSystem.Exit` fades out where it was while the rest closes up.
- **Matched by name.** A node that appears with the [name](#matching-by-name) of one that was there before flies in from its spot.
- **Kept alive** and **carried.** See [keeping an object alive](#keeping-an-object-alive-across-the-jump).

Text keeps its final size and only moves (it would re-wrap at sizes in between); images and plain boxes resize smoothly.

## Showing and hiding

- **Show** with `SetActive(true)`, or `LayoutSystem.Show(node)`.
- **Take out** with `LayoutSystem.Exit(node, onExited)`. The node plays its exit while the rest closes up, then its object is deactivated and `onExited` runs (destroy it, pool it, or leave it for next time).
- **Hide in place** with `LayoutSystem.Hide(node)`, like CSS `visibility: hidden` beside `Exit`'s `display: none`: the node keeps its space but isn't drawn and takes no clicks, and to transitions it's gone, names and all. It plays its animator's exit where it is (or fades out there without one), or flies into a node that appears with its name (see [matching by name](#matching-by-name)). `Show` brings it back. Hiding and then exiting is how a list item plays out before the list closes up: hide it, and when that transition finishes, exit it; it has gone from view already, so it plays no second exit and the rest closes up at once.
- **Bring back something that is leaving** with `LayoutSystem.Show(node)`: its exit is cancelled and, inside a transition, it enters again (a fade goes back up from wherever it had got to; an exit effect is stopped and its enter effect plays). `SetActive(true)` can't do this, since a leaving object is still active.
- `node.Shown` reads "active, not leaving and not hidden", and setting it calls `Show` or `Exit`.

```csharp
LayoutSystem.StartViewTransition(() => panel.Shown = !panel.Shown);
```

Don't use `SetActive(false)` inside a transition: a disabled object can't be drawn, so it just vanishes. The editor warns when that happens.

Outside a transition, `Exit` still plays the node's [exit effect](#custom-enter-and-exit-effects) (or leaves at once without one), and a node appearing under something already on screen plays its enter effect. Only the transition makes everything else glide.

## Timing

A transition's timing is a `LayoutTransition`: a duration and an optional delay, with a **motion** saying how things travel. `Over(duration, ease)` is a straight slide on that ease; the default is a quarter second easing out.

```csharp
LayoutSystem.StartViewTransition(Change, LayoutTransition.Over(0.4f, EaseType.OutBack));
// A list item leaves first, then the list closes up:
LayoutSystem.StartViewTransition(() => LayoutSystem.Exit(item, () => Destroy(item.gameObject)),
    LayoutTransition.Over(0.25f).After(0.2f));
```

A `LayoutMotion` has two phases: **Depart** takes a node out of where it was and **Arrive** brings it into where it is going, with an optional **Gap** between them. Each phase can **move** it along the path, **fade** it and **scale** it (to nothing departing, from nothing arriving), on its own **ease**. **Midpoint** says where in the duration the departure ends.

- When both phases move they share the path half each, wherever the midpoint is: with a midpoint of 0.25 the node is halfway there a quarter of the way through.
- When only one moves it covers the whole path, and the other holds still: a **Drop** (no departure, an arrival that moves and fades in) falls into place from where it came from.
- When neither moves the node jumps across in the gap: a teleport. **Pop**, **Soft**, **Blink**, **Snap** and **Wobble** are teleports with different eases, fades and pauses.
- **Curvature** bends the path into an arc that sets off along the shorter axis and arrives along the longer one; `Curved(c)` is the shorthand.

A matched pair's two halves, and the parts riding inside a travelling node, change from their old state to their new one as the path is travelled: gradually while the node moves, at once where it jumps, so a teleport's contents appear already in their new arrangement. Only what moves as a whole follows a motion: the parts inside a moving panel ride along with it. A node entering or leaving where it stands is not shaped; its fade follows the phases' eases.

```csharp
LayoutSystem.StartViewTransition(Change, LayoutTransition.Over(0.35f).Curved(0.6f));      // an arc
LayoutSystem.StartViewTransition(Change, LayoutTransition.Over(0.5f).With(LayoutMotion.Pop));   // a teleport
var fall = new LayoutMotion                                                                  // your own
{
    Midpoint = 0.3f,
    Depart = new MotionPhase(move: true, fade: false, scale: false, EaseType.InQuad),
    Arrive = new MotionPhase(move: true, fade: false, scale: false, EaseType.OutBounce),
};
```

A node can set its own **Transition** in the inspector, in two parts that inherit separately. Its timing: **Inherit** uses the call's, **Custom** sets its own duration and delay; a Custom with no duration and no delay (`LayoutTransition.Instant` in code) makes that node snap while everything around it animates. Its **Motion**: **Inherit** uses the call's, **Custom** sets its own, starting from a preset or field by field.

So the motion usually belongs to the interaction that makes the change (a switch arcs, a choice pops), passed with the call, and a node sets its own only when it should travel differently from what moves around it: a wallet that arcs through a tab switch that teleports, at the switch's pace.

```csharp
knob.Transition = new LayoutTransition { Duration = 0.18f };  // this node moves faster, however the call has it travel
badge.Transition = LayoutTransition.Instant;                    // this one snaps
badge.Transition = null;                                        // back to the call's timing
wallet.Motion = LayoutMotion.Slide().Curved(0.5f);              // arcs at the call's pace, whatever the call's motion
wallet.Motion = null;                                           // back to the call's motion
```

A setting for the whole UI, whoever authored the moves, goes in `LayoutSystem.AdjustTransition`: a function that gets each node and the timing and motion it resolved, and returns what it moves with instead. A "reduce motion" option is the classic use:

```csharp
LayoutSystem.AdjustTransition = reduceMotion
    ? (node, t) => t.With(LayoutMotion.Slide())      // no arcs or teleports, same timing
    : null;                                          // everything as authored
```

## Matching by name

Give two nodes the same **View Transition Name** and a transition treats them as one thing: when the name moves from one to the other, the new one flies in from the old one's spot while the old one flies out to meet it, the two cross-fading. A thumbnail becomes a header, a card's title becomes a page's title.

If the old one stays in the page, it stays put and the new one grows out of it. To make it look as if it has gone into the new one instead, like a store card that opens into a dialog over the store, `Hide` it in the same update: it lifts off, flies into the dialog, and waits in its slot unseen, so nothing reflows. `Show` it when the dialog closes and the dialog shrinks back and lands onto it. A hidden node carries no names, so the card and the dialog can share theirs.

```csharp
// Open: the card goes into the dialog.
LayoutSystem.StartViewTransition(() => { LayoutSystem.Hide(card); LayoutSystem.Show(dialog); });
// Close: the dialog goes back into the card.
LayoutSystem.StartViewTransition(() => { LayoutSystem.Show(card); LayoutSystem.Exit(dialog); });
```

A name found in only one of the two states does nothing special. The same name on two nodes at once skips the whole transition with a warning.

A node with no counterpart can still come from somewhere. Give it a **View Transition Origin**, any `RectTransform`, and it grows out of the origin's rect when it enters, as though its other half were just that rect, and undoes that when it leaves, shrinking back into the origin. Leaving is a move of its own, not the entrance rewound: what faded in fades out, but on the same ease running forwards in time, so it keeps pace with everything else in that transition (a page shrinking into its tile beside the tile's own parts). The origin takes no part: it doesn't move, hide or need a name. A popover opening out of its button is the classic case; with a `LayoutMotion.Drop` it falls out of the button fading in, and rises back into it on the way out. A name pairing the node wins over its origin.

```csharp
popover.ViewTransitionOrigin = button.RectTransform;   // scene data, or set just before showing (a menu from the row clicked)
popover.Motion = LayoutMotion.Drop;
LayoutSystem.StartViewTransition(() => LayoutSystem.Show(popover));   // out of the button
LayoutSystem.StartViewTransition(() => LayoutSystem.Exit(popover));   // back into it
```

While a node flies, its rect goes from one size to the other, and by default its content keeps the layout it has, so a growing node uncovers its content and a shrinking one clips it. That suits a card opening into a page, where the two are laid out differently. For two halves laid out alike at different sizes, such as a compact wallet and a detailed one, set **View Transition Fit** to **Scale** on both: each keeps its own layout and is scaled to the moving rect, a morph, like the web's scaled snapshots. It scales uniformly, by the width. A named part inside still flies its own way, a kept or carried object always resizes, and it works for a node growing out of its origin too.

```csharp
compactWallet.ViewTransitionFit = ViewTransitionFit.Scale;
detailedWallet.ViewTransitionFit = ViewTransitionFit.Scale;
```

For two halves of different shapes, a uniform scale can't fill the rect: a wide tile scaled to a tall page's width is still far shorter than the page. **Stretch** scales each axis on its own so each half fills the moving rect exactly, its content stretched and squashed on the way, like the web's `object-fit: fill`. It suits a tile opening into a page, where the two read as one thing growing.

```csharp
tile.ViewTransitionFit = ViewTransitionFit.Stretch;
page.ViewTransitionFit = ViewTransitionFit.Stretch;
```

## Names in lists

A list made from one prefab would have the same names many times over. Author the names in the prefab ("avatar", "title"), and when you bind an instance to its item, set **View Transition Scope** on its root to the item's id: every name below it becomes `id/avatar`, `id/title`. Give the detail page the same scope when you open it, and its parts pair with that one card's.

```csharp
card.Node.ViewTransitionScope = item.Id;     // when the card is bound
page.Node.ViewTransitionScope = item.Id;     // when the page opens
```

## Custom enter and exit effects

Without anything extra, appearing things fade in and leaving things fade out. For your own effect, put an `IViewTransitionAnimator` on the node's object: the engine calls `Enter` and `Exit` with a `done` to call when the effect ends, and `Skip` when it should stop at once.

With the Sequencer package, **View Transition Sequences** does this with authored sequences: point **Enter** and **Exit** at sequences that tween the node's `Offset`, a CanvasGroup's alpha, a scale, anything.

**Types** say what kind of change a transition is, so effects can differ by direction:

```csharp
LayoutSystem.StartViewTransition(ShowNextTab, Switch, forward ? "forward" : "back");
```

In **View Transition Sequences**, **By Type** rules pick other sequences for a type ("forward": enter from the right, exit to the left; "back": the reverse); the first rule that matches wins, and anything else plays the default Enter and Exit. In your own animator, read `transition.HasType("back")`, and `transition.TransitionFor(node)` for the timing the moves use. The transition is null outside one. Advance your effect by `LayoutSystem.DeltaTime` so it keeps to the same clock as the moves.

## Content that was already there: Settle

Only a node appearing under something already on screen enters. A whole new page and everything in it arrives as one: the page enters, the rows inside it ride along. A scene's first layout arrives nowhere.

When you fill a container that's already showing with content that "was already there" (a list restored from a save, a feed rebuilt in an open page), open a `Settle` scope so the new nodes just appear:

```csharp
using (LayoutSystem.Settle(feed))
    RebuildFeed();
```

## Spawned pages

The two states only have to exist for the length of the transition. Instantiate and bind the page inside the update, and `Exit` it with a destroy callback on the way back:

```csharp
// Open: a page spawned for the item. Its parts pair with the card's.
LayoutSystem.StartViewTransition(() =>
{
    page = Instantiate(pagePrefab, parent);
    page.Bind(item);                      // sets the page's scope to the item's id
    LayoutSystem.Exit(list);
});

// Back: the page leaves and is destroyed once it has gone; the list returns and the parts fly home.
LayoutSystem.StartViewTransition(() =>
{
    LayoutSystem.Exit(page.Node, () => Destroy(page.gameObject));
    list.gameObject.SetActive(true);
});
```

## Keeping an object alive across the jump

A matched pair is two objects cross-fading. Sometimes you need one object to survive the jump with its state: a playing video, a running animation.

**Move it.** Flag the object **View Transition Persist** and move it to its new parent inside the update. It's lifted above everything and carried from its old slot to its new one, the same object throughout, so every reference to it stays right:

```csharp
LayoutSystem.StartViewTransition(() =>
{
    card.Media.transform.SetParent(page.MediaSlot, false);   // Media is flagged Persist
    LayoutSystem.Exit(list);
    page.gameObject.SetActive(true);
});
pageControls.Target = card.Media;     // the same object for its whole life
```

On the way back, move it home before the page leaves. Put it in an empty slot node on each side, and size it Grow so it fills whichever slot it is in.

**Or swap copies.** When both screens have their own copy with the same name and you can't reach both from one place, flag the copy that holds the state as Persist. The kept object flies into the other copy's spot and the two swap places when the transition ends. Flagging one copy is enough. References to "the other copy" point at the other object until the trip back. See [in depth](ViewTransitions-Advanced.md#persisting-pairs).

## Running more than one

A new transition over the same things **takes over** the one in flight: things keep moving from where they are, with no jump. That makes rapid clicks feel smooth.

Transitions in unrelated parts of the UI shouldn't touch each other at all. Give a transition a **scope**, and only that subtree is animated; a transition in a subtree that doesn't overlap plays on untouched.

```csharp
LayoutSystem.StartViewTransition(this, () => AddAlert(alert));     // this panel only
```

The scope's own node doesn't move, and changes outside it apply at once. The editor warns if your change moved something outside the scope, which means the scope should be wider. Without a scope, a transition covers the whole UI.

## When to wrap

Wrap a change when it should be *seen* as a change in what is there or where it is: things appearing, leaving, moving, one screen becoming another. Use a plain tween or sequence for effects that aren't layout changes: a press pulse, a highlight, a shake.

## Glossary

- **Root**: the top node of a tree, whose parent has no Layout Node. A root's own rect never animates, because a ScrollRect or a UGUI parent reads it straight away.
- **Moves**: nodes that ended up somewhere else and travel there.
- **Appears / leaves**: nodes that entered (the topmost of what appeared) or were handed to `Exit`.
- **Matched by name**: two nodes with the same View Transition Name, one flying into the other's place.
- **Kept alive / carried**: an object flagged Persist that survives the jump.
- **Scope** (of a transition): the subtree a transition animates.
- **View Transition Scope** (of a node): the prefix for the names below it, usually an item's id.
- **Overlay**: where things matched by name, kept alive or carried fly, above everything else. See [in depth](ViewTransitions-Advanced.md#the-overlay).
