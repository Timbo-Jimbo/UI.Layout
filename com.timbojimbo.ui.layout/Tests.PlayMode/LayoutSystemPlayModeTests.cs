using System.Collections;
using NUnit.Framework;
using TimboJimbo.Core;
using TimboJimbo.UI.Layout;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TimboJimboTests.UI.Layout.PlayMode
{
    /// <summary>Play-mode tests of the scheduler, the leaf dirty callback and the boundaries with UGUI layout. View transitions are in <see cref="ViewTransitionPlayModeTests"/>.</summary>
    public sealed class LayoutSystemPlayModeTests
    {
        private const float Tolerance = 1e-3f;
        private GameObject _canvas;

        [SetUp]
        public void SetUp()
        {
            _canvas = new GameObject("Canvas", typeof(Canvas));
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_canvas);
            yield return null;
        }

        private static LayoutNode Node(Transform parent, string name, Sizing width, Sizing height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.pivot = new Vector2(0f, 1f);
            var node = go.AddComponent<LayoutNode>();
            node.Width = width;
            node.Height = height;
            return node;
        }

        private static RectTransform Rect(Component c) => (RectTransform)c.transform;

        [UnityTest]
        public IEnumerator Scheduler_LaysOutBeforeRender_AndIdlesWhenClean()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(100f), Sizing.Fixed(100f));
            root.Direction = LayoutDirection.TopToBottom;
            Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));

            yield return null;
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-20f).Within(Tolerance));

            int passes = LayoutEngine.PassCount;
            yield return null;
            Assert.That(LayoutEngine.PassCount, Is.EqualTo(passes), "a clean frame must not run a pass");

            root.Gap = 10f;
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-20f).Within(Tolerance), "the pass waits for the render");
            yield return null;
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-30f).Within(Tolerance));
            Assert.That(LayoutEngine.PassCount, Is.EqualTo(passes + 1));
        }

        [UnityTest]
        public IEnumerator UguiImage_SpriteChangeReflowsWithoutMarkDirty()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fit(), Sizing.Fit());
            var a = Node(root.transform, "A", Sizing.Fit(), Sizing.Fit());
            var image = a.gameObject.AddComponent<Image>();
            var small = new Texture2D(16, 8);
            var large = new Texture2D(32, 8);
            image.sprite = Sprite.Create(small, new Rect(0f, 0f, 16f, 8f), new Vector2(0.5f, 0.5f), 100f);

            yield return null;
            Assert.That(root.RectTransform.rect.width, Is.EqualTo(16f).Within(Tolerance));

            image.sprite = Sprite.Create(large, new Rect(0f, 0f, 32f, 8f), new Vector2(0.5f, 0.5f), 100f);
            yield return null;
            Assert.That(root.RectTransform.rect.width, Is.EqualTo(32f).Within(Tolerance));

            Object.Destroy(small);
            Object.Destroy(large);
        }

        [UnityTest]
        public IEnumerator Bridge_FitRootIsStackedByAUguiGroup()
        {
            var groupGo = new GameObject("Group", typeof(RectTransform), typeof(VerticalLayoutGroup));
            var groupRect = (RectTransform)groupGo.transform;
            groupRect.SetParent(_canvas.transform, false);
            groupRect.pivot = new Vector2(0f, 1f);
            groupRect.sizeDelta = new Vector2(200f, 300f);
            var group = groupGo.GetComponent<VerticalLayoutGroup>();
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandHeight = false;
            group.spacing = 0f;

            var before = new GameObject("Before", typeof(RectTransform), typeof(LayoutElement));
            before.transform.SetParent(groupRect, false);
            before.GetComponent<LayoutElement>().preferredHeight = 20f;

            var root = Node(groupRect, "Root", Sizing.Grow(), Sizing.Fit());
            root.Direction = LayoutDirection.TopToBottom;
            root.gameObject.AddComponent<LayoutRootBridge>();
            var child = Node(root.transform, "Child", Sizing.Fixed(30f), Sizing.Fixed(30f));

            var after = new GameObject("After", typeof(RectTransform), typeof(LayoutElement));
            after.transform.SetParent(groupRect, false);
            after.GetComponent<LayoutElement>().preferredHeight = 20f;
            Rect(after.transform).pivot = new Vector2(0f, 1f);

            yield return null;

            Assert.That(root.RectTransform.rect.height, Is.EqualTo(30f).Within(Tolerance));
            Assert.That(root.RectTransform.rect.width, Is.EqualTo(200f).Within(Tolerance));
            Assert.That(Rect(after.transform).anchoredPosition.y, Is.EqualTo(-50f).Within(Tolerance));
            Assert.That(child.RectTransform.rect.width, Is.EqualTo(30f).Within(Tolerance));
        }

        [UnityTest]
        public IEnumerator OutsideAViewTransition_LayoutIsInstant_WhateverANodesTransitionIs()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(100f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            b.Transition = LayoutTransition.Over(0.25f, EaseType.Linear);
            yield return null;
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-20f).Within(Tolerance));

            root.Gap = 40f;
            yield return null;
            Assert.That(b.IsTransitioning, Is.False, "a change outside a view transition applies at once");
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-60f).Within(Tolerance));
            Assert.That(b.VisualRect, Is.EqualTo(b.LayoutRect));
        }

        [UnityTest]
        public IEnumerator NestedRoot_FollowsTheOuterRootInTheSameFlush()
        {
            var outer = Node(_canvas.transform, "Outer", Sizing.Fixed(200f), Sizing.Fixed(100f));
            outer.Direction = LayoutDirection.TopToBottom;
            var slot = Node(outer.transform, "Slot", Sizing.Grow(), Sizing.Fixed(40f));
            var plain = new GameObject("Plain", typeof(RectTransform)).GetComponent<RectTransform>();
            plain.SetParent(slot.transform, false);
            plain.anchorMin = Vector2.zero;
            plain.anchorMax = Vector2.one;
            plain.offsetMin = Vector2.zero;
            plain.offsetMax = Vector2.zero;
            var inner = Node(plain, "Inner", Sizing.Grow(), Sizing.Fixed(40f));
            inner.RectTransform.anchorMin = Vector2.zero;
            inner.RectTransform.anchorMax = Vector2.one;
            inner.RectTransform.offsetMin = Vector2.zero;
            inner.RectTransform.offsetMax = Vector2.zero;
            var child = Node(inner.transform, "Child", Sizing.Grow(), Sizing.Fixed(10f));

            yield return null;
            Assert.That(Rect(child).rect.width, Is.EqualTo(200f).Within(Tolerance));

            outer.Width = Sizing.Fixed(120f);
            yield return null;

            Assert.That(Rect(child).rect.width, Is.EqualTo(120f).Within(Tolerance), "the nested root was resized by the outer pass and laid out in the same flush");
        }

        [UnityTest]
        public IEnumerator UguiGroupInsideANode_LaysOutItsOwnChildren()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(100f), Sizing.Fixed(40f));
            root.Direction = LayoutDirection.TopToBottom;
            var holder = Node(root.transform, "Holder", Sizing.Grow(), Sizing.Fixed(40f));
            var group = holder.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.spacing = 0f;
            var left = new GameObject("Left", typeof(RectTransform), typeof(LayoutElement));
            left.transform.SetParent(holder.transform, false);
            var right = new GameObject("Right", typeof(RectTransform), typeof(LayoutElement));
            right.transform.SetParent(holder.transform, false);

            yield return null;

            Assert.That(holder.RectTransform.rect.width, Is.EqualTo(100f).Within(Tolerance));
            Assert.That(Rect(left.transform).rect.width, Is.EqualTo(50f).Within(Tolerance));
            Assert.That(Rect(right.transform).rect.width, Is.EqualTo(50f).Within(Tolerance));
        }
    }
}
