using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TimboJimbo.Core;
using TimboJimbo.UI.Layout;
using UnityEngine;
using UnityEngine.TestTools;

namespace TimboJimboTests.UI.Layout.PlayMode
{
    /// <summary>State a persisting object carries: proof that the object that lands is the one that left.</summary>
    public sealed class TestState : MonoBehaviour { }

    /// <summary>A motion that goes straight, but keeps its contents in their old state until halfway and then jumps them to the new one.</summary>
    [System.Serializable]
    public sealed class SwapHalfwayMotion : ITransitionMotion
    {
        public MotionFrame Evaluate(in MotionInput input)
        {
            var frame = MotionFrame.Straight(input);
            frame.Morph = input.Progress < 0.5f ? 0f : 1f;
            return frame;
        }
    }

    /// <summary>A motion that travels straight at half scale and half opacity until the move ends.</summary>
    [System.Serializable]
    public sealed class HalfLookMotion : ITransitionMotion
    {
        public MotionFrame Evaluate(in MotionInput input)
        {
            var frame = MotionFrame.Straight(input);
            if (input.Progress < 1f)
            {
                frame.Scale = 0.5f;
                frame.Opacity = 0.5f;
            }
            return frame;
        }
    }

    /// <summary>
    /// Play-mode tests of view transitions: movers, the groups (pairs, persisting pairs, exits, enters), the
    /// transition layer and its placeholders, carries, timing, types, scopes, the hand-over between transitions,
    /// hiding in place, calling a leaving node back, arrivals outside a transition and the animator contract.
    /// </summary>
    public sealed class ViewTransitionPlayModeTests
    {
        private const float Tolerance = 1e-3f;
        private static readonly LayoutTransition Linear = LayoutTransition.Over(0.25f, EaseType.Linear);
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
            LayoutSystem.AdjustTransition = null;
            LayoutSystem.SkipAllViewTransitions();
            Object.Destroy(_canvas);
            yield return null;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static LayoutNode Node(Transform parent, string name, Sizing width, Sizing height, string transitionName = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.pivot = new Vector2(0f, 1f);
            var node = go.AddComponent<LayoutNode>();
            node.Width = width;
            node.Height = height;
            node.ViewTransitionName = transitionName;
            return node;
        }

        private static RectTransform Rect(Component c) => (RectTransform)c.transform;

        private static Rect World(Component c) => LayoutEngine.WorldRect(Rect(c));

        private Transform Layer()
        {
            var layer = _canvas.transform.Find("View Transition Layer");
            return layer;
        }

        private static IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
                yield return null;
        }

        private static IEnumerator Finish(ViewTransition vt)
        {
            for (float t = 0f; !vt.IsFinished && t < 3f; t += Time.unscaledDeltaTime)
                yield return null;
            Assert.That(vt.IsFinished, Is.True, "the transition finished");
        }

        private static float Eased(ViewTransition.Flight flight)
        {
            float elapsed = flight.Elapsed - flight.Transition.Delay;
            float t = flight.Transition.Duration > 0f ? Mathf.Clamp01(elapsed / flight.Transition.Duration) : 1f;
            return EaseUtility.Evaluate(t, flight.Transition.Ease);
        }

        // The centre of a world rect, whose y is its top edge with y up.
        private static Vector2 CentreOf(Rect world) => new(world.x + world.width * 0.5f, world.y - world.height * 0.5f);

        private static void AssertPoint(Vector2 actual, float x, float y, string because)
        {
            Assert.That(actual.x, Is.EqualTo(x).Within(Tolerance), because + " (x)");
            Assert.That(actual.y, Is.EqualTo(y).Within(Tolerance), because + " (y)");
        }

        private static void AssertSameRect(Rect actual, Rect expected, string because, float within = 1f)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(within), because + " (x)");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(within), because + " (y)");
            Assert.That(actual.width, Is.EqualTo(expected.width).Within(within), because + " (width)");
            Assert.That(actual.height, Is.EqualTo(expected.height).Within(within), because + " (height)");
        }

        // A list on the left with a card in it, and a page on the right that is inactive until the test opens it:
        // the shape of every "a card opens into a page" transition.
        private (LayoutNode root, LayoutNode list, LayoutNode page) ListAndPage(float listWidth = 200f, float pageWidth = 400f)
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(listWidth + pageWidth), Sizing.Fixed(400f));
            var list = Node(root.transform, "List", Sizing.Fixed(listWidth), Sizing.Fixed(400f));
            var page = Node(root.transform, "Page", Sizing.Fixed(pageWidth), Sizing.Fixed(300f));
            page.gameObject.SetActive(false);
            return (root, list, page);
        }

        // ── Movers ─────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Movers_TravelFromWhereTheyWere_LayoutRectIsTheTarget_AndFinishedFires()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(100f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            yield return null;

            bool finished = false;
            var vt = LayoutSystem.StartViewTransition(() => root.Gap = 40f, Linear);
            vt.Finished += () => finished = true;

            Assert.That(b.IsTransitioning, Is.True);
            Assert.That(b.LayoutRect.y, Is.EqualTo(60f).Within(Tolerance), "the target is known at once");
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-20f).Within(Tolerance), "and the node still shows where it was");

            yield return Wait(0.1f);
            float y = Rect(b).anchoredPosition.y;
            Assert.That(y, Is.LessThan(-20f).And.GreaterThan(-60f), "on its way");
            Assert.That(b.VisualRect.y, Is.EqualTo(-y).Within(Tolerance));

            yield return Finish(vt);
            Assert.That(finished, Is.True);
            Assert.That(b.IsTransitioning, Is.False);
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-60f).Within(Tolerance));
        }

        [UnityTest]
        public IEnumerator Movers_ANodesOwnTransitionOverridesTheDefault_AndADelayWaits()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(100f), Sizing.Fixed(300f));
            root.Direction = LayoutDirection.TopToBottom;
            Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var c = Node(root.transform, "C", Sizing.Fixed(50f), Sizing.Fixed(20f));
            c.Transition = LayoutTransition.Over(0.5f, EaseType.Linear).After(0.15f);
            yield return null;

            var vt = LayoutSystem.StartViewTransition(() => root.Gap = 40f, LayoutTransition.Over(0.1f, EaseType.Linear));
            yield return Wait(0.1f);
            Assert.That(Rect(c).anchoredPosition.y, Is.EqualTo(-40f).Within(Tolerance), "still waiting through its delay");
            yield return Wait(0.1f);
            Assert.That(b.IsTransitioning, Is.False, "the default ended");
            Assert.That(c.IsTransitioning, Is.True, "the override runs on");
            Assert.That(Rect(c).anchoredPosition.y, Is.LessThan(-40f).And.GreaterThan(-120f));
            yield return Finish(vt);
            Assert.That(Rect(c).anchoredPosition.y, Is.EqualTo(-120f).Within(Tolerance));
        }

        [UnityTest]
        public IEnumerator Movers_AChildThatOnlyRodeItsParentDoesNothingOfItsOwn()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(300f), Sizing.Fixed(300f));
            root.Direction = LayoutDirection.TopToBottom;
            Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var box = Node(root.transform, "Box", Sizing.Fixed(100f), Sizing.Fixed(100f));
            box.Padding = new Vector4(10f, 0f, 10f, 0f);
            var inner = Node(box.transform, "Inner", Sizing.Fixed(20f), Sizing.Fixed(20f));
            yield return null;
            var innerStart = World(inner);

            var vt = LayoutSystem.StartViewTransition(() => root.Gap = 40f, Linear);
            Assert.That(box.IsTransitioning, Is.True);
            Assert.That(inner.IsTransitioning, Is.False, "its rect inside the box did not change");
            AssertSameRect(World(inner), innerStart, "it starts where it was, carried by the box");

            yield return Wait(0.1f);
            var boxNow = World(box);
            Assert.That(World(inner).x, Is.EqualTo(boxNow.x + 10f).Within(Tolerance), "and rides the box");
            Assert.That(World(inner).y, Is.EqualTo(boxNow.y - 10f).Within(Tolerance));
            yield return Finish(vt);
            Assert.That(Rect(inner).anchoredPosition, Is.EqualTo(new Vector2(10f, -10f)));
        }

        [UnityTest]
        public IEnumerator Movers_AReparentedNodeMovesFromWhereItWasShown()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(400f), Sizing.Fixed(100f));
            var left = Node(root.transform, "Left", Sizing.Fixed(200f), Sizing.Fixed(100f));
            var right = Node(root.transform, "Right", Sizing.Fixed(200f), Sizing.Fixed(100f));
            right.Padding = new Vector4(50f, 0f, 20f, 0f);
            var item = Node(left.transform, "Item", Sizing.Fixed(30f), Sizing.Fixed(30f));
            yield return null;
            var start = World(item);

            var vt = LayoutSystem.StartViewTransition(() => item.transform.SetParent(right.transform, false), Linear);
            Assert.That(item.IsTransitioning, Is.True);
            AssertSameRect(World(item), start, "it leaves from where it was, under its new parent");
            Assert.That(Rect(item).anchoredPosition, Is.EqualTo(new Vector2(-200f, 0f)), "which is left of the right box");
            yield return Finish(vt);
            Assert.That(Rect(item).anchoredPosition, Is.EqualTo(new Vector2(50f, -20f)));
        }

        [UnityTest]
        public IEnumerator Movers_ARootsRectNeverAnimates()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(100f), Sizing.Fit());
            root.Direction = LayoutDirection.TopToBottom;
            Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            b.gameObject.SetActive(false);
            yield return null;
            Assert.That(root.RectTransform.rect.height, Is.EqualTo(20f).Within(Tolerance));

            LayoutSystem.StartViewTransition(() => b.gameObject.SetActive(true), Linear);
            Assert.That(root.IsTransitioning, Is.False);
            Assert.That(root.RectTransform.rect.height, Is.EqualTo(40f).Within(Tolerance), "what a ScrollRect or a UGUI parent reads is final at once");
            Assert.That(b.IsTransitioning, Is.True, "while the new row enters");
        }

        [UnityTest]
        public IEnumerator Movers_AMeasuredLeafTakesItsSizeAtOnce_AndAnimatesOnlyItsPosition()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(300f), Sizing.Fixed(100f));
            root.Padding = new Vector4(10f, 0f, 0f, 0f);
            var leaf = Node(root.transform, "Leaf", Sizing.Fit(), Sizing.Fit());
            var content = leaf.gameObject.AddComponent<TestContent>();
            content.Size = new Vector2(100f, 10f);
            yield return null;

            var vt = LayoutSystem.StartViewTransition(() => { root.Padding = new Vector4(60f, 0f, 0f, 0f); content.Size = new Vector2(150f, 30f); leaf.MarkDirty(); }, Linear);
            Assert.That(Rect(leaf).sizeDelta, Is.EqualTo(new Vector2(150f, 30f)), "the size snaps");
            Assert.That(Rect(leaf).anchoredPosition.x, Is.EqualTo(10f).Within(Tolerance), "the position starts where it was");
            yield return Finish(vt);
            Assert.That(Rect(leaf).anchoredPosition.x, Is.EqualTo(60f).Within(Tolerance));
        }

        [UnityTest]
        public IEnumerator Movers_APlainPassDuringTheMoveKeepsIt_AndAChangedTargetRetargetsWithItsTiming()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(100f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            yield return null;

            LayoutSystem.StartViewTransition(() => root.Gap = 40f, LayoutTransition.Over(0.5f, EaseType.Linear));
            yield return null;
            float before = Rect(b).anchoredPosition.y;
            LayoutSystem.ForceLayout(root);
            Assert.That(b.IsTransitioning, Is.True, "a plain pass does not end the move");
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(before).Within(Tolerance));

            root.Gap = 10f;
            yield return null;
            Assert.That(b.LayoutRect.y, Is.EqualTo(30f).Within(Tolerance));
            Assert.That(b.IsTransitioning, Is.True, "retargeted rather than snapped");
            for (float t = 0f; b.IsTransitioning && t < 2f; t += Time.unscaledDeltaTime) yield return null;
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-30f).Within(Tolerance));
        }

        // ── Enters and exits ───────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Enter_TheTopmostNewNodeFadesInInPlace_AndItsSubtreeRides()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(200f), Sizing.Fixed(200f));
            var panel = Node(root.transform, "Panel", Sizing.Fixed(100f), Sizing.Fixed(100f));
            var inner = Node(panel.transform, "Inner", Sizing.Fixed(20f), Sizing.Fixed(20f));
            panel.gameObject.SetActive(false);
            yield return null;

            var vt = LayoutSystem.StartViewTransition(() => panel.gameObject.SetActive(true), Linear);
            var group = panel.GetComponent<CanvasGroup>();
            Assert.That(group, Is.Not.Null.And.Property("alpha").EqualTo(0f).Within(Tolerance), "it fades in through a group added for it");
            Assert.That(inner.GetComponent<CanvasGroup>(), Is.Null, "the node inside it rides");
            Assert.That(panel.transform.parent, Is.SameAs(root.transform), "in place, not lifted");
            yield return Wait(0.1f);
            Assert.That(group.alpha, Is.GreaterThan(0f).And.LessThan(1f));
            yield return Finish(vt);
            Assert.That(group.alpha, Is.EqualTo(1f).Within(Tolerance), "and stays on the object, back at its alpha");
        }

        [UnityTest]
        public IEnumerator Exit_FadesOutInPlace_ThenDeactivatesBeforeTheCallback_AndLetsClicksThrough()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(200f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            var a = Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            yield return null;

            bool? activeInCallback = null;
            var vt = LayoutSystem.StartViewTransition(() => LayoutSystem.Exit(a, () => activeInCallback = a.gameObject.activeSelf), Linear);
            Assert.That(a.IsExiting, Is.True);
            Assert.That(b.IsTransitioning, Is.True, "the sibling closes the gap");
            Assert.That(b.LayoutRect.y, Is.EqualTo(0f).Within(Tolerance));
            var group = a.GetComponent<CanvasGroup>();
            Assert.That(group, Is.Not.Null.And.Property("alpha").EqualTo(1f).Within(Tolerance));
            Assert.That(group.blocksRaycasts, Is.False, "a leaving node lets clicks through");
            yield return Wait(0.1f);
            Assert.That(group.alpha, Is.LessThan(1f));
            yield return Finish(vt);
            Assert.That(activeInCallback, Is.False, "deactivated before the callback runs");
            Assert.That(a.IsExiting, Is.False);
            Assert.That(group.blocksRaycasts, Is.True, "put back for its next showing");
        }

        [UnityTest]
        public IEnumerator Exit_OutsideAViewTransition_LeavesAtOnce_OrPlaysItsAnimator()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(200f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            var a = Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var c = Node(root.transform, "C", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var animator = b.gameObject.AddComponent<TestAnimator>();
            yield return null;

            LayoutSystem.Exit(a);
            yield return null;
            Assert.That(a.gameObject.activeSelf, Is.False, "no animator, no transition: gone at that pass");
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(0f).Within(Tolerance), "and the siblings snap");

            LayoutSystem.Exit(b);
            yield return null;
            Assert.That(animator.Exits, Is.EqualTo(1));
            Assert.That(animator.LastTransition, Is.Null, "an exit outside a transition hands the animator null");
            Assert.That(b.gameObject.activeSelf, Is.True, "still shown while its effect plays");
            Assert.That(Rect(c).anchoredPosition.y, Is.EqualTo(0f).Within(Tolerance));
            animator.Complete();
            Assert.That(b.gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator Animator_PlaysTheEnterAndTheExit_AndFinishedWaitsForBoth()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(200f), Sizing.Fixed(200f));
            var leaving = Node(root.transform, "Leaving", Sizing.Fixed(50f), Sizing.Fixed(50f));
            var entering = Node(root.transform, "Entering", Sizing.Fixed(50f), Sizing.Fixed(50f));
            entering.gameObject.SetActive(false);
            var exitAnimator = leaving.gameObject.AddComponent<TestAnimator>();
            var enterAnimator = entering.gameObject.AddComponent<TestAnimator>();
            yield return null;

            var vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(leaving);
                entering.gameObject.SetActive(true);
            }, LayoutTransition.Over(0.05f, EaseType.Linear));
            Assert.That(exitAnimator.Exits, Is.EqualTo(1));
            Assert.That(enterAnimator.Enters, Is.EqualTo(1));
            Assert.That(enterAnimator.LastTransition, Is.SameAs(vt));
            Assert.That(entering.GetComponent<CanvasGroup>(), Is.Null, "an animator replaces the fade");

            yield return Wait(0.2f);
            Assert.That(vt.IsFinished, Is.False, "waits for the effects");
            Assert.That(leaving.gameObject.activeSelf, Is.True);
            exitAnimator.Complete();
            Assert.That(leaving.gameObject.activeSelf, Is.False, "the exit's done lets the node go");
            Assert.That(vt.IsFinished, Is.False);
            enterAnimator.Complete();
            Assert.That(vt.IsFinished, Is.True);
        }

        [UnityTest]
        public IEnumerator Hide_KeepsItsPlace_FadingOutWhereItIs_OrAtOnceOutsideATransition()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(200f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            var a = Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            yield return null;

            var vt = LayoutSystem.StartViewTransition(() => LayoutSystem.Hide(a), Linear);
            Assert.That(a.Shown, Is.False);
            Assert.That(b.IsTransitioning, Is.False, "nothing closes up: it keeps its place");
            var group = a.GetComponent<CanvasGroup>();
            Assert.That(group, Is.Not.Null.And.Property("alpha").EqualTo(1f).Within(Tolerance), "fading out where it is");
            Assert.That(group.blocksRaycasts, Is.False);
            yield return Wait(0.1f);
            Assert.That(group.alpha, Is.GreaterThan(0f).And.LessThan(1f));
            yield return Finish(vt);
            Assert.That(a.gameObject.activeSelf, Is.True, "still there");
            Assert.That(group.alpha, Is.EqualTo(0f).Within(Tolerance), "unseen");
            Assert.That(group.blocksRaycasts, Is.False, "and letting clicks through");
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-20f).Within(Tolerance), "in the slot it keeps");

            vt = LayoutSystem.StartViewTransition(() => LayoutSystem.Show(a), Linear);
            Assert.That(a.Shown, Is.True);
            Assert.That(group.alpha, Is.EqualTo(0f).Within(Tolerance), "shown again, it enters");
            yield return Finish(vt);
            Assert.That(group.alpha, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(group.blocksRaycasts, Is.True);

            LayoutSystem.Hide(a);
            Assert.That(group.alpha, Is.EqualTo(0f).Within(Tolerance), "outside a transition it goes at once");
            Assert.That(group.blocksRaycasts, Is.False);
            LayoutSystem.Show(a);
            Assert.That(group.alpha, Is.EqualTo(1f).Within(Tolerance), "and comes back at once");
            Assert.That(group.blocksRaycasts, Is.True);
        }

        // ── Pairs and the layer ────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Pair_BothHalvesAreLiftedIntoTheLayer_FlyOneLineToTheNewSlot_AndComeBackDown()
        {
            var (root, list, page) = ListAndPage();
            list.Padding = new Vector4(10f, 0f, 20f, 0f);
            var card = Node(list.transform, "Card", Sizing.Fixed(100f), Sizing.Fixed(50f), "card");
            page.Padding = new Vector4(100f, 0f, 200f, 0f);
            var header = Node(page.transform, "Header", Sizing.Fixed(200f), Sizing.Fixed(60f), "card");
            yield return null;
            var cardStart = World(card);

            var vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(list);
                page.gameObject.SetActive(true);
            }, Linear);

            var layer = Layer();
            Assert.That(layer, Is.Not.Null.And.Property("parent").SameAs(_canvas.transform));
            Assert.That(layer.GetSiblingIndex(), Is.EqualTo(_canvas.transform.childCount - 1), "the layer is the canvas's last child");
            Assert.That(card.transform.parent, Is.SameAs(layer), "the old half is lifted");
            Assert.That(header.transform.parent, Is.SameAs(layer), "and the new half");
            Assert.That(header.transform.GetSiblingIndex(), Is.GreaterThan(card.transform.GetSiblingIndex()), "the new half draws above the old");
            Assert.That(header._placeholder, Is.Not.Null.And.Property("transform").Property("parent").SameAs(page.transform), "a placeholder holds the new half's slot");
            AssertSameRect(World(header), cardStart, "the new half starts where the card was");
            AssertSameRect(World(card), cardStart, "so does the old");
            Assert.That(header.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0f).Within(Tolerance), "fading in");
            Assert.That(card.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f).Within(Tolerance), "over the old half fading out");

            yield return Wait(0.1f);
            AssertSameRect(World(card), World(header), "the two stay together on the way");
            var flight = vt.FlightOf(header);
            var end = World(header._placeholder);
            var expected = new Rect(Vector2.Lerp(cardStart.position, end.position, Eased(flight)), Vector2.Lerp(cardStart.size, end.size, Eased(flight)));
            AssertSameRect(World(header), expected, "on the straight line to its slot");

            yield return Finish(vt);
            Assert.That(header.transform.parent, Is.SameAs(page.transform), "back in its tree");
            Assert.That(header._placeholder, Is.Null);
            Assert.That(Rect(header).anchoredPosition, Is.EqualTo(new Vector2(100f, -200f)), "at its slot");
            Assert.That(Rect(header).sizeDelta, Is.EqualTo(new Vector2(200f, 60f)));
            Assert.That(header.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(list.gameObject.activeSelf, Is.False, "the list has left");
            Assert.That(card.transform.parent, Is.SameAs(list.transform), "with its card back inside it");
            Assert.That(layer.childCount, Is.EqualTo(0), "and the layer is empty");
        }

        [UnityTest]
        public IEnumerator Pair_AnOldHalfThatStaysInThePageDoesNotFly()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(400f), Sizing.Fixed(200f));
            var a = Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(50f), "badge");
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(50f));
            b.gameObject.SetActive(false);
            yield return null;

            var vt = LayoutSystem.StartViewTransition(() =>
            {
                a.ViewTransitionName = null;
                b.ViewTransitionName = "badge";
                b.gameObject.SetActive(true);
            }, Linear);
            Assert.That(a.transform.parent, Is.SameAs(root.transform), "still in the page, so simply still there");
            Assert.That(a.IsTransitioning, Is.False);
            Assert.That(b.transform.parent, Is.SameAs(Layer()), "the new half flies alone from the old one's spot");
            AssertSameRect(World(b), World(a), "starting on top of it");
            yield return Finish(vt);
            Assert.That(Rect(b).anchoredPosition, Is.EqualTo(new Vector2(50f, 0f)));
        }

        [UnityTest]
        public IEnumerator Pair_AHiddenOldHalfFliesIntoTheNewOne_AndWaitsInItsSlotUnseen_UntilShownAgain()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(400f), Sizing.Fixed(200f));
            var a = Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(50f), "badge");
            var b = Node(root.transform, "B", Sizing.Fixed(100f), Sizing.Fixed(100f), "badge");
            b.gameObject.SetActive(false);
            yield return null;
            var start = World(a);

            // The name stays where it is: a hidden node carries none, so the node that appears has it to itself.
            var vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Hide(a);
                b.gameObject.SetActive(true);
            }, Linear);
            Assert.That(a.transform.parent, Is.SameAs(Layer()), "the hidden old half lifts off and flies into the new one");
            Assert.That(b.transform.parent, Is.SameAs(Layer()));
            AssertSameRect(World(a), start, "both starting where it was");
            AssertSameRect(World(b), start, "(the new half too)");
            yield return Finish(vt);
            var group = a.GetComponent<CanvasGroup>();
            Assert.That(a.transform.parent, Is.SameAs(root.transform), "back in its slot");
            Assert.That(Rect(a).anchoredPosition, Is.EqualTo(new Vector2(0f, 0f)));
            Assert.That(Rect(b).anchoredPosition, Is.EqualTo(new Vector2(50f, 0f)), "which it keeps");
            Assert.That(group.alpha, Is.EqualTo(0f).Within(Tolerance), "unseen");
            Assert.That(group.blocksRaycasts, Is.False);

            var from = World(b);
            vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Show(a);
                LayoutSystem.Exit(b);
            }, Linear);
            Assert.That(a.transform.parent, Is.SameAs(Layer()), "shown again, it is the new half");
            AssertSameRect(World(a), from, "flying out of the leaving one's spot");
            Assert.That(group.alpha, Is.EqualTo(0f).Within(Tolerance), "fading in");
            yield return Finish(vt);
            Assert.That(a.transform.parent, Is.SameAs(root.transform));
            Assert.That(Rect(a).anchoredPosition, Is.EqualTo(new Vector2(0f, 0f)));
            Assert.That(group.alpha, Is.EqualTo(1f).Within(Tolerance), "seen again");
            Assert.That(group.blocksRaycasts, Is.True);
            Assert.That(b.gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator Pair_ANamedPartInsideANamedContainerFliesItsOwnStraightLine()
        {
            var (root, list, page) = ListAndPage();
            list.Padding = new Vector4(10f, 0f, 10f, 0f);
            var card = Node(list.transform, "Card", Sizing.Fixed(100f), Sizing.Fixed(50f), "card");
            card.Padding = new Vector4(5f, 0f, 5f, 0f);
            var cardTitle = Node(card.transform, "Title", Sizing.Fixed(40f), Sizing.Fixed(10f), "title");
            page.Padding = new Vector4(100f, 0f, 200f, 0f);
            var body = Node(page.transform, "Body", Sizing.Fixed(300f), Sizing.Fixed(200f), "card");
            body.Padding = new Vector4(50f, 0f, 50f, 0f);
            var pageTitle = Node(body.transform, "Title", Sizing.Fixed(80f), Sizing.Fixed(20f), "title");
            yield return null;
            var titleStart = World(cardTitle);

            var vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(list);
                page.gameObject.SetActive(true);
            }, Linear);
            var layer = Layer();
            Assert.That(pageTitle.transform.parent, Is.SameAs(layer), "the part is lifted out of its container");
            Assert.That(pageTitle.transform.GetSiblingIndex(), Is.GreaterThan(body.transform.GetSiblingIndex()), "and stacks above it");
            // Where the title ends: the page's slot (at the root's left, since the list left the flow), plus the
            // body's slot in it, plus the title's in that.
            var rootTopLeft = World(root);
            var titleEnd = new Rect(rootTopLeft.x + 100f + 50f, rootTopLeft.y - 200f - 50f, 80f, 20f);

            yield return Wait(0.1f);
            var flight = vt.FlightOf(pageTitle);
            var expected = new Rect(Vector2.Lerp(titleStart.position, titleEnd.position, Eased(flight)), Vector2.Lerp(titleStart.size, titleEnd.size, Eased(flight)));
            AssertSameRect(World(pageTitle), expected, "a straight line from the card's title to the page's, whatever the body is doing");
            AssertSameRect(World(cardTitle), World(pageTitle), "with the old title on the same line");

            yield return Finish(vt);
            Assert.That(pageTitle.transform.parent, Is.SameAs(body.transform));
            Assert.That(Rect(pageTitle).anchoredPosition, Is.EqualTo(new Vector2(50f, -50f)));
            AssertSameRect(World(pageTitle), titleEnd, "landed", 0.01f);
        }

        [UnityTest]
        public IEnumerator Pair_LandsWhereLayoutPutsTheSlotByThen()
        {
            var (root, list, page) = ListAndPage();
            var card = Node(list.transform, "Card", Sizing.Fixed(100f), Sizing.Fixed(50f), "card");
            page.Padding = new Vector4(20f, 0f, 20f, 0f);
            var header = Node(page.transform, "Header", Sizing.Fixed(100f), Sizing.Fixed(50f), "card");
            yield return null;

            var vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(list);
                page.gameObject.SetActive(true);
            }, Linear);
            yield return Wait(0.05f);
            // The page reflows under the flight (a scroll, a resize): the slot moves and the flight follows it.
            page.Padding = new Vector4(120f, 0f, 80f, 0f);
            yield return Finish(vt);
            Assert.That(Rect(header).anchoredPosition, Is.EqualTo(new Vector2(120f, -80f)), "landed at the slot's rect now");
        }

        [UnityTest]
        public IEnumerator Pair_GroupsStackInCaptureOrder()
        {
            var (root, list, page) = ListAndPage();
            list.Direction = LayoutDirection.TopToBottom;
            var oldA = Node(list.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(50f), "a");
            var oldB = Node(list.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(50f), "b");
            page.Direction = LayoutDirection.TopToBottom;
            var newB = Node(page.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(50f), "b");
            var newA = Node(page.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(50f), "a");
            yield return null;

            LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(list);
                page.gameObject.SetActive(true);
            }, Linear);
            // Old paint order decides, whatever the new page's order is: a below b, each old half below its new half.
            int[] order = { oldA.transform.GetSiblingIndex(), newA.transform.GetSiblingIndex(), oldB.transform.GetSiblingIndex(), newB.transform.GetSiblingIndex() };
            Assert.That(order, Is.Ordered.Ascending, "old a, new a, old b, new b");
        }

        [UnityTest]
        public IEnumerator Pair_AMeasuredNewHalfTakesItsTargetSizeAtOnce_AndTheOldHalfKeepsItsOwn()
        {
            var (root, list, page) = ListAndPage();
            var card = Node(list.transform, "Card", Sizing.Fit(), Sizing.Fit(), "title");
            card.gameObject.AddComponent<TestContent>().Size = new Vector2(40f, 10f);
            var header = Node(page.transform, "Header", Sizing.Fit(), Sizing.Fit(), "title");
            header.gameObject.AddComponent<TestContent>().Size = new Vector2(80f, 20f);
            yield return null;

            LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(list);
                page.gameObject.SetActive(true);
            }, Linear);
            Assert.That(Rect(header).sizeDelta, Is.EqualTo(new Vector2(80f, 20f)), "text never shows an in-between size");
            Assert.That(Rect(card).sizeDelta, Is.EqualTo(new Vector2(40f, 10f)));
            Assert.That(World(header).position, Is.EqualTo(World(card).position), "positions still travel together");
        }

        [UnityTest]
        public IEnumerator Scope_NamespacesTheNamesBelowIt_SoPrefabInstancesPairByTheirItem()
        {
            var (root, list, page) = ListAndPage();
            list.Direction = LayoutDirection.TopToBottom;
            var first = Node(list.transform, "Card 1", Sizing.Fixed(100f), Sizing.Fixed(50f));
            first.ViewTransitionScope = "1";
            var firstAvatar = Node(first.transform, "Avatar", Sizing.Fixed(20f), Sizing.Fixed(20f), "avatar");
            var second = Node(list.transform, "Card 2", Sizing.Fixed(100f), Sizing.Fixed(50f));
            second.ViewTransitionScope = "2";
            var secondAvatar = Node(second.transform, "Avatar", Sizing.Fixed(20f), Sizing.Fixed(20f), "avatar");
            page.Padding = new Vector4(50f, 0f, 100f, 0f);
            var pageAvatar = Node(page.transform, "Avatar", Sizing.Fixed(40f), Sizing.Fixed(40f), "avatar");
            yield return null;
            var secondStart = World(secondAvatar);

            page.ViewTransitionScope = "2";
            var vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(list);
                page.gameObject.SetActive(true);
            }, Linear);
            AssertSameRect(World(pageAvatar), secondStart, "the page's avatar starts on the second card's, at its size");
            Assert.That(secondAvatar.transform.parent, Is.SameAs(Layer()), "which flies out to it");
            Assert.That(firstAvatar.transform.parent, Is.SameAs(first.transform), "while the first card's stays put");
            yield return Finish(vt);
            Assert.That(Rect(pageAvatar).anchoredPosition, Is.EqualTo(new Vector2(50f, -100f)));

            // Back: only the card in scope "2" takes the page's avatar.
            vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(page);
                list.gameObject.SetActive(true);
            }, Linear);
            Assert.That(secondAvatar.transform.parent, Is.SameAs(Layer()), "the second card's avatar flies in from the page");
            Assert.That(firstAvatar.transform.parent, Is.SameAs(first.transform), "the first card's appears in place");
            yield return Finish(vt);
            Assert.That(secondAvatar.transform.parent, Is.SameAs(second.transform));
        }

        [UnityTest]
        public IEnumerator DuplicateName_SkipsTheTransition_WithAWarning()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(300f), Sizing.Fixed(100f));
            Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(50f), "same");
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(50f), "same");
            b.gameObject.SetActive(false);
            var c = Node(root.transform, "C", Sizing.Fixed(50f), Sizing.Fixed(50f));
            yield return null;

            LogAssert.Expect(LogType.Warning, new Regex("two nodes at once"));
            var vt = LayoutSystem.StartViewTransition(() => { b.gameObject.SetActive(true); root.Gap = 20f; }, Linear);
            Assert.That(vt.IsFinished, Is.True);
            Assert.That(c.IsTransitioning, Is.False, "the update applied at once");
            Assert.That(Rect(c).anchoredPosition.x, Is.EqualTo(140f).Within(Tolerance));
        }

        // ── Persist ────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Persist_TheKeptObjectFliesToTheCopysSlot_AndTheTwoSwapWhenTheTransitionEnds()
        {
            var (root, list, page) = ListAndPage();
            var card = Node(list.transform, "Card", Sizing.Fixed(100f), Sizing.Fixed(100f));
            card.Padding = new Vector4(10f, 0f, 20f, 0f);
            var slot = Node(card.transform, "Slot", Sizing.Fixed(40f), Sizing.Fixed(40f));
            var live = Node(slot.transform, "Live", Sizing.Grow(), Sizing.Grow(), "live");
            live.ViewTransitionPersist = true;
            var marker = live.gameObject.AddComponent<TestState>();
            page.Padding = new Vector4(50f, 0f, 60f, 0f);
            var pageSlot = Node(page.transform, "Slot", Sizing.Fixed(120f), Sizing.Fixed(120f));
            var copy = Node(pageSlot.transform, "Live", Sizing.Grow(), Sizing.Grow(), "live");
            copy.ViewTransitionPersist = true;
            yield return null;
            var liveStart = World(live);

            var vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(list);
                page.gameObject.SetActive(true);
            }, Linear);
            Assert.That(live.transform.parent, Is.SameAs(Layer()), "the kept object is lifted");
            Assert.That(live.GetComponent<TestState>(), Is.SameAs(marker), "it is the same object");
            Assert.That(live.GetComponent<CanvasGroup>(), Is.Null, "and does not fade");
            Assert.That(copy.transform.parent, Is.SameAs(pageSlot.transform), "the copy marks the slot");
            Assert.That(copy.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0f).Within(Tolerance), "hidden");
            AssertSameRect(World(live), liveStart, "starting where it was");

            yield return Wait(0.1f);
            var flight = vt.FlightOf(live);
            var end = World(copy);
            Assert.That(World(live).width, Is.EqualTo(Mathf.Lerp(40f, 120f, Eased(flight))).Within(1f), "growing on the way");

            yield return Finish(vt);
            Assert.That(live.transform.parent, Is.SameAs(pageSlot.transform), "landed in the copy's slot");
            Assert.That(copy.transform.parent, Is.SameAs(slot.transform), "the copy took the old one");
            Assert.That(copy.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(Rect(live).sizeDelta, Is.EqualTo(new Vector2(120f, 120f)), "sized by its slot");
            AssertSameRect(World(live), end, "exactly where the copy was", 0.01f);
        }

        [UnityTest]
        public IEnumerator Persist_TheKeptObjectLandsInTheNewTree_WhenTheOldTreeIsDestroyedFirst()
        {
            var (root, list, page) = ListAndPage();
            list.Transition = LayoutTransition.Over(0.05f, EaseType.Linear);
            var card = Node(list.transform, "Card", Sizing.Fixed(100f), Sizing.Fixed(100f));
            var live = Node(card.transform, "Live", Sizing.Fixed(40f), Sizing.Fixed(40f), "live");
            live.ViewTransitionPersist = true;
            var pageSlot = Node(page.transform, "Slot", Sizing.Fixed(120f), Sizing.Fixed(120f));
            var copy = Node(pageSlot.transform, "Live", Sizing.Fixed(40f), Sizing.Fixed(40f), "live");
            copy.ViewTransitionPersist = true;
            yield return null;

            var vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(list, () => Object.Destroy(list.gameObject));
                page.gameObject.SetActive(true);
            }, Linear);
            yield return Wait(0.15f);
            Assert.That(list == null, Is.True, "the list is gone before the flight lands");
            Assert.That(live.transform.parent, Is.SameAs(Layer()), "the kept object is still flying");
            yield return Finish(vt);
            yield return null;
            Assert.That(live != null && live.transform.parent == pageSlot.transform, Is.True, "and lands in the new tree");
            Assert.That(copy == null, Is.True, "the copy went with the old tree");
        }

        [UnityTest]
        public IEnumerator Carry_APersistingNodeMovedToANewParentFliesThereLifted_TheSameObjectThroughout()
        {
            var (root, list, page) = ListAndPage();
            var slot = Node(list.transform, "Slot", Sizing.Fixed(40f), Sizing.Fixed(40f));
            var live = Node(slot.transform, "Live", Sizing.Grow(), Sizing.Grow());
            live.ViewTransitionPersist = true;
            var marker = live.gameObject.AddComponent<TestState>();
            page.Padding = new Vector4(50f, 0f, 60f, 0f);
            var pageSlot = Node(page.transform, "Slot", Sizing.Fixed(120f), Sizing.Fixed(120f));
            yield return null;
            var start = World(live);

            var vt = LayoutSystem.StartViewTransition(() =>
            {
                live.transform.SetParent(pageSlot.transform, false);
                LayoutSystem.Exit(list);
                page.gameObject.SetActive(true);
            }, Linear);
            Assert.That(live.transform.parent, Is.SameAs(Layer()), "the moved node is lifted");
            Assert.That(live._placeholder.transform.parent, Is.SameAs(pageSlot.transform), "its placeholder holds its new slot");
            Assert.That(live.GetComponent<CanvasGroup>(), Is.Null, "it does not fade");
            AssertSameRect(World(live), start, "it starts where it was");

            yield return Finish(vt);
            Assert.That(live.transform.parent, Is.SameAs(pageSlot.transform), "and lands in its new slot");
            Assert.That(live.GetComponent<TestState>(), Is.SameAs(marker), "the same object throughout");
            AssertSameRect(World(live), World(pageSlot), "filling it", 0.01f);
        }

        // ── Interruption ───────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator StartingAnotherTransition_TakesOver_MovesGoOnFromWhereTheyAre_EffectsPlayOn_WhileSkipEndsThem()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(300f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            var entering = Node(root.transform, "Entering", Sizing.Fixed(50f), Sizing.Fixed(50f));
            entering.gameObject.SetActive(false);
            var animator = entering.gameObject.AddComponent<TestAnimator>();
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            yield return null;

            var first = LayoutSystem.StartViewTransition(() => entering.gameObject.SetActive(true), Linear);
            Assert.That(b.IsTransitioning, Is.True);
            yield return Wait(0.05f);
            float before = Rect(b).anchoredPosition.y;
            Assert.That(before, Is.LessThan(0f).And.GreaterThan(-50f), "on its way");

            var second = LayoutSystem.StartViewTransition(() => root.Gap = 30f, Linear);
            Assert.That(first.IsFinished, Is.True, "the first hands over and finishes");
            Assert.That(animator.Skips, Is.EqualTo(0), "but its enter plays on");
            Assert.That(b.LayoutRect.y, Is.EqualTo(80f).Within(Tolerance));
            Assert.That(b.IsTransitioning, Is.True, "the move goes on");
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(before).Within(Tolerance), "from where it was, with no jump");
            Assert.That(second.IsFinished, Is.False);
            animator.Complete();

            second.SkipTransition();
            Assert.That(second.IsFinished, Is.True);
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-80f).Within(Tolerance), "skip lands everything at once");

            var third = LayoutSystem.StartViewTransition(() => LayoutSystem.Exit(entering), Linear);
            Assert.That(animator.Exits, Is.EqualTo(1));
            third.SkipTransition();
            Assert.That(animator.Skips, Is.EqualTo(1), "an explicit skip ends effects too");
            Assert.That(entering.gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator ATransitionStartedInsideAnUpdateJoinsIt_SoEveryLeavingNodeLeavesFromItsOwnPlace()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(100f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            var a = Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var c = Node(root.transform, "C", Sizing.Fixed(50f), Sizing.Fixed(20f));
            yield return null;

            // What a "clear all" does when each item dismisses itself with its own transition.
            var vt = LayoutSystem.StartViewTransition(() =>
            {
                foreach (var node in new[] { a, b, c })
                    LayoutSystem.StartViewTransition(() => LayoutSystem.Exit(node), Linear);
            }, Linear);
            Assert.That(a.IsExiting && b.IsExiting && c.IsExiting, Is.True);
            Assert.That(Rect(b).anchoredPosition.y, Is.EqualTo(-20f).Within(Tolerance), "b leaves from its own place");
            Assert.That(Rect(c).anchoredPosition.y, Is.EqualTo(-40f).Within(Tolerance), "and so does c");
            yield return Finish(vt);
        }

        [UnityTest]
        public IEnumerator Carry_OrderedByWhereItWasCaptured_SoItDrawsAboveTheBodyItLeft()
        {
            var (root, list, page) = ListAndPage();
            var card = Node(list.transform, "Card", Sizing.Fixed(100f), Sizing.Fixed(100f), "card");
            var slot = Node(card.transform, "Slot", Sizing.Fixed(40f), Sizing.Fixed(40f));
            var live = Node(slot.transform, "Live", Sizing.Grow(), Sizing.Grow());
            live.ViewTransitionPersist = true;
            var header = Node(page.transform, "Header", Sizing.Fixed(200f), Sizing.Fixed(200f), "card");
            var pageSlot = Node(header.transform, "Slot", Sizing.Fixed(120f), Sizing.Fixed(120f));
            yield return null;

            yield return Finish(LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(list);
                page.gameObject.SetActive(true);
                live.transform.SetParent(pageSlot.transform, false);
            }, Linear));

            // On the way back it has already been moved into the list, which draws before the page; it is still
            // ordered by where it was captured, inside the page's header, so it draws above the header flying home.
            var back = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(page);
                list.gameObject.SetActive(true);
                live.transform.SetParent(slot.transform, false);
            }, Linear);
            Assert.That(live.transform.parent, Is.SameAs(Layer()));
            Assert.That(header.transform.parent, Is.SameAs(Layer()));
            Assert.That(live.transform.GetSiblingIndex(), Is.GreaterThan(header.transform.GetSiblingIndex()), "above the body it flies out of");
            yield return Finish(back);
            Assert.That(live.transform.parent, Is.SameAs(slot.transform));
        }

        [UnityTest]
        public IEnumerator Scopes_TransitionsInSeparateSubtreesRunSideBySide_AnOverlappingOneTakesOver()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(400f), Sizing.Fixed(200f));
            var left = Node(root.transform, "Left", Sizing.Fixed(200f), Sizing.Fixed(200f));
            left.Direction = LayoutDirection.TopToBottom;
            var right = Node(root.transform, "Right", Sizing.Fixed(200f), Sizing.Fixed(200f));
            right.Direction = LayoutDirection.TopToBottom;
            Node(left.transform, "LA", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var lb = Node(left.transform, "LB", Sizing.Fixed(50f), Sizing.Fixed(20f));
            Node(right.transform, "RA", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var rb = Node(right.transform, "RB", Sizing.Fixed(50f), Sizing.Fixed(20f));
            yield return null;

            var slow = LayoutTransition.Over(0.5f, EaseType.Linear);
            var onLeft = LayoutSystem.StartViewTransition(left, () => left.Gap = 40f, slow);
            var onRight = LayoutSystem.StartViewTransition(right, () => right.Gap = 40f, slow);
            Assert.That(onLeft.IsFinished, Is.False, "a transition in another subtree leaves it alone");
            Assert.That(lb.IsTransitioning && rb.IsTransitioning, Is.True);

            yield return Wait(0.1f);
            float before = Rect(lb).anchoredPosition.y;
            var againOnLeft = LayoutSystem.StartViewTransition(left, () => left.Gap = 10f, slow);
            Assert.That(onLeft.IsFinished, Is.True, "one over the same subtree takes over");
            Assert.That(onRight.IsFinished, Is.False);
            Assert.That(lb.IsTransitioning, Is.True);
            Assert.That(Rect(lb).anchoredPosition.y, Is.EqualTo(before).Within(Tolerance), "going on from where it was");
            Assert.That(lb.LayoutRect.y, Is.EqualTo(30f).Within(Tolerance));

            var everything = LayoutSystem.StartViewTransition(() => { }, slow);
            Assert.That(againOnLeft.IsFinished && onRight.IsFinished, Is.True, "an unscoped transition overlaps every other");
            yield return Finish(everything);
            Assert.That(Rect(lb).anchoredPosition.y, Is.EqualTo(-30f).Within(Tolerance));
            Assert.That(Rect(rb).anchoredPosition.y, Is.EqualTo(-60f).Within(Tolerance));
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator Scope_WarnsAboutANodeOutsideItThatTheUpdateMoved()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(200f), Sizing.Fixed(300f));
            root.Direction = LayoutDirection.TopToBottom;
            var box = Node(root.transform, "Box", Sizing.Fixed(100f), Sizing.Fit());
            var inner = Node(box.transform, "Inner", Sizing.Fixed(20f), Sizing.Fixed(20f));
            Node(root.transform, "After", Sizing.Fixed(20f), Sizing.Fixed(20f));
            yield return null;

            LogAssert.Expect(LogType.Warning, new Regex("'After' is outside the view transition's scope"));
            var vt = LayoutSystem.StartViewTransition(box, () => inner.Height = Sizing.Fixed(60f), Linear);
            yield return Finish(vt);
        }

        [UnityTest]
        public IEnumerator DeactivatingANodeInsideATransition_WarnsThatItVanished()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(200f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            var a = Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            yield return null;

            LogAssert.Expect(LogType.Warning, new Regex("'A' was deactivated inside a view transition"));
            var vt = LayoutSystem.StartViewTransition(() => a.gameObject.SetActive(false), Linear);
            yield return Finish(vt);
        }
#endif

        [Test]
        public void Path_CurvatureBendsAStraightLineIntoAnArcTowardsTheShorterAxisFirstCorner()
        {
            var from = Vector2.zero;
            var to = new Vector2(100f, 40f);   // y is the shorter axis: the L's corner is (0, 40)

            var straight = new ArcMotion(0f);
            AssertPoint(straight.PointAlongPath(from, to, 0.25f), 25f, 10f, "no curvature is the straight line, at an even pace");

            var arc = new ArcMotion(1f);   // the control point at the corner
            AssertPoint(arc.PointAlongPath(from, to, 0.25f), 6.25f, 17.5f, "setting off along the shorter axis");
            AssertPoint(arc.PointAlongPath(from, to, 0.5f), 25f, 30f, "bowed towards the corner");
            AssertPoint(arc.PointAlongPath(from, to, 1f), 100f, 40f, "and ending where the line does");

            var half = new ArcMotion(0.5f);  // the control point halfway from the midpoint to the corner
            AssertPoint(half.PointAlongPath(from, to, 0.5f), 37.5f, 25f, "a gentler arc");

            var transition = LayoutTransition.Over(1f).Curved(0.5f);
            Assert.That(transition.Motion, Is.InstanceOf<ArcMotion>(), "Curved is an arc motion");
            Assert.That(transition.After(0.2f).Motion, Is.SameAs(transition.Motion), "After keeps the motion");
            Assert.That(LayoutTransition.Over(1f).Curved(0f).Motion, Is.Null, "no curvature is no motion: a straight line");
        }

        [UnityTest]
        public IEnumerator Motion_AndTimingInheritSeparately()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(300f), Sizing.Fixed(300f));
            var ownMotion = Node(root.transform, "Own motion", Sizing.Fixed(20f), Sizing.Fixed(20f));
            var ownTiming = Node(root.transform, "Own timing", Sizing.Fixed(20f), Sizing.Fixed(20f));
            ownMotion.Motion = new StraightMotion();
            ownTiming.Transition = LayoutTransition.Over(2f, EaseType.Linear);
            yield return null;

            var vt = LayoutSystem.StartViewTransition(() => root.Padding = Insets.Of(left: 100f, top: 40f),
                LayoutTransition.Over(1f, EaseType.Linear).Curved(1f));
            Assert.That(ownMotion._animTransition.Motion, Is.InstanceOf<StraightMotion>(), "a node's own motion replaces the transition's");
            Assert.That(ownMotion._animTransition.Duration, Is.EqualTo(1f), "at the transition's pace");
            Assert.That(ownTiming._animTransition.Motion, Is.InstanceOf<ArcMotion>(), "a node with only its own timing keeps the transition's motion");
            Assert.That(ownTiming._animTransition.Duration, Is.EqualTo(2f), "at its own pace");
            Assert.That(ownTiming.Transition?.Motion, Is.Null, "a node's timing never carries a motion");
            yield return Finish(vt);
        }

        [UnityTest]
        public IEnumerator AdjustTransition_ChangesHowEveryNodeMoves_WhoeverAuthoredIt()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(300f), Sizing.Fixed(300f));
            var plain = Node(root.transform, "Plain", Sizing.Fixed(20f), Sizing.Fixed(20f));
            var authored = Node(root.transform, "Authored", Sizing.Fixed(20f), Sizing.Fixed(20f));
            authored.Transition = LayoutTransition.Over(0.5f).Curved(1f);
            yield return null;

            LayoutSystem.AdjustTransition = (node, t) =>
            {
                t.Duration *= 2f;
                return t.With(new StraightMotion());
            };
            var vt = LayoutSystem.StartViewTransition(() => root.Padding = Insets.Of(left: 100f, top: 40f),
                LayoutTransition.Over(1f, EaseType.Linear).Curved(1f));
            Assert.That(plain._animTransition.Duration, Is.EqualTo(2f), "the call's timing, adjusted");
            Assert.That(authored._animTransition.Duration, Is.EqualTo(1f), "a node's own timing, adjusted too");
            Assert.That(plain._animTransition.Motion, Is.InstanceOf<StraightMotion>());
            Assert.That(authored._animTransition.Motion, Is.InstanceOf<StraightMotion>(), "whoever authored the motion");
            LayoutSystem.AdjustTransition = null;
            yield return Finish(vt);
        }

        [UnityTest]
        public IEnumerator Path_AMoverFollowsItsTransitionsCurve()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(300f), Sizing.Fixed(300f));
            var b = Node(root.transform, "B", Sizing.Fixed(20f), Sizing.Fixed(20f));
            yield return null;

            // Right 100, down 40: the arc sets off downwards, so early on it is further along down than across.
            var vt = LayoutSystem.StartViewTransition(() => root.Padding = Insets.Of(left: 100f, top: 40f),
                LayoutTransition.Over(2f, EaseType.Linear).Curved(1f));
            yield return Wait(0.3f);
            var position = Rect(b).anchoredPosition;
            Assert.That(-position.y / 40f, Is.GreaterThan(position.x / 100f + 0.01f), "further along down than across");
            yield return Finish(vt);
            AssertPoint(Rect(b).anchoredPosition, 100f, -40f, "it ends at its target");
        }

        [UnityTest]
        public IEnumerator Path_OnlyWhatMovesAsAWholeCurves_ANodeMovingInsideItRidesAlong()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(400f), Sizing.Fixed(400f));
            var box = Node(root.transform, "Box", Sizing.Fixed(200f), Sizing.Fixed(200f));
            var inner = Node(box.transform, "Inner", Sizing.Fixed(20f), Sizing.Fixed(20f));
            var still = Node(root.transform, "Still", Sizing.Fixed(20f), Sizing.Fixed(20f));
            yield return null;

            var vt = LayoutSystem.StartViewTransition(() =>
            {
                root.Padding = Insets.Of(left: 100f, top: 40f);   // the box moves, and the node beside it
                box.Padding = Insets.Of(left: 30f, top: 60f);     // and the inner node moves inside the box
            }, LayoutTransition.Over(1f, EaseType.Linear).Curved(1f));
            Assert.That(box.IsTransitioning && inner.IsTransitioning && still.IsTransitioning, Is.True);
            Assert.That(box._animTransition.Motion, Is.Not.Null, "the box moves as a whole: it curves");
            Assert.That(still._animTransition.Motion, Is.Not.Null, "so does its sibling");
            Assert.That(inner._animTransition.Motion, Is.Null, "the node inside it goes straight within it");
            yield return Finish(vt);
        }

        [UnityTest]
        public IEnumerator Morph_DecidesHowTheTwoHalvesOfAPairShow()
        {
            var (root, list, page) = ListAndPage();
            var card = Node(list.transform, "Card", Sizing.Fixed(100f), Sizing.Fixed(50f), "card");
            var header = Node(page.transform, "Header", Sizing.Fixed(200f), Sizing.Fixed(60f), "card");
            yield return null;

            var vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(list);
                page.gameObject.SetActive(true);
            }, LayoutTransition.Over(1f, EaseType.Linear).With(new SwapHalfwayMotion()));
            yield return Wait(0.2f);
            Assert.That(card.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f).Within(Tolerance), "before the swap the old half shows fully");
            Assert.That(header.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0f).Within(Tolerance), "and the new half not at all: no cross-fade");
            yield return Wait(0.5f);
            Assert.That(card.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0f).Within(Tolerance), "after it, the other way round");
            Assert.That(header.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f).Within(Tolerance));
            yield return Finish(vt);
            Assert.That(header.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f).Within(Tolerance), "and the fade is put back when it lands");
        }

        [UnityTest]
        public IEnumerator Morph_APartInsideAFlyingContainerKeepsItsOldPlaceInItUntilItMorphs()
        {
            var (root, list, page) = ListAndPage();
            var card = Node(list.transform, "Card", Sizing.Fixed(100f), Sizing.Fixed(100f), "card");
            card.Padding = Insets.Of(left: 10f, top: 10f);
            var cardTitle = Node(card.transform, "Title", Sizing.Fixed(40f), Sizing.Fixed(20f), "title");
            var header = Node(page.transform, "Header", Sizing.Fixed(300f), Sizing.Fixed(200f), "card");
            header.Padding = Insets.Of(left: 150f, top: 120f);
            var headerTitle = Node(header.transform, "Title", Sizing.Fixed(40f), Sizing.Fixed(20f), "title");
            yield return null;
            var oldOffset = CentreOf(World(cardTitle)) - CentreOf(World(card));

            var vt = LayoutSystem.StartViewTransition(() =>
            {
                LayoutSystem.Exit(list);
                page.gameObject.SetActive(true);
            }, LayoutTransition.Over(1f, EaseType.Linear).With(new SwapHalfwayMotion()));
            yield return Wait(0.2f);
            var offset = CentreOf(World(headerTitle)) - CentreOf(World(header));
            AssertPoint(offset, oldOffset.x, oldOffset.y, "before the morph the part keeps its old place in the container, wherever the container is");
            yield return Wait(0.5f);
            var afterMorph = CentreOf(World(headerTitle)) - CentreOf(World(header));
            yield return Finish(vt);
            var newOffset = CentreOf(World(headerTitle)) - CentreOf(World(header));
            AssertPoint(afterMorph, newOffset.x, newOffset.y, "after it, its new place");
        }

        [UnityTest]
        public IEnumerator Morph_ANodeMovingInsideAMovingParentKeepsItsOldPlaceInItUntilTheParentMorphs()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(400f), Sizing.Fixed(400f));
            var box = Node(root.transform, "Box", Sizing.Fixed(200f), Sizing.Fixed(200f));
            var inner = Node(box.transform, "Inner", Sizing.Fixed(20f), Sizing.Fixed(20f));
            yield return null;

            var vt = LayoutSystem.StartViewTransition(() =>
            {
                root.Padding = Insets.Of(left: 100f, top: 40f);   // the box moves
                box.Padding = Insets.Of(left: 30f, top: 60f);     // and the inner node moves inside it
            }, LayoutTransition.Over(1f, EaseType.Linear).With(new SwapHalfwayMotion()));
            yield return Wait(0.2f);
            Assert.That(Rect(box).anchoredPosition.x, Is.GreaterThan(0f), "the box is on its way");
            AssertPoint(Rect(inner).anchoredPosition, 0f, 0f, "while the inner node keeps its old place in it");
            yield return Wait(0.5f);
            AssertPoint(Rect(inner).anchoredPosition, 30f, -60f, "then, the box's contents morphed, its new one");
            yield return Finish(vt);
        }

        [UnityTest]
        public IEnumerator Motion_ACustomMotionScalesAroundTheCentreAndFades_ThenTheLookComesOff()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(400f), Sizing.Fixed(400f));
            var b = Node(root.transform, "B", Sizing.Fixed(40f), Sizing.Fixed(40f));
            yield return null;

            var vt = LayoutSystem.StartViewTransition(() => root.Padding = Insets.Of(left: 100f, top: 40f),
                LayoutTransition.Over(1f, EaseType.Linear).With(new HalfLookMotion()));
            yield return Wait(0.1f);
            Assert.That(Rect(b).localScale.x, Is.EqualTo(0.5f).Within(Tolerance), "scaled by the motion");
            Assert.That(b.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0.5f).Within(Tolerance), "and faded");
            var drawn = World(b);
            var laidOut = b.VisualRect;
            Assert.That(drawn.width, Is.EqualTo(laidOut.width * 0.5f).Within(0.01f));
            // Scaled around its centre, which is where the layout rect's centre is.
            var expectedCentre = LayoutEngine.WorldRect(Rect(root)).position + new Vector2(laidOut.center.x, -laidOut.center.y);
            Assert.That(drawn.x + drawn.width * 0.5f, Is.EqualTo(expectedCentre.x).Within(0.01f), "around its centre (x)");
            Assert.That(drawn.y - drawn.height * 0.5f, Is.EqualTo(expectedCentre.y).Within(0.01f), "around its centre (y)");

            yield return Finish(vt);
            Assert.That(Rect(b).localScale, Is.EqualTo(Vector3.one), "the look comes off when the move ends");
            Assert.That(b.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f).Within(Tolerance));
            AssertPoint(Rect(b).anchoredPosition, 100f, -40f, "and it rests at its target");
        }

        [UnityTest]
        public IEnumerator NodeTransition_InstantSnapsWhileTheRestAnimate_AndNullInherits()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(100f), Sizing.Fixed(300f));
            root.Direction = LayoutDirection.TopToBottom;
            Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var c = Node(root.transform, "C", Sizing.Fixed(50f), Sizing.Fixed(20f));
            c.Transition = LayoutTransition.Instant;
            yield return null;

            var vt = LayoutSystem.StartViewTransition(() => root.Gap = 40f, Linear);
            Assert.That(b.IsTransitioning, Is.True);
            Assert.That(c.IsTransitioning, Is.False, "an Instant node snaps");
            Assert.That(Rect(c).anchoredPosition.y, Is.EqualTo(-120f).Within(Tolerance));
            Assert.That(vt.TransitionFor(c).IsAnimated, Is.False);

            c.Transition = null;
            Assert.That(c.Transition.HasValue, Is.False);
            Assert.That(vt.TransitionFor(c).Duration, Is.EqualTo(Linear.Duration), "null inherits the transition's timing");
            yield return Finish(vt);
        }

        [UnityTest]
        public IEnumerator Types_AndTiming_AreReadableByAnimators()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(200f), Sizing.Fixed(200f));
            var entering = Node(root.transform, "Entering", Sizing.Fixed(50f), Sizing.Fixed(50f));
            entering.gameObject.SetActive(false);
            var animator = entering.gameObject.AddComponent<TestAnimator>();
            yield return null;

            var vt = LayoutSystem.StartViewTransition(() => entering.gameObject.SetActive(true), Linear, "forward");
            Assert.That(animator.LastTransition, Is.SameAs(vt));
            Assert.That(vt.HasType("forward"), Is.True);
            Assert.That(vt.HasType("back"), Is.False);
            Assert.That(vt.Types.Count, Is.EqualTo(1));
            Assert.That(vt.Transition.Duration, Is.EqualTo(Linear.Duration));
            entering.Transition = LayoutTransition.Over(1f);
            Assert.That(vt.TransitionFor(entering).Duration, Is.EqualTo(1f), "a node's own timing comes first");
            animator.Complete();
            yield return Finish(vt);
        }

        // ── Calling back, arrivals and Settle ──────────────────────────────────

        [UnityTest]
        public IEnumerator Show_CallsBackALeavingNode_WhichEntersAgain_AndItsExitNeverConcludes()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(200f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            var a = Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var animator = a.gameObject.AddComponent<TestAnimator>();
            yield return null;

            bool exited = false;
            LayoutSystem.StartViewTransition(() => LayoutSystem.Exit(a, () => exited = true), Linear);
            Assert.That(animator.Exits, Is.EqualTo(1));
            Assert.That(a.Shown, Is.False, "on its way out");
            yield return null;

            var second = LayoutSystem.StartViewTransition(() => LayoutSystem.Show(a), Linear);
            Assert.That(a.IsExiting, Is.False);
            Assert.That(a.Shown, Is.True);
            Assert.That(animator.Skips, Is.EqualTo(1), "the exit effect is told to stop");
            Assert.That(animator.Enters, Is.EqualTo(1), "and the node enters again");
            Assert.That(animator.LastTransition, Is.SameAs(second));
            animator.Complete();
            yield return Finish(second);
            Assert.That(exited, Is.False, "the exit's callback never runs");
            Assert.That(a.gameObject.activeSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator Show_ALeavingNodesFadeGoesOnFromWhereItIs()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(200f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            var a = Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            yield return null;

            var slow = LayoutTransition.Over(0.5f, EaseType.Linear);
            LayoutSystem.StartViewTransition(() => LayoutSystem.Exit(a), slow);
            yield return Wait(0.2f);
            var group = a.GetComponent<CanvasGroup>();
            float alpha = group.alpha;
            Assert.That(alpha, Is.GreaterThan(0f).And.LessThan(1f));

            var vt = LayoutSystem.StartViewTransition(() => a.Shown = true, slow);
            Assert.That(group.alpha, Is.EqualTo(alpha).Within(Tolerance), "it fades back in from where it had got to");
            yield return Wait(0.1f);
            Assert.That(group.alpha, Is.GreaterThan(alpha));
            yield return Finish(vt);
            Assert.That(group.alpha, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(a.gameObject.activeSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator Arrival_OutsideATransition_PlaysTheAnimatorsEnter_ButNotOnAFirstLayoutOrInsideSettle()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(200f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            yield return null;

            var item = Node(root.transform, "Item", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var animator = item.gameObject.AddComponent<TestAnimator>();
            yield return null;
            Assert.That(animator.Enters, Is.EqualTo(1), "a node appearing under a shown node arrives");
            Assert.That(animator.LastTransition, Is.Null, "with no transition");
            animator.Complete();

            TestAnimator settled;
            using (LayoutSystem.Settle(root))
                settled = Node(root.transform, "Settled", Sizing.Fixed(50f), Sizing.Fixed(20f)).gameObject.AddComponent<TestAnimator>();
            yield return null;
            Assert.That(settled.Enters, Is.EqualTo(0), "inside Settle it just appears");

            var fresh = Node(_canvas.transform, "Fresh", Sizing.Fixed(100f), Sizing.Fixed(100f));
            var inner = Node(fresh.transform, "Inner", Sizing.Fixed(20f), Sizing.Fixed(20f)).gameObject.AddComponent<TestAnimator>();
            yield return null;
            Assert.That(inner.Enters, Is.EqualTo(0), "a tree's first layout arrives nowhere");
        }

        [UnityTest]
        public IEnumerator Settle_InsideATransition_NewNodesAppearWithoutEntering()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(200f), Sizing.Fixed(200f));
            root.Direction = LayoutDirection.TopToBottom;
            var a = Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            yield return null;

            LayoutNode row = null;
            var vt = LayoutSystem.StartViewTransition(() =>
            {
                using (LayoutSystem.Settle(root))
                    row = Node(root.transform, "Row", Sizing.Fixed(50f), Sizing.Fixed(20f));
                row.transform.SetAsFirstSibling();
            }, Linear);
            Assert.That(row.GetComponent<CanvasGroup>(), Is.Null, "no fade");
            Assert.That(row.IsTransitioning, Is.False);
            Assert.That(a.IsTransitioning, Is.True, "while what was there still moves");
            yield return Finish(vt);
        }

        [UnityTest]
        public IEnumerator FollowsTheUnscaledClockUnlessUseScaledTimeIsOn()
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
                // The frame that sets the scale already took its delta at zero; the next one moves.
                for (int i = 0; i < 5 && Rect(a).anchoredPosition.x >= paused; i++)
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
