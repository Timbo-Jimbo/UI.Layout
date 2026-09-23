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
- **Hide in place** with `LayoutSystem.Hide(node)`, like CSS `visibility: hidden` beside `Exit`'s `display: none`: the node keeps its space but isn't drawn and takes no clicks, and to transitions it's gone, names and all. It fades out where it is, or flies into a node that appears with its name (see [matching by name](#matching-by-name)). `Show` brings it back.
- **Bring back something that is leaving** with `LayoutSystem.Show(node)`: its exit is cancelled and, inside a transition, it enters again (a fade goes back up from wherever it had got to; an exit effect is stopped and its enter effect plays). `SetActive(true)` can't do this, since a leaving object is still active.
- `node.Shown` reads "active, not leaving and not hidden", and setting it calls `Show` or `Exit`.

```csharp
LayoutSystem.StartViewTransition(() => panel.Shown = !panel.Shown);
```

Don't use `SetActive(false)` inside a transition: a disabled object can't be drawn, so it just vanishes. The editor warns when that happens.

Outside a transition, `Exit` still plays the node's [exit effect](#custom-enter-and-exit-effects) (or leaves at once without one), and a node appearing under something already on screen plays its enter effect. Only the transition makes everything else glide.

## Timing

A transition's timing is a `LayoutTransition`: a duration, an ease and an optional delay, a quarter second easing out by default.

```csharp
LayoutSystem.StartViewTransition(Change, LayoutTransition.Over(0.4f, EaseType.OutBack));
// A list item leaves first, then the list closes up:
LayoutSystem.StartViewTransition(() => LayoutSystem.Exit(item, () => Destroy(item.gameObject)),
    LayoutTransition.Over(0.25f).After(0.2f));
```

A **motion** says how things travel. Without one they go in a straight line. `ArcMotion` bends the path into a smooth arc that sets off along the shorter axis and arrives along the longer one, for panels that swing into place rather than slide; `Curved(c)` is the shorthand. Only what moves as a whole follows a motion: the parts inside a moving panel ride along with it.

```csharp
LayoutSystem.StartViewTransition(Change, LayoutTransition.Over(0.35f).Curved(0.6f));
```

Write your own by implementing `ITransitionMotion`, a pure function: given where the thing starts and ends and how far along it is, return a `MotionFrame` with its centre, size, a scale around its centre, an opacity, and a **morph**: how far its contents have gone from the old state to the new, 0 to 1. A matched pair's two halves blend by it, and the parts riding inside move from their old place to their new one by it. The default moves it with the eased progress, a cross-fade with the parts gliding into place; hold it at 0 and jump it to 1 while nothing shows, as the teleport does, and the contents stay pinned in their old arrangement until they appear in their new one. The engine is the only thing that touches the objects, and takes the look off when the move ends. Make it a `[Serializable]` class with a parameterless constructor and it appears in the inspector's Motion list; keep it stateless, since one instance may drive many moves. The GameUI demo's `TeleportMotion` (shrink and fade out, reappear at the target, pop and settle) is an example.

```csharp
[Serializable]
public sealed class PopMotion : ITransitionMotion
{
    public MotionFrame Evaluate(in MotionInput input)
    {
        var frame = MotionFrame.Straight(input);          // the straight line...
        frame.Scale = 1f + 0.2f * Mathf.Sin(input.Progress * Mathf.PI);   // ...swelling on the way
        return frame;
    }
}
```

A node can set its own **Transition** in the inspector: **Inherit** uses the call's timing, **Custom** sets its own (duration, ease, delay and motion). A Custom with no duration and no delay (`LayoutTransition.Instant` in code) makes that node snap while everything around it animates.

```csharp
knob.Transition = LayoutTransition.Over(0.18f);     // this node moves faster
badge.Transition = LayoutTransition.Instant;        // this one snaps
badge.Transition = null;                            // back to the call's timing
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
