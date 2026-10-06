using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace TreeMotion.Tests
{
    internal sealed class TestTreeMotionItemView : MonoBehaviour, ITreeMotionPresentationHandler
    {
        public int BoundId { get; set; }
        public TreeMotionPresentation Presentation { get; private set; }
        public int Resets { get; private set; }
        public void ResetPresentation()
        {
            Resets++;
            transform.localScale = Vector3.one;
        }
        public void ApplyPresentation(in TreeMotionPresentation presentation)
        {
            Presentation = presentation;
            transform.localScale = presentation.Role == TreeMotionPresentationRole.Exiting ? Vector3.one * 0.9f : Vector3.one;
        }
    }

    public sealed class TreeMotionScrollViewTests
    {
        private sealed class Adapter : ITreeMotionAdapter<int, string>
        {
            internal readonly Dictionary<int, GameObject> Prefabs = new Dictionary<int, GameObject>();
            internal readonly Dictionary<int, int> Types = new Dictionary<int, int>();
            internal readonly HashSet<int> Groups = new HashSet<int>();
            internal readonly Dictionary<int, GameObject> Views = new Dictionary<int, GameObject>();
            internal readonly Dictionary<int, int> Binds = new Dictionary<int, int>();
            private int GetTestType(int id, string item) => Types.TryGetValue(id, out var type) ? type : Groups.Contains(id) ? 100 : 0;
            public GameObject GetItemPrefab(int id, string item) => Prefabs.TryGetValue(GetTestType(id, item), out var prefab) ? prefab : null;
            public float GetItemSize(int id, string item) => Sizes.TryGetValue(id, out var size) ? size :
                Heights.TryGetValue(GetTestType(id, item), out var height) ? height : 50f;
            internal readonly Dictionary<int, float> Sizes = new Dictionary<int, float>();
            internal readonly Dictionary<int, float> Heights = new Dictionary<int, float>();
            public void Bind(GameObject view, int id, string item, VisibleRow<int> row)
            {
                Views[id] = view;
                if (view.TryGetComponent<TestTreeMotionItemView>(out var itemView))
                    itemView.BoundId = id;
                Binds.TryGetValue(id, out var count);
                Binds[id] = count + 1;
            }
        }
        private GameObject _root;
        private TreeMotionScrollView _scrollView;
        private UnityEngine.UI.ScrollRect _scrollRect;
        private RectTransform _content;
        private TreeStore<int, string> _tree;
        private Adapter _adapter;
        private TreeMotionBinding<int> _binding;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Test", typeof(RectTransform));
            _scrollRect = _root.AddComponent<UnityEngine.UI.ScrollRect>();
            _scrollView = _root.AddComponent<TreeMotionScrollView>();
            var viewport = Rect("Viewport", _root.transform);
            viewport.anchorMin = viewport.anchorMax = new Vector2(.5f, .5f);
            viewport.sizeDelta = new Vector2(300f, 100f);
            _content = Rect("Content", viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.gameObject.AddComponent<TreeMotionGroupView>().Configure(_content, 8f, new TreeMotionPadding(12, 12, 12, 12));
            _scrollRect.viewport = viewport;
            _scrollRect.content = _content;
            _scrollView.Configure(_scrollRect, viewport, _content);
            _tree = new TreeStore<int, string>();
            _adapter = new Adapter();
            var item = Rect("ItemPrefab", _root.transform).gameObject.AddComponent<TestTreeMotionItemView>();
            _adapter.Prefabs[0] = item.gameObject;
            _adapter.Prefabs[100] = GroupPrefab().gameObject;
        }
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(_root);
        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, 1);
            rect.sizeDelta = Vector2.zero;
            return rect;
        }
        private TreeMotionGroupView GroupPrefab()
        {
            var group = Rect("GroupPrefab", _root.transform).gameObject.AddComponent<TreeMotionGroupView>();
            Rect("Header", group.transform);
            var frame = Rect("ChildrenFrame", group.transform);
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.one;
            frame.offsetMin = Vector2.zero;
            frame.offsetMax = new Vector2(0, -53f);
            group.Configure(frame, 3f, new TreeMotionPadding(12, 12, 0, 8));
            return group;
        }
        private void Load(params TreeNodeRecord<int, string>[] records)
        {
            _tree.LoadSnapshot(records);
            _binding = _scrollView.Bind(_tree, _adapter);
        }
        private void Scroll(float y)
        {
            _content.anchoredPosition = new Vector2(0, y);
            _scrollRect.onValueChanged.Invoke(Vector2.zero);
        }
        private void Tick(float time = 1f) => _scrollView.Tick(time);
        private void Viewport(float height) => _scrollRect.viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

        [Test]
        public void Reload_PreservesViewsByIdAndRebindsAfterSnapshotReorder()
        {
            Viewport(500f);
            Load(new TreeNodeRecord<int, string>(1, "One"), new TreeNodeRecord<int, string>(2, "Two"));
            var first = _adapter.Views[1];
            var second = _adapter.Views[2];
            var firstBinds = _adapter.Binds[1];
            var secondBinds = _adapter.Binds[2];
            _tree.LoadSnapshot(new[] { new TreeNodeRecord<int, string>(2, "Updated two"), new TreeNodeRecord<int, string>(1, "Updated one") });

            _binding.Reload();

            Assert.That(_adapter.Views[1], Is.SameAs(first));
            Assert.That(_adapter.Views[2], Is.SameAs(second));
            Assert.That(_adapter.Binds[1], Is.EqualTo(firstBinds + 1));
            Assert.That(_adapter.Binds[2], Is.EqualTo(secondBinds + 1));
            Assert.That(first.GetComponent<RectTransform>().anchoredPosition.y,
                Is.LessThan(second.GetComponent<RectTransform>().anchoredPosition.y));
        }

        [Test]
        public void Reload_ReplacesChangedPrefabAndReleasesRemovedView()
        {
            Viewport(500f);
            Load(new TreeNodeRecord<int, string>(1, "One"), new TreeNodeRecord<int, string>(2, "Two"));
            var first = _adapter.Views[1];
            var removed = _adapter.Views[2];
            _adapter.Prefabs[1] = Rect("OtherPrefab", _root.transform).gameObject;
            _adapter.Types[1] = 1;
            _tree.LoadSnapshot(new[] { new TreeNodeRecord<int, string>(1, "Changed") });

            _binding.Reload();

            Assert.That(_adapter.Views[1], Is.Not.SameAs(first));
            Assert.That(first.activeSelf, Is.False);
            Assert.That(removed.activeSelf, Is.False);
        }

        [TestCase(12)]
        [TestCase(256)]
        public void SteadyAnimationRefresh_DoesNotAllocateManagedMemory(int itemCount)
        {
            Viewport(itemCount * 60f);
            var records = new TreeNodeRecord<int, string>[itemCount];
            for (var i = 0; i < records.Length; i++)
                records[i] = new TreeNodeRecord<int, string>(i, "Item");
            Load(records);
            _binding.Apply(_tree.BeginUpdate().Swap(0, itemCount - 1).Commit(), duration: 10f);
            Action<float> tick = _scrollView.Tick;
            for (var i = 0; i < 100; i++)
                tick(.001f);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++)
                tick(.001f);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, "100 warmed animation refreshes should not allocate.");
            Assert.That(_binding.IsAnimating, Is.True);
            Tick(10f);
            for (var i = 1; i < _tree.VisibleCount; i++)
                Assert.That(_adapter.Views[_tree.GetVisibleRow(i - 1).Id].transform.GetSiblingIndex(),
                    Is.LessThan(_adapter.Views[_tree.GetVisibleRow(i).Id].transform.GetSiblingIndex()));
        }

        [Test]
        public void DrawOrder_RemainsCorrectWithInactivePooledViewsAndReuse()
        {
            Viewport(500f);
            Load(new TreeNodeRecord<int, string>(1, "One"), new TreeNodeRecord<int, string>(2, "Two"), new TreeNodeRecord<int, string>(3, "Three"));
            var removed = _adapter.Views[1];
            _binding.Apply(_tree.BeginUpdate().Remove(1).Commit(), duration: 0f);
            Assert.That(removed.activeSelf, Is.False);
            Assert.That(removed.transform.GetSiblingIndex(), Is.LessThan(_adapter.Views[2].transform.GetSiblingIndex()));
            Assert.That(_adapter.Views[2].transform.GetSiblingIndex(), Is.LessThan(_adapter.Views[3].transform.GetSiblingIndex()));
            _binding.Apply(_tree.BeginUpdate().Insert(4, "Four", 0).Commit(), duration: 0f);
            Assert.That(_adapter.Views[4], Is.SameAs(removed));
            Assert.That(_adapter.Views[4].transform.GetSiblingIndex(), Is.LessThan(_adapter.Views[2].transform.GetSiblingIndex()));
            Assert.That(_adapter.Views[2].transform.GetSiblingIndex(), Is.LessThan(_adapter.Views[3].transform.GetSiblingIndex()));
        }

        [Test]
        public void ApplyAsync_CompletesAfterFinalFrame()
        {
            Load(new TreeNodeRecord<int, string>(1, "One"));
            var task = _binding.ApplyAsync(_tree.BeginUpdate().Insert(2, "Two").Commit());
            Assert.That(task.IsCompleted, Is.False);
            Tick(.1f);
            Assert.That(task.IsCompleted, Is.False);
            Tick();
            Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(_binding.IsAnimating, Is.False);
            Assert.That(_adapter.Views[2].GetComponent<RectTransform>().rect.height, Is.EqualTo(50f));
        }

        [Test]
        public void Rebinding_CancelsOldWaitAndInvalidatesPreviousBinding()
        {
            Load(new TreeNodeRecord<int, string>(1, "One"));
            var previous = _binding;
            var pending = previous.ApplyAsync(_tree.BeginUpdate().Insert(2, "Two").Commit());
            _binding = _scrollView.Bind(_tree, _adapter);
            Assert.That(pending.IsCanceled, Is.True);
            Assert.Throws<ObjectDisposedException>(() => previous.Refresh(1));
            Assert.That(_binding.IsAnimating, Is.False);
            Assert.That(_adapter.Views[2].activeSelf, Is.True);
        }

        [Test]
        public void ItemSize_IsMeasuredPerNodeAndRecomputedAfterUpdate()
        {
            Viewport(500f);
            _adapter.Sizes[1] = 30f;
            _adapter.Sizes[2] = 90f;
            Load(new TreeNodeRecord<int, string>(1, "One"), new TreeNodeRecord<int, string>(2, "Two"));
            var first = _adapter.Views[1];
            Assert.That(first.GetComponent<RectTransform>().rect.height, Is.EqualTo(30f));
            Assert.That(_adapter.Views[2].GetComponent<RectTransform>().rect.height, Is.EqualTo(90f));
            _adapter.Sizes[1] = 80f;
            _binding.Apply(_tree.BeginUpdate().Update(1, "Changed size").Commit());
            Tick();
            Assert.That(_adapter.Views[1], Is.SameAs(first));
            Assert.That(first.GetComponent<RectTransform>().rect.height, Is.EqualTo(80f));
            Assert.That(_adapter.Views[2].GetComponent<RectTransform>().rect.height, Is.EqualTo(90f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Apply_CustomDurationOverridesDefaultForOneOperation(bool asynchronous)
        {
            Load(new TreeNodeRecord<int, string>(1, "One"));
            var changes = _tree.BeginUpdate().Insert(2, "Two").Commit();
            Task completion = null;
            if (asynchronous)
                completion = _binding.ApplyAsync(changes, duration: 1f);
            else
                _binding.Apply(changes, duration: 1f);
            Tick(.3f);
            Assert.That(_binding.IsAnimating, Is.True);
            if (completion != null)
                Assert.That(completion.IsCompleted, Is.False);
            Tick(.7f);
            Assert.That(_binding.IsAnimating, Is.False);
            if (completion != null)
                Assert.That(completion.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            _binding.Apply(_tree.BeginUpdate().Remove(2).Commit());
            Tick(.26f);
            Assert.That(_binding.IsAnimating, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Apply_ZeroDurationImmediatelyReflectsInsertAndRemove(bool asynchronous)
        {
            Load(new TreeNodeRecord<int, string>(1, "One"));
            var changes = _tree.BeginUpdate().Insert(2, "Two").Commit();
            if (asynchronous)
                Assert.That(_binding.ApplyAsync(changes, duration: 0f).Status, Is.EqualTo(TaskStatus.RanToCompletion));
            else
                _binding.Apply(changes, duration: 0f);
            Assert.That(_binding.IsAnimating, Is.False);
            var item = _adapter.Views[2];
            Assert.That(item.activeSelf, Is.True);
            Assert.That(item.GetComponent<TestTreeMotionItemView>().Presentation.Progress, Is.EqualTo(1f));
            var pending = _binding.ApplyAsync(_tree.BeginUpdate().MoveToRoot(2, 0).Commit());
            changes = _tree.BeginUpdate().Remove(2).Commit();
            if (asynchronous)
                Assert.That(_binding.ApplyAsync(changes, duration: 0f).Status, Is.EqualTo(TaskStatus.RanToCompletion));
            else
                _binding.Apply(changes, duration: 0f);
            Assert.That(pending.IsCanceled, Is.True);
            Assert.That(item.activeSelf, Is.False);
            Assert.That(_binding.IsAnimating, Is.False);
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Apply_InvalidDurationDoesNotInterruptExistingWait(float duration)
        {
            Load(new TreeNodeRecord<int, string>(1, "One"));
            var pending = _binding.ApplyAsync(_tree.BeginUpdate().Insert(2, "Two").Commit());
            var empty = _tree.BeginUpdate().Commit();
            Assert.Throws<InvalidOperationException>(() => _binding.Apply(empty, duration));
            Assert.Throws<InvalidOperationException>(() => _binding.ApplyAsync(empty, duration));
            Assert.That(pending.IsCompleted, Is.False);
            Tick();
            Assert.That(pending.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }

        [Test]
        public void ApplyAsync_EmptyAndLabelOnlyChangesCompleteImmediately()
        {
            Load(new TreeNodeRecord<int, string>(1, "One"));
            Assert.That(_binding.ApplyAsync(_tree.BeginUpdate().Commit()).IsCompleted, Is.True);
            Assert.That(_binding.ApplyAsync(_tree.BeginUpdate().Update(1, "Updated").Commit()).Status,
                Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(_binding.IsAnimating, Is.False);
        }

        [TestCase("Apply")]
        [TestCase("ApplyAsync")]
        [TestCase("Reload")]
        [TestCase("Dispose")]
        public void ApplyAsync_ReplacementCancelsPreviousWait(string operation)
        {
            Load(new TreeNodeRecord<int, string>(1, "One"));
            var previous = _binding.ApplyAsync(_tree.BeginUpdate().Insert(2, "Two").Commit());
            switch (operation)
            {
                case "Apply":
                    _binding.Apply(_tree.BeginUpdate().Commit());
                    break;
                case "ApplyAsync":
                    _binding.ApplyAsync(_tree.BeginUpdate().Remove(2).Commit());
                    break;
                case "Reload":
                    _binding.Reload();
                    break;
                case "Dispose":
                    // EditMode does not guarantee MonoBehaviour.OnDestroy without a play-mode Awake.
                    _scrollView.OnDestroy();
                    UnityEngine.Object.DestroyImmediate(_scrollView);
                    break;
            }
            Assert.That(previous.IsCanceled, Is.True);
        }

        [Test]
        public async Task ApplyAsync_TokenCancelsWaitButAnimationContinues()
        {
            Load(new TreeNodeRecord<int, string>(1, "One"));
            using (var cancellation = new CancellationTokenSource())
            {
                var task = _binding.ApplyAsync(_tree.BeginUpdate().Insert(2, "Two").Commit(), cancellation.Token);
                cancellation.Cancel();
                try
                {
                    await task;
                    Assert.Fail("Expected canceled wait.");
                }
                catch (OperationCanceledException error) { Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token)); }
                Assert.That(_binding.IsAnimating, Is.True);
                Tick();
                Assert.That(_adapter.Views[2].activeSelf, Is.True);
                Assert.That(_binding.IsAnimating, Is.False);
            }
        }

        [Test]
        public void ApplyAsync_PreCanceledTokenDoesNotStartAnimation()
        {
            Load(new TreeNodeRecord<int, string>(1, "One"));
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var task = _binding.ApplyAsync(_tree.BeginUpdate().Insert(2, "Two").Commit(), cancellation.Token);
                Assert.That(task.IsCanceled, Is.True);
                Assert.That(_binding.IsAnimating, Is.False);
                Assert.That(_adapter.Views.ContainsKey(2), Is.False);
            }
        }

        [Test]
        public void Scrolling_BindsOnlyRowsEnteringViewport()
        {
            Load(new TreeNodeRecord<int, string>(1, "One"), new TreeNodeRecord<int, string>(2, "Two"), new TreeNodeRecord<int, string>(3, "Three"));
            Assert.That(_adapter.Binds.Keys, Is.EquivalentTo(new[] { 1, 2 }));
            Scroll(1f);
            Assert.That(_adapter.Binds[1], Is.EqualTo(1));
            Assert.That(_adapter.Binds[2], Is.EqualTo(1));
            Scroll(63f);
            Assert.That(_adapter.Binds[3], Is.EqualTo(1));
        }
        [Test]
        public void RowTouchingViewportBoundary_IsReturnedToPool()
        {
            Load(new TreeNodeRecord<int, string>(1, "One"), new TreeNodeRecord<int, string>(2, "Two"));
            Viewport(70f);
            Scroll(0f);
            Assert.That(_adapter.Views[1].activeSelf, Is.True);
            Assert.That(_adapter.Views[2].activeSelf, Is.False);
        }
        [Test]
        public void Types_SelectDifferentPrefabsAndReturnToTheirOwnPools()
        {
            var alternate = Rect("Alternate", _root.transform).gameObject.AddComponent<TestTreeMotionItemView>();
            _adapter.Prefabs[1] = alternate.gameObject;
            _adapter.Types[2] = 1;
            _adapter.Types[4] = 1;
            Viewport(50f);
            Load(new TreeNodeRecord<int, string>(1, "One"), new TreeNodeRecord<int, string>(2, "Two"), new TreeNodeRecord<int, string>(3, "Three"), new TreeNodeRecord<int, string>(4, "Four"));
            var first = _adapter.Views[1];
            Scroll(70f);
            var second = _adapter.Views[2];
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.name, Does.StartWith("Alternate"));
            Scroll(128f);
            Assert.That(_adapter.Views[3], Is.SameAs(first));
            Scroll(186f);
            Assert.That(_adapter.Views[4], Is.SameAs(second));
        }
        [Test]
        public void SamePrefab_UpdateRetainsViewAndSharesPoolAcrossItemKinds()
        {
            _adapter.Prefabs[1] = _adapter.Prefabs[0];
            Load(new TreeNodeRecord<int, string>(1, "Card"));
            var original = _adapter.Views[1];
            _adapter.Types[1] = 1;
            _adapter.Sizes[1] = 80f;
            _binding.Apply(_tree.BeginUpdate().Update(1, "Expanded card").Commit());
            Assert.That(_adapter.Views[1], Is.SameAs(original));
            Tick();
            Assert.That(original.GetComponent<RectTransform>().rect.height, Is.EqualTo(80f));
            _binding.Apply(_tree.BeginUpdate().Remove(1).Commit());
            Tick();
            _binding.Apply(_tree.BeginUpdate().Insert(2, "Other card").Commit());
            Assert.That(_adapter.Views[2], Is.SameAs(original));
        }

        [Test]
        public void AdapterReturningDifferentPrefab_ReplacesViewWithoutChangingItemKind()
        {
            Load(new TreeNodeRecord<int, string>(1, "One"));
            var original = _adapter.Views[1];
            _adapter.Prefabs[0] = Rect("Replacement", _root.transform).gameObject;
            _binding.Apply(_tree.BeginUpdate().Update(1, "Changed").Commit());
            Assert.That(_adapter.Views[1], Is.Not.SameAs(original));
            Assert.That(original.activeSelf, Is.False);
        }

        [Test]
        public void PrefabChange_ReplacesViewWhileKeepingNodeIdentity()
        {
            var other = Rect("Other", _root.transform).gameObject.AddComponent<TestTreeMotionItemView>();
            _adapter.Prefabs[1] = other.gameObject;
            Load(new TreeNodeRecord<int, string>(1, "One"));
            var original = _adapter.Views[1];
            _adapter.Types[1] = 1;
            _binding.Apply(_tree.BeginUpdate().Update(1, "Changed").Commit());
            Assert.That(_adapter.Views[1], Is.Not.SameAs(original));
            Assert.That(original.activeSelf, Is.False);
            _adapter.Types[1] = 0;
            _binding.Apply(_tree.BeginUpdate().Update(1, "Again").Commit());
            Assert.That(_adapter.Views[1], Is.SameAs(original));
        }
        [Test]
        public void PresentationCause_DistinguishesInsertRemoveFromExpandCollapse()
        {
            _adapter.Groups.UnionWith(new[] { 1, 2 });
            Viewport(500f);
            Load(new TreeNodeRecord<int, string>(1, "Group", isExpanded: true),
                new TreeNodeRecord<int, string>(2, "Nested", parentId: 1, isExpanded: true),
                new TreeNodeRecord<int, string>(3, "Child", parentId: 2));
            _binding.Apply(_tree.BeginUpdate().Expand(1, false).Commit());
            var child = _adapter.Views[3].GetComponent<TestTreeMotionItemView>();
            Assert.That(child.Presentation.Role, Is.EqualTo(TreeMotionPresentationRole.Exiting));
            Assert.That(child.Presentation.Cause, Is.EqualTo(TreeChangeKind.Collapse));
            Tick();
            _binding.Apply(_tree.BeginUpdate().Expand(1, true).Commit());
            child = _adapter.Views[3].GetComponent<TestTreeMotionItemView>();
            Assert.That(child.Presentation.Role, Is.EqualTo(TreeMotionPresentationRole.Entering));
            Assert.That(child.Presentation.Cause, Is.EqualTo(TreeChangeKind.Expand));
            Tick(0.05f);
            _binding.Apply(_tree.BeginUpdate().Insert(4, "Other").Commit());
            Assert.That(child.Presentation.Cause, Is.EqualTo(TreeChangeKind.Expand));
            Tick();
            Assert.That(child.Presentation.Cause, Is.EqualTo(null));
            _binding.Apply(_tree.BeginUpdate().Remove(2).Commit());
            Assert.That(child.Presentation.Role, Is.EqualTo(TreeMotionPresentationRole.Exiting));
            Assert.That(child.Presentation.Cause, Is.EqualTo(TreeChangeKind.Remove));
            Tick();
            _binding.Apply(_tree.BeginUpdate().Insert(1, 5, "New child").Commit());
            var added = _adapter.Views[5].GetComponent<TestTreeMotionItemView>();
            Assert.That(added.Presentation.Role, Is.EqualTo(TreeMotionPresentationRole.Entering));
            Assert.That(added.Presentation.Cause, Is.EqualTo(TreeChangeKind.Insert));
        }

        [Test]
        public void EmptyGroup_RetainsGroupPrefabAndHidesChildrenFrame()
        {
            _adapter.Groups.Add(1);
            Load(new TreeNodeRecord<int, string>(1, "Empty", isExpanded: true));
            var group = _adapter.Views[1].GetComponent<TreeMotionGroupView>();
            Assert.That(group.ChildrenFrame.gameObject.activeSelf, Is.False);
            _binding.Apply(_tree.BeginUpdate().Insert(1, 2, "Child").Commit());
            Tick();
            Assert.That(_adapter.Views[1], Is.SameAs(group.gameObject));
            Assert.That(group.ChildrenFrame.gameObject.activeSelf, Is.True);
            _binding.Apply(_tree.BeginUpdate().Remove(2).Commit());
            Tick();
            Assert.That(_adapter.Views[1], Is.SameAs(group.gameObject));
            Assert.That(group.ChildrenFrame.gameObject.activeSelf, Is.False);
        }
        [Test]
        public void GroupOwnsHeaderAndFrame_ChildrenRemainContentSiblings()
        {
            _adapter.Groups.UnionWith(new[] { 1, 2 });
            Viewport(500f);
            Load(new TreeNodeRecord<int, string>(1, "Group", isExpanded: true), new TreeNodeRecord<int, string>(2, "Nested", parentId: 1, isExpanded: true), new TreeNodeRecord<int, string>(3, "Item", parentId: 2));
            var parent = _adapter.Views[1].GetComponent<TreeMotionGroupView>();
            var nested = _adapter.Views[2].GetComponent<TreeMotionGroupView>();
            var leaf = _adapter.Views[3];
            Assert.That(parent.transform.Find("Header").parent, Is.EqualTo(parent.transform));
            Assert.That(parent.ChildrenFrame.parent, Is.EqualTo(parent.transform));
            Assert.That(leaf.transform.parent, Is.EqualTo(_content));
            Assert.That(nested.transform.parent, Is.EqualTo(_content));
            Assert.That(parent.GetComponent<RectTransform>().rect.height, Is.GreaterThan(nested.GetComponent<RectTransform>().rect.height));
            Assert.That(parent.ChildrenFrame.offsetMax.y, Is.EqualTo(-53f));
            Assert.That(leaf.GetComponent<RectTransform>().offsetMin.x, Is.GreaterThan(nested.GetComponent<RectTransform>().offsetMin.x));
            Assert.That(parent.transform.GetSiblingIndex(), Is.LessThan(nested.transform.GetSiblingIndex()));
            var height = _content.rect.height;
            _binding.Apply(_tree.BeginUpdate().Expand(1, false).Commit());
            Assert.That(parent.ChildrenFrame.gameObject.activeSelf, Is.True);
            Assert.That(leaf.activeSelf, Is.True);
            Tick();
            Assert.That(parent.ChildrenFrame.gameObject.activeSelf, Is.False);
            Assert.That(leaf.activeSelf, Is.False);
            Assert.That(_content.rect.height, Is.LessThan(height));
        }
        [Test]
        public void GroupHeaderOffscreen_KeepsWholeGroupButOnlyVisibleChildren()
        {
            _adapter.Groups.Add(1);
            var records = new List<TreeNodeRecord<int, string>> { new TreeNodeRecord<int, string>(1, "Group", isExpanded: true) };
            for (int id = 2; id < 30; id++)
                records.Add(new TreeNodeRecord<int, string>(id, "Child", parentId: 1));
            Load(records.ToArray());
            var group = _adapter.Views[1];
            Scroll(600f);
            Assert.That(_adapter.Views[1], Is.SameAs(group));
            Assert.That(group.activeSelf, Is.True);
            Assert.That(_adapter.Binds[2], Is.EqualTo(1));
            Assert.That((_adapter.Views[2].GetComponent<TestTreeMotionItemView>()).BoundId, Is.Not.EqualTo(2));
            Assert.That(_content.GetComponentsInChildren<TestTreeMotionItemView>().Length, Is.LessThan(6));
        }
        [Test]
        public void Swap_ReusesIdentityViewsAndPresentsBothAsSwap()
        {
            Load(new TreeNodeRecord<int, string>(1, "One"), new TreeNodeRecord<int, string>(2, "Two"));
            var first = _adapter.Views[1].GetComponent<TestTreeMotionItemView>();
            var second = _adapter.Views[2].GetComponent<TestTreeMotionItemView>();
            _binding.Apply(_tree.BeginUpdate().Swap(1, 2).Commit());
            Assert.That(_adapter.Binds[1], Is.EqualTo(1));
            Assert.That(_adapter.Binds[2], Is.EqualTo(1));
            Assert.That(first.Presentation.Kind, Is.EqualTo(TreeMotionAnimationKind.Swap));
            Assert.That(second.Presentation.Kind, Is.EqualTo(TreeMotionAnimationKind.Swap));
        }
        [Test]
        public void OffscreenSwap_IsLeasedWhenItsCurrentTrackEntersViewport()
        {
            Viewport(50f);
            Load(new TreeNodeRecord<int, string>(1, "One"), new TreeNodeRecord<int, string>(2, "Two"), new TreeNodeRecord<int, string>(3, "Three"));
            Scroll(75f); // Neither endpoint of the swap intersects this narrow middle viewport.
            _binding.Apply(_tree.BeginUpdate().Swap(1, 3).Commit());
            Tick(0.125f);
            Assert.That(_adapter.Views[1].activeSelf, Is.True);
            Assert.That(_adapter.Views[3].activeSelf, Is.True);
        }

        [Test]
        public void Move_OnlyExplicitTargetAppearsAboveDisplacedItems_ThenRestoresOrder()
        {
            Viewport(500f);
            Load(new TreeNodeRecord<int, string>(1, "One"), new TreeNodeRecord<int, string>(2, "Two"), new TreeNodeRecord<int, string>(3, "Three"));
            _binding.Apply(_tree.BeginUpdate().MoveToRoot(3, 0).Commit());
            Assert.That(_adapter.Views[3].transform.GetSiblingIndex(), Is.GreaterThan(_adapter.Views[1].transform.GetSiblingIndex()));
            Assert.That(_adapter.Views[3].transform.GetSiblingIndex(), Is.GreaterThan(_adapter.Views[2].transform.GetSiblingIndex()));
            Tick();
            Assert.That(_adapter.Views[3].transform.GetSiblingIndex(), Is.LessThan(_adapter.Views[1].transform.GetSiblingIndex()));
        }

        [Test]
        public void Swap_BothTargetsAppearAboveUnrelatedItem()
        {
            Viewport(500f);
            Load(new TreeNodeRecord<int, string>(1, "One"), new TreeNodeRecord<int, string>(2, "Two"), new TreeNodeRecord<int, string>(3, "Three"));
            _binding.Apply(_tree.BeginUpdate().Swap(1, 2).Commit());
            foreach (var id in new[] { 1, 2 })
                Assert.That(_adapter.Views[id].transform.GetSiblingIndex(), Is.GreaterThan(_adapter.Views[3].transform.GetSiblingIndex()));
            Tick();
            Assert.That(_adapter.Views[1].transform.GetSiblingIndex(), Is.LessThan(_adapter.Views[3].transform.GetSiblingIndex()));
        }

        [Test]
        public void MoveGroup_BringsEntireSubtreeForwardWithFrameBehindChildren()
        {
            Viewport(500f);
            _adapter.Groups.Add(1);
            Load(new TreeNodeRecord<int, string>(3, "Other"), new TreeNodeRecord<int, string>(1, "Group", isExpanded: true), new TreeNodeRecord<int, string>(2, "Child", parentId: 1));
            _binding.Apply(_tree.BeginUpdate().MoveToRoot(1, 0).Commit());
            var frame = _adapter.Views[1].transform;
            var child = _adapter.Views[2].transform;
            var other = _adapter.Views[3].transform;
            Assert.That(frame.GetSiblingIndex(), Is.GreaterThan(other.GetSiblingIndex()));
            Assert.That(child.GetSiblingIndex(), Is.GreaterThan(frame.GetSiblingIndex()));
            Tick();
            Assert.That(child.GetSiblingIndex(), Is.LessThan(other.GetSiblingIndex()));
        }

        [Test]
        public void HorizontalOnlyMove_BringsTargetForward()
        {
            Viewport(500f);
            _adapter.Groups.Add(1);
            var rootGroup = _content.GetComponent<TreeMotionGroupView>();
            rootGroup.Configure(_content, 0f, rootGroup.ChildrenPadding);
            Load(new TreeNodeRecord<int, string>(1, "Group", isExpanded: true), new TreeNodeRecord<int, string>(2, "Moving"), new TreeNodeRecord<int, string>(3, "Other"));
            var top = _adapter.Views[2].GetComponent<RectTransform>().anchoredPosition.y;
            _binding.Apply(_tree.BeginUpdate().Move(2, 1).Commit());
            Tick(.125f);
            Assert.That(_adapter.Views[2].GetComponent<RectTransform>().anchoredPosition.y, Is.EqualTo(top).Within(.001f));
            Assert.That(_adapter.Views[2].transform.GetSiblingIndex(), Is.GreaterThan(_adapter.Views[3].transform.GetSiblingIndex()));
            Tick();
            Assert.That(_adapter.Views[2].transform.GetSiblingIndex(), Is.LessThan(_adapter.Views[3].transform.GetSiblingIndex()));
        }
        [Test]
        public void MoveAcrossGroups_InterpolatesHorizontalInsetsAndKeepsIdentity()
        {
            _adapter.Groups.UnionWith(new[] { 1, 2, 4 });
            Viewport(500f);
            Load(new TreeNodeRecord<int, string>(1, "Root", isExpanded: true), new TreeNodeRecord<int, string>(2, "Nested", parentId: 1, isExpanded: true), new TreeNodeRecord<int, string>(3, "Item", parentId: 1), new TreeNodeRecord<int, string>(4, "Other", isExpanded: true));
            var leaf = _adapter.Views[3];
            var before = leaf.GetComponent<RectTransform>().offsetMin.x;
            _binding.Apply(_tree.BeginUpdate().Move(3, 2).Commit());
            Assert.That(leaf.GetComponent<RectTransform>().offsetMin.x, Is.EqualTo(before));
            Tick(0.125f);
            var middle = leaf.GetComponent<RectTransform>().offsetMin.x;
            Assert.That(middle, Is.GreaterThan(before));
            Tick();
            Assert.That(leaf.GetComponent<RectTransform>().offsetMin.x, Is.GreaterThan(middle));
            Assert.That(_adapter.Views[3], Is.SameAs(leaf));
        }
        [Test]
        public void InterruptedCollapseAndExpand_RetargetFromCurrentGeometry()
        {
            _adapter.Groups.Add(1);
            Viewport(500f);
            Load(new TreeNodeRecord<int, string>(1, "Group", isExpanded: true), new TreeNodeRecord<int, string>(2, "Item", parentId: 1));
            var group = _adapter.Views[1];
            _binding.Apply(_tree.BeginUpdate().Expand(1, false).Commit());
            Tick(0.1f);
            var height = group.GetComponent<RectTransform>().rect.height;
            _binding.Apply(_tree.BeginUpdate().Expand(1, true).Commit());
            Assert.That(group.GetComponent<RectTransform>().rect.height, Is.EqualTo(height).Within(0.01f));
            Tick();
            Assert.That(_adapter.Views[2].activeSelf, Is.True);
            Assert.That(_binding.IsAnimating, Is.False);
        }
        [Test]
        public void DataUpdate_RebindsOnlyDirtyView()
        {
            Load(new TreeNodeRecord<int, string>(1, "One"), new TreeNodeRecord<int, string>(2, "Two"));
            _binding.Apply(_tree.BeginUpdate().Update(1, "Updated").Commit());
            Assert.That(_adapter.Binds[1], Is.EqualTo(2));
            Assert.That(_adapter.Binds[2], Is.EqualTo(1));
        }
        [Test]
        public void InvalidItemWithChildren_IsRejectedIncludingHiddenNodes()
        {
            _adapter.Groups.Add(1);
            _tree.LoadSnapshot(new[] { new TreeNodeRecord<int, string>(1, "Collapsed"), new TreeNodeRecord<int, string>(2, "Item", parentId: 1), new TreeNodeRecord<int, string>(3, "Child", parentId: 2) });
            Assert.Throws<InvalidOperationException>(() => _scrollView.Bind(_tree, _adapter));
        }
        [Test]
        public void InvalidTypeChange_DoesNotReleaseExistingViews_AndCanRecover()
        {
            _adapter.Groups.Add(1);
            Load(new TreeNodeRecord<int, string>(1, "Group", isExpanded: true), new TreeNodeRecord<int, string>(2, "Item", parentId: 1));
            var group = _adapter.Views[1];
            _adapter.Groups.Remove(1);
            Assert.Throws<InvalidOperationException>(() => _binding.Apply(_tree.BeginUpdate().Update(1, "Invalid").Commit()));
            Assert.That(group.activeSelf, Is.True);
            _adapter.Groups.Add(1);
            _binding.Reload();
            Assert.That(_adapter.Views[1], Is.SameAs(group));
        }
        [Test]
        public void PlainItemAndGroup_DoNotAcquireCanvasGroupOrEffects()
        {
            _adapter.Prefabs[0] = Rect("Plain", _root.transform).gameObject;
            _adapter.Groups.Add(1);
            Viewport(500f);
            Load(new TreeNodeRecord<int, string>(1, "Group", isExpanded: true), new TreeNodeRecord<int, string>(2, "Item", parentId: 1));
            Assert.That(_adapter.Views[1].GetComponent<CanvasGroup>(), Is.Null);
            Assert.That(_adapter.Views[2].GetComponent<CanvasGroup>(), Is.Null);
            _binding.Apply(_tree.BeginUpdate().Remove(2).Commit());
            Tick();
            Assert.That(_adapter.Views[2].GetComponent<CanvasGroup>(), Is.Null);
        }

        [Test]
        public void ExplicitFade_BlocksRaycastsWhileAnimatingAndResetsAlpha()
        {
            var prefab = _adapter.Prefabs[0];
            var canvas = prefab.AddComponent<CanvasGroup>();
            canvas.alpha = 0.5f;
            canvas.interactable = false;
            prefab.AddComponent<TreeMotionFade>();
            Load(new TreeNodeRecord<int, string>(1, "One"));
            var original = _adapter.Views[1];
            _binding.Apply(_tree.BeginUpdate().Insert(2, "Two").Commit());
            var entering = _adapter.Views[2].GetComponent<CanvasGroup>();
            Assert.That(entering.alpha, Is.Zero);
            Assert.That(entering.blocksRaycasts, Is.False);
            Tick(0.125f);
            Assert.That(entering.alpha, Is.EqualTo(0.5f).Within(0.001f));
            Tick();
            Assert.That(entering.alpha, Is.EqualTo(1f));
            Assert.That(entering.interactable, Is.False);
            Assert.That(entering.blocksRaycasts, Is.True);
            _binding.Apply(_tree.BeginUpdate().Remove(1).Commit());
            Assert.That(original.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
            Tick();
            Assert.That(original.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f));
            _binding.Apply(_tree.BeginUpdate().Insert(3, "Three").Commit());
            Tick();
            Assert.That(_adapter.Views[3], Is.SameAs(original));
            Assert.That(original.GetComponent<CanvasGroup>().interactable, Is.False);
        }

        [Test]
        public void WithoutBlocker_RendererDoesNotChangeInteractionOrAlpha()
        {
            var canvas = _adapter.Prefabs[0].AddComponent<CanvasGroup>();
            canvas.alpha = 0.4f;
            canvas.blocksRaycasts = false;
            Load(new TreeNodeRecord<int, string>(1, "One"));
            var view = _adapter.Views[1];
            _binding.Apply(_tree.BeginUpdate().Remove(1).Commit());
            Assert.That(view.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0.4f));
            Assert.That(view.GetComponent<CanvasGroup>().interactable, Is.True);
            Assert.That(view.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
        }

        [Test]
        public void PerTypeHeightAndSpacing_ControlNestedItemLayout()
        {
            _adapter.Groups.Add(1);
            _adapter.Types[3] = 1;
            _adapter.Prefabs[1] = Rect("Tall", _root.transform).gameObject;
            _adapter.Heights[1] = 80f;
            Viewport(500f);
            Load(new TreeNodeRecord<int, string>(1, "Group", isExpanded: true),
                new TreeNodeRecord<int, string>(2, "Short", parentId: 1), new TreeNodeRecord<int, string>(3, "Tall", parentId: 1));
            var shortRect = _adapter.Views[2].GetComponent<RectTransform>();
            var tallRect = _adapter.Views[3].GetComponent<RectTransform>();
            Assert.That(shortRect.rect.height, Is.EqualTo(50f));
            Assert.That(tallRect.rect.height, Is.EqualTo(80f));
            Assert.That(-tallRect.offsetMax.y + shortRect.offsetMax.y, Is.EqualTo(53f));
        }

        [Test]
        public void InvalidHeight_IsRejectedBeforeReleasingExistingView()
        {
            Load(new TreeNodeRecord<int, string>(1, "One"));
            var original = _adapter.Views[1];
            _adapter.Prefabs[1] = Rect("Invalid", _root.transform).gameObject;
            _adapter.Heights[1] = float.NaN;
            _adapter.Types[1] = 1;
            Assert.Throws<InvalidOperationException>(() => _binding.Apply(_tree.BeginUpdate().Update(1, "Changed").Commit()));
            Assert.That(original.activeSelf, Is.True);
        }

        [Test]
        public void MultipleCustomHandlers_ReceiveProgressAndResetOnReuse()
        {
            _adapter.Prefabs[0].AddComponent<TestTreeMotionItemView>();
            Load(new TreeNodeRecord<int, string>(1, "One"));
            var view = _adapter.Views[1];
            _binding.Apply(_tree.BeginUpdate().Remove(1).Commit());
            foreach (var handler in view.GetComponents<TestTreeMotionItemView>())
                Assert.That(handler.Presentation.Role, Is.EqualTo(TreeMotionPresentationRole.Exiting));
            Tick();
            _tree.LoadSnapshot(new[] { new TreeNodeRecord<int, string>(2, "Two") });
            _binding.Reload();
            Assert.That(_adapter.Views[2], Is.SameAs(view));
            foreach (var handler in view.GetComponents<TestTreeMotionItemView>())
            {
                Assert.That(handler.Resets, Is.GreaterThanOrEqualTo(3));
                Assert.That(handler.Presentation.Role, Is.EqualTo(TreeMotionPresentationRole.Visible));
            }
            Assert.That(view.transform.localScale, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void NestedGroupAndItem_ReceiveSameParentPaddingWithoutInsetCompensation()
        {
            var prefab = _adapter.Prefabs[100].GetComponent<TreeMotionGroupView>();
            prefab.ChildrenFrame.offsetMin = new Vector2(8f, 0f);
            prefab.ChildrenFrame.offsetMax = new Vector2(-9f, -53f);
            prefab.Configure(prefab.ChildrenFrame, 3f, new TreeMotionPadding(10, 11, 0, 8));
            _adapter.Groups.UnionWith(new[] { 1, 2 });
            Viewport(500f);
            Load(new TreeNodeRecord<int, string>(1, "Parent", isExpanded: true),
                new TreeNodeRecord<int, string>(2, "Nested", parentId: 1),
                new TreeNodeRecord<int, string>(3, "Item", parentId: 1));
            var parent = _adapter.Views[1].GetComponent<RectTransform>();
            var nested = _adapter.Views[2].GetComponent<RectTransform>();
            var item = _adapter.Views[3].GetComponent<RectTransform>();
            Assert.That(nested.offsetMin.x - parent.offsetMin.x, Is.EqualTo(18f));
            Assert.That(parent.offsetMax.x - nested.offsetMax.x, Is.EqualTo(20f));
            Assert.That(nested.offsetMin.x, Is.EqualTo(item.offsetMin.x));
            Assert.That(nested.offsetMax.x, Is.EqualTo(item.offsetMax.x));
        }

        [Test]
        public void ContentRootGroup_ControlsRootSpacingAndPadding()
        {
            _content.GetComponent<TreeMotionGroupView>().Configure(_content, 17f, new TreeMotionPadding(9, 11, 13, 15));
            Viewport(500f);
            Load(new TreeNodeRecord<int, string>(1, "One"), new TreeNodeRecord<int, string>(2, "Two"));
            var first = _adapter.Views[1].GetComponent<RectTransform>();
            var second = _adapter.Views[2].GetComponent<RectTransform>();
            Assert.That(-first.offsetMax.y, Is.EqualTo(13f));
            Assert.That(-second.offsetMax.y, Is.EqualTo(80f));
            Assert.That(first.offsetMin.x, Is.EqualTo(9f));
            Assert.That(-first.offsetMax.x, Is.EqualTo(11f));
            Assert.That(_content.rect.height, Is.EqualTo(145f));
        }

        [Test]
        public void ParentGroupSpacing_IsIndependentOfChildType_AndNeedsNoHeader()
        {
            var prefab = _adapter.Prefabs[100].GetComponent<TreeMotionGroupView>();
            UnityEngine.Object.DestroyImmediate(prefab.transform.Find("Header").gameObject);
            prefab.Configure(prefab.ChildrenFrame, 19f, new TreeMotionPadding(12, 12, 4, 8));
            _adapter.Groups.Add(1);
            _adapter.Types[3] = 1;
            _adapter.Prefabs[1] = Rect("Tall", _root.transform).gameObject;
            _adapter.Heights[1] = 80f;
            _adapter.Heights[100] = float.NaN;
            Viewport(500f);
            Load(new TreeNodeRecord<int, string>(1, "Group", isExpanded: true), new TreeNodeRecord<int, string>(2, "One", parentId: 1), new TreeNodeRecord<int, string>(3, "Two", parentId: 1));
            var first = _adapter.Views[2].GetComponent<RectTransform>();
            var second = _adapter.Views[3].GetComponent<RectTransform>();
            Assert.That(-second.offsetMax.y + first.offsetMax.y, Is.EqualTo(69f));
            Assert.That(_adapter.Views[1].GetComponent<RectTransform>().rect.height, Is.EqualTo(214f));
        }

        private static float Left(RectTransform rect, RectTransform content) =>
            rect.localPosition.x - rect.rect.width * rect.pivot.x - content.rect.xMin;
        private static float Top(RectTransform rect, RectTransform content) =>
            content.rect.yMax - rect.localPosition.y - rect.rect.height * (1f - rect.pivot.y);

        [TestCase(0f, 0f, 7f, 19f)]
        [TestCase(.5f, .5f, 7f, 117f)]
        [TestCase(1f, 1f, -7f, 201f)]
        public void FixedWidth_PreservesAnchorsPivotAndWidth(float anchor, float pivot, float offset, float expectedLeft)
        {
            var prefab = _adapter.Prefabs[0].GetComponent<RectTransform>();
            prefab.anchorMin = new Vector2(anchor, .25f);
            prefab.anchorMax = new Vector2(anchor, .75f);
            prefab.pivot = new Vector2(pivot, .3f);
            prefab.sizeDelta = new Vector2(80, 20);
            prefab.anchoredPosition = new Vector2(offset, 999);
            Load(new TreeNodeRecord<int, string>(1, "Fixed"));
            var rect = _adapter.Views[1].GetComponent<RectTransform>();
            Assert.That(rect.anchorMin, Is.EqualTo(prefab.anchorMin));
            Assert.That(rect.anchorMax, Is.EqualTo(prefab.anchorMax));
            Assert.That(rect.pivot, Is.EqualTo(prefab.pivot));
            Assert.That(rect.rect.width, Is.EqualTo(80));
            Assert.That(rect.sizeDelta.x, Is.EqualTo(80));
            Assert.That(Left(rect, _content), Is.EqualTo(expectedLeft).Within(.001f));
            Assert.That(Top(rect, _content), Is.EqualTo(12).Within(.001f));
            Assert.That(rect.rect.height, Is.EqualTo(50));
        }

        [Test]
        public void PartialStretchAndOffset_AreResolvedAgainstVirtualParent_AndResizeWithoutDrift()
        {
            var prefab = _adapter.Prefabs[0].GetComponent<RectTransform>();
            prefab.anchorMin = new Vector2(.25f, 1);
            prefab.anchorMax = new Vector2(.75f, 1);
            prefab.pivot = new Vector2(.2f, .8f);
            prefab.sizeDelta = new Vector2(-20, 0);
            prefab.anchoredPosition = new Vector2(5, 0);
            Load(new TreeNodeRecord<int, string>(1, "Stretch"));
            var rect = _adapter.Views[1].GetComponent<RectTransform>();
            Assert.That(rect.rect.width, Is.EqualTo(118));
            Assert.That(Left(rect, _content), Is.EqualTo(90).Within(.001f));
            _scrollRect.viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 400);
            Tick();
            Assert.That(rect.rect.width, Is.EqualTo(168));
            Assert.That(Left(rect, _content), Is.EqualTo(115).Within(.001f));
            _binding.Reload();
            Assert.That(Left(rect, _content), Is.EqualTo(115).Within(.001f));
        }

        [Test]
        public void WidthChangeDuringInsert_SnapsWidthWithoutExtendingCompletion()
        {
            Load(new TreeNodeRecord<int, string>(1, "One"));
            var completion = _binding.ApplyAsync(_tree.BeginUpdate().Insert(2, "Two").Commit());
            Tick(.1f);
            var progress = _adapter.Views[2].GetComponent<TestTreeMotionItemView>().Presentation.Progress;
            _scrollRect.viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 400);
            Tick(0f);
            Assert.That(_adapter.Views[1].GetComponent<RectTransform>().rect.width, Is.EqualTo(376f).Within(.001f));
            Assert.That(_adapter.Views[2].GetComponent<RectTransform>().rect.width, Is.EqualTo(376f).Within(.001f));
            Assert.That(_adapter.Views[2].GetComponent<TestTreeMotionItemView>().Presentation.Progress, Is.EqualTo(progress));
            Assert.That(completion.IsCompleted, Is.False);
            Tick(.16f);
            Assert.That(completion.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }

        [Test]
        public void WidthChangeDuringRemove_PreservesExitingViewGeometry()
        {
            Load(new TreeNodeRecord<int, string>(1, "One"), new TreeNodeRecord<int, string>(2, "Two"));
            var exiting = _adapter.Views[2];
            var width = exiting.GetComponent<RectTransform>().rect.width;
            _binding.Apply(_tree.BeginUpdate().Remove(2).Commit());
            Tick(.1f);
            _scrollRect.viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 400);
            Tick(0f);
            Assert.That(exiting.activeSelf, Is.True);
            Assert.That(exiting.GetComponent<RectTransform>().rect.width, Is.EqualTo(width).Within(.001f));
            Tick();
            Assert.That(exiting.activeSelf, Is.False);
        }

        [Test]
        public void OversizedFixedWidth_IsNotShrunkAndCanExtendBeyondParent()
        {
            var prefab = _adapter.Prefabs[0].GetComponent<RectTransform>();
            prefab.anchorMin = prefab.anchorMax = new Vector2(.5f, 1);
            prefab.sizeDelta = new Vector2(400, 0);
            Load(new TreeNodeRecord<int, string>(1, "Wide"));
            var rect = _adapter.Views[1].GetComponent<RectTransform>();
            Assert.That(rect.rect.width, Is.EqualTo(400));
            Assert.That(Left(rect, _content), Is.EqualTo(-50).Within(.001f));
        }

        [Test]
        public void FixedWidthGroup_ContainsChildrenWithinItsActualWidth()
        {
            var prefab = _adapter.Prefabs[100].GetComponent<RectTransform>();
            prefab.anchorMin = prefab.anchorMax = new Vector2(.5f, 1);
            prefab.pivot = new Vector2(.5f, 1);
            prefab.sizeDelta = new Vector2(160, 60);
            _adapter.Groups.Add(1);
            Viewport(500f);
            Load(new TreeNodeRecord<int, string>(1, "Group", isExpanded: true), new TreeNodeRecord<int, string>(2, "Child", parentId: 1));
            var group = _adapter.Views[1].GetComponent<RectTransform>();
            var item = _adapter.Views[2].GetComponent<RectTransform>();
            Assert.That(group.rect.width, Is.EqualTo(160));
            Assert.That(item.rect.width, Is.EqualTo(136));
            Assert.That(Left(item, _content) - Left(group, _content), Is.EqualTo(12).Within(.001f));
        }

        [Test]
        public void FixedWidthMovingBetweenGroups_InterpolatesPositionAndKeepsWidth()
        {
            var prefab = _adapter.Prefabs[0].GetComponent<RectTransform>();
            prefab.anchorMin = prefab.anchorMax = new Vector2(.5f, 1);
            prefab.sizeDelta = new Vector2(80, 0);
            _adapter.Groups.UnionWith(new[] { 1, 2 });
            Viewport(500f);
            Load(new TreeNodeRecord<int, string>(1, "Outer", isExpanded: true), new TreeNodeRecord<int, string>(2, "Inner", parentId: 1, isExpanded: true), new TreeNodeRecord<int, string>(3, "Fixed", parentId: 1));
            var view = _adapter.Views[3];
            var rect = view.GetComponent<RectTransform>();
            _binding.Apply(_tree.BeginUpdate().Move(3, 2).Commit());
            Tick(.125f);
            Assert.That(rect.rect.width, Is.EqualTo(80).Within(.001f));
            Tick();
            Assert.That(_adapter.Views[3], Is.SameAs(view));
            Assert.That(rect.rect.width, Is.EqualTo(80).Within(.001f));
            Assert.That(rect.anchorMin.x, Is.EqualTo(.5f));
        }

        [Test]
        public void MissingPrefabAndInvalidGroupReferences_AreRejected()
        {
            _adapter.Types[1] = 999;
            _tree.LoadSnapshot(new[] { new TreeNodeRecord<int, string>(1, "Missing") });
            Assert.Throws<InvalidOperationException>(() => _scrollView.Bind(_tree, _adapter));
            _adapter.Types[1] = 100;
            var group = _adapter.Prefabs[100].GetComponent<TreeMotionGroupView>();
            group.Configure(null, 3f, default);
            Assert.Throws<InvalidOperationException>(() => _scrollView.Bind(_tree, _adapter));
        }
    }
}
