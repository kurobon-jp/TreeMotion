using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

        public TreeMotionPresentation(TreeMotionAnimationKind kind, TreeMotionPresentationRole role, float progress,
            bool isAnimating)
        {
            Kind = kind;
            Role = role;
            Progress = progress;
            IsAnimating = isAnimating;
        }

        public override string ToString()
        {
            return $"Kind:{Kind}, Role:{Role}, Progress:{Progress}, IsAnimating:{IsAnimating}";
        }
    }

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
        /// <summary>Waits for layout animation. A later Apply, Reload or disposal cancels the wait.
        /// Token cancellation cancels only the wait; an already started animation continues.
        /// Call on Unity's main thread. Independent presentation effects are not awaited.</summary>
        public Task ApplyAsync(TreeChangeSet<TId> changes, CancellationToken cancellationToken = default)
            => _driver.ApplyAsync(changes, cancellationToken);
        public void Refresh(TId id) => _driver.Refresh(id);
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
        Task ApplyAsync(TreeChangeSet<TId> changes, CancellationToken cancellationToken);
        void Refresh(TId id);
    }

    /// <summary>Stable-ID virtualization with per-type pools and whole Group prefabs.</summary>
    public sealed class TreeMotionScrollView : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.ScrollRect _scrollRect;
        [SerializeField] private RectTransform _viewport;
        [SerializeField] private RectTransform _content;
        [SerializeField, Min(0.01f)] private float _animationDuration = 0.25f;
        private ITreeMotionDriver _driver;
        private TreeMotionGroupView _rootGroup;

        public void Configure(UnityEngine.UI.ScrollRect scrollRect, RectTransform viewport, RectTransform content)
        {
            _scrollRect = scrollRect;
            _viewport = viewport;
            _content = content;
        }

        public TreeMotionViewController<TId> SetDataSource<TId, TItem>(TreeStore<TId, TItem> tree,
            ITreeMotionDataSource<TId, TItem> source)
        {
            if (_scrollRect == null || _viewport == null || _content == null)
                throw new InvalidOperationException("Scroll view references are not assigned.");
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            if (source == null) throw new ArgumentNullException(nameof(source));
            ValidateNumber(_animationDuration, "AnimationDuration", true);
            _rootGroup = _content.GetComponent<TreeMotionGroupView>();
            if (_rootGroup == null) throw new InvalidOperationException("Content requires RootGroup.");
            _rootGroup.ValidatePrefab(true);
            var driver = new Driver<TId, TItem>(this, tree, source);
            driver.ValidateData();
            _driver?.Dispose();
            _driver = driver;
            driver.Reload();
            driver.Connect();
            return new TreeMotionViewController<TId>(driver);
        }

        private void Update() => _driver?.Tick(Time.unscaledDeltaTime);
        private void OnDestroy() => _driver?.Dispose();

        internal static void Position(RectTransform rect, float left, float right, float top, float height)
        {
            var parent = (RectTransform)rect.parent;
            var bounds = parent.rect;
            var width = bounds.width - left - right;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            var anchor = new Vector2(
                Mathf.LerpUnclamped(rect.anchorMin.x, rect.anchorMax.x, rect.pivot.x),
                Mathf.LerpUnclamped(rect.anchorMin.y, rect.anchorMax.y, rect.pivot.y));
            rect.anchoredPosition = new Vector2(
                left + width * rect.pivot.x - bounds.width * anchor.x,
                bounds.height * (1f - anchor.y) - top - height * (1f - rect.pivot.y));
        }

        internal static void ValidateNumber(float value, string name, bool positive = false)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || (positive && value == 0f))
                throw new InvalidOperationException($"Invalid {name}.");
        }

        private sealed class Driver<TId, TItem> : ITreeMotionViewDriver<TId>
        {
            private sealed class Layout
            {
                internal VisibleRow<TId> Row;
                internal TItem Item;
                internal int Type, Order;
                internal TypeDefinition Definition;
                internal float Top, Height, Left, Right;
                internal Layout Parent;
                internal bool Foreground;
                internal TreeMotionGroupView Group => Definition.Group;

                internal float LeadingBottom =>
                    Top + (Group != null ? Group.FrameInsets.top : Height);
            }

            private sealed class TypeDefinition
            {
                internal GameObject Prefab;
                internal TreeMotionGroupView Group;
                internal float Height;
                internal float WidthDelta, PositionX, AnchorMinX, AnchorMaxX, PivotX;
            }

            private sealed class Lease
            {
                internal readonly int Type;
                internal readonly GameObject GameObject;
                internal readonly RectTransform RectTransform;
                internal readonly TreeMotionGroupView GroupView;
                private readonly ITreeMotionPresentationHandler[] _handlers;

                internal Lease(int type, GameObject go)
                {
                    Type = type;
                    GameObject = go;
                    RectTransform = (RectTransform)go.transform;
                    GroupView = go.GetComponent<TreeMotionGroupView>();
                    _handlers = go.GetComponents<ITreeMotionPresentationHandler>();
                }

                internal void Reset()
                {
                    foreach (var handler in _handlers) handler.ResetPresentation();
                }

                internal void Present(in TreeMotionPresentation presentation)
                {
                    foreach (var handler in _handlers) handler.SetTreeMotionPresentation(presentation);
                }
            }

            private readonly TreeMotionScrollView _host;
            private readonly TreeStore<TId, TItem> _tree;
            private readonly ITreeMotionDataSource<TId, TItem> _source;
            private readonly TreeMotionAnimation<TId> _vertical, _horizontal;
            private readonly Dictionary<int, TypeDefinition> _types = new();
            private readonly Dictionary<int, Stack<Lease>> _available = new();
            private readonly Dictionary<TId, Layout> _byId;
            private readonly Dictionary<TId, Lease> _leased;
            private readonly Dictionary<TId, int> _nodeTypes;
            private readonly HashSet<TId> _targets, _dirty, _renderIds, _foregroundRoots;
            private readonly List<Layout> _layouts = new();
            private readonly List<Layout> _groups = new();
            private readonly List<Layout> _render = new();
            private readonly List<Lease> _all = new();
            private readonly List<TId> _stale = new();
            private readonly Stack<Layout> _open = new();
            private readonly Stack<TId> _validation = new();
            private readonly List<TreeMotionLayout<TId>> _verticalTargets = new();
            private readonly List<TreeMotionLayout<TId>> _horizontalTargets = new();
            private float _heightFrom, _heightTo;
            private float _layoutWidth;
            private bool _disposed;
            private TaskCompletionSource<bool> _completion;

            internal Driver(TreeMotionScrollView host, TreeStore<TId, TItem> tree,
                ITreeMotionDataSource<TId, TItem> source)
            {
                _host = host;
                _tree = tree;
                _source = source;
                _vertical = new TreeMotionAnimation<TId>(tree.Comparer);
                _horizontal = new TreeMotionAnimation<TId>(tree.Comparer);
                _byId = new Dictionary<TId, Layout>(tree.Comparer);
                _leased = new Dictionary<TId, Lease>(tree.Comparer);
                _nodeTypes = new Dictionary<TId, int>(tree.Comparer);
                _targets = new HashSet<TId>(tree.Comparer);
                _dirty = new HashSet<TId>(tree.Comparer);
                _renderIds = new HashSet<TId>(tree.Comparer);
                _foregroundRoots = new HashSet<TId>(tree.Comparer);
            }

            internal void Connect() => _host._scrollRect.onValueChanged.AddListener(OnScroll);
            public bool IsAnimating => _vertical.IsAnimating || _horizontal.IsAnimating;

            internal void ValidateData()
            {
                _validation.Clear();
                _nodeTypes.Clear();
                for (var i = 0; i < _tree.RootCount; i++) _validation.Push(_tree.GetRootId(i));
                while (_validation.Count > 0)
                {
                    var id = _validation.Pop();
                    var type = _source.GetItemType(id, _tree.GetItem(id));
                    if (!_types.TryGetValue(type, out var definition))
                    {
                        var prefab = _source.GetItemPrefab(type);
                        if (prefab == null || !(prefab.transform is RectTransform))
                            throw new InvalidOperationException($"ItemType {type} requires a RectTransform prefab.");
                        var group = prefab.GetComponent<TreeMotionGroupView>();
                        var height = group == null ? _source.GetItemHeight(type) : 0f;
                        if (group == null) ValidateNumber(height, "ItemHeight", true);
                        if (prefab.GetComponentInChildren<Canvas>(true) != null)
                            throw new InvalidOperationException("Node prefabs must use the scroll view's Canvas.");
                        group?.ValidatePrefab();
                        var rect = (RectTransform)prefab.transform;
                        definition = new TypeDefinition
                        {
                            Prefab = prefab, Height = height, Group = group,
                            WidthDelta = rect.sizeDelta.x, PositionX = rect.anchoredPosition.x,
                            AnchorMinX = rect.anchorMin.x, AnchorMaxX = rect.anchorMax.x, PivotX = rect.pivot.x
                        };
                        _types.Add(type, definition);
                    }

                    var children = _tree.GetChildCount(id);
                    if (children > 0 && definition.Group == null)
                        throw new InvalidOperationException($"Node '{id}' has children but its ItemType is not Group.");
                    _nodeTypes.Add(id, type);
                    for (var i = 0; i < children; i++) _validation.Push(_tree.GetChildId(id, i));
                }
            }

            public void Reload()
            {
                ThrowIfDisposed();
                ValidateData();
                CancelCompletion();
                foreach (var pair in _leased) Release(pair.Value);
                _leased.Clear();
                _byId.Clear();
                _dirty.Clear();
                _foregroundRoots.Clear();
                BuildLayout();
                _vertical.Snap(_verticalTargets);
                _horizontal.Snap(_horizontalTargets);
                _heightFrom = _heightTo;
                SetHeight(_heightTo);
                RefreshVisibleViews();
            }

            public void Apply(TreeChangeSet<TId> changes)
            {
                ThrowIfDisposed();
                if (changes == null) throw new ArgumentNullException(nameof(changes));
                CancelCompletion();
                if (changes.Count == 0) return;
                ValidateData();
                _foregroundRoots.Clear();
                for (var i = 0; i < changes.Count; i++)
                {
                    var change = changes[i];
                    if (change.Kind is TreeChangeKind.Move or TreeChangeKind.Swap)
                        _foregroundRoots.Add(change.FirstId);
                    if (change.Kind == TreeChangeKind.Swap)
                        _foregroundRoots.Add(change.SecondId);
                    if (changes[i].Kind != TreeChangeKind.Remove && changes[i].Kind != TreeChangeKind.Swap)
                        _dirty.Add(changes[i].FirstId);
                }
                BuildLayout();
                _heightFrom = _host._content.rect.height;
                _vertical.Retarget(_verticalTargets, _host._animationDuration, changes);
                _horizontal.Retarget(_horizontalTargets, _host._animationDuration, changes);
                RefreshVisibleViews();
            }

            public Task ApplyAsync(TreeChangeSet<TId> changes, CancellationToken cancellationToken)
            {
                ThrowIfDisposed();
                if (changes == null) throw new ArgumentNullException(nameof(changes));
                if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
                Apply(changes);
                if (changes.Count == 0 || !IsAnimating) return Task.CompletedTask;
                _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                return cancellationToken.CanBeCanceled
                    ? WaitForCompletion(_completion, cancellationToken) : _completion.Task;
            }

            private static async Task WaitForCompletion(TaskCompletionSource<bool> completion, CancellationToken token)
            {
                using (token.Register(() => completion.TrySetCanceled(token)))
                    await completion.Task.ConfigureAwait(false);
            }

            private void CancelCompletion()
            {
                var completion = _completion;
                _completion = null;
                completion?.TrySetCanceled();
            }

            public void Refresh(TId id)
            {
                ThrowIfDisposed();
                _dirty.Add(id);
                RefreshVisibleViews();
            }

            private void BuildLayout()
            {
                _layoutWidth = _host._content.rect.width;
                _layouts.Clear();
                _groups.Clear();
                _open.Clear();
                _targets.Clear();
                _verticalTargets.Clear();
                _horizontalTargets.Clear();
                var y = _host._rootGroup.ChildrenPadding.top;
                Layout previous = null;
                for (var i = 0; i < _tree.VisibleCount; i++)
                {
                    var row = _tree.GetVisibleRow(i);
                    while (_open.Count > 0 && _open.Peek().Row.Depth >= row.Depth)
                    {
                        previous = _open.Pop();
                        y += previous.Group.ChildrenPadding.bottom + previous.Group.FrameInsets.bottom;
                        previous.Height = y - previous.Top;
                    }

                    if (previous != null && previous.Row.Depth >= row.Depth)
                        y += _open.Count == 0 ? _host._rootGroup.ChildrenSpacing : _open.Peek().Group.ChildrenSpacing;
                    var type = _nodeTypes[row.Id];
                    var definition = _types[type];
                    var layout = new Layout
                    {
                        Row = row, Item = _tree.GetItem(row.Id), Type = type, Definition = definition, Order = i,
                        Top = y, Left = _host._rootGroup.ChildrenPadding.left, Right = _host._rootGroup.ChildrenPadding.right
                    };
                    if (_open.Count > 0)
                    {
                        var parent = _open.Peek();
                        layout.Parent = parent;
                        layout.Left = parent.Left + parent.Group.FrameInsets.left + parent.Group.ChildrenPadding.left;
                        layout.Right = parent.Right + parent.Group.FrameInsets.right +
                                       parent.Group.ChildrenPadding.right;
                    }

                    var regionWidth = Mathf.Max(0f, _layoutWidth - layout.Left - layout.Right);
                    var width = Mathf.Max(0f, regionWidth * (definition.AnchorMaxX - definition.AnchorMinX) + definition.WidthDelta);
                    var pivotX = layout.Left + regionWidth * Mathf.LerpUnclamped(definition.AnchorMinX, definition.AnchorMaxX, definition.PivotX) + definition.PositionX;
                    layout.Left = pivotX - width * definition.PivotX;
                    layout.Right = _layoutWidth - layout.Left - width;

                    if (_byId.TryGetValue(row.Id, out var old) && (old.Type != type || !SameRow(old.Row, row)))
                        _dirty.Add(row.Id);
                    _byId[row.Id] = layout;
                    _layouts.Add(layout);
                    _targets.Add(row.Id);
                    if (layout.Group != null)
                    {
                        _groups.Add(layout);
                        layout.Height = layout.Group.FrameInsets.top + layout.Group.FrameInsets.bottom;
                        y += layout.Height;
                        if (row.IsExpanded && row.HasChildren)
                        {
                            y -= layout.Group.FrameInsets.bottom;
                            y += layout.Group.ChildrenPadding.top;
                            _open.Push(layout);
                        }
                    }
                    else
                    {
                        layout.Height = definition.Height;
                        y += layout.Height;
                    }

                    previous = layout;
                }

                while (_open.Count > 0)
                {
                    var group = _open.Pop();
                    y += group.Group.ChildrenPadding.bottom + group.Group.FrameInsets.bottom;
                    group.Height = y - group.Top;
                }

                foreach (var layout in _layouts)
                {
                    _verticalTargets.Add(new TreeMotionLayout<TId>(layout.Row.Id, layout.Top, layout.Height));
                    _horizontalTargets.Add(new TreeMotionLayout<TId>(layout.Row.Id, layout.Left,
                        Mathf.Max(0f, _layoutWidth - layout.Left - layout.Right)));
                }

                _heightTo = y + _host._rootGroup.ChildrenPadding.bottom;
            }

            public void Tick(float deltaTime)
            {
                if (_disposed) return;
                RefreshWidth();
                if (!IsAnimating) return;
                _vertical.Advance(deltaTime);
                _horizontal.Advance(deltaTime);
                var p = _vertical.Progress;
                p = p * p * (3f - 2f * p);
                SetHeight(Mathf.LerpUnclamped(_heightFrom, _heightTo, p));
                RefreshVisibleViews();
                if (!IsAnimating)
                {
                    _foregroundRoots.Clear();
                    _stale.Clear();
                    foreach (var pair in _byId)
                        if (!_targets.Contains(pair.Key))
                            _stale.Add(pair.Key);
                    foreach (var id in _stale)
                    {
                        _byId.Remove(id);
                        _dirty.Remove(id);
                    }
                    var completion = _completion;
                    _completion = null;
                    completion?.TrySetResult(true);
                }
            }

            private void RefreshWidth()
            {
                if (Mathf.Approximately(_layoutWidth, _host._content.rect.width)) return;
                BuildLayout();
                if (IsAnimating) _horizontal.SnapGeometry(_horizontalTargets);
                else _horizontal.Snap(_horizontalTargets);
                RefreshVisibleViews();
            }

            private void OnScroll(Vector2 _)
            {
                if (_disposed) return;
                RefreshWidth();
                RefreshVisibleViews();
            }

            private void AddRender(Layout layout)
            {
                if (_renderIds.Add(layout.Row.Id)) _render.Add(layout);
            }

            private void RefreshVisibleViews()
            {
                var top = Mathf.Max(0f, _host._content.anchoredPosition.y);
                var bottom = top + Mathf.Max(0f, _host._viewport.rect.height);
                _render.Clear();
                _renderIds.Clear();
                if (IsAnimating)
                {
                    // Current tracks include nodes arriving from outside the viewport.
                    for (var i = 0; i < _vertical.Count; i++)
                    {
                        var v = _vertical.GetValue(i);
                        if (v.Offset + v.Size > top && v.Offset < bottom && _byId.TryGetValue(v.Id, out var layout))
                            AddRender(layout);
                    }
                }
                else
                {
                    var low = 0;
                    var high = _layouts.Count;
                    while (low < high)
                    {
                        var mid = low + (high - low) / 2;
                        if (_layouts[mid].LeadingBottom <= top) low = mid + 1;
                        else high = mid;
                    }

                    for (var i = low; i < _layouts.Count && _layouts[i].Top < bottom; i++) AddRender(_layouts[i]);
                    foreach (var group in _groups)
                        if (group.Top + group.Height > top && group.Top < bottom)
                            AddRender(group);
                }

                foreach (var layout in _render)
                {
                    layout.Foreground = false;
                    for (var ancestor = layout; ancestor != null; ancestor = ancestor.Parent)
                        if (IsAnimating && _foregroundRoots.Contains(ancestor.Row.Id))
                        {
                            layout.Foreground = true;
                            break;
                        }
                }

                _render.Sort((a, b) =>
                {
                    var aExit = !_targets.Contains(a.Row.Id);
                    var bExit = !_targets.Contains(b.Row.Id);
                    if (a.Foreground != b.Foreground) return a.Foreground ? 1 : -1;
                    if (aExit != bExit) return aExit ? 1 : -1;
                    return a.Order.CompareTo(b.Order);
                });
                _stale.Clear();
                foreach (var pair in _leased)
                    if (!_renderIds.Contains(pair.Key))
                        _stale.Add(pair.Key);
                foreach (var id in _stale)
                {
                    Release(_leased[id]);
                    _leased.Remove(id);
                }

                foreach (var layout in _render)
                {
                    var id = layout.Row.Id;
                    var needsBind = _dirty.Remove(id);
                    if (_leased.TryGetValue(id, out var lease) && lease.Type != layout.Type)
                    {
                        Release(lease);
                        _leased.Remove(id);
                    }

                    if (!_leased.TryGetValue(id, out lease))
                    {
                        if (!_available.TryGetValue(layout.Type, out var pool))
                        {
                            pool = new Stack<Lease>();
                            _available.Add(layout.Type, pool);
                        }

                        Lease view;
                        if (pool.Count > 0) view = pool.Pop();
                        else
                        {
                            view = new Lease(layout.Type, Instantiate(layout.Definition.Prefab,
                                _host._content));
                            _all.Add(view);
                        }

                        view.Reset();
                        view.GameObject.SetActive(false);
                        lease = view;
                        _leased.Add(id, lease);
                        needsBind = true;
                    }

                    if (needsBind)
                        _source.Bind(lease.GameObject, id, layout.Item, layout.Row);
                    _vertical.TryGetValue(id, out var vertical);
                    _horizontal.TryGetValue(id, out var horizontal);
                    Position(lease.RectTransform, horizontal.Offset,
                        _host._content.rect.width - horizontal.Offset - horizontal.Size, vertical.Offset, vertical.Size);
                    if (lease.GroupView != null)
                        lease.GroupView.SetGeometry(vertical.Size);
                    var role = vertical.Kind == TreeMotionAnimationKind.Remove ? TreeMotionPresentationRole.Exiting :
                        vertical.Kind == TreeMotionAnimationKind.Insert ? TreeMotionPresentationRole.Entering :
                        TreeMotionPresentationRole.Current;
                    lease.Present(new TreeMotionPresentation(vertical.Kind, role, vertical.Progress,
                        vertical.IsAnimating));
                    lease.RectTransform.SetAsLastSibling();
                    lease.GameObject.SetActive(true);
                }
            }

            private void Release(Lease lease)
            {
                lease.GameObject.SetActive(false);
                lease.Reset();
                _available[lease.Type].Push(lease);
            }

            private void SetHeight(float height) =>
                _host._content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

            private void ThrowIfDisposed()
            {
                if (_disposed) throw new ObjectDisposedException(nameof(TreeMotionViewController<TId>));
            }

            private static bool SameRow(VisibleRow<TId> a, VisibleRow<TId> b) => a.Depth == b.Depth &&
                a.HasChildren == b.HasChildren && a.IsExpanded == b.IsExpanded;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                CancelCompletion();
                if (_host != null && _host._scrollRect != null)
                    _host._scrollRect.onValueChanged.RemoveListener(OnScroll);
                foreach (var view in _all)
                    if (view.GameObject != null)
                    {
                        view.GameObject.SetActive(false);
                        if (Application.isPlaying) Destroy(view.GameObject);
                        else DestroyImmediate(view.GameObject);
                    }

                _all.Clear();
                _leased.Clear();
                _available.Clear();
                _foregroundRoots.Clear();
            }
        }
    }
}
