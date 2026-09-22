# Timbo Jimbo - UI Layout

A layout engine for UGUI, modelled on [Clay](https://github.com/nicbarker/clay).

📐 **One Component**

Add a `LayoutNode` to each `RectTransform` you want laid out. The node whose parent has no node is the root; the engine owns everything below it.

🧮 **Pure Pass**

Layout is computed from the nodes' settings and the leaves' measurements, and the `RectTransform`s are written once at the end. Nothing is read back from the transforms, so offsets and animation can sit on top without fighting the layout.

📏 **Clay's Model**

Fit, Grow, Fixed and Percent sizing per axis with min and max, padding, gap, direction, child alignment, floating elements and aspect ratios. No margins, no child wrapping.

🔌 **UGUI Friendly**

A node measures stock UGUI components (LayoutElement, Image, TextMeshPro) as its content with no adapter, and `LayoutRootBridge` lets a tree sit inside a UGUI layout group or a ScrollRect.

🧩 **Styling Friendly**

Every setting is a plain serialized field with a dirtying setter, so it works with animation, the Property Bindings package and the Styling package out of the box.

# Installation

This package is available on [OpenUPM](https://openupm.com/packages/com.timbojimbo.ui.layout)

1. Add the Scoped Registry:
	- Open **Edit > Project Settings > Package Manager**
	- Add a new Scoped Registry (or append the missing scope if you already have one):
		- Name: `OpenUPM`
		- URL: `https://package.openupm.com/`
		- Scope(s): `com.timbojimbo`
2. Install the package
	- Open **Window > Package Manager**
	- Click Add and select **Add package by name...**
	- Paste name: `com.timbojimbo.ui.layout`

Done!

> [!WARNING]
> This package is new - use at your own risk! :)

# Usage

## Nodes

Add **Timbo Jimbo > UI > Layout > Layout Node** to a `RectTransform`. A node lays out its child nodes; children without a node are left alone and follow their parent through their anchors.

- **Width / Height** are a mode and its values:
  - **Fit** sizes to the children (or the content of a leaf), between **Min** and **Max**.
  - **Grow** fills the space the parent has left, shared with other Grow children, between **Min** and **Max**. On the axis across the flow it fills the parent's inner size.
  - **Fixed** is an exact size.
  - **Percent** is a fraction of the parent's inner size (padding and gaps excluded).
- **Direction** lays children out left to right or top to bottom.
- **Padding** insets the children (left, right, top, bottom); **Gap** is the space between them.
- **Align X / Align Y** place the children within the free space, along the flow and across it.
- **Aspect Ratio** (width over height) derives a Fit height from the width, for images.
- **Attach To** takes the node out of the flow and floats it: pick the attach point on the element and on the target (the parent, the root or another node) and an offset. Floating nodes never affect their parent's Fit size.
- **Offset** translates the node after layout. Changing it on a settled tree moves just that node, so it is the field to animate.

The root's size is its `RectTransform` rect unless its sizing is Fit or Fixed, in which case the root sizes itself.

When children overflow the space, Grow children shrink first down to their Min, then Fit children down to their minimum (a leaf's content minimum, such as the longest word). Fixed and Percent children never shrink.

## Transitions

Layout is instant. Animation comes from **view transitions**, after the web API: `LayoutSystem.StartViewTransition(update)` is `document.startViewTransition` for these trees. It captures where every node is shown, runs `update`, lays the new state out, and then animates every difference as one transition. Nodes that ended up somewhere else travel there from where they were, whatever parent or tree they moved to. The rest is sorted into **groups** the way the web sorts its captured elements: a node that appears with a **View Transition Name** another node carried before the update pairs with it, flying in from its spot while the old node, if it is leaving, flies out to the new spot, the two cross-fading; nodes handed to `LayoutSystem.Exit` inside the update leave; the topmost node of each subtree that appeared enters. The named groups (pairs, and persisting pairs) are lifted out of their trees for the length of the transition into a **transition layer**, a plain rect kept as the last child of their canvas, and stacked there in capture order, exactly as the web's groups are in its top layer: what draws over what no longer depends on where the nodes sit in their trees, and a part flies the same straight line however deep it is nested and whatever its container is doing. A placeholder of the same size keeps each lifted node's slot, so the tree lays out exactly as before and the slot, read live, is where the flight lands: scrolling or a reflow during the transition is honoured. A lifted container keeps the layout its content was given for its final size, clipped by its own mask, the way a snapshot would. Entering and exiting nodes fade (or play their animator) in place, beneath the layer, keeping their scroll-view clipping. A name found in only one state rides whatever its tree does. There are no snapshots: the objects themselves move, so anything animating on them keeps animating through the transition, and nothing is left on an object afterwards but the CanvasGroup a fade added.

Fades follow the move's own ease, so the crossover of a pair rides the motion whichever way a transition runs. Two ordinary alpha fades let some of what is behind show through at the crossover (the web hides this by blending its two snapshots with `plus-lighter`); the answer here is to ground the pair. Build a card or page as an unnamed container holding a persisting background node and then a named body with the content: the surface itself grows from the card into the page, opaque, while the bodies cross-fade over it. The background goes *before* the body because groups stack in capture order, so an earlier sibling draws below the body and a named child of the body would draw above the body's content, which is the web's rule for named descendants too. **Persist** (`ViewTransitionPersist`, after Astro's `transition:persist`) keeps an object instead of cross-fading it: flag the media node in both the card and the page, and when the two pair the objects swap places, the kept one flying into the new spot with its state intact (a running animation, a playing video) and the copy waiting where it was for the trip back.

Names are authored in prefabs ("avatar", "title") and made unique per instance with a **View Transition Scope**: set it to the item's id on the instance's root when it is bound, and every name below resolves to `scope/name`. A list of one prefab then has no duplicate names, and a page given the same scope pairs with that instance's parts, on the way there and back, without touching a part name.

Nodes move with the `LayoutTransition` you pass, a delay, a duration and an ease like CSS `animation-delay`, `animation-duration` and `animation-timing-function` (the default is a quarter second easing out), unless they set a **Transition** of their own, the way a CSS rule on `::view-transition-group(name)` overrides the default. A delayed default makes every move wait, which is how a list closes up only after the leaving item has played out. The call returns a `ViewTransition` with `Finished` and `SkipTransition()`. Only one runs at a time: starting another completes the one in flight (its flights and moves land at once; its enter and exit effects play on, since they belong to their nodes and touch no layout), and a name found on two nodes in one state skips the transition with a warning, both as on the web. `SkipTransition()` ends everything at once, effects included. Content that cannot be drawn between two sizes (text) takes its new size at once and animates only its position; an `Img` scales. A measured leaf's position still animates. A layout pass during a transition leaves the moves alone, and a changed target retargets a moving node with the move's own timing. `LayoutRect` is always the target; `VisualRect` and `IsTransitioning` tell you where a node is on the way. A root's own rect never animates: it is the tree's contract with whatever holds it (a ScrollRect, a UGUI parent), which reads the transform at once, so a scroll position set right after `StartViewTransition` is against the final content size. Moves follow the unscaled clock, so menus keep moving while the game is paused; set `LayoutSystem.UseScaledTime` to have them follow `Time.timeScale` instead.

**Entering and leaving.** What a node looks like while it enters or leaves is not layout's business, just as the web leaves it to keyframes on `::view-transition-new` and `::view-transition-old`. Put an `IViewTransitionAnimator` on the node's object and the engine hands it the moments: `Enter` on the node's first layout inside a transition, `Exit` when `LayoutSystem.Exit` takes it out (the tree has already reflowed without it and it is still drawn where it was), each with a `done` to call when the effect ends, which `Finished` waits for and which lets the leaving node go (it is deactivated, then `Exit`'s callback runs); and `Skip` when the transition is skipped. Only the topmost new node of a subtree enters; the nodes inside it ride with it. With the Sequencer installed, **View Transition Sequences** implements it with two authored sequences, Enter and Exit, on a `SequenceProvider`: tween the node's `Offset`, a CanvasGroup's alpha, a scale, anything. Without an animator, an entering or leaving subtree fades through a CanvasGroup, added the first time the object needs one and left in place. Outside a view transition `Exit` still plays the animator's exit, and a node without one leaves at once.

```csharp
// A card in a list opens into a detail page. Both carry the item's id as their name.
detailScreen.ViewTransitionScope = item.Id;   // the card's root got the same scope when it was bound
LayoutSystem.StartViewTransition(() =>
{
    LayoutSystem.Exit(listScreen);    // plays its exit sequence (or fades); its named parts fly to where the page puts them
    detailScreen.SetActive(true);     // plays its enter sequence (or fades); its named parts fly in from the card
});

// A list item leaves first, then the list closes up.
LayoutSystem.StartViewTransition(() => LayoutSystem.Exit(item, () => Destroy(item.gameObject)),
    LayoutTransition.Over(0.25f).After(0.2f));
```

## Leaves

A node with no child nodes is a leaf, and the component next to it is its content. The main path is a component implementing `ILayoutMeasurable`: `Measure(availableWidth)` returns the content size for a width (negative means unconstrained) and `MinWidth` is how narrow it can go. `TextBlock` (UI Text) and `Img` (UI) implement it when this package is installed, so a text is just a node plus a TextBlock. Stock UGUI components (LayoutElement, Image, TextMeshPro) need nothing extra either: with no measurable on the object the node reads their `ILayoutElement` values the way a UGUI group would and relays their dirty callbacks, so a sprite or string change reflows the tree. That compatibility path has two limits, kept in `LayoutNode.Compatibility.cs`: `ILayoutElement` only answers for the width on its transform, so the node sets that width before reading, and the elements' minimum width is 0 for a text, so a squeezed TextMeshPro can wrap narrower than its longest word.

## Driving nodes from code, sequences and styles

Every setting has a setter that marks the tree, and `Offset` moves the node in place without a pass. With the Property Bindings package installed, `LayoutNodeProperties` provides descriptors for Offset, FloatOffset, Padding, Gap, AspectRatio, the Width and Height values, Direction and the alignments, so the Sequencer and Styling drive a node through those same setters:

```csharp
var offset = LayoutNodeProperties.Offset.Create(row);   // a BindableProperty for a tween or a style
```

## Sample

The **Layout** sample (Package Manager > UI Layout > Samples) is a showcase scene with every feature in one place: the sizing modes side by side, alignment on both axes, a narrow row where text shrinks and wraps, a floating badge and tooltip, aspect-ratio tiles and a scrolling list. Its buttons add and remove rows, flip the sizing row between the two directions, change its gap and pulse a block's Offset, so you can see what reflows and what does not.

## Inside UGUI layout

Add **Layout Root Bridge** to a root that lives inside a UGUI layout group or a ScrollRect content. It reports the root's Fit size to UGUI and re-lays the tree out when UGUI resizes the root. A UGUI layout group inside a node still lays out its own children after the node sizes it.

## Scripting API

```csharp
var list = gameObject.AddComponent<LayoutNode>();
list.Direction = LayoutDirection.TopToBottom;
list.Width = Sizing.Grow();                 // fill the rect
list.Height = Sizing.Fit();                 // size to the rows
list.Gap = 6f;
list.Padding = new Vector4(8f, 8f, 8f, 8f); // left, right, top, bottom

var row = rowObject.AddComponent<LayoutNode>();
row.Width = Sizing.Grow();
row.Height = Sizing.Fixed(56f);

var badge = badgeObject.AddComponent<LayoutNode>();
badge.AttachTo = AttachTo.Parent;
badge.ElementPoint = AttachPoint.CenterCenter;
badge.ParentPoint = AttachPoint.RightTop;

row.Offset = new Vector2(0f, -4f);           // nudge after layout, no pass

LayoutSystem.ForceLayout(list);              // settle now instead of before the next render
Debug.Log(row.LayoutRect);                   // engine space: from the parent's top-left, y down
```

# AI Usage Disclosure

Parts of this package were written with the help of AI tools. Everything is reviewed and tested by a human before release.
