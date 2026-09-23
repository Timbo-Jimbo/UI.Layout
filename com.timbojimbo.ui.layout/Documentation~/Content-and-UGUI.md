# Content and UGUI

[← Back to the README](../../README.md)

- [Content: what a leaf sizes to](#content-what-a-leaf-sizes-to)
- [Your own content](#your-own-content)
- [Stock UGUI components](#stock-ugui-components)
- [Inside a ScrollRect or a UGUI layout group](#inside-a-scrollrect-or-a-ugui-layout-group)
- [A UGUI layout group inside a node](#a-ugui-layout-group-inside-a-node)

## Content: what a leaf sizes to

A node with no child nodes is a leaf, and the component next to it is its content: the text or image a Fit leaf hugs. Content answers two questions:

- How big am I at this width? Text answers with its height once the width is known, so it wraps before the heights are worked out.
- How narrow can I go? For text, its longest word. The engine won't [shrink](Layout.md#when-things-dont-fit) a leaf below it.

## Your own content

Implement `ILayoutMeasurable` on a component next to the node:

```csharp
public sealed class Badge : MonoBehaviour, ILayoutMeasurable
{
    // A negative width means "as wide as you like".
    public Vector2 Measure(float availableWidth) => new(48f, 24f);
    public float MinWidth => 48f;
    // True when the content can be drawn at any size in between (an image scales);
    // false (the default) when it can't (text re-wraps), so a transition only moves it.
    public bool SizeIsAnimatable => true;
}
```

`TextBlock` (UI Text) and `Img` (UI) implement it when those packages are installed, so a text is just a node plus a TextBlock. When the content changes, call `node.MarkDirty()` and the tree lays out again before the next frame.

## Stock UGUI components

`LayoutElement`, `Image` and TextMeshPro need nothing extra. With no `ILayoutMeasurable` on the object, the node reads their `ILayoutElement` values the way a UGUI layout group would, and a sprite or text change lays the tree out again by itself.

That path has two limits:

- `ILayoutElement` only answers for the width its transform has, so the node sets that width before reading it.
- The minimum width UGUI reports for a text is 0, so a squeezed TextMeshPro text can wrap narrower than its longest word.

Components that implement `ILayoutMeasurable` avoid both. Images, raw images and bare LayoutElements resize smoothly in a transition; other graphics that size themselves (texts) keep their final size and only move.

## Inside a ScrollRect or a UGUI layout group

Add **Layout Root Bridge** to a root that lives inside a UGUI layout group or a ScrollRect's content. It reports the root's Fit size to UGUI, and lays the tree out again when UGUI resizes the root, so the content is right in the same frame. Each UGUI query is a full layout computation, so a bridged root costs three per UGUI rebuild.

A root's rect never animates in a view transition, so a ScrollRect always sees the final content size at once.

## A UGUI layout group inside a node

A UGUI layout group inside a node still lays out its own children, after the node has sized it.
