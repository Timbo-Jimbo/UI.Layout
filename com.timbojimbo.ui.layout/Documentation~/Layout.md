# Layout

[← Back to the README](../../README.md)

- [How it works](#how-it-works)
- [Sizing](#sizing)
- [Direction, padding and gap](#direction-padding-and-gap)
- [Alignment](#alignment)
- [When things don't fit](#when-things-dont-fit)
- [Aspect ratio](#aspect-ratio)
- [Floating: Attach To](#floating-attach-to)
- [Offset: nudging after layout](#offset-nudging-after-layout)
- [What it doesn't do](#what-it-doesnt-do)

## How it works

Add a **Layout Node** to each `RectTransform` you want laid out. A node lays out the child objects that have a node too; children without one are left alone and follow their parent through their anchors.

The **root** is the top node: the first one whose parent object has no Layout Node. The engine owns everything below a root. The root's own size is its `RectTransform` rect (so you place and anchor it as usual), unless its sizing is Fit or Fixed, in which case the root sizes itself.

A node with no child nodes is a **leaf**, and the text or image on it is its **content**: a leaf with Fit sizing hugs it. See [Content and UGUI](Content-and-UGUI.md) for how content is measured.

Layout runs just before the canvas draws, whenever something changed. It is worked out from the nodes' settings and their content alone, and the `RectTransform`s are written once at the end. Nothing is read back from the transforms, which is what lets [Offset](#offset-nudging-after-layout) and animation sit on top without fighting the layout.

The model is [Clay](https://github.com/nicbarker/clay)'s. If you know CSS flexbox: Fit is `fit-content`, Grow is `flex-grow`, and a node is a flex container with no wrapping.

## Sizing

Width and height each have a mode:

- **Fit** hugs the children (or the content of a leaf), between **Min** and **Max**.
- **Grow** fills the space the parent has left, shared equally with the other Grow siblings, between **Min** and **Max**. Across the parent's direction it fills the parent's inner size.
- **Fixed** is an exact size.
- **Percent** is a fraction of the parent's inner size (padding, and along the direction the gaps, excluded).

A Max of 0 means no maximum.

```csharp
row.Width = Sizing.Grow();               // fill
row.Width = Sizing.Grow(min: 80f);       // fill, but never narrower than 80
row.Height = Sizing.Fixed(40f);
label.Width = Sizing.Fit(max: 280f);     // hug the text, wrapping past 280
image.Width = Sizing.Percent(0.5f);      // half the parent's inner width
```

## Direction, padding and gap

**Direction** lays the children out left to right or top to bottom. **Padding** insets them from the node's edges; **Gap** is the space between two children.

Padding is a `Vector4` in left, right, top, bottom order (like `RectOffset`). The `Insets` helpers read better in code:

```csharp
panel.Padding = Insets.All(16f);
panel.Padding = Insets.Symmetric(horizontal: 24f, vertical: 12f);
panel.Padding = Insets.Of(left: 8f, top: 4f);
```

## Alignment

**Align X** and **Align Y** place the children inside the node's free space: Left, Center or Right, and Top, Center or Bottom. Along the direction they move the whole run of children; across it they place each child.

**SpaceBetween** shares the free space out between the children, the first and last touching the padding: a title on the left and a button on the right, with nothing in between. It adds to the gap, and only applies along the direction (Align X in a left-to-right node, Align Y in a top-to-bottom one); across it acts as Left or Top. With one child, or no free space (a Grow child takes it all), the children sit at the start.

**Align Self** on a child overrides its parent's alignment across the direction, for that child only: Start, Center or End (Auto uses the parent's). An icon centred beside top-aligned text, a chat bubble on the right in a column of left-aligned ones.

```csharp
header.AlignX = AlignX.SpaceBetween;       // title left, close button right
bubble.AlignSelf = AlignSelf.End;          // this one sits on the right
```

## When things don't fit

When the children overflow a node, they shrink before anything overflows:

1. **Grow** children shrink first, largest first, down to their Min.
2. Then **Fit** children, largest first, down to their minimum: for a text, its longest word; for a container, what its own children can shrink to.
3. **Fixed** and **Percent** children never shrink.

Text re-wraps at the width it ends up with, and the height follows.

## Aspect ratio

**Aspect Ratio** (width over height) keeps a node's shape:

- With a **Fit height**, the height follows the width. Use it for images in a column: the width comes from the column, the height from the picture.
- With a **Fit width and a Fixed height**, the width follows the height. Use it for a row of fixed-height thumbnails whose widths follow each picture's shape.

A Grow or Percent height doesn't drive the width: widths are settled before heights.

## Floating: Attach To

**Attach To** takes a node out of the flow and floats it, like CSS absolute or anchor positioning. A floating node never affects its parent's Fit size.

- **Parent**, **Root**, or **Element** (another node in the same tree, set in **Attach Element**) is what it floats against.
- **Element Point** is the point on the floating node that is placed at **Parent Point** on the target (for example: my left-top at its right-top).
- **Float Offset** shifts it from there, in layout space: x right, y **down**.

A floating node's **Grow** and **Percent** sizes are of its parent's whole rect, padding included (the rect its attach points are placed on), as CSS sizes an absolutely positioned box. So a background or highlight at Percent 100% × 100% attached to a padded button covers the whole button.

An **Element** target is how a tooltip, a dropdown or a tutorial callout points at a control while living somewhere else in the hierarchy, where it isn't clipped and draws on top. The target has to be a node in the same layout tree; one that isn't is ignored in favour of the parent, with a warning in the editor. A target that is itself floating must come before the node in the hierarchy.

## Offset: nudging after layout

**Offset** moves a node after layout, in canvas units (x right, y **up**, like `anchoredPosition`). It never moves siblings or resizes the parent, and changing it never runs a layout pass, so it is the field to animate for a bump, a shake or a slide-in. It has no effect on a root.

## What it doesn't do

- **No margins.** Use the parent's padding and gap, or wrap the child in a node with padding.
- **No wrapping onto a new line.** Build grids from rows of nodes.
- **No automatic animation.** Layout is instant; see [View Transitions](ViewTransitions.md).
