using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TreeMotion
{
    public enum TreeMotionPresentationRole
    {
        Visible,
        Entering,
        Exiting
    }

    public readonly struct TreeMotionPresentation
    {
        public TreeMotionAnimationKind Kind { get; }
        public TreeMotionPresentationRole Role { get; }
        public TreeChangeKind? Cause { get; }

        /// <summary>Linear presentation progress without easing. Retargeting preserves the current value.</summary>
        public float Progress { get; }

        public bool IsAnimating { get; }

        public TreeMotionPresentation(TreeMotionAnimationKind kind, TreeMotionPresentationRole role, float progress,
            bool isAnimating, TreeChangeKind? cause = null)
        {
            Kind = kind;
            Role = role;
            Cause = cause;
            Progress = progress;
            IsAnimating = isAnimating;
        }

        public override string ToString()
        {
            return $"Kind:{Kind}, Role:{Role}, Cause:{Cause}, Progress:{Progress}, IsAnimating:{IsAnimating}";
        }
    }

    /// <summary>Connection to a ScrollView, exposing data application and animation completion.</summary>
    public sealed class TreeMotionBinding<TId>
    {
        private readonly ITreeMotionBindingDriver<TId> _driver;

        internal TreeMotionBinding(ITreeMotionBindingDriver<TId> driver)
        {
            _driver = driver;
        }

        public bool IsAnimating => _driver.IsAnimating;
        public void Reload() => _driver.Reload();
        public void Apply(TreeChangeSet<TId> changes) => _driver.Apply(changes, null);
        public void Apply(TreeChangeSet<TId> changes, float duration) => _driver.Apply(changes, duration);

        /// <summary>Waits for layout animation. A later Apply, Reload or disposal cancels the wait.
        /// Token cancellation cancels only the wait; an already started animation continues.
        /// Call on Unity's main thread. Independent presentation effects are not awaited.</summary>
        public Task ApplyAsync(TreeChangeSet<TId> changes, CancellationToken cancellationToken = default)
            => _driver.ApplyAsync(changes, null, cancellationToken);

        public Task ApplyAsync(TreeChangeSet<TId> changes, float duration,
            CancellationToken cancellationToken = default)
            => _driver.ApplyAsync(changes, duration, cancellationToken);

        public void Refresh(TId id) => _driver.Refresh(id);
        public void Refresh(params TId[] ids) => _driver.Refresh(ids);
    }

    internal interface ITreeMotionDriver
    {
        bool IsAnimating { get; }
        void Tick(float deltaTime);
        void Dispose();
    }

    internal interface ITreeMotionBindingDriver<TId> : ITreeMotionDriver
    {
        void Reload();
        void Apply(TreeChangeSet<TId> changes, float? duration);
        Task ApplyAsync(TreeChangeSet<TId> changes, float? duration, CancellationToken cancellationToken);
        void Refresh(TId id);
        void Refresh(params TId[] ids);
    }

    /// <summary>Stable-ID virtualization with per-prefab pools and whole Group prefabs.</summary>
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

        /// <summary>Connects data to this ScrollView and returns its operation handle.
        /// Rebinding disposes the previous connection and cancels pending animation waits.</summary>
        public TreeMotionBinding<TId> Bind<TId, TItem>(TreeStore<TId, TItem> tree,
            ITreeMotionAdapter<TId, TItem> adapter)
        {
            if (_scrollRect == null || _viewport == null || _content == null)
                throw new InvalidOperationException("Scroll view references are not assigned.");
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));
            ValidateNumber(_animationDuration, "AnimationDuration", true);
            _rootGroup = _content.GetComponent<TreeMotionGroupView>();
            if (_rootGroup == null) throw new InvalidOperationException("Content requires RootGroup.");
            _rootGroup.ValidatePrefab(true);
            var driver = new Driver<TId, TItem>(this, tree, adapter);
            driver.ValidateData();
            _driver?.Dispose();
            _driver = driver;
            driver.Reload();
            driver.Connect();
            return new TreeMotionBinding<TId>(driver);
        }

        private void Update() => Tick(Time.deltaTime);
        internal void Tick(float deltaTime) => _driver?.Tick(deltaTime);
        internal void OnDestroy() => _driver?.Dispose();

        internal static void Position(RectTransform rect, Rect bounds, float left, float width, float top, float height)
        {
            var min = rect.anchorMin;
            var max = rect.anchorMax;
            var pivot = rect.pivot;
            var size = new Vector2(width - bounds.width * (max.x - min.x),
                height - bounds.height * (max.y - min.y));
            var currentSize = rect.sizeDelta;
            if (currentSize.x != size.x || currentSize.y != size.y) rect.sizeDelta = size;
            var anchor = new Vector2(
                Mathf.LerpUnclamped(min.x, max.x, pivot.x),
                Mathf.LerpUnclamped(min.y, max.y, pivot.y));
            var position = new Vector2(
                left + width * pivot.x - bounds.width * anchor.x,
                bounds.height * (1f - anchor.y) - top - height * (1f - pivot.y));
            var currentPosition = rect.anchoredPosition;
            if (currentPosition.x != position.x || currentPosition.y != position.y)
                rect.anchoredPosition = position;
        }

        internal static void ValidateNumber(float value, string name, bool positive = false)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || (positive && value == 0f))
                throw new InvalidOperationException($"Invalid {name}.");
        }

        private sealed class Driver<TId, TItem> : ITreeMotionBindingDriver<TId>
        {
            private sealed class Layout
            {
                internal VisibleRow<TId> Row;
                internal TItem Item;
                internal int Order;
                internal GameObject Prefab;
                internal PrefabDefinition Definition;
                internal float Top, Height, Left, Right;
                internal Layout Parent;
                internal bool Foreground, Exiting;
                internal TreeChangeKind? Cause;
                internal TreeMotionGroupView Group => Definition.Group;

                internal float LeadingBottom =>
                    Top + (Group != null ? Group.FrameInsets.top : Height);
            }

            private static int CompareRender(Layout a, Layout b)
            {
                if (a.Foreground != b.Foreground) return a.Foreground ? 1 : -1;
                if (a.Exiting != b.Exiting) return a.Exiting ? 1 : -1;
                return a.Order.CompareTo(b.Order);
            }

            private sealed class PrefabDefinition
            {
                internal GameObject Prefab;
                internal TreeMotionGroupView Group;
                internal RectTransform Rect;
            }

            private sealed class Lease
            {
                internal readonly GameObject Prefab;
                internal readonly GameObject GameObject;
                internal readonly RectTransform RectTransform;
                internal readonly TreeMotionGroupView GroupView;
                private readonly ITreeMotionPresentationHandler[] _handlers;

                internal Lease(GameObject prefab, GameObject go)
                {
                    Prefab = prefab;
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
                    foreach (var handler in _handlers) handler.ApplyPresentation(presentation);
                }
            }

            private readonly TreeMotionScrollView _host;
            private readonly TreeStore<TId, TItem> _tree;
            private readonly ITreeMotionAdapter<TId, TItem> _adapter;
            private readonly TreeMotionAnimation<TId> _vertical, _horizontal;
            private readonly Dictionary<GameObject, PrefabDefinition> _definitions = new();
            private readonly Dictionary<GameObject, Stack<Lease>> _available = new();
            private readonly Dictionary<TId, Layout> _byId;
            private readonly Dictionary<TId, Lease> _leased;
            private readonly Dictionary<TId, GameObject> _nodePrefabs;
            private readonly Dictionary<TId, TreeChangeKind> _causeRoots;
            private readonly HashSet<TId> _targets, _dirty, _renderIds, _foregroundRoots;
            private readonly List<Layout> _layouts = new();
            private readonly List<Layout> _groups = new();
            private readonly List<Layout> _render = new();
            private readonly List<Lease> _all = new();
            private readonly List<Lease> _orderedViews = new();
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
                ITreeMotionAdapter<TId, TItem> adapter)
            {
                _host = host;
                _tree = tree;
                _adapter = adapter;
                _vertical = new TreeMotionAnimation<TId>(tree.Comparer);
                _horizontal = new TreeMotionAnimation<TId>(tree.Comparer);
                _byId = new Dictionary<TId, Layout>(tree.Comparer);
                _leased = new Dictionary<TId, Lease>(tree.Comparer);
                _nodePrefabs = new Dictionary<TId, GameObject>(tree.Comparer);
                _causeRoots = new Dictionary<TId, TreeChangeKind>(tree.Comparer);
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
                _nodePrefabs.Clear();
                for (var i = 0; i < _tree.RootCount; i++) _validation.Push(_tree.GetRootId(i));
                while (_validation.Count > 0)
                {
                    var id = _validation.Pop();
                    var prefab = _adapter.GetItemPrefab(id, _tree.GetItem(id));
                    if (prefab == null || !(prefab.transform is RectTransform))
                        throw new InvalidOperationException($"Node '{id}' requires a RectTransform prefab.");
                    if (!_definitions.TryGetValue(prefab, out var definition))
                    {
                        var group = prefab.GetComponent<TreeMotionGroupView>();
                        if (prefab.GetComponentInChildren<Canvas>(true) != null)
                            throw new InvalidOperationException("Node prefabs must use the scroll view's Canvas.");
                        group?.ValidatePrefab();
                        var rect = (RectTransform)prefab.transform;
                        definition = new PrefabDefinition
                        {
                            Prefab = prefab, Rect = rect, Group = group
                        };
                        _definitions.Add(prefab, definition);
                    }

                    var children = _tree.GetChildCount(id);
                    if (children > 0 && definition.Group == null)
                        throw new InvalidOperationException(
                            $"Node '{id}' has children but its prefab has no TreeMotionGroupView.");
                    _nodePrefabs.Add(id, prefab);
                    for (var i = 0; i < children; i++) _validation.Push(_tree.GetChildId(id, i));
                }
            }

            public void Reload()
            {
                ThrowIfDisposed();
                ValidateData();
                CancelCompletion();
                _byId.Clear();
                _dirty.Clear();
                _foregroundRoots.Clear();
                BuildLayout();
                _vertical.Snap(_verticalTargets);
                _horizontal.Snap(_horizontalTargets);
                _heightFrom = _heightTo;
                SetHeight(_heightTo);
                foreach (var pair in _leased)
                {
                    pair.Value.Reset();
                    _dirty.Add(pair.Key);
                }
                RefreshVisibleViews();
            }

            public void Apply(TreeChangeSet<TId> changes, float? duration)
            {
                ThrowIfDisposed();
                if (changes == null) throw new ArgumentNullException(nameof(changes));
                var seconds = duration ?? _host._animationDuration;
                ValidateNumber(seconds, "AnimationDuration");
                CancelCompletion();
                if (changes.Count == 0) return;
                ValidateData();
                _foregroundRoots.Clear();
                _causeRoots.Clear();
                for (var i = 0; i < changes.Count; i++)
                {
                    var change = changes[i];
                    if (change.Kind != TreeChangeKind.Update ||
                        !_causeRoots.ContainsKey(change.FirstId))
                        _causeRoots[change.FirstId] = change.Kind;
                    if (change.Kind == TreeChangeKind.Swap)
                        _causeRoots[change.SecondId] = TreeChangeKind.Swap;
                    if (change.Kind is TreeChangeKind.Move or TreeChangeKind.Swap)
                        _foregroundRoots.Add(change.FirstId);
                    if (change.Kind == TreeChangeKind.Swap)
                        _foregroundRoots.Add(change.SecondId);
                    if (changes[i].Kind != TreeChangeKind.Remove && changes[i].Kind != TreeChangeKind.Swap)
                        _dirty.Add(changes[i].FirstId);
                }

                BuildLayout();
                _heightFrom = _host._content.rect.height;
                if (seconds == 0f)
                {
                    _vertical.Snap(_verticalTargets);
                    _horizontal.Snap(_horizontalTargets);
                    SetHeight(_heightTo);
                    _heightFrom = _heightTo;
                    _foregroundRoots.Clear();
                    RefreshVisibleViews();
                    CompleteTransition();
                    return;
                }

                _vertical.Retarget(_verticalTargets, seconds, changes);
                _horizontal.Retarget(_horizontalTargets, seconds, changes);
                AssignPresentationCauses();
                RefreshVisibleViews();
            }

            private void AssignPresentationCauses()
            {
                foreach (var pair in _byId)
                {
                    var layout = pair.Value;
                    _vertical.TryGetValue(pair.Key, out var value);
                    if (!value.IsAnimating)
                    {
                        layout.Cause = null;
                        continue;
                    }

                    var found = false;
                    for (var ancestor = layout; ancestor != null; ancestor = ancestor.Parent)
                    {
                        if (!_causeRoots.TryGetValue(ancestor.Row.Id, out var cause)) continue;
                        // An unrelated ancestor update must not replace an ongoing entry/exit cause.
                        if (ancestor != layout && cause == TreeChangeKind.Update) continue;
                        layout.Cause = cause;
                        found = true;
                        break;
                    }

                    if (!found && value.Kind != TreeMotionAnimationKind.Insert &&
                        value.Kind != TreeMotionAnimationKind.Remove)
                        layout.Cause = null;
                }
            }

            public Task ApplyAsync(TreeChangeSet<TId> changes, float? duration, CancellationToken cancellationToken)
            {
                ThrowIfDisposed();
                if (changes == null) throw new ArgumentNullException(nameof(changes));
                ValidateNumber(duration ?? _host._animationDuration, "AnimationDuration");
                if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
                Apply(changes, duration);
                if (changes.Count == 0 || !IsAnimating) return Task.CompletedTask;
                _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                return cancellationToken.CanBeCanceled
                    ? WaitForCompletion(_completion, cancellationToken)
                    : _completion.Task;
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

            public void Refresh(params TId[] ids)
            {
                ThrowIfDisposed();
                foreach (var id in ids)
                {
                    _dirty.Add(id);
                }

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
                    var prefab = _nodePrefabs[row.Id];
                    var definition = _definitions[prefab];
                    var layout = new Layout
                    {
                        Row = row, Item = _tree.GetItem(row.Id), Prefab = prefab, Definition = definition, Order = i,
                        Top = y, Left = _host._rootGroup.ChildrenPadding.left,
                        Right = _host._rootGroup.ChildrenPadding.right
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
                    var rect = definition.Rect;
                    var width = Mathf.Max(0f, regionWidth * (rect.anchorMax.x - rect.anchorMin.x) + rect.sizeDelta.x);
                    var pivotX = layout.Left +
                                 regionWidth * Mathf.LerpUnclamped(rect.anchorMin.x, rect.anchorMax.x, rect.pivot.x) +
                                 rect.anchoredPosition.x;
                    layout.Left = pivotX - width * rect.pivot.x;
                    layout.Right = _layoutWidth - layout.Left - width;

                    if (_byId.TryGetValue(row.Id, out var old))
                    {
                        layout.Cause = old.Cause;
                        if (old.Prefab != prefab || !SameRow(old.Row, row)) _dirty.Add(row.Id);
                    }

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
                        layout.Height = _adapter.GetItemSize(row.Id, layout.Item);
                        ValidateNumber(layout.Height, "ItemSize", true);
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
                    CompleteTransition();
            }

            private void CompleteTransition()
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

                var isAnimating = IsAnimating;
                foreach (var layout in _render)
                {
                    layout.Exiting = !_targets.Contains(layout.Row.Id);
                    layout.Foreground = false;
                    if (!isAnimating || _foregroundRoots.Count == 0) continue;
                    for (var ancestor = layout; ancestor != null; ancestor = ancestor.Parent)
                        if (_foregroundRoots.Contains(ancestor.Row.Id))
                        {
                            layout.Foreground = true;
                            break;
                        }
                }

                SortRender();
                _stale.Clear();
                foreach (var pair in _leased)
                    if (!_renderIds.Contains(pair.Key))
                        _stale.Add(pair.Key);
                foreach (var id in _stale)
                {
                    Release(_leased[id]);
                    _leased.Remove(id);
                }

                var bounds = _host._content.rect;
                _orderedViews.Clear();
                foreach (var layout in _render)
                {
                    var id = layout.Row.Id;
                    var needsBind = _dirty.Remove(id);
                    if (_leased.TryGetValue(id, out var lease) && lease.Prefab != layout.Prefab)
                    {
                        Release(lease);
                        _leased.Remove(id);
                    }

                    if (!_leased.TryGetValue(id, out lease))
                    {
                        if (!_available.TryGetValue(layout.Prefab, out var pool))
                        {
                            pool = new Stack<Lease>();
                            _available.Add(layout.Prefab, pool);
                        }

                        Lease view;
                        if (pool.Count > 0) view = pool.Pop();
                        else
                        {
                            view = new Lease(layout.Prefab, Instantiate(layout.Definition.Prefab,
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
                        _adapter.Bind(lease.GameObject, id, layout.Item, layout.Row);
                    _vertical.TryGetValue(id, out var vertical);
                    _horizontal.TryGetValue(id, out var horizontal);
                    Position(lease.RectTransform, bounds, horizontal.Offset, horizontal.Size, vertical.Offset,
                        vertical.Size);
                    if (lease.GroupView != null)
                        lease.GroupView.SetGeometry(vertical.Size);
                    var role = vertical.Kind == TreeMotionAnimationKind.Remove ? TreeMotionPresentationRole.Exiting :
                        vertical.Kind == TreeMotionAnimationKind.Insert ? TreeMotionPresentationRole.Entering :
                        TreeMotionPresentationRole.Visible;
                    lease.Present(new TreeMotionPresentation(vertical.Kind, role, vertical.Progress,
                        vertical.IsAnimating, vertical.IsAnimating ? layout.Cause : null));
                    _orderedViews.Add(lease);
                    if (!lease.GameObject.activeSelf) lease.GameObject.SetActive(true);
                }

                // All leases exist now; inactive pooled views stay before the visible draw order.
                var firstSibling = _host._content.childCount - _orderedViews.Count;
                for (var i = _orderedViews.Count - 1; i >= 0; i--)
                {
                    var rect = _orderedViews[i].RectTransform;
                    var sibling = firstSibling + i;
                    if (rect.GetSiblingIndex() != sibling) rect.SetSiblingIndex(sibling);
                }
            }

            private void SortRender()
            {
                // Avoid List.Sort's runtime comparer/delegate allocations (visible in Deep Profile).
                // Insertion sort handles small visible sets; heap sort bounds larger sets to O(n log n).
                if (_render.Count <= 16)
                {
                    for (var i = 1; i < _render.Count; i++)
                    {
                        var value = _render[i];
                        var j = i - 1;
                        while (j >= 0 && CompareRender(_render[j], value) > 0)
                        {
                            _render[j + 1] = _render[j];
                            j--;
                        }

                        _render[j + 1] = value;
                    }

                    return;
                }

                for (var i = _render.Count / 2 - 1; i >= 0; i--)
                    SiftRenderDown(i, _render.Count);
                for (var end = _render.Count - 1; end > 0; end--)
                {
                    (_render[0], _render[end]) = (_render[end], _render[0]);
                    SiftRenderDown(0, end);
                }
            }

            private void SiftRenderDown(int root, int count)
            {
                var value = _render[root];
                while (root < count / 2)
                {
                    var child = root * 2 + 1;
                    if (child + 1 < count && CompareRender(_render[child], _render[child + 1]) < 0) child++;
                    if (CompareRender(value, _render[child]) >= 0) break;
                    _render[root] = _render[child];
                    root = child;
                }

                _render[root] = value;
            }

            private void Release(Lease lease)
            {
                lease.GameObject.SetActive(false);
                lease.Reset();
                _available[lease.Prefab].Push(lease);
            }

            private void SetHeight(float height)
            {
                if (_host._content.rect.height != height)
                    _host._content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            }

            private void ThrowIfDisposed()
            {
                if (_disposed) throw new ObjectDisposedException(nameof(TreeMotionBinding<TId>));
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
                _orderedViews.Clear();
                _leased.Clear();
                _available.Clear();
                _foregroundRoots.Clear();
            }
        }
    }
}
