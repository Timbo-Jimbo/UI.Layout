## [Unreleased]

### Added

- `LayoutSystem.Current`: the change being made, while `Animate`'s update runs
- `LayoutSystem.AnimateValue`: a value something draws itself (a colour, a corner radius; up to four components) moves with the change being made as a node's place does: on a spring, from where it is drawn at the velocity it has, held by the change until it comes to rest, and put there when the change is skipped. Outside a change it goes there at once, or, on its way, heads there instead
- `LayoutAnimationPreset.None`, first in the inspector's presets: no animation, a duration of 0. It is lit by a duration of 0 alone, whatever the bounce and curvature; another preset chosen after it gives the animation the default duration back

### Changed

- A `LayoutAnimation` with a duration of 0 is no animation, as UIKit's zero-duration one: what a change gives somewhere new is there in that change's frame, held by nothing, or with a delay, there once the delay is up. It was a very stiff spring before, which took a frame or more to settle and showed shapes in between (a device turning drawn part way)

## [0.4.0] - 01/10/2026

The first published release; 0.1.0 to 0.3.0 were never released.

### Added

- `LayoutNode`: Clay's layout model for UGUI. Each axis is sized `Fit`, `Grow`, `Fixed` or `Percent` (with a min and max), with padding, a gap between children, a direction, alignment and an aspect ratio. A node whose parent is not a node is a root: it keeps the rect it is given and lays its children out inside it, and the system drives every other node's RectTransform
- Content: a component implementing `ILayoutMeasurable` (the UI package's `Img`, UI Text's `TextBlock`) is its node's content, measured unwrapped for fit widths and wrapped to the final width for heights, and told the size its node is going to (`Arrange`)
- `Floating`: a node floats out of its parent's flow against its parent, its root, or another node anywhere in its tree (`FloatingAttach.Element`), which it follows as it moves and scrolls
- `Wrap`: children break into lines, `ChildGap` apart, as CSS's flex-wrap and SwiftUI's lazy grids. `Lines` wraps as text does, each line then laid out as a row; `Grid(n)` lays lines of n equal cells, and `Adaptive(min)` as many cells at least min long as fit, each child sized in its cell by its own sizing. Lines and Adaptive wrap left to right; a Grid either way
- Safe area, as SwiftUI's: an outermost root keeps its content clear of the notch, rounded corners and home bar on the edges its `SafeArea` names (all by default), adding as much of `Screen.safeArea`'s unsafe area as it covers to its padding. A node with `IgnoresSafeArea` reaches back out to the screen's edge where it lies against the safe area, its padding growing by as much, so what is inside stays clear: a bar's colour under the notch, a list scrolling under the home bar and coming to rest above it. A node floating against the root is placed inside the safe area
- `Display` (Visible, Hidden, None), and `Offset`, `Scale` and `Opacity`, which move, scale and fade a node without taking space
- `LayoutSystem.Animate`: a change made inside it moves everything it gives somewhere new on springs, from where each node is drawn and at the velocity it has, as SwiftUI's withAnimation. Each node moves on its own `LayoutAnimation` (duration, bounce, curvature, delay, presets). The returned `LayoutTransition` reports `Finished` and `Completed`, can be skipped, carries type names, and with `interactive` false lets the pointer through what it moves until it lands. `Fling`, `Catch`, `Velocity` and `SizeVelocity` hand motion to and from gestures
- `DisplayEffect`: a node shown or hidden inside Animate fades, shrinks and slides past an edge of its parent, the same both ways, turning back from where it is when toggled again; a hidden node is drawn until it has gone. `ShownChanged` hands its shown value to effects of your own
- Names: nodes sharing a `MatchName` (and `MatchId`, set in code and inherited from above) pair up inside Animate. One shown as another is hidden takes over from where that one is drawn, the two cross-fading; one shown or hidden next to one that stays shown grows out of it and shrinks back into it. Pairs, and nodes moved to a new parent inside Animate, fly above everything and out of every clip until they land
- Scrolling, as a UIScrollView's: `Scroll` clips a node's children and scrolls them with drags, flicks, rubber banding, the wheel and touch to stop. `ScrollOffset`, `ScrollTo` and `ScrollIntoView` scroll from code, on a spring inside Animate, and `Scrolled` reports the offset. `ScrollAnchor` End keeps a chat or a log at its end as it grows; `ScrollSnap` rests on whole pages or on children, one per flick; scroll indicators show while it scrolls and fade out after (`ShowsScrollIndicators`, `ScrollIndicatorColor`)
- Nested scrolling: a drag passes on from an inner list to the outer, as UIKit chains them, and an `ILayoutDraggable` (a sheet's height, a card's pull) takes part too, offered each move before the lists inside it or, with `PassOnMidDrag` false, given whole drags when they have no room
- Inspector for `LayoutNode`, its settings grouped as they read, with what does not apply hidden, lit buttons for wraps, edges and display edges, and play mode readouts of the scroll and the match id
