# Timbo Jimbo - UI Layout

**Flexbox-style layout for Unity UI, with animation that just happens.**

Add a Layout Node, say how things should size (fit their content, fill the space, or a fixed size) and your UI lays itself out. No rebuild calls, no anchors to fight. When something changes, wrap the change in one line and everything glides to its new place.

📐 **One component**

Put a `LayoutNode` on your panels and it arranges its children: in a row or a column, with padding, gaps and alignment.

🔌 **Works with what you have**

Images, TextMeshPro texts and LayoutElements size themselves with no adapter, and a layout can live inside a ScrollRect or a UGUI layout group.

✨ **Animation for free**

Items slide into place, fade in and out, and even fly from a card into a full page, with one call around the change you were making anyway.

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

# Your first layout

**In the inspector:**

1. Under your Canvas, add **Timbo Jimbo > UI > Layout > Layout Node** to a panel. Set **Direction** to *Top To Bottom* and **Gap** to 8.
2. Give the panel a few children with an **Image** and a Layout Node each. Set their **Width** to *Grow* and their **Height** to *Fixed* 40.
3. Add a TextMeshPro text as another child, with a Layout Node. It sizes itself to its text.

That's it. The panel stacks its children, and whenever something changes (a new child, a longer text, a different gap) it lays out again before the next frame. There is nothing to rebuild.

**In code:**

```csharp
var list = panel.AddComponent<LayoutNode>();
list.Direction = LayoutDirection.TopToBottom;
list.Gap = 8f;
list.Padding = Insets.All(12f);

var row = rowObject.AddComponent<LayoutNode>();
row.Width = Sizing.Grow();          // fill the list's width
row.Height = Sizing.Fixed(40f);
```

**Sizing, per axis:**

| Mode | What it does |
|---|---|
| **Fit** | Hugs its content: the children, or the text or image on it. |
| **Grow** | Fills the space its parent has left, shared with other Grow siblings. |
| **Fixed** | Exactly this size. |
| **Percent** | A fraction of the parent's inner size. |

Fit and Grow take an optional Min and Max. More in [Layout](com.timbojimbo.ui.layout/Documentation~/Layout.md).

# Your first transition

Layout changes are instant. To animate one, make the change inside `LayoutSystem.StartViewTransition`: everything that moved glides from where it was to where it is now. (The idea comes from the web's View Transitions API, if you know it.)

**Animate any change.** Reorder, resize, realign or show something, and it all glides:

```csharp
LayoutSystem.StartViewTransition(() => toggle.AlignX = isOn ? AlignX.Right : AlignX.Left);
```

**Add an item.** It fades in and its neighbours make room:

```csharp
LayoutSystem.StartViewTransition(() => Instantiate(rowPrefab, list.transform));
```

**Remove an item.** `Exit` fades it out while the list closes up, then you can destroy it:

```csharp
LayoutSystem.StartViewTransition(() => LayoutSystem.Exit(row, () => Destroy(row.gameObject)));
```

Want the gap to close only after it has gone? Add a delay: `LayoutTransition.Over(0.25f).After(0.2f)`.

**Move something to a new parent.** It's the same object, so it flies across:

```csharp
LayoutSystem.StartViewTransition(() => highlight.transform.SetParent(selectedTab.transform, false));
```

**Open a card into a page.** Give the card's picture and the page's picture the same **View Transition Name** in the inspector, then swap one screen for the other:

```csharp
LayoutSystem.StartViewTransition(() =>
{
    LayoutSystem.Exit(list);
    page.gameObject.SetActive(true);
});
```

The picture flies from the card to the page, and back again when you swap them back. Lots of cards? See [names in lists](com.timbojimbo.ui.layout/Documentation~/ViewTransitions.md#names-in-lists).

Custom slide-ins, your own timing, keeping a playing video alive across the jump: it's all in [View Transitions](com.timbojimbo.ui.layout/Documentation~/ViewTransitions.md).

# Try the samples

Import them from **Package Manager > UI Layout > Samples**.

- **Hello Layout**: start here. The recipes above in one small scene.
- **Layout**: every sizing and alignment feature side by side, with buttons that change things.
- **View Transitions**: one stage per feature. Good first stages: *1. Layout change*, *2. Enter and exit* and *11. Why wrap*, then *4. Delay*, *6. Reparent*, *7. Named pair* and *10. Together: a card opens into a page*.

# Learn more

- [Layout](com.timbojimbo.ui.layout/Documentation~/Layout.md): sizing, alignment, what happens when things don't fit, floating elements, aspect ratios and `Offset`.
- [View Transitions](com.timbojimbo.ui.layout/Documentation~/ViewTransitions.md): showing and hiding, timing, names, custom enter and exit effects, and running several at once.
- [View Transitions in depth](com.timbojimbo.ui.layout/Documentation~/ViewTransitions-Advanced.md): how a transition runs, the overlay things fly in, draw order, keeping objects alive, and the fine print.
- [Content and UGUI](com.timbojimbo.ui.layout/Documentation~/Content-and-UGUI.md): how texts and images are measured, your own content, and layouts inside ScrollRects and UGUI layout groups.
- [Scripting and styling](com.timbojimbo.ui.layout/Documentation~/Scripting-and-Styling.md): driving nodes from code, sequences and styles.

# AI Usage Disclosure

Parts of this package were written with the help of AI tools. Everything is reviewed and tested by a human before release.
