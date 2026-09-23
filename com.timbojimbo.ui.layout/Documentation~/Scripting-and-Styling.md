# Scripting and styling

[← Back to the README](../../README.md)

- [Driving nodes from code](#driving-nodes-from-code)
- [When layout happens](#when-layout-happens)
- [Reading where a node is](#reading-where-a-node-is)
- [Sequences, styles and property bindings](#sequences-styles-and-property-bindings)
- [Animation clips](#animation-clips)

## Driving nodes from code

Every inspector setting is a property with a setter that marks the tree for layout, so code, sequences, styles and animation clips all drive a node the same way:

```csharp
var list = gameObject.AddComponent<LayoutNode>();
list.Direction = LayoutDirection.TopToBottom;
list.Width = Sizing.Grow();                 // fill the rect
list.Height = Sizing.Fit();                 // size to the rows
list.Gap = 6f;
list.Padding = Insets.All(8f);

var row = rowObject.AddComponent<LayoutNode>();
row.Width = Sizing.Grow();
row.Height = Sizing.Fixed(56f);
row.AlignSelf = AlignSelf.Center;

var badge = badgeObject.AddComponent<LayoutNode>();
badge.AttachTo = AttachTo.Parent;
badge.ElementPoint = AttachPoint.CenterCenter;
badge.ParentPoint = AttachPoint.RightTop;

row.Offset = new Vector2(0f, -4f);          // nudge after layout, without a pass
```

## When layout happens

Marked trees are laid out once per frame, just before the canvases draw, in edit mode and play mode, and ahead of UGUI's own rebuild so the graphics it resizes rebuild in the same frame. To settle a tree right now:

```csharp
LayoutSystem.ForceLayout(list);
```

`node.MarkDirty()` marks a tree by hand, for content that changed behind the node's back.

## Reading where a node is

- `LayoutRect` is the rect the last layout gave the node, in layout space: from its parent node's top-left corner, y down. A root's is (0, 0, width, height).
- `VisualRect` is where the node is drawn right now; it differs from `LayoutRect` only while a view transition moves it.
- `IsTransitioning` is true while it moves; `IsExiting` while it leaves; `Shown` while it is active and not leaving.
- `ParentNode`, `IsRoot` and `Root` walk the tree.

## Sequences, styles and property bindings

With the Property Bindings package installed, `LayoutNodeProperties` provides descriptors for Offset, FloatOffset, Padding, Gap, AspectRatio, the Width and Height values, Direction, AlignX, AlignY and AlignSelf. The Sequencer and Styling drive a node through those same setters:

```csharp
var offset = LayoutNodeProperties.Offset.Create(row);   // a BindableProperty for a tween or a style
```

Tween `Offset` for motion that shouldn't reflow anything (a bump, a shake, an enter or exit slide); tween Padding, Gap or a size when the reflow is the point.

## Animation clips

A Unity Animation clip writes the fields directly; the node notices and lays the tree out again.
