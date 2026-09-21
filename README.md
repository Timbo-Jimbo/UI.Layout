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

Give a node a **Transition** (a duration and an ease) and it moves to a new rect instead of jumping there: position and size together, from wherever it is shown at that moment, so a pass in the middle of a move retargets smoothly. Only what a pass changes animates; siblings whose rect did not change stay put, and edit mode commits instantly. Transition state is scoped to one enabled span: the first layout after a node is enabled snaps, so a node never animates from where it was when it was last disabled. To animate a node in from its laid-out spot, enable it, call `LayoutSystem.ForceLayout` on its root, then change the property; the change animates from the spot the forced layout gave it. To bring a node in from somewhere off-layout, tween its `Offset` instead. A measured leaf (a node with an `ILayoutMeasurable`, such as a text) animates only its position and takes its new size at once, since its size follows its content and the content would wrap or clip at the intermediate sizes. `LayoutRect` is always the target; `VisualRect` and `IsTransitioning` tell you where the node is on the way. Put a transition on list rows and the others slide when one is added or removed.

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
