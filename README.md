# UI Layout

Layout and motion for UGUI in one system, modelled on [Clay](https://github.com/nicbarker/clay) and moved like SwiftUI. A layout pass says where every node goes; nodes get there on springs, from where they are drawn and at the velocity they have. On top of that: wrapping and grids, the safe area, show and hide effects, matched names that fly between places, and scrolling with nested drags, paging and indicators.

Requires Unity 6000.0 or later, `com.unity.ugui` 2.0.0 and `com.timbojimbo.core`.

## Layout

Add a `LayoutNode` to a RectTransform. A node whose parent is not a node is a root: it keeps the rect it is given and lays its children out inside it. The system owns every other node's RectTransform.

```csharp
var row = gameObject.AddComponent<LayoutNode>();
row.Direction = LayoutDirection.LeftToRight;
row.Width = Sizing.Grow();
row.Height = Sizing.Fit();
row.ChildGap = 12f;
row.Padding = Insets.All(16f);
row.ChildAlignY = AlignY.Center;
```

- **Sizing** per axis: `Fit` (its content, with an optional min and max), `Grow` (a share of what is left), `Fixed`, or `Percent` of its parent. `AspectRatio` sets the height from the width.
- **Wrap** breaks the children into lines, `ChildGap` apart, as CSS's flex-wrap and SwiftUI's lazy grids: `Wrap.Lines` as text wraps (tags), `Wrap.Grid(3)` in lines of three equal cells, `Wrap.Adaptive(120f)` in as many cells at least 120 long as fit. Lines and Adaptive wrap left to right; a Grid either way (a shelf of two rows that scrolls sideways).
- **Content**: a component on the node implementing `ILayoutMeasurable` is its content (a text, a picture), measured at the width it gets and told the size it is going to.
- **Floating** takes a node out of the flow and places it against its parent, its root, or any other node in its tree (`FloatingAttach.Element`), following that node as it moves and scrolls.
- **Display**: `Hidden` keeps a node's space, `None` takes it out of layout.
- **Offset**, **Scale** and **Opacity** move, scale and fade a node without taking space, for gestures.

## Safe area

As in SwiftUI, a root keeps its content clear of the notch, rounded corners and home bar (`Screen.safeArea`) on the edges its `SafeArea` names, all of them by default, adding as much as it covers to its padding; its own background still fills the screen. A node with `IgnoresSafeArea` reaches back out to the screen's edge where it lies against the safe area, its padding growing by as much, so what is inside stays clear:

```csharp
header.IgnoresSafeArea = Edges.Top | Edges.Left | Edges.Right; // its colour under the notch, its title below it
list.IgnoresSafeArea = Edges.Bottom;                            // rows scroll under the home bar, and rest above it
```

A node floating against the root sits inside the safe area. The Game view's safe area is the whole screen: the Simulator shows a phone's.

## Motion

Layout moves on the springs of [Motion](https://github.com/Timbo-Jimbo/Motion). A change made inside `MotionSystem.Animate` moves everything it gives somewhere new on springs, as SwiftUI's `withAnimation`; any other change goes there at once.

```csharp
var snappy = MotionAnimation.Default.Use(MotionAnimationPreset.Snappy);
MotionSystem.Animate(snappy, () =>
{
    panel.Width = Sizing.Fixed(480f);
    badge.Display = DisplayMode.Visible;
}).Finished += () => Debug.Log("landed");
```

- The change carries its animation: a duration, a bounce, a delay and a curvature that bows a node's path out (`MotionAnimation.Default` when it is given none). A duration of 0 (the None preset) doesn't animate: what moves is there at once, after its delay.
- A node's `Animation` overrides it for the node and everything inside it, as SwiftUI's `.transaction`: a device that turns at once (None) around an app that springs (its own). Left on Inherit, a node moves on the nearest one above it, or else on the change's. `LayoutSystem.AnimationOf(node)` says which.
- Nodes turn from where they are when a change interrupts them. `Fling` throws a node with a velocity, `Catch` stops it where it is drawn, and `Velocity` reads how fast it is moving.
- The returned `MotionTransition` reports `Finished` and `Completed`, can be `Skip`ped, and carries the type names you gave it. `Animate(update, interactive: false)` lets the pointer through what it moves until it lands.
- Values you draw yourself (a colour, a corner radius) can move with a change too, with Motion's `AnimateValue`.

## Show and hide

A node shown or hidden inside `Animate` plays its `DisplayEffect`: fading, shrinking and sliding past an edge of its parent, together, the same both ways. A hidden node is drawn until it has gone, and the change finishes then. `ShownChanged` hands the shown value to effects of your own.

## Names

Nodes with the same `MatchName` (and `MatchId`, set in code and inherited from above) pair up inside `Animate`:

- One shown as another is hidden **takes over** from where that one is drawn and flies to its own place, the two cross-fading: a cell zooming into the page it opens.
- One shown or hidden next to one that **stays shown grows out of it** and shrinks back into it: a dropdown's list out of its button.

`MatchFit` says how a matched node fills the rect it moves through, as CSS's `object-fit`. `MatchWidth`, the default as on the web, keeps it at its own size and scales it, as a picture of itself, evenly to that rect's width; `Fill`, `Contain` and `Cover` scale it to the rect exactly, to fit inside it or to cover it; `Resize` changes the rect's size instead, its content laid out at its own size. `MatchClip` cuts both halves to that rect while they fly. The node taking over decides for the pair: its fit, its clip, and the animation both halves move on, so an `Animation` given to one end plays the way a pair is taken over to it.

Pairs and nodes moved to a new parent inside `Animate` fly above everything, out of every clip, until they land.

## Scrolling

Set a node's `Scroll` and it clips its children and scrolls them, with drags, flicks, rubber banding, the wheel and touch to stop, as a UIScrollView.

- `ScrollOffset`, `ScrollTo` and `ScrollIntoView` scroll from code, on a spring inside `Animate`. `Scrolled` reports the offset.
- `ScrollAnchor` End keeps a chat or a log at its end as it grows.
- `ScrollSnap` rests on whole pages or on its children, one per flick, as UIKit's paging and SwiftUI's view-aligned scrolling.
- Scroll indicators show while it scrolls and fade out after, as on iOS (`ShowsScrollIndicators`, `ScrollIndicatorColor`).
- Nested lists pass a drag on from the inner one to the outer, as UIKit chains them. An `ILayoutDraggable` (a sheet's height, a card's pull) takes part too: offered each move before the lists inside it, or, with `PassOnMidDrag` false, given whole drags when the lists have no room.
