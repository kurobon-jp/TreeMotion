using System;
using System.Collections.Generic;
using UnityEngine;

namespace TreeMotion
{
    public enum TreeMotionPresentationRole
    {
        Current,
        Entering,
        Exiting
    }

    public readonly struct TreeMotionPresentation
    {
        public TreeMotionAnimationKind Kind { get; }
        public TreeMotionPresentationRole Role { get; }
        public float Progress { get; }
        public bool IsAnimating { get; }

        public TreeMotionPresentation(TreeMotionAnimationKind kind,
            TreeMotionPresentationRole role, float progress, bool isAnimating)
        {
            Kind = kind;
            Role = role;
            Progress = progress;
            IsAnimating = isAnimating;
        }
    }

    public interface ITreeMotionItemView
    {
        RectTransform RectTransform { get; }
        void SetTreeMotionPresentation(in TreeMotionPresentation presentation);
    }

    public delegate void TreeMotionItemBinder<TId, TItem, in TView>(TView view, TId id,
        TItem item, VisibleRow<TId> row) where TView : Component, ITreeMotionItemView;

    public sealed class TreeMotionViewController<TId>
    {
        private readonly ITreeMotionViewDriver<TId> _driver;

        internal TreeMotionViewController(ITreeMotionViewDriver<TId> driver)
        {
            _driver = driver;
        }

        public bool IsAnimating => _driver.IsAnimating;
        public void Reload() => _driver.Reload();
        public void Apply(TreeChangeSet<TId> changes) => _driver.Apply(changes);
    }

    internal interface ITreeMotionDriver
    {
        bool IsAnimating { get; }
        void Tick(float deltaTime);
        void Dispose();
    }

    internal interface ITreeMotionViewDriver<TId> : ITreeMotionDriver
    {
        void Reload();
        void Apply(TreeChangeSet<TId> changes);
    }

    /// <summary>
    /// Unity-facing fixed-height tree renderer. It owns viewport culling, prefab pooling, row
    /// positioning, and structural animation so sample/application code only supplies data and a
    /// prefab binder.
    /// </summary>
    public sealed class TreeMotionScrollView : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.ScrollRect _scrollRect;
        [SerializeField] private RectTransform _viewport;
        [SerializeField] private RectTransform _content;
        [SerializeField, Min(0f)] private float _spacing = 8f;
        [SerializeField, Min(0f)] private float _paddingTop = 12f;
        [SerializeField, Min(0f)] private float _paddingBottom = 12f;
        [SerializeField, Min(0f)] private float _paddingLeft = 12f;
        [SerializeField, Min(0f)] private float _paddingRight = 12f;
        [SerializeField, Min(0.01f)] private float _animationDuration = 0.25f;

        private ITreeMotionDriver _driver;

        public void Configure(UnityEngine.UI.ScrollRect scrollRect, RectTransform viewport,
            RectTransform content)
        {
            _scrollRect = scrollRect;
            _viewport = viewport;
            _content = content;
        }

        public TreeMotionViewController<TId> SetDataSource<TId, TItem, TView>(
            TreeStore<TId, TItem> tree, TView itemPrefab, float itemHeight,
            TreeMotionItemBinder<TId, TItem, TView> binder)
            where TView : Component, ITreeMotionItemView
        {
            if (_scrollRect == null || _viewport == null || _content == null)
                throw new InvalidOperationException("TreeMotionScrollView references are not assigned.");
            if (tree == null)
                throw new ArgumentNullException(nameof(tree));
            if (itemPrefab == null)
                throw new ArgumentNullException(nameof(itemPrefab));
            if (itemHeight <= 0f || float.IsNaN(itemHeight) || float.IsInfinity(itemHeight))
                throw new ArgumentOutOfRangeException(nameof(itemHeight));
            if (binder == null)
                throw new ArgumentNullException(nameof(binder));

            _driver?.Dispose();
            var driver = new Driver<TId, TItem, TView>(this, tree, itemPrefab, itemHeight,
                binder);
            _driver = driver;
            driver.Reload();
            return new TreeMotionViewController<TId>(driver);
        }

        private void Update()
        {
            _driver?.Tick(Time.unscaledDeltaTime);
        }

        private void OnDestroy()
        {
            _driver?.Dispose();
            _driver = null;
        }

        private sealed class Driver<TId, TItem, TView> : ITreeMotionViewDriver<TId>
            where TView : Component, ITreeMotionItemView
        {
            private readonly struct RowLayout
            {
                internal readonly VisibleRow<TId> Row;
                internal readonly TItem Item;
                internal readonly float Top;
                internal readonly float Height;
                internal float Bottom => Top + Height;

                internal RowLayout(VisibleRow<TId> row, TItem item, float top, float height)
                {
                    Row = row;
                    Item = item;
                    Top = top;
                    Height = height;
                }
            }

            private readonly TreeMotionScrollView _host;
            private readonly TreeStore<TId, TItem> _tree;
            private readonly TView _prefab;
            private readonly float _itemHeight;
            private readonly TreeMotionItemBinder<TId, TItem, TView> _binder;
            private readonly TreeMotionAnimation<TId> _animation;
            private readonly List<RowLayout> _layouts = new List<RowLayout>();
            private readonly List<TreeMotionLayout<TId>> _targets =
                new List<TreeMotionLayout<TId>>();
            private readonly Dictionary<TId, RowLayout> _layoutsById;
            private readonly Dictionary<TId, TView> _leasedViews;
            private readonly List<TView> _allViews = new List<TView>();
            private readonly Stack<TView> _availableViews = new Stack<TView>();
            private readonly List<TId> _renderIds = new List<TId>();
            private readonly List<TId> _previousRenderedIds = new List<TId>();
            private readonly HashSet<TId> _renderIdSet;
            private readonly HashSet<TId> _targetIdSet;
            private readonly HashSet<TId> _dirtyIds;
            private readonly List<TId> _staleIds = new List<TId>();
            private float _contentHeightFrom;
            private float _contentHeightTo;
            private bool _disposed;

            internal Driver(TreeMotionScrollView host, TreeStore<TId, TItem> tree,
                TView prefab, float itemHeight, TreeMotionItemBinder<TId, TItem, TView> binder)
            {
                _host = host;
                _tree = tree;
                _prefab = prefab;
                _itemHeight = itemHeight;
                _binder = binder;
                _animation = new TreeMotionAnimation<TId>(tree.Comparer);
                _layoutsById = new Dictionary<TId, RowLayout>(tree.Comparer);
                _leasedViews = new Dictionary<TId, TView>(tree.Comparer);
                _renderIdSet = new HashSet<TId>(tree.Comparer);
                _targetIdSet = new HashSet<TId>(tree.Comparer);
                _dirtyIds = new HashSet<TId>(tree.Comparer);
                _host._scrollRect.onValueChanged.AddListener(OnScroll);
            }

            public bool IsAnimating => _animation.IsAnimating;

            public void Reload()
            {
                ThrowIfDisposed();
                ReleaseAllLeasedViews();
                _layoutsById.Clear();
                _dirtyIds.Clear();
                BuildLayout();
                _animation.Snap(_targets);
                _contentHeightFrom = _contentHeightTo;
                SetContentHeight(_contentHeightTo);
                RefreshVisibleViews();
            }

            public void Apply(TreeChangeSet<TId> changes)
            {
                ThrowIfDisposed();
                if (changes == null)
                    throw new ArgumentNullException(nameof(changes));
                if (changes.Count == 0)
                    return;

                for (var i = 0; i < changes.Count; i++)
                {
                    var change = changes[i];
                    if (change.Kind != TreeChangeKind.Remove &&
                        change.Kind != TreeChangeKind.Swap)
                    {
                        _dirtyIds.Add(change.FirstId);
                    }
                }

                BuildLayout();
                _contentHeightFrom = _host._content.rect.height;
                _animation.Retarget(_targets, _host._animationDuration, changes);
                RefreshVisibleViews();
            }

            public void Tick(float deltaTime)
            {
                if (_disposed || !_animation.IsAnimating)
                    return;
                _animation.Advance(deltaTime);
                var progress = Smooth(_animation.Progress);
                SetContentHeight(Mathf.LerpUnclamped(_contentHeightFrom, _contentHeightTo,
                    progress));
                RefreshVisibleViews();
                if (!_animation.IsAnimating)
                    RemoveStaleVisuals();
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                if (_host != null && _host._scrollRect != null)
                    _host._scrollRect.onValueChanged.RemoveListener(OnScroll);
                for (var i = 0; i < _allViews.Count; i++)
                {
                    if (_allViews[i] != null)
                        UnityEngine.Object.Destroy(_allViews[i].gameObject);
                }
                _allViews.Clear();
                _availableViews.Clear();
                _leasedViews.Clear();
            }

            private void BuildLayout()
            {
                _layouts.Clear();
                _targets.Clear();
                _targetIdSet.Clear();
                var y = _host._paddingTop;
                for (var i = 0; i < _tree.VisibleCount; i++)
                {
                    var row = _tree.GetVisibleRow(i);
                    var layout = new RowLayout(row, _tree.GetItem(row.Id), y, _itemHeight);
                    if (_layoutsById.TryGetValue(row.Id, out var previous) &&
                        !HasSamePresentation(previous.Row, row))
                        _dirtyIds.Add(row.Id);
                    _layouts.Add(layout);
                    _targets.Add(new TreeMotionLayout<TId>(row.Id, y, _itemHeight));
                    _layoutsById[row.Id] = layout;
                    _targetIdSet.Add(row.Id);
                    y += _itemHeight;
                    if (i + 1 < _tree.VisibleCount)
                        y += _host._spacing;
                }
                _contentHeightTo = y + _host._paddingBottom;
            }

            private void OnScroll(Vector2 _)
            {
                if (!_disposed)
                    RefreshVisibleViews();
            }

            private void RefreshVisibleViews()
            {
                var scrollTop = Mathf.Max(0f, _host._content.anchoredPosition.y);
                var viewportHeight = Mathf.Max(0f, _host._viewport.rect.height);
                var visibleTop = scrollTop;
                var visibleBottom = scrollTop + viewportHeight;
                CollectRenderIds(visibleTop, visibleBottom);
                ReleaseUnrenderedViews();

                for (var i = 0; i < _renderIds.Count; i++)
                {
                    var id = _renderIds[i];
                    if (!_animation.TryGetValue(id, out var value) ||
                        !_layoutsById.TryGetValue(id, out var layout))
                        continue;

                    var view = LeaseView(id, layout);
                    var role = value.Kind == TreeMotionAnimationKind.Insert
                        ? TreeMotionPresentationRole.Entering
                        : value.Kind == TreeMotionAnimationKind.Remove
                            ? TreeMotionPresentationRole.Exiting
                            : TreeMotionPresentationRole.Current;
                    Present(view, layout, value, role);
                }

                _previousRenderedIds.Clear();
                _previousRenderedIds.AddRange(_renderIds);
            }

            private void CollectRenderIds(float visibleTop, float visibleBottom)
            {
                _renderIds.Clear();
                _renderIdSet.Clear();
                var first = FindFirstVisible(visibleTop);
                for (var i = first; i < _layouts.Count; i++)
                {
                    var layout = _layouts[i];
                    if (layout.Top >= visibleBottom)
                        break;
                    AddRenderId(layout.Row.Id);
                }

                if (!_animation.IsAnimating)
                    return;
                for (var i = 0; i < _previousRenderedIds.Count; i++)
                {
                    var id = _previousRenderedIds[i];
                    if (_animation.TryGetValue(id, out var value) &&
                        value.Offset + value.Size > visibleTop && value.Offset < visibleBottom)
                        AddRenderId(id);
                }
            }

            private int FindFirstVisible(float visibleTop)
            {
                var low = 0;
                var high = _layouts.Count;
                while (low < high)
                {
                    var middle = low + (high - low) / 2;
                    if (_layouts[middle].Bottom <= visibleTop)
                        low = middle + 1;
                    else
                        high = middle;
                }
                return low;
            }

            private void AddRenderId(TId id)
            {
                if (_renderIdSet.Add(id))
                    _renderIds.Add(id);
            }

            private TView LeaseView(TId id, RowLayout layout)
            {
                if (!_leasedViews.TryGetValue(id, out var view))
                {
                    view = AcquireView();
                    _leasedViews.Add(id, view);
                    Bind(view, layout);
                    _dirtyIds.Remove(id);
                }
                else if (_dirtyIds.Remove(id))
                {
                    Bind(view, layout);
                }

                return view;
            }

            private TView AcquireView()
            {
                if (_availableViews.Count > 0)
                    return _availableViews.Pop();
                var view = UnityEngine.Object.Instantiate(_prefab, _host._content);
                view.name = $"PooledTreeItem{_allViews.Count}";
                _allViews.Add(view);
                return view;
            }

            private void Bind(TView view, RowLayout layout)
            {
                view.gameObject.SetActive(true);
                _binder(view, layout.Row.Id, layout.Item, layout.Row);
            }

            private void Present(TView view, RowLayout layout,
                TreeMotionAnimationValue<TId> value, TreeMotionPresentationRole role)
            {
                view.gameObject.SetActive(true);
                Position(view.RectTransform, _host._paddingLeft, _host._paddingRight,
                    value.Offset, value.Size);
                view.SetTreeMotionPresentation(new TreeMotionPresentation(value.Kind, role,
                    value.Progress, value.IsAnimating));
                view.transform.SetAsLastSibling();
            }

            private void ReleaseUnrenderedViews()
            {
                _staleIds.Clear();
                foreach (var pair in _leasedViews)
                {
                    if (!_renderIdSet.Contains(pair.Key))
                        _staleIds.Add(pair.Key);
                }
                for (var i = 0; i < _staleIds.Count; i++)
                {
                    var id = _staleIds[i];
                    var view = _leasedViews[id];
                    _leasedViews.Remove(id);
                    ReleaseView(view);
                }

            }

            private void ReleaseAllLeasedViews()
            {
                foreach (var view in _leasedViews.Values)
                    ReleaseView(view);
                _leasedViews.Clear();
            }

            private void ReleaseView(TView view)
            {
                view.gameObject.SetActive(false);
                _availableViews.Push(view);
            }

            private void RemoveStaleVisuals()
            {
                _staleIds.Clear();
                foreach (var pair in _layoutsById)
                {
                    if (!_targetIdSet.Contains(pair.Key))
                        _staleIds.Add(pair.Key);
                }
                for (var i = 0; i < _staleIds.Count; i++)
                {
                    _layoutsById.Remove(_staleIds[i]);
                    _dirtyIds.Remove(_staleIds[i]);
                }
            }

            private void SetContentHeight(float height)
                => _host._content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    Mathf.Max(0f, height));

            private void ThrowIfDisposed()
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(TreeMotionViewController<TId>));
            }

            private static void Position(RectTransform rect, float left, float right,
                float top, float height)
            {
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2(left, -top - height);
                rect.offsetMax = new Vector2(-right, -top);
            }

            private static float Smooth(float value) => value * value * (3f - 2f * value);

            private static bool HasSamePresentation(VisibleRow<TId> left, VisibleRow<TId> right)
                => left.Depth == right.Depth && left.HasChildren == right.HasChildren &&
                    left.IsExpanded == right.IsExpanded;
        }
    }
}
