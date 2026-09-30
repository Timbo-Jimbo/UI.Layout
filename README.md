# UI Layout

Layout and motion for UGUI in one system, modelled on [Clay](https://github.com/nicbarker/clay) and moved like SwiftUI. A layout pass says where every node goes; nodes get there on springs, from where they are drawn and at the velocity they have. On top of that: show and hide effects, matched names that fly between places, and scrolling with nested drags, paging and indicators.

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
- **Content**: a component on the node implementing `ILayoutMeasurable` is its content (a text, a picture), measured at the width it gets and told the size it is going to.
- **Floating** takes a node out of the flow and places it against its parent, its root, or any other node in its tree (`FloatingAttach.Element`), following that node as it moves and scrolls.
- **Display**: `Hidden` keeps a node's space, `None` takes it out of layout.
- **Offset**, **Scale** and **Opacity** move, scale and fade a node without taking space, for gestures.

## Motion

A change made inside `LayoutSystem.Animate` moves everything it gives somewhere new on springs, as SwiftUI's `withAnimation`; any other change goes there at once.

```csharp
LayoutSystem.Animate(() =>
{
    panel.Width = Sizing.Fixed(480f);
    badge.Display = DisplayMode.Visible;
}).Finished += () => Debug.Log("landed");
```

- Each node moves on its own `Animation`: a duration, a bounce, a delay and a curvature that bows its path out.
- Nodes turn from where they are when a change interrupts them. `Fling` throws a node with a velocity, `Catch` stops it where it is drawn, and `Velocity` reads how fast it is moving.
- The returned `LayoutTransition` reports `Finished` and `Completed`, can be `Skip`ped, and carries the type names you gave it. `Animate(update, interactive: false)` lets the pointer through what it moves until it lands.

## Show and hide

A node shown or hidden inside `Animate` plays its `DisplayEffect`: fading, shrinking and sliding past an edge of its parent, together, the same both ways. A hidden node is drawn until it has gone, and the change finishes then. `ShownChanged` hands the shown value to effects of your own.

## Names

Nodes with the same `MatchName` (and `MatchId`, set in code and inherited from above) pair up inside `Animate`:

- One shown as another is hidden **takes over** from where that one is drawn and flies to its own place, the two cross-fading: a cell zooming into the page it opens.
- One shown or hidden next to one that **stays shown grows out of it** and shrinks back into it: a dropdown's list out of its button.

Pairs and nodes moved to a new parent inside `Animate` fly above everything, out of every clip, until they land.

## Scrolling

Set a node's `Scroll` and it clips its children and scrolls them, with drags, flicks, rubber banding, the wheel and touch to stop, as a UIScrollView.

- `ScrollOffset`, `ScrollTo` and `ScrollIntoView` scroll from code, on a spring inside `Animate`. `Scrolled` reports the offset.
- `ScrollAnchor` End keeps a chat or a log at its end as it grows.
- `ScrollSnap` rests on whole pages or on its children, one per flick, as UIKit's paging and SwiftUI's view-aligned scrolling.
- Scroll indicators show while it scrolls and fade out after, as on iOS (`ShowsScrollIndicators`, `ScrollIndicatorColor`).
- Nested lists pass a drag on from the inner one to the outer, as UIKit chains them. An `ILayoutDraggable` (a sheet's height, a card's pull) takes part too: offered each move before the lists inside it, or, with `PassOnMidDrag` false, given whole drags when the lists have no room.
