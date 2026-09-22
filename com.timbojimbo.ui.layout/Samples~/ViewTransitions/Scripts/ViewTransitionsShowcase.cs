using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TimboJimbo.UI.Layout.Samples.ViewTransitions
{
    /// <summary>
    /// One stage per view transition feature, in isolation, from the simplest to the ones that compose several.
    /// Every button is a single <see cref="LayoutSystem.StartViewTransition"/> around an ordinary change to the
    /// tree; the stages differ only in what the nodes declare (a transition of their own, an animator, a name, a
    /// scope, a persist flag) and in how the tree is authored.
    /// </summary>
    public sealed class ViewTransitionsShowcase : MonoBehaviour
    {
        private static readonly LayoutTransition Slow = LayoutTransition.Over(0.6f);

        [Serializable]
        public class Demo
        {
            public LayoutNode Stage;
            public LayoutNode A;
            public LayoutNode B;
            public LayoutNode Template;
            public List<LayoutNode> Items = new();
        }

        [Serializable]
        public class Item
        {
            public string Id;
            public string Title;
            public Color Colour;
        }

        [SerializeField] private Demo _reorder;
        [SerializeField] private Demo _enterExit;
        [SerializeField] private Demo _authored;
        [SerializeField] private Demo _delay;
        [SerializeField] private Demo _override;
        [SerializeField] private Demo _reparent;
        [SerializeField] private Demo _pair;
        [SerializeField] private Demo _scope;
        [SerializeField] private Demo _persist;
        [SerializeField] private Demo _page;
        [SerializeField] private Demo _compare;
        [SerializeField] private Demo _interrupt;
        [SerializeField] private GameObject _pagePrefab;
        [SerializeField] private List<Item> _items = new();

        private DetailPage _openPage;

        public void Setup(Demo reorder, Demo enterExit, Demo authored, Demo delay, Demo @override, Demo reparent, Demo pair, Demo scope, Demo persist, Demo page, Demo compare, Demo interrupt, GameObject pagePrefab, List<Item> items)
        {
            _reorder = reorder; _enterExit = enterExit; _authored = authored; _delay = delay; _override = @override;
            _reparent = reparent; _pair = pair; _scope = scope; _persist = persist; _page = page; _compare = compare;
            _interrupt = interrupt; _pagePrefab = pagePrefab; _items = items;
        }

        private void Start()
        {
            foreach (var demo in new[] { _enterExit, _authored, _compare })
                if (demo.Template != null) demo.Template.gameObject.SetActive(false);
            foreach (var demo in new[] { _pair, _scope, _persist })
                if (demo.B != null) demo.B.gameObject.SetActive(false);
            WireBlocks(_compare.A, false);
            WireBlocks(_compare.B, true);
        }

        // 1. A layout change: nodes are matched by identity and move from where they were.
        public void Reorder() => MoveLastToFront(_reorder, ViewTransition.DefaultTransition);

        // 2. A node that appears enters (fades in); one handed to Exit leaves (fades out) while the row closes up.
        public void Add() => AddItem(_enterExit);
        public void Remove() => RemoveItem(_enterExit);

        // 3. The same, with an animator on the block: its enter and exit are its own (SlideFadeAnimator).
        public void AddAuthored() => AddItem(_authored);
        public void RemoveAuthored() => RemoveItem(_authored);

        // 4. A delayed transition: the siblings wait for the exit before closing the gap.
        public void RemoveDelayed()
        {
            LayoutNode middle = null;
            foreach (var item in _delay.Items)
                if (item.gameObject.activeSelf && !item.IsExiting) { middle = item; break; }
            if (middle == null) return;
            LayoutSystem.StartViewTransition(() => LayoutSystem.Exit(middle), LayoutTransition.Over(0.3f).After(0.35f));
        }

        public void RestoreDelayed()
        {
            LayoutSystem.StartViewTransition(() =>
            {
                foreach (var item in _delay.Items)
                    item.gameObject.SetActive(true);
            });
        }

        // 5. One node sets a Transition of its own, which overrides the one the call was started with.
        public void Override() => MoveLastToFront(_override, LayoutTransition.Over(0.25f));

        // 6. A node moved to another parent is still the same node: it flies across by identity.
        public void Reparent()
        {
            var from = _reparent.A.transform.childCount > 0 ? _reparent.A : _reparent.B;
            var to = ReferenceEquals(from, _reparent.A) ? _reparent.B : _reparent.A;
            if (from.transform.childCount == 0) return;
            LayoutSystem.StartViewTransition(() => from.transform.GetChild(0).SetParent(to.transform, false), Slow);
        }

        // 7. Two different nodes with the same name are a pair: the new one flies in from the old one's spot while
        // the old one flies out to it, cross-fading, both lifted above everything else.
        public void TogglePair() => Toggle(_pair);

        // 8. A scope makes prefab-style names unique per instance: the detail takes the scope of the item it opens.
        public void OpenScoped(int index)
        {
            if (!_scope.A.gameObject.activeSelf) return;
            _scope.B.ViewTransitionScope = index.ToString();
            Toggle(_scope);
        }

        public void BackScoped()
        {
            if (_scope.A.gameObject.activeSelf) return;
            Toggle(_scope);
        }

        // 9. Persist: the spinning block is the same object on both sides; it flies across and the copies swap at the end.
        public void TogglePersist() => Toggle(_persist);

        // 10. Everything together: a card opens into a page spawned from a prefab. The card's surface persists
        // into the page's, the bodies pair and cross-fade over it, the avatar and title fly to the header, the
        // spinner persists; the page is destroyed on the way back.
        public void OpenPage(int index)
        {
            if (_openPage != null || _pagePrefab == null || index < 0 || index >= _items.Count) return;
            var item = _items[index];
            LayoutSystem.StartViewTransition(() =>
            {
                _openPage = Instantiate(_pagePrefab, _page.Stage.transform).GetComponent<DetailPage>();
                _openPage.Bind(item.Id, item.Title, item.Colour);
                _openPage.BackButton.onClick.AddListener(ClosePage);
                _openPage.SurfaceButton.onClick.AddListener(ClosePage);
                LayoutSystem.Exit(_page.A);
            }, Slow);
        }

        public void ClosePage()
        {
            if (_openPage == null) return;
            var page = _openPage;
            _openPage = null;
            LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(page.Node, () => Destroy(page.gameObject));
                _page.A.gameObject.SetActive(true);
            }, Slow);
        }

        // 11. The same block with the same animator, inserted in the middle and removed on click: left plays the
        // animator itself and the others snap; right wraps the change in a view transition and they glide.
        public void InsertPlain() => Insert(_compare.A, false);
        public void InsertWrapped() => Insert(_compare.B, true);

        // 12. Starting a transition completes the one in flight: click repeatedly and the blocks retarget from where they are.
        public void Interrupt() => MoveLastToFront(_interrupt, LayoutTransition.Over(1.5f));

        private void Insert(LayoutNode column, bool wrapped)
        {
            if (_compare.Template == null || column.transform.childCount >= 6) return;
            LayoutNode item = null;
            void Add()
            {
                item = Instantiate(_compare.Template, column.transform);
                item.transform.SetSiblingIndex(1 + (column.transform.childCount - 2) / 2);
                item.gameObject.SetActive(true);
                Wire(item, wrapped);
            }
            if (wrapped)
                LayoutSystem.StartViewTransition(Add);
            else
            {
                Add();
                if (item.TryGetComponent<IViewTransitionAnimator>(out var animator))
                    animator.Enter(null, () => { });
            }
        }

        private static void RemoveBlock(LayoutNode item, bool wrapped)
        {
            if (item == null || item.IsExiting) return;
            if (wrapped)
                LayoutSystem.StartViewTransition(() => LayoutSystem.Exit(item, () => Destroy(item.gameObject)));
            else
                LayoutSystem.Exit(item, () => Destroy(item.gameObject));
        }

        private static void WireBlocks(LayoutNode column, bool wrapped)
        {
            if (column == null) return;
            foreach (Transform child in column.transform)
                if (child.TryGetComponent<LayoutNode>(out var node) && child.TryGetComponent<Button>(out _))
                    Wire(node, wrapped);
        }

        private static void Wire(LayoutNode item, bool wrapped)
        {
            if (item.TryGetComponent<Button>(out var button))
                button.onClick.AddListener(() => RemoveBlock(item, wrapped));
        }

        private static void MoveLastToFront(Demo demo, LayoutTransition transition)
        {
            var row = demo.A.transform;
            if (row.childCount < 2) return;
            LayoutSystem.StartViewTransition(() => row.GetChild(row.childCount - 1).SetAsFirstSibling(), transition);
        }

        private static void AddItem(Demo demo)
        {
            if (demo.Template == null || demo.Items.Count >= 5) return;
            LayoutSystem.StartViewTransition(() =>
            {
                var item = Instantiate(demo.Template, demo.A.transform);
                item.gameObject.SetActive(true);
                demo.Items.Add(item);
            });
        }

        private static void RemoveItem(Demo demo)
        {
            if (demo.Items.Count == 0) return;
            var last = demo.Items[demo.Items.Count - 1];
            demo.Items.RemoveAt(demo.Items.Count - 1);
            LayoutSystem.StartViewTransition(() => LayoutSystem.Exit(last, () => Destroy(last.gameObject)));
        }

        private static void Toggle(Demo demo)
        {
            var shown = demo.A.gameObject.activeSelf ? demo.A : demo.B;
            var other = ReferenceEquals(shown, demo.A) ? demo.B : demo.A;
            LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(shown);
                other.gameObject.SetActive(true);
            }, Slow);
        }
    }
}
