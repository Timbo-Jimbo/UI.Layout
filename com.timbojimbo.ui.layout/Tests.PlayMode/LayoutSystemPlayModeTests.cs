using System.Collections;
using NUnit.Framework;
using TimboJimbo.Core;
using TimboJimbo.UI.Layout;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TimboJimboTests.UI.Layout.PlayMode
{
    /// <summary>Play-mode tests of the scheduler, the leaf dirty callback and the boundaries with UGUI layout.</summary>
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
        public IEnumerator ViewTransition_ANodesOwnTransitionOverridesTheDefault_AndLayoutRectIsTheTarget()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(100f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            var a = Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var c = Node(root.transform, "C", Sizing.Fixed(50f), Sizing.Fixed(20f));
            c.Transition = LayoutTransition.Over(1f, EaseType.Linear);
            yield return null;

            LayoutSystem.StartViewTransition(() => root.Gap = 40f, LayoutTransition.Over(0.2f, EaseType.Linear));
            yield return null;

            Assert.That(b.LayoutRect.y, Is.EqualTo(60f).Within(Tolerance), "LayoutRect is the target");
            Assert.That(b.IsTransitioning, Is.True);
            Assert.That(Rect(b).anchoredPosition.y, Is.LessThan(-20f).And.GreaterThan(-60f), "one frame in, b is on its way");
            Assert.That(-b.VisualRect.y, Is.EqualTo(Rect(b).anchoredPosition.y).Within(Tolerance), "VisualRect is what is on the transform");
            Assert.That(Rect(a).anchoredPosition.y, Is.EqualTo(0f).Within(Tolerance), "a node whose rect did not change stays put");

            float elapsed = 0f;
            while (b.IsTransitioning && elapsed < 2f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-60f).Within(Tolerance));
            Assert.That(c.IsTransitioning, Is.True, "c moves with its own, longer transition");
            Assert.That(Rect(c).anchoredPosition.y, Is.GreaterThan(-120f), "and is still short of its target at 120");
            while (c.IsTransitioning && elapsed < 3f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            Assert.That(Rect(c).anchoredPosition.y, Is.EqualTo(-120f).Within(Tolerance));
            Assert.That(LayoutSystem.TransitioningCount, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator ViewTransition_ADelayedTransitionWaitsBeforeMoving()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(100f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            yield return null;

            var vt = LayoutSystem.StartViewTransition(() => root.Gap = 40f, LayoutTransition.Over(0.25f, EaseType.Linear).After(0.5f));
            yield return null;
            Assert.That(b.IsTransitioning, Is.True);
            Assert.That(b.LayoutRect.y, Is.EqualTo(60f).Within(Tolerance));
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-20f).Within(Tolerance), "still where it was through the delay");

            float elapsed = 0f;
            while (elapsed < 0.6f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            Assert.That(Rect(b).anchoredPosition.y, Is.LessThan(-20f), "moving once the delay has passed");
            while (!vt.IsFinished && elapsed < 3f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-60f).Within(Tolerance));
        }

        [UnityTest]
        public IEnumerator ViewTransition_AMeasuredLeafTakesItsSizeAtOnce_AndAnimatesOnlyItsPosition()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(200f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var label = Node(root.transform, "Label", Sizing.Fit(), Sizing.Fit());
            var content = label.gameObject.AddComponent<TestContent>();
            content.Size = new Vector2(60f, 10f);
            yield return null;
            Assert.That(Rect(label).sizeDelta, Is.EqualTo(new Vector2(60f, 10f)));

            // The content grows and the label moves down in the same transition.
            LayoutSystem.StartViewTransition(() =>
            {
                content.Size = new Vector2(120f, 10f);
                root.Gap = 40f;
                LayoutSystem.MarkDirty(label);
            }, LayoutTransition.Over(0.25f, EaseType.Linear));
            yield return null;

            Assert.That(label.IsTransitioning, Is.True);
            Assert.That(Rect(label).sizeDelta, Is.EqualTo(new Vector2(120f, 10f)), "a measured leaf takes its new size at once");
            Assert.That(Rect(label).anchoredPosition.y, Is.LessThan(-20f).And.GreaterThan(-60f), "its position is on its way");

            // A size-only change on a measured leaf does not start a move at all.
            float elapsed = 0f;
            while (label.IsTransitioning && elapsed < 2f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            LayoutSystem.StartViewTransition(() =>
            {
                content.Size = new Vector2(80f, 10f);
                LayoutSystem.MarkDirty(label);
            }, LayoutTransition.Over(0.25f, EaseType.Linear));
            Assert.That(label.IsTransitioning, Is.False);
            Assert.That(Rect(label).sizeDelta, Is.EqualTo(new Vector2(80f, 10f)));
            yield return null;
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
        [UnityTest]
        public IEnumerator ViewTransition_MovesEveryChangedNodeWithTheDefault_Finishes_AndCanBeSkipped()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(100f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            yield return null;
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-20f).Within(Tolerance));

            var vt = LayoutSystem.StartViewTransition(() => root.Gap = 40f, LayoutTransition.Over(0.25f, EaseType.Linear));
            bool finished = false;
            vt.Finished += () => finished = true;

            Assert.That(b.LayoutRect.y, Is.EqualTo(60f).Within(Tolerance), "the update is laid out at once");
            Assert.That(b.IsTransitioning, Is.True, "a node without a transition of its own moves with the default");
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-20f).Within(Tolerance), "and starts from where it was");
            Assert.That(vt.IsFinished, Is.False);
            Assert.That(LayoutSystem.CurrentViewTransition, Is.SameAs(vt));

            yield return null;
            Assert.That(Rect(b).anchoredPosition.y, Is.LessThan(-20f).And.GreaterThan(-60f));

            vt.SkipTransition();
            Assert.That(vt.IsFinished, Is.True);
            Assert.That(finished, Is.True, "Finished fires on a skip");
            Assert.That(b.IsTransitioning, Is.False);
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-60f).Within(Tolerance), "skipped to its end");
            Assert.That(LayoutSystem.CurrentViewTransition, Is.Null);

            // Left alone, it finishes on its own.
            vt = LayoutSystem.StartViewTransition(() => root.Gap = 0f, LayoutTransition.Over(0.25f, EaseType.Linear));
            float elapsed = 0f;
            while (!vt.IsFinished && elapsed < 2f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            Assert.That(vt.IsFinished, Is.True);
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-20f).Within(Tolerance));

            // No duration applies the update at once; a new transition skips the one running.
            vt = LayoutSystem.StartViewTransition(() => root.Gap = 40f, LayoutTransition.None);
            Assert.That(vt.IsFinished, Is.True);
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-60f).Within(Tolerance));
            vt = LayoutSystem.StartViewTransition(() => root.Gap = 0f, LayoutTransition.Over(0.25f, EaseType.Linear));
            yield return null;
            var next = LayoutSystem.StartViewTransition(() => root.Gap = 80f, LayoutTransition.Over(0.25f, EaseType.Linear));
            Assert.That(vt.IsFinished, Is.True, "the running transition is skipped");
            Assert.That(next.IsFinished, Is.False);
            Assert.That(b.LayoutRect.y, Is.EqualTo(100f).Within(Tolerance));
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-20f).Within(Tolerance), "the new one starts from the skipped end");
        }

        [UnityTest]
        public IEnumerator ViewTransition_ANewNodeFadesInThroughAGroupAddedForTheTransition()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(100f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            yield return null;

            LayoutNode b = null;
            var vt = LayoutSystem.StartViewTransition(() => b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f)), LayoutTransition.Over(0.25f, EaseType.Linear));

            var group = b.GetComponent<CanvasGroup>();
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-20f).Within(Tolerance), "laid out at its spot");
            Assert.That(group, Is.Not.Null, "a group is added for the fade");
            Assert.That(group.alpha, Is.EqualTo(0f).Within(Tolerance), "and starts invisible");

            yield return null;
            Assert.That(group.alpha, Is.GreaterThan(0f).And.LessThan(1f));

            float elapsed = 0f;
            while (!vt.IsFinished && elapsed < 2f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            yield return null;
            Assert.That(b.GetComponent<CanvasGroup>(), Is.Null, "the group added for the transition is gone again, leaving the node fully shown");
        }

        [UnityTest]
        public IEnumerator ViewTransition_AnAnimatorPlaysTheEnterAndExit_AndFinishedWaitsForIt()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(100f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var c = Node(root.transform, "C", Sizing.Fixed(50f), Sizing.Fixed(20f));
            yield return null;

            LayoutNode b = null;
            TestAnimator animator = null;
            var vt = LayoutSystem.StartViewTransition(() =>
            {
                b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
                b.transform.SetSiblingIndex(1);
                animator = b.gameObject.AddComponent<TestAnimator>();
            }, LayoutTransition.Over(0.25f, EaseType.Linear));

            Assert.That(animator.Enters, Is.EqualTo(1), "the animator gets the enter");
            Assert.That(b.GetComponent<CanvasGroup>(), Is.Null, "and no default fade is added");
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-20f).Within(Tolerance), "the node sits at its final rect");
            Assert.That(c.IsTransitioning, Is.True, "the sibling makes room with the transition");

            float elapsed = 0f;
            while (c.IsTransitioning && elapsed < 2f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            Assert.That(vt.IsFinished, Is.False, "Finished waits for the enter effect");
            animator.Complete();
            Assert.That(vt.IsFinished, Is.True);

            // Exit: the animator plays it out; the node leaves when it reports done.
            vt = LayoutSystem.StartViewTransition(() => LayoutSystem.Exit(b), LayoutTransition.Over(0.25f, EaseType.Linear));
            Assert.That(animator.Exits, Is.EqualTo(1));
            Assert.That(b.IsExiting, Is.True);
            Assert.That(b.gameObject.activeSelf, Is.True);
            Assert.That(c.LayoutRect.y, Is.EqualTo(20f).Within(Tolerance), "the tree reflowed without it");
            elapsed = 0f;
            while (c.IsTransitioning && elapsed < 2f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            Assert.That(vt.IsFinished, Is.False);
            animator.Complete();
            Assert.That(b.gameObject.activeSelf, Is.False, "deactivated once the effect is done");
            Assert.That(vt.IsFinished, Is.True);

            // Skipped: the animator is told, and the exit concludes at once.
            b.gameObject.SetActive(true);
            yield return null;
            vt = LayoutSystem.StartViewTransition(() => LayoutSystem.Exit(b), LayoutTransition.Over(0.25f, EaseType.Linear));
            vt.SkipTransition();
            Assert.That(animator.Skips, Is.EqualTo(1));
            Assert.That(b.gameObject.activeSelf, Is.False);

            // Outside a view transition the exit still goes through the animator, with a null transition.
            b.gameObject.SetActive(true);
            yield return null;
            LayoutSystem.Exit(b);
            yield return null;
            Assert.That(animator.Exits, Is.EqualTo(3));
            Assert.That(animator.LastTransition, Is.Null);
            animator.Complete();
            Assert.That(b.gameObject.activeSelf, Is.False);

            // A new node inside a new subtree rides with the subtree: only the topmost new node enters.
            LayoutNode inner = null;
            TestAnimator innerAnimator = null;
            LayoutSystem.StartViewTransition(() =>
            {
                var outer = Node(root.transform, "Outer", Sizing.Fixed(50f), Sizing.Fixed(40f));
                inner = Node(outer.transform, "Inner", Sizing.Fixed(20f), Sizing.Fixed(20f));
                innerAnimator = inner.gameObject.AddComponent<TestAnimator>();
            }, LayoutTransition.Over(0.25f, EaseType.Linear));
            Assert.That(innerAnimator.Enters, Is.EqualTo(0), "a nested new node does not enter on its own");
        }

        [UnityTest]
        public IEnumerator Exit_LeavesAtOnceOutsideAViewTransition_AndFadesOutInsideOne()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(100f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var c = Node(root.transform, "C", Sizing.Fixed(50f), Sizing.Fixed(20f));
            yield return null;
            Assert.That(Rect(c).anchoredPosition.y, Is.EqualTo(-40f).Within(Tolerance));

            LayoutSystem.Exit(b);
            yield return null;
            Assert.That(b.gameObject.activeSelf, Is.False, "outside a view transition a node with no animator leaves at once");
            Assert.That(Rect(c).anchoredPosition.y, Is.EqualTo(-20f).Within(Tolerance));

            b.gameObject.SetActive(true);
            yield return null;
            var vt = LayoutSystem.StartViewTransition(() => LayoutSystem.Exit(b), LayoutTransition.Over(0.25f, EaseType.Linear));

            Assert.That(b.IsExiting, Is.True);
            Assert.That(b.gameObject.activeSelf, Is.True, "still shown while on its way out");
            Assert.That(c.LayoutRect.y, Is.EqualTo(20f).Within(Tolerance), "the tree reflows without it at once");
            Assert.That(c.IsTransitioning, Is.True, "and the sibling closes the gap with the transition's timing");
            Assert.That(Rect(b).anchoredPosition, Is.EqualTo(new Vector2(0f, -20f)), "the exiting node keeps its rect");
            yield return null;
            Assert.That(b.GetComponent<CanvasGroup>().alpha, Is.LessThan(1f).And.GreaterThan(0f), "fading out");

            float elapsed = 0f;
            while (!vt.IsFinished && elapsed < 2f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            Assert.That(b.gameObject.activeSelf, Is.False, "deactivated once out");
            Assert.That(b.IsExiting, Is.False);
            yield return null;
            Assert.That(b.GetComponent<CanvasGroup>(), Is.Null, "the group added for the fade is gone");

            // A callback takes the place of the deactivation.
            b.gameObject.SetActive(true);
            yield return null;
            bool exited = false;
            vt = LayoutSystem.StartViewTransition(() => LayoutSystem.Exit(b, () => exited = true), LayoutTransition.Over(0.25f, EaseType.Linear));
            elapsed = 0f;
            while (!vt.IsFinished && elapsed < 2f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            Assert.That(exited, Is.True);
            Assert.That(b.gameObject.activeSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator ViewTransition_ANamedNodeTakesTheOldNodesPlace_WhileTheOldOneFliesOutToIt()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(300f), Sizing.Fixed(200f));
            var list = Node(root.transform, "List", Sizing.Fixed(150f), Sizing.Fixed(200f));
            list.Direction = LayoutDirection.TopToBottom;
            list.Padding = new Vector4(20f, 0f, 30f, 0f);
            var card = Node(list.transform, "Card", Sizing.Fixed(50f), Sizing.Fixed(20f));
            card.ViewTransitionName = "title";
            var page = Node(root.transform, "Page", Sizing.Fixed(150f), Sizing.Fixed(200f));
            page.Direction = LayoutDirection.TopToBottom;
            var header = Node(page.transform, "Header", Sizing.Fixed(100f), Sizing.Fixed(40f));
            header.ViewTransitionName = "title";
            page.gameObject.SetActive(false);
            yield return null;
            Assert.That(Rect(card).anchoredPosition, Is.EqualTo(new Vector2(20f, -30f)));

            var vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(list);
                page.gameObject.SetActive(true);
            }, LayoutTransition.Over(0.25f, EaseType.Linear));

            // The list left the flow, so the page is the first child and its header is laid out at the page's top-left.
            Assert.That(header.LayoutRect, Is.EqualTo(new Rect(0f, 0f, 100f, 40f)));
            Assert.That(Rect(header).anchoredPosition, Is.EqualTo(new Vector2(20f, -30f)), "the header starts where the card was");
            Assert.That(Rect(header).sizeDelta, Is.EqualTo(new Vector2(50f, 20f)), "at the card's size");
            Assert.That(header.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0f).Within(Tolerance), "invisible: the page is drawn above the list, so its side fades");
            Assert.That(card.IsTransitioning, Is.True, "the card flies out to the header's spot");
            Assert.That(card.GetComponent<CanvasGroup>(), Is.Null, "and stays opaque underneath, so the background never shows through the pair");
            Assert.That(list.GetComponent<CanvasGroup>(), Is.Null, "the list is covered by the entering page and stays opaque too");

            yield return null;
            Assert.That(Rect(header).anchoredPosition.x, Is.LessThan(20f).And.GreaterThan(0f));
            Assert.That(Rect(header).sizeDelta.x, Is.GreaterThan(50f).And.LessThan(100f));
            Assert.That(Rect(card).anchoredPosition.x, Is.LessThan(20f).And.GreaterThan(0f), "on its way to (0, 0)");
            Assert.That(Rect(card).sizeDelta.x, Is.GreaterThan(50f).And.LessThan(100f));
            Assert.That(header.GetComponent<CanvasGroup>().alpha, Is.GreaterThan(0f).And.LessThan(1f));

            float elapsed = 0f;
            while (!vt.IsFinished && elapsed < 2f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            Assert.That(Rect(header).anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(Rect(header).sizeDelta, Is.EqualTo(new Vector2(100f, 40f)));
            Assert.That(list.gameObject.activeSelf, Is.False, "the list left");
            yield return null;
            Assert.That(header.GetComponent<CanvasGroup>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator ViewTransition_AReparentedNodeMovesFromWhereItWasShown()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(200f), Sizing.Fixed(200f));
            var left = Node(root.transform, "Left", Sizing.Fixed(100f), Sizing.Fixed(200f));
            left.Direction = LayoutDirection.TopToBottom;
            var right = Node(root.transform, "Right", Sizing.Fixed(100f), Sizing.Fixed(200f));
            right.Direction = LayoutDirection.TopToBottom;
            var item = Node(left.transform, "Item", Sizing.Fixed(50f), Sizing.Fixed(20f));
            yield return null;

            var vt = LayoutSystem.StartViewTransition(() => item.transform.SetParent(right.transform, false), LayoutTransition.Over(0.25f, EaseType.Linear));

            Assert.That(item.LayoutRect.position, Is.EqualTo(Vector2.zero), "laid out at the top of its new parent");
            Assert.That(Rect(item).anchoredPosition.x, Is.EqualTo(-100f).Within(Tolerance), "but shown where it was, under the old parent");
            yield return null;
            Assert.That(Rect(item).anchoredPosition.x, Is.GreaterThan(-100f).And.LessThan(0f));

            float elapsed = 0f;
            while (!vt.IsFinished && elapsed < 2f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            Assert.That(Rect(item).anchoredPosition, Is.EqualTo(Vector2.zero));
        }

        [UnityTest]
        public IEnumerator ViewTransition_APlainPassDuringTheMoveKeepsIt_AndAChangedTargetRetargetsWithItsTiming()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(100f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            yield return null;

            LayoutSystem.StartViewTransition(() => root.Gap = 40f, LayoutTransition.Over(0.5f, EaseType.Linear));
            yield return null;
            Assert.That(b.IsTransitioning, Is.True);
            float before = Rect(b).anchoredPosition.y;

            // A pass with nothing changed, outside any transition: the move carries on.
            LayoutSystem.ForceLayout(root);
            Assert.That(b.IsTransitioning, Is.True, "a plain pass does not end the move");
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(before).Within(Tolerance));
            yield return null;
            Assert.That(Rect(b).anchoredPosition.y, Is.LessThan(before), "still on its way");

            // A change outside any transition retargets the node from where it is, with the move's own timing.
            root.Gap = 10f;
            yield return null;
            Assert.That(b.LayoutRect.y, Is.EqualTo(30f).Within(Tolerance));
            Assert.That(b.IsTransitioning, Is.True, "retargeted rather than snapped");
            float elapsed = 0f;
            while (b.IsTransitioning && elapsed < 2f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-30f).Within(Tolerance));
        }

        [UnityTest]
        public IEnumerator ViewTransition_ANamedContainerMorphs_AndANamedPartInsideItFliesToItsOwnCounterpart()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(600f), Sizing.Fixed(400f));
            var list = Node(root.transform, "List", Sizing.Fixed(200f), Sizing.Fixed(400f));
            list.Padding = new Vector4(10f, 0f, 10f, 0f);
            var card = Node(list.transform, "Card", Sizing.Fixed(100f), Sizing.Fixed(50f));
            card.Padding = new Vector4(5f, 0f, 5f, 0f);
            card.ViewTransitionName = "card";
            var cardTitle = Node(card.transform, "Title", Sizing.Fixed(40f), Sizing.Fixed(10f));
            cardTitle.ViewTransitionName = "title";
            var page = Node(root.transform, "Page", Sizing.Fixed(400f), Sizing.Fixed(300f));
            page.Padding = new Vector4(100f, 0f, 200f, 0f);
            page.ViewTransitionName = "card";
            var pageTitle = Node(page.transform, "Title", Sizing.Fixed(80f), Sizing.Fixed(20f));
            pageTitle.ViewTransitionName = "title";
            page.gameObject.SetActive(false);
            yield return null;

            var titleStart = LayoutEngine.WorldRect(cardTitle.RectTransform);
            var vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(list);
                page.gameObject.SetActive(true);
            }, LayoutTransition.Over(0.5f, EaseType.Linear));

            // Where the page's title ends up on screen: the page's final rect plus the title's inside it (canvas scale 1, y up).
            var rootTopLeft = LayoutEngine.WorldRect(root.RectTransform);
            float targetX = rootTopLeft.x + page.LayoutRect.x + pageTitle.LayoutRect.x;
            float targetY = rootTopLeft.y - page.LayoutRect.y - pageTitle.LayoutRect.y;

            Assert.That(page.IsTransitioning, Is.True, "the page grows out of the card");
            Assert.That(Rect(page).sizeDelta, Is.EqualTo(new Vector2(100f, 50f)), "starting at the card's size");
            Assert.That(card.IsTransitioning, Is.True, "the card flies out to the page");
            Assert.That(cardTitle.IsTransitioning, Is.True, "and its title to the page's title");

            float elapsed = 0f;
            while (elapsed < 0.25f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            float t = cardTitle.EasedProgress();
            var now = LayoutEngine.WorldRect(cardTitle.RectTransform);
            Assert.That(now.x, Is.EqualTo(Mathf.Lerp(titleStart.x, targetX, t)).Within(1f), "the old title travels a straight line to the new title's spot, riding its flying container");
            Assert.That(now.y, Is.EqualTo(Mathf.Lerp(titleStart.y, targetY, t)).Within(1f));
            var newNow = LayoutEngine.WorldRect(pageTitle.RectTransform);
            Assert.That(newNow.x, Is.EqualTo(now.x).Within(1f), "and the new title is on the same path");
            Assert.That(newNow.y, Is.EqualTo(now.y).Within(1f));

            while (!vt.IsFinished && elapsed < 3f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            Assert.That(list.gameObject.activeSelf, Is.False);
            Assert.That(Rect(page).sizeDelta, Is.EqualTo(new Vector2(400f, 300f)));
            Assert.That(Rect(pageTitle).anchoredPosition, Is.EqualTo(new Vector2(100f, -200f)));
        }

        [UnityTest]
        public IEnumerator ViewTransition_AScopeNamespacesTheNamesBelowIt_SoPrefabInstancesPairByTheirItem()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(400f), Sizing.Fixed(200f));
            var list = Node(root.transform, "List", Sizing.Fixed(200f), Sizing.Fixed(200f));
            list.Direction = LayoutDirection.TopToBottom;
            // Two instances of one "prefab": the same part name under different scopes.
            var first = Node(list.transform, "Card 1", Sizing.Fixed(100f), Sizing.Fixed(50f));
            first.ViewTransitionScope = "1";
            var firstAvatar = Node(first.transform, "Avatar", Sizing.Fixed(20f), Sizing.Fixed(20f));
            firstAvatar.ViewTransitionName = "avatar";
            var second = Node(list.transform, "Card 2", Sizing.Fixed(100f), Sizing.Fixed(50f));
            second.ViewTransitionScope = "2";
            var secondAvatar = Node(second.transform, "Avatar", Sizing.Fixed(20f), Sizing.Fixed(20f));
            secondAvatar.ViewTransitionName = "avatar";
            var page = Node(root.transform, "Page", Sizing.Fixed(200f), Sizing.Fixed(200f));
            page.Padding = new Vector4(50f, 0f, 100f, 0f);
            var pageAvatar = Node(page.transform, "Avatar", Sizing.Fixed(40f), Sizing.Fixed(40f));
            pageAvatar.ViewTransitionName = "avatar";
            page.gameObject.SetActive(false);
            yield return null;

            // The page opens the second card: its scope picks that instance's avatar, and the list keeps its two.
            page.ViewTransitionScope = "2";
            var vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(list);
                page.gameObject.SetActive(true);
            }, LayoutTransition.Over(0.25f, EaseType.Linear));

            // The list left the flow, so the page sits where it was; the second avatar was 50 down (the first at 0).
            Assert.That(Rect(pageAvatar).anchoredPosition, Is.EqualTo(new Vector2(0f, -50f)), "the page's avatar starts where the second card's avatar was");
            Assert.That(secondAvatar.IsTransitioning, Is.True, "the second card's avatar flies out to it");
            Assert.That(firstAvatar.IsTransitioning, Is.False, "the first card's stays put");

            float elapsed = 0f;
            while (!vt.IsFinished && elapsed < 2f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            Assert.That(Rect(pageAvatar).anchoredPosition, Is.EqualTo(new Vector2(50f, -100f)));

            // Back: the list's two avatars are new; only the one in scope "2" takes the page's.
            vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(page);
                list.gameObject.SetActive(true);
            }, LayoutTransition.Over(0.25f, EaseType.Linear));
            Assert.That(secondAvatar.IsTransitioning, Is.True, "the second card's avatar flies in from the page");
            Assert.That(firstAvatar.IsTransitioning, Is.False, "the first card's appears in place");
            Assert.That(vt.IsFinished, Is.False);
        }

        [UnityTest]
        public IEnumerator ViewTransition_OnlyTheSideDrawnOnTopFades()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(200f), Sizing.Fixed(100f));
            var list = Node(root.transform, "List", Sizing.Fixed(100f), Sizing.Fixed(100f));
            var page = Node(root.transform, "Page", Sizing.Fixed(100f), Sizing.Fixed(100f));
            page.gameObject.SetActive(false);
            yield return null;

            // The page is the later sibling, drawn above: it fades in while the list stays opaque underneath.
            var vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(list);
                page.gameObject.SetActive(true);
            }, LayoutTransition.Over(0.25f, EaseType.Linear));
            Assert.That(page.GetComponent<CanvasGroup>(), Is.Not.Null.And.Property("alpha").EqualTo(0f).Within(Tolerance));
            Assert.That(list.GetComponent<CanvasGroup>(), Is.Null, "covered by the entering page: no fade");
            float elapsed = 0f;
            while (!vt.IsFinished && elapsed < 2f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            Assert.That(list.gameObject.activeSelf, Is.False);

            // Back: the page is still the one on top, so it fades out over the returning list, which stays opaque.
            vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(page);
                list.gameObject.SetActive(true);
            }, LayoutTransition.Over(0.25f, EaseType.Linear));
            Assert.That(page.GetComponent<CanvasGroup>(), Is.Not.Null.And.Property("alpha").EqualTo(1f).Within(Tolerance), "fading out from opaque");
            Assert.That(list.GetComponent<CanvasGroup>(), Is.Null, "revealed underneath: no fade");
            yield return null;
            Assert.That(page.GetComponent<CanvasGroup>().alpha, Is.LessThan(1f));
        }

        [UnityTest]
        public IEnumerator ViewTransition_APersistingPairSwapsPlaces_SoTheOldObjectFliesInWithItsState()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(400f), Sizing.Fixed(200f));
            var list = Node(root.transform, "List", Sizing.Fixed(200f), Sizing.Fixed(200f));
            var card = Node(list.transform, "Card", Sizing.Fixed(100f), Sizing.Fixed(100f));
            card.Padding = new Vector4(10f, 0f, 20f, 0f);
            var live = Node(card.transform, "Live", Sizing.Fixed(40f), Sizing.Fixed(40f));
            live.ViewTransitionName = "live";
            live.ViewTransitionPersist = true;
            var marker = live.gameObject.AddComponent<TestContent>();
            var page = Node(root.transform, "Page", Sizing.Fixed(200f), Sizing.Fixed(200f));
            page.Padding = new Vector4(50f, 0f, 60f, 0f);
            var copy = Node(page.transform, "Live", Sizing.Fixed(40f), Sizing.Fixed(40f));
            copy.ViewTransitionName = "live";
            copy.ViewTransitionPersist = true;
            page.gameObject.SetActive(false);
            yield return null;

            var vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(list);
                page.gameObject.SetActive(true);
            }, LayoutTransition.Over(0.25f, EaseType.Linear));

            Assert.That(live.transform.parent, Is.SameAs(page.transform), "the kept object moved into the page");
            Assert.That(copy.transform.parent, Is.SameAs(card.transform), "and its copy waits where it was");
            Assert.That(live.GetComponent<TestContent>(), Is.SameAs(marker), "state comes along: it is the same object");
            Assert.That(live.IsTransitioning, Is.True, "it flies from the card to the page's spot");
            // The list left the flow, so the page sits where it was and the card's padding is the whole distance.
            Assert.That(Rect(live).anchoredPosition, Is.EqualTo(new Vector2(10f, -20f)), "starting where the card had it");
            Assert.That(live.GetComponent<CanvasGroup>(), Is.Null, "no cross-fade for a persisting pair");
            Assert.That(copy.IsTransitioning, Is.False);

            float elapsed = 0f;
            while (!vt.IsFinished && elapsed < 2f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            Assert.That(Rect(live).anchoredPosition, Is.EqualTo(new Vector2(50f, -60f)), "at the copy's laid-out spot");
            Assert.That(list.gameObject.activeSelf, Is.False);

            // Back: the copy is the new side now, the kept object the old; they swap again.
            vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(page);
                list.gameObject.SetActive(true);
            }, LayoutTransition.Over(0.25f, EaseType.Linear));
            Assert.That(live.transform.parent, Is.SameAs(card.transform), "home again");
            Assert.That(copy.transform.parent, Is.SameAs(page.transform));
            Assert.That(live.IsTransitioning, Is.True);
            elapsed = 0f;
            while (!vt.IsFinished && elapsed < 2f) { elapsed += Time.unscaledDeltaTime; yield return null; }
            Assert.That(Rect(live).anchoredPosition, Is.EqualTo(new Vector2(10f, -20f)));
        }

        [UnityTest]
        public IEnumerator ViewTransition_FollowsTheUnscaledClockUnlessUseScaledTimeIsOn()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(200f), Sizing.Fixed(100f));
            var a = Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(50f));
            yield return null;

            Time.timeScale = 0f;
            try
            {
                LayoutSystem.StartViewTransition(() => root.Padding = new Vector4(100f, 0f, 0f, 0f), LayoutTransition.Over(0.2f, EaseType.Linear));
                yield return null;
                yield return null;
                Assert.That(Rect(a).anchoredPosition.x, Is.GreaterThan(0f), "unscaled by default: the move goes on while the game is paused");

                LayoutSystem.UseScaledTime = true;
                LayoutSystem.StartViewTransition(() => root.Padding = Vector4.zero, LayoutTransition.Over(0.2f, EaseType.Linear));
                float paused = Rect(a).anchoredPosition.x;
                yield return null;
                yield return null;
                Assert.That(Rect(a).anchoredPosition.x, Is.EqualTo(paused).Within(Tolerance), "scaled: the move stands still at time scale zero");
                Assert.That(a.IsTransitioning, Is.True);

                Time.timeScale = 1f;
                yield return null;
                Assert.That(Rect(a).anchoredPosition.x, Is.LessThan(paused), "and goes on when time does");
            }
            finally
            {
                Time.timeScale = 1f;
                LayoutSystem.UseScaledTime = false;
            }
        }

    }
}
