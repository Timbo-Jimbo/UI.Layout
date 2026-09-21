using NUnit.Framework;
using TimboJimbo.UI.Layout;
using UnityEngine;
using UnityEngine.UI;

namespace TimboJimboTests.UI.Layout.PlayMode
{
    /// <summary>Tests of the engine through <see cref="LayoutSystem.ForceLayout"/>; positions are asserted with a top-left pivot so anchoredPosition is (x, -y) of the engine-space rect.</summary>
    public sealed class LayoutEngineTests
    {
        private const float Tolerance = 1e-3f;
        private GameObject _canvas;

        [SetUp]
        public void SetUp()
        {
            _canvas = new GameObject("Canvas", typeof(Canvas));
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_canvas);

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

        private LayoutNode Root(float width, float height, LayoutDirection direction)
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(width), Sizing.Fixed(height));
            root.Direction = direction;
            return root;
        }

        private static Vector2 Pos(LayoutNode n) => n.RectTransform.anchoredPosition;
        private static Vector2 Size(LayoutNode n) => n.RectTransform.rect.size;

        private static void AssertVector(Vector2 actual, float x, float y)
        {
            Assert.That(actual.x, Is.EqualTo(x).Within(Tolerance));
            Assert.That(actual.y, Is.EqualTo(y).Within(Tolerance));
        }

        [Test]
        public void Fit_VerticalStack_SumsChildrenGapsAndPadding()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fixed(200f), Sizing.Fit());
            root.Direction = LayoutDirection.TopToBottom;
            root.Gap = 6f;
            root.Padding = new Vector4(5f, 5f, 10f, 10f);
            var a = Node(root.transform, "A", Sizing.Fixed(100f), Sizing.Fixed(30f));
            var b = Node(root.transform, "B", Sizing.Fixed(100f), Sizing.Fixed(30f));
            var c = Node(root.transform, "C", Sizing.Fixed(100f), Sizing.Fixed(30f));

            LayoutSystem.ForceLayout(root);

            Assert.That(Size(root).y, Is.EqualTo(90f + 12f + 20f).Within(Tolerance));
            AssertVector(Pos(a), 5f, -10f);
            AssertVector(Pos(b), 5f, -46f);
            AssertVector(Pos(c), 5f, -82f);
            AssertVector(Size(b), 100f, 30f);
            Assert.That(b.RectTransform.anchorMin, Is.EqualTo(Vector2.up));
            Assert.That(b.RectTransform.anchorMax, Is.EqualTo(Vector2.up));
        }

        [Test]
        public void Commit_PlacesThePivot()
        {
            var root = Root(200f, 100f, LayoutDirection.TopToBottom);
            var a = Node(root.transform, "A", Sizing.Fixed(40f), Sizing.Fixed(20f));
            a.RectTransform.pivot = new Vector2(0.5f, 0.5f);

            LayoutSystem.ForceLayout(root);

            AssertVector(Pos(a), 20f, -10f);
        }

        [Test]
        public void Grow_SharesSpaceSmallestFirstUpToMax()
        {
            var root = Root(300f, 50f, LayoutDirection.LeftToRight);
            var a = Node(root.transform, "A", Sizing.Grow(0f, 100f), Sizing.Fixed(10f));
            var b = Node(root.transform, "B", Sizing.Grow(), Sizing.Fixed(10f));

            LayoutSystem.ForceLayout(root);

            Assert.That(Size(a).x, Is.EqualTo(100f).Within(Tolerance));
            Assert.That(Size(b).x, Is.EqualTo(200f).Within(Tolerance));
            Assert.That(Pos(b).x, Is.EqualTo(100f).Within(Tolerance));

            a.Width = Sizing.Grow(50f);
            LayoutSystem.ForceLayout(root);

            Assert.That(Size(a).x, Is.EqualTo(150f).Within(Tolerance));
            Assert.That(Size(b).x, Is.EqualTo(150f).Within(Tolerance));
        }

        [Test]
        public void Grow_LeavesRoomForFixedAndGaps()
        {
            var root = Root(100f, 50f, LayoutDirection.LeftToRight);
            root.Gap = 10f;
            Node(root.transform, "A", Sizing.Fixed(30f), Sizing.Fixed(10f));
            var b = Node(root.transform, "B", Sizing.Grow(), Sizing.Fixed(10f));

            LayoutSystem.ForceLayout(root);

            Assert.That(Size(b).x, Is.EqualTo(60f).Within(Tolerance));
            Assert.That(Pos(b).x, Is.EqualTo(40f).Within(Tolerance));
        }

        [Test]
        public void Shrink_FitLeafWrapsToItsMinimum_FixedUntouched()
        {
            var root = Root(100f, 50f, LayoutDirection.LeftToRight);
            var a = Node(root.transform, "A", Sizing.Fixed(30f), Sizing.Fixed(10f));
            var b = Node(root.transform, "B", Sizing.Fit(), Sizing.Fit());
            var content = b.gameObject.AddComponent<TestContent>();
            content.Min = 20f;
            content.Measurer = w => w < 0f ? new Vector2(100f, 10f) : new Vector2(w, 1000f / w);

            LayoutSystem.ForceLayout(root);

            Assert.That(Size(a).x, Is.EqualTo(30f).Within(Tolerance));
            Assert.That(Size(b).x, Is.EqualTo(70f).Within(Tolerance));
            Assert.That(Size(b).y, Is.EqualTo(1000f / 70f).Within(Tolerance));
            Assert.That(content.Widths, Does.Contain(70f));
        }

        [Test]
        public void Shrink_FitContainerShrinksToItsMinimum_AndItsChildWrapsAcross()
        {
            var root = Root(100f, 50f, LayoutDirection.LeftToRight);
            var c = Node(root.transform, "C", Sizing.Fit(), Sizing.Fit());
            c.Direction = LayoutDirection.TopToBottom;
            var leaf = Node(c.transform, "Leaf", Sizing.Fit(), Sizing.Fit());
            var content = leaf.gameObject.AddComponent<TestContent>();
            content.Size = new Vector2(90f, 10f);
            content.Min = 10f;
            var d = Node(root.transform, "D", Sizing.Fixed(50f), Sizing.Fixed(10f));

            LayoutSystem.ForceLayout(root);

            Assert.That(Size(d).x, Is.EqualTo(50f).Within(Tolerance));
            Assert.That(Size(c).x, Is.EqualTo(50f).Within(Tolerance));
            Assert.That(Size(leaf).x, Is.EqualTo(50f).Within(Tolerance));
        }

        [Test]
        public void Percent_IsOfInnerSizeMinusGaps()
        {
            var root = Root(200f, 50f, LayoutDirection.LeftToRight);
            root.Padding = new Vector4(10f, 10f, 0f, 0f);
            root.Gap = 20f;
            var a = Node(root.transform, "A", Sizing.Percent(0.5f), Sizing.Fixed(10f));
            var b = Node(root.transform, "B", Sizing.Fixed(10f), Sizing.Fixed(10f));

            LayoutSystem.ForceLayout(root);

            Assert.That(Size(a).x, Is.EqualTo(80f).Within(Tolerance));
            Assert.That(Pos(a).x, Is.EqualTo(10f).Within(Tolerance));
            Assert.That(Pos(b).x, Is.EqualTo(110f).Within(Tolerance));
        }

        [Test]
        public void Across_GrowFillsInner_AlignmentPlacesFixed()
        {
            var root = Root(200f, 100f, LayoutDirection.TopToBottom);
            root.Padding = new Vector4(10f, 10f, 0f, 0f);
            var a = Node(root.transform, "A", Sizing.Grow(), Sizing.Fixed(10f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(10f));

            LayoutSystem.ForceLayout(root);
            Assert.That(Size(a).x, Is.EqualTo(180f).Within(Tolerance));
            Assert.That(Pos(a).x, Is.EqualTo(10f).Within(Tolerance));
            Assert.That(Pos(b).x, Is.EqualTo(10f).Within(Tolerance));

            root.AlignX = AlignX.Center;
            LayoutSystem.ForceLayout(root);
            Assert.That(Pos(b).x, Is.EqualTo(75f).Within(Tolerance));

            root.AlignX = AlignX.Right;
            LayoutSystem.ForceLayout(root);
            Assert.That(Pos(b).x, Is.EqualTo(140f).Within(Tolerance));
        }

        [Test]
        public void Along_AlignmentCentresAndEndsTheRun()
        {
            var root = Root(100f, 200f, LayoutDirection.TopToBottom);
            var a = Node(root.transform, "A", Sizing.Fixed(10f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(10f), Sizing.Fixed(20f));

            root.AlignY = AlignY.Center;
            LayoutSystem.ForceLayout(root);
            AssertVector(Pos(a), 0f, -80f);
            AssertVector(Pos(b), 0f, -100f);

            root.AlignY = AlignY.Bottom;
            LayoutSystem.ForceLayout(root);
            AssertVector(Pos(b), 0f, -180f);
        }

        [Test]
        public void Leaves_PlainChildrenAreUntouched_ContentIgnoredWhenThereAreChildNodes()
        {
            var root = Root(200f, 100f, LayoutDirection.TopToBottom);
            var plain = new GameObject("Plain", typeof(RectTransform)).GetComponent<RectTransform>();
            plain.SetParent(root.transform, false);
            plain.anchoredPosition = new Vector2(7f, 7f);
            var container = Node(root.transform, "Container", Sizing.Fit(), Sizing.Fit());
            var content = container.gameObject.AddComponent<TestContent>();
            content.Size = new Vector2(500f, 500f);
            Node(container.transform, "Child", Sizing.Fixed(40f), Sizing.Fixed(20f));

            LayoutSystem.ForceLayout(root);

            AssertVector(plain.anchoredPosition, 7f, 7f);
            AssertVector(Size(container), 40f, 20f);
            Assert.That(content.Widths, Is.Empty);
        }

        [Test]
        public void Floating_AttachesPointsAndLeavesTheFlow()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fit(), Sizing.Fixed(100f));
            root.Direction = LayoutDirection.LeftToRight;
            var a = Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(10f));
            var f = Node(root.transform, "F", Sizing.Fixed(20f), Sizing.Fixed(20f));
            f.AttachTo = AttachTo.Parent;
            f.ElementPoint = AttachPoint.CenterCenter;
            f.ParentPoint = AttachPoint.RightTop;
            f.FloatOffset = new Vector2(-5f, 5f);

            LayoutSystem.ForceLayout(root);

            Assert.That(Size(root).x, Is.EqualTo(50f).Within(Tolerance));
            AssertVector(Pos(a), 0f, 0f);
            // Centre (10, 10) of F sits at the parent's top-right (50, 0) plus the offset: F's top-left is (35, -5) in engine space.
            AssertVector(Pos(f), 35f, 5f);
        }

        [Test]
        public void AspectRatio_DerivesFitHeightFromWidth()
        {
            var root = Root(200f, 200f, LayoutDirection.TopToBottom);
            var a = Node(root.transform, "A", Sizing.Fixed(100f), Sizing.Fit());
            a.AspectRatio = 2f;

            LayoutSystem.ForceLayout(root);

            AssertVector(Size(a), 100f, 50f);
        }

        [Test]
        public void Offset_MovesOnlyThatNode_WithoutAPassOnACleanTree()
        {
            var root = Root(100f, 100f, LayoutDirection.TopToBottom);
            var a = Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            LayoutSystem.ForceLayout(root);
            int passes = LayoutEngine.PassCount;

            a.Offset = new Vector2(3f, -4f);

            Assert.That(LayoutSystem.IsDirty(root), Is.False);
            Assert.That(LayoutEngine.PassCount, Is.EqualTo(passes));
            AssertVector(Pos(a), 3f, -4f);
            AssertVector(Pos(b), 0f, -20f);

            LayoutSystem.ForceLayout(root);
            AssertVector(Pos(a), 3f, -4f);
            Assert.That(a.LayoutRect, Is.EqualTo(new Rect(0f, 0f, 50f, 20f)));
        }

        [Test]
        public void Purity_RepeatedPassesAreIdentical_AndLayoutRectMatches()
        {
            var root = Root(100f, 100f, LayoutDirection.TopToBottom);
            root.Gap = 4f;
            var a = Node(root.transform, "A", Sizing.Grow(), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Grow(), Sizing.Fixed(20f));

            LayoutSystem.ForceLayout(root);
            var first = (Pos(a), Size(a), Pos(b), Size(b));
            LayoutSystem.ForceLayout(root);

            Assert.That((Pos(a), Size(a), Pos(b), Size(b)), Is.EqualTo(first));
            Assert.That(b.LayoutRect, Is.EqualTo(new Rect(0f, 24f, 100f, 20f)));
            Assert.That(root.LayoutRect, Is.EqualTo(new Rect(0f, 0f, 100f, 100f)));
        }

        [Test]
        public void UguiElement_WidthDependentHeightIsMeasuredAtTheFinalWidth_InOnePass()
        {
            var root = Root(100f, 50f, LayoutDirection.LeftToRight);
            Node(root.transform, "A", Sizing.Fixed(30f), Sizing.Fixed(10f));
            var text = Node(root.transform, "Text", Sizing.Fit(), Sizing.Fit());
            var element = text.gameObject.AddComponent<WidthDependentElement>();
            element.UnconstrainedWidth = 100f;
            element.Area = 1000f;
            element.MinimumWidth = 20f;
            int passes = LayoutEngine.PassCount;

            LayoutSystem.ForceLayout(root);

            Assert.That(LayoutEngine.PassCount, Is.EqualTo(passes + 1), "one pass, no settling");
            Assert.That(Size(text).x, Is.EqualTo(70f).Within(Tolerance), "shrunk to the room left");
            Assert.That(Size(text).y, Is.EqualTo(1000f / 70f).Within(Tolerance), "height for the width it ended up with");
        }

        [Test]
        public void Floating_FitKeepsItsOwnSize_AndAttachesToAFlowElementLaterInOrder()
        {
            var root = Root(100f, 100f, LayoutDirection.TopToBottom);
            var host = Node(root.transform, "Host", Sizing.Fixed(40f), Sizing.Fixed(20f));
            var tip = Node(host.transform, "Tip", Sizing.Fit(), Sizing.Fit());
            var content = tip.gameObject.AddComponent<TestContent>();
            content.Size = new Vector2(150f, 10f);
            var anchor = Node(root.transform, "Anchor", Sizing.Fixed(60f), Sizing.Fixed(20f));
            tip.AttachTo = AttachTo.Element;
            tip.AttachElement = anchor.RectTransform;
            tip.ElementPoint = AttachPoint.LeftTop;
            tip.ParentPoint = AttachPoint.LeftBottom;

            LayoutSystem.ForceLayout(root);

            Assert.That(Size(tip).x, Is.EqualTo(150f).Within(Tolerance), "wider than its host and not clamped to it");
            // Anchor is the second flow child: rect (0, 20, 60, 20) from the root; the tip hangs under it. The tip is a child
            // of Host (at 0, 0), so its position relative to Host is (0, 40).
            AssertVector(Pos(tip), 0f, -40f);
            Assert.That(Size(host).y, Is.EqualTo(20f).Within(Tolerance), "the floating child adds nothing to its host");
        }

        [Test]
        public void DisablingANode_MarksTheTreeItLeaves()
        {
            var root = Root(100f, 100f, LayoutDirection.TopToBottom);
            var a = Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            LayoutSystem.ForceLayout(root);

            a.enabled = false;

            Assert.That(LayoutSystem.IsDirty(root), Is.True);
            LayoutSystem.ForceLayout(root);
            AssertVector(Pos(b), 0f, 0f);
        }

#if TJ_LAYOUT_PROPERTY_BINDINGS
        [Test]
        public void Bindings_OffsetWritesInPlace_OtherPropertiesReLayout()
        {
            var root = Root(100f, 100f, LayoutDirection.TopToBottom);
            var a = Node(root.transform, "A", Sizing.Fixed(50f), Sizing.Fixed(20f));
            var b = Node(root.transform, "B", Sizing.Fixed(50f), Sizing.Fixed(20f));
            LayoutSystem.ForceLayout(root);
            int passes = LayoutEngine.PassCount;

            var offset = TimboJimbo.PropertyBindings.BindableProperty.Create(a, LayoutNodeProperties.Offset);
            using (var bindings = TimboJimbo.PropertyBindings.PropertyBindingCollection.Bind(a.gameObject, new[] { offset }))
            {
                Assert.That(bindings.TryGetBindingType(offset, out var bindingType) && bindingType == typeof(LayoutNodePropertyBinding), Is.True, "the node's own binder is chosen");
                Assert.That(bindings.TryWrite(offset, TimboJimbo.PropertyBindings.ValueContainer.From(new Vector2(3f, -4f))), Is.True);
            }
            AssertVector(Pos(a), 3f, -4f);
            AssertVector(Pos(b), 0f, -20f);
            Assert.That(LayoutSystem.IsDirty(root), Is.False);
            Assert.That(LayoutEngine.PassCount, Is.EqualTo(passes));

            var gap = TimboJimbo.PropertyBindings.BindableProperty.Create(root, LayoutNodeProperties.Gap);
            using (var bindings = TimboJimbo.PropertyBindings.PropertyBindingCollection.Bind(root.gameObject, new[] { gap }))
                bindings.TryWrite(gap, TimboJimbo.PropertyBindings.ValueContainer.FromFloat(10f));
            Assert.That(LayoutSystem.IsDirty(root), Is.True);
            LayoutSystem.ForceLayout(root);
            AssertVector(Pos(b), 0f, -30f);
            AssertVector(Pos(a), 3f, -4f);
        }
#endif

        [Test]
        public void UguiElements_LayoutElementAndImage_AreContentWithoutAnyAdapter()
        {
            var root = Node(_canvas.transform, "Root", Sizing.Fit(), Sizing.Fit());
            root.Direction = LayoutDirection.TopToBottom;
            var a = Node(root.transform, "A", Sizing.Fit(), Sizing.Fit());
            var element = a.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 40f;
            element.preferredHeight = 30f;

            var b = Node(root.transform, "B", Sizing.Fit(), Sizing.Fit());
            var image = b.gameObject.AddComponent<Image>();
            var texture = new Texture2D(16, 8);
            image.sprite = Sprite.Create(texture, new Rect(0f, 0f, 16f, 8f), new Vector2(0.5f, 0.5f), 100f);

            LayoutSystem.ForceLayout(root);

            AssertVector(Size(a), 40f, 30f);
            AssertVector(Size(b), 16f, 8f);
            AssertVector(Size(root), 40f, 38f);

            Object.DestroyImmediate(image.sprite);
            Object.DestroyImmediate(texture);
        }
    }
}
