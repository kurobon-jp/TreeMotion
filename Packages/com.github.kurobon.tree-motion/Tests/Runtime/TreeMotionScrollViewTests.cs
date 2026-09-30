using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TreeMotion.Tests
{
    internal sealed class TestTreeMotionItemView : MonoBehaviour, ITreeMotionItemView
    {
        public RectTransform RectTransform => (RectTransform)transform;
        public TreeMotionPresentation Presentation { get; private set; }

        public void SetTreeMotionPresentation(in TreeMotionPresentation presentation)
        {
            Presentation = presentation;
        }
    }

    public sealed class TreeMotionScrollViewTests
    {
        private GameObject _root;
        private GameObject _prefabObject;
        private TreeMotionScrollView _treeView;
        private UnityEngine.UI.ScrollRect _scrollRect;
        private RectTransform _content;
        private TestTreeMotionItemView _prefab;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("TreeMotionScrollViewTests", typeof(RectTransform));
            _scrollRect = _root.AddComponent<UnityEngine.UI.ScrollRect>();
            _treeView = _root.AddComponent<TreeMotionScrollView>();

            var viewportObject = new GameObject("Viewport", typeof(RectTransform));
            var viewport = viewportObject.GetComponent<RectTransform>();
            viewport.SetParent(_root.transform, false);
            viewport.sizeDelta = new Vector2(300f, 100f);

            var contentObject = new GameObject("Content", typeof(RectTransform));
            _content = contentObject.GetComponent<RectTransform>();
            _content.SetParent(viewport, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);

            _scrollRect.viewport = viewport;
            _scrollRect.content = _content;
            _treeView.Configure(_scrollRect, viewport, _content);

            _prefabObject = new GameObject("ItemPrefab", typeof(RectTransform));
            _prefab = _prefabObject.AddComponent<TestTreeMotionItemView>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_prefabObject);
        }

        [Test]
        public void Scrolling_BindsOnlyRowsEnteringViewport()
        {
            var tree = new TreeStore<int, string>();
            tree.LoadSnapshot(new[]
            {
                TreeNodeRecord<int, string>.Root(1, "One", 0),
                TreeNodeRecord<int, string>.Root(2, "Two", 1),
                TreeNodeRecord<int, string>.Root(3, "Three", 2),
                TreeNodeRecord<int, string>.Root(4, "Four", 3)
            });
            var bindCounts = new Dictionary<int, int>();

            _treeView.SetDataSource(tree, _prefab, 50f,
                (_, id, _, _) => Increment(bindCounts, id));

            Assert.That(bindCounts, Is.EquivalentTo(new Dictionary<int, int>
            {
                [1] = 1,
                [2] = 1
            }));

            ScrollTo(1f);
            Assert.That(bindCounts.Count, Is.EqualTo(2));
            Assert.That(bindCounts[1] + bindCounts[2], Is.EqualTo(2));

            ScrollTo(63f);
            Assert.That(bindCounts, Is.EquivalentTo(new Dictionary<int, int>
            {
                [1] = 1,
                [2] = 1,
                [3] = 1
            }));

            ScrollTo(64f);
            Assert.That(bindCounts.Count, Is.EqualTo(3));
            Assert.That(bindCounts[1] + bindCounts[2] + bindCounts[3], Is.EqualTo(3));
        }

        [Test]
        public void RowTouchingViewportBoundary_IsReturnedToPool()
        {
            var tree = new TreeStore<int, string>();
            tree.LoadSnapshot(new[]
            {
                TreeNodeRecord<int, string>.Root(1, "Visible", 0),
                TreeNodeRecord<int, string>.Root(2, "Touches bottom edge", 1)
            });
            var views = new Dictionary<int, TestTreeMotionItemView>();

            _treeView.SetDataSource(tree, _prefab, 50f,
                (view, id, _, _) => views[id] = view);

            Assert.That(views.Keys, Is.EquivalentTo(new[] { 1, 2 }));
            _scrollRect.viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 70f);
            _scrollRect.onValueChanged.Invoke(Vector2.zero);

            Assert.That(views[1].gameObject.activeSelf, Is.True);
            Assert.That(views[2].gameObject.activeSelf, Is.False);
        }

        [Test]
        public void Apply_RebindsUpdatedRowAndNewlyVisibleChildOnly()
        {
            var tree = new TreeStore<int, string>();
            tree.LoadSnapshot(new[]
            {
                TreeNodeRecord<int, string>.Root(1, "Group", 0, false),
                TreeNodeRecord<int, string>.Child(2, 1, "Child", 0),
                TreeNodeRecord<int, string>.Root(3, "Other", 1)
            });
            var bindCounts = new Dictionary<int, int>();
            var view = _treeView.SetDataSource(tree, _prefab, 50f,
                (_, id, _, _) => Increment(bindCounts, id));

            var changes = tree.BeginUpdate().SetExpanded(1, true).Commit();
            view.Apply(changes);

            Assert.That(bindCounts, Is.EquivalentTo(new Dictionary<int, int>
            {
                [1] = 2,
                [2] = 1,
                [3] = 1
            }));
        }

        [Test]
        public void NestedRows_UseSameHorizontalLayoutAsRootRows()
        {
            var tree = new TreeStore<int, string>();
            tree.LoadSnapshot(new[]
            {
                TreeNodeRecord<int, string>.Root(1, "Group", 0, true),
                TreeNodeRecord<int, string>.Child(2, 1, "Child", 0)
            });
            var views = new Dictionary<int, RectTransform>();

            _treeView.SetDataSource(tree, _prefab, 40f,
                (view, id, _, _) => views[id] = view.RectTransform);

            Assert.That(views[2].offsetMin.x, Is.EqualTo(views[1].offsetMin.x));
            Assert.That(views[2].offsetMax.x, Is.EqualTo(views[1].offsetMax.x));
        }

        [Test]
        public void SwapNodes_ReusesIdentityViewsAndPresentsBothAsSwap()
        {
            var tree = new TreeStore<int, string>();
            tree.BeginUpdate().InsertRoot(1, "first").InsertRoot(2, "second").Commit();
            var views = new Dictionary<int, TestTreeMotionItemView>();
            var bindCounts = new Dictionary<int, int>();
            var controller = _treeView.SetDataSource(tree, _prefab, 40f,
                (view, id, _, _) =>
                {
                    views[id] = view;
                    Increment(bindCounts, id);
                });
            var firstView = views[1];
            var secondView = views[2];

            var changes = tree.BeginUpdate().SwapNodes(1, 2).Commit();
            controller.Apply(changes);

            Assert.That(bindCounts[1], Is.EqualTo(1));
            Assert.That(bindCounts[2], Is.EqualTo(1));
            Assert.That(firstView.Presentation.Kind, Is.EqualTo(TreeMotionAnimationKind.Swap));
            Assert.That(secondView.Presentation.Kind, Is.EqualTo(TreeMotionAnimationKind.Swap));
            Assert.That(firstView.Presentation.Role, Is.EqualTo(TreeMotionPresentationRole.Current));
            Assert.That(secondView.Presentation.Role, Is.EqualTo(TreeMotionPresentationRole.Current));
        }

        private void ScrollTo(float position)
        {
            _content.anchoredPosition = new Vector2(0f, position);
            _scrollRect.onValueChanged.Invoke(Vector2.zero);
        }

        private static void Increment(IDictionary<int, int> counts, int id)
        {
            counts.TryGetValue(id, out var count);
            counts[id] = count + 1;
        }
    }
}
