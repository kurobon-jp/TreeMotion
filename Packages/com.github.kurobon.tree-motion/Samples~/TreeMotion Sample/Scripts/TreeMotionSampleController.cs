using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

namespace TreeMotion.Samples
{
    public sealed class TreeMotionSampleController : MonoBehaviour
    {
        private const float ContentPadding = 18f;
        private const float RootSpacing = 22f;
        private const float DepthInset = 26f;
        private const float FrameInnerPadding = 18f;
        private const float GroupPaddingTop = 14f;
        private const float GroupPaddingBottom = 16f;
        private const float GroupHeaderHeight = 46f;
        private const float ItemHeight = 68f;
        private const float NodeSpacing = 10f;
        private const float AnimationDuration = 0.28f;
        private const string DiagnosticsFileName = "TreeMotionDiagnostics.log";

        [SerializeField] private UnityEngine.UI.ScrollRect _scrollRect;
        [SerializeField] private RectTransform _viewport;
        [SerializeField] private RectTransform _content;
        [FormerlySerializedAs("_rowPrototype")]
        [SerializeField] private TreeMotionSampleRow _itemPrefab;
        [SerializeField] private UnityEngine.UI.Image _groupFramePrefab;
        [SerializeField] private UnityEngine.UI.Button _addRootButton;
        [SerializeField] private UnityEngine.UI.Button _addChildButton;
        [SerializeField] private UnityEngine.UI.Button _toggleButton;
        [SerializeField] private UnityEngine.UI.Button _moveButton;
        [SerializeField] private UnityEngine.UI.Button _removeButton;
        [SerializeField] private TextMeshProUGUI _statusLabel;

        private readonly TreeStore<int, string> _tree = new TreeStore<int, string>();
        private readonly List<NodeLayout> _nodeLayouts = new List<NodeLayout>();
        private readonly List<GroupLayout> _groupLayouts = new List<GroupLayout>();
        private readonly List<TreeMotionSampleRow> _rowPool = new List<TreeMotionSampleRow>();
        private readonly List<UnityEngine.UI.Image> _groupFramePool =
            new List<UnityEngine.UI.Image>();
        private readonly List<CanvasGroup> _groupFrameCanvasGroups = new List<CanvasGroup>();
        private readonly List<TreeMotionLayout<int>> _nodeTargets =
            new List<TreeMotionLayout<int>>();
        private readonly List<TreeMotionLayout<int>> _groupTargets =
            new List<TreeMotionLayout<int>>();
        private readonly Dictionary<int, NodeLayout> _nodeVisuals =
            new Dictionary<int, NodeLayout>();
        private readonly Dictionary<int, GroupLayout> _groupVisuals =
            new Dictionary<int, GroupLayout>();
        private readonly List<int> _staleIds = new List<int>();
        private readonly List<int> _renderIds = new List<int>();
        private readonly List<int> _previousRenderedNodeIds = new List<int>();
        private readonly List<int> _previousRenderedGroupIds = new List<int>();
        private readonly HashSet<int> _renderIdSet = new HashSet<int>();
        private readonly TreeMotionAnimation<int> _nodeAnimation = new TreeMotionAnimation<int>();
        private readonly TreeMotionAnimation<int> _groupAnimation = new TreeMotionAnimation<int>();
        private int _nextId = 1;
        private int _selectedId = -1;
        private int _lastClickedId = -1;
        private float _lastClickTime = float.NegativeInfinity;
        private float _contentHeightFrom;
        private float _contentHeightTo;
        private string _lastOperation = "Select a card, then try the controls.";
        private string _diagnosticsPath;

        public void Configure(UnityEngine.UI.ScrollRect scrollRect, RectTransform viewport,
            RectTransform content, TreeMotionSampleRow itemPrefab,
            UnityEngine.UI.Image groupFramePrefab, UnityEngine.UI.Button addRootButton,
            UnityEngine.UI.Button addChildButton, UnityEngine.UI.Button toggleButton,
            UnityEngine.UI.Button moveButton, UnityEngine.UI.Button removeButton, TextMeshProUGUI statusLabel)
        {
            _scrollRect = scrollRect;
            _viewport = viewport;
            _content = content;
            _itemPrefab = itemPrefab;
            _groupFramePrefab = groupFramePrefab;
            _addRootButton = addRootButton;
            _addChildButton = addChildButton;
            _toggleButton = toggleButton;
            _moveButton = moveButton;
            _removeButton = removeButton;
            _statusLabel = statusLabel;
        }

        private void Awake()
        {
            EnsureInputModule();
            if (_itemPrefab == null || _groupFramePrefab == null)
                throw new InvalidOperationException("TreeMotion sample prefabs are not assigned.");
            InitializeDiagnostics();
            _addRootButton.onClick.AddListener(AddRoot);
            _addChildButton.onClick.AddListener(AddChild);
            _toggleButton.onClick.AddListener(ToggleSelected);
            _moveButton.onClick.AddListener(MoveSelectedToRoot);
            _removeButton.onClick.AddListener(RemoveSelected);
            _scrollRect.onValueChanged.AddListener(_ => RefreshVisibleViews());

            BuildInitialTree();
            RebuildLayout();
            _nodeAnimation.Snap(_nodeTargets);
            _groupAnimation.Snap(_groupTargets);
            _contentHeightFrom = _contentHeightTo;
            SetContentHeight(_contentHeightTo);
            Canvas.ForceUpdateCanvases();
            RefreshVisibleViews();
        }

        private void Update()
        {
            if (!_nodeAnimation.IsAnimating && !_groupAnimation.IsAnimating)
                return;

            _nodeAnimation.Advance(Time.unscaledDeltaTime);
            _groupAnimation.Advance(Time.unscaledDeltaTime);
            var progress = _nodeAnimation.Progress;
            var eased = progress * progress * (3f - 2f * progress);
            SetContentHeight(Mathf.LerpUnclamped(_contentHeightFrom, _contentHeightTo, eased));
            RefreshVisibleViews();

            if (!_nodeAnimation.IsAnimating && !_groupAnimation.IsAnimating)
                RemoveStaleVisuals();
        }

        private void Start()
        {
            Canvas.ForceUpdateCanvases();
            _scrollRect.verticalNormalizedPosition = 1f;
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, 0f);
            RefreshVisibleViews();
        }

        internal void Select(int id)
        {
            var now = Time.unscaledTime;
            if (_lastClickedId == id && now - _lastClickTime <= 0.35f)
            {
                _lastClickedId = -1;
                _lastClickTime = float.NegativeInfinity;
                SwapWithNext(id);
                return;
            }
            _lastClickedId = id;
            _lastClickTime = now;
            _selectedId = id;
            _lastOperation = $"Selected '{_tree.GetItem(id)}'.";
            RefreshVisibleViews();
        }

        internal void Toggle(int id)
        {
            if (_tree.GetChildCount(id) == 0)
                return;
            var expanded = !_tree.IsExpanded(id);
            var changes = _tree.BeginUpdate().SetExpanded(id, expanded).Commit();
            _lastOperation = expanded ? "Expanded the selected group." : "Collapsed the selected group.";
            RefreshAfterStructureChange(changes);
        }

        private void AddRoot()
        {
            var id = _nextId++;
            var changes = _tree.BeginUpdate().InsertRoot(id, $"New root {id}").Commit();
            _selectedId = id;
            _lastOperation = "Inserted a root node.";
            RefreshAfterStructureChange(changes);
        }

        private void AddChild()
        {
            if (_selectedId < 0 || !_tree.Contains(_selectedId))
            {
                _lastOperation = "Select a parent before adding a child.";
                UpdateStatus();
                return;
            }

            var id = _nextId++;
            var changes = _tree.BeginUpdate()
                .SetExpanded(_selectedId, true)
                .Insert(_selectedId, id, $"Child {id}")
                .Commit();
            _selectedId = id;
            _lastOperation = "Inserted a child and expanded its parent.";
            RefreshAfterStructureChange(changes);
        }

        private void ToggleSelected()
        {
            if (_selectedId >= 0 && _tree.Contains(_selectedId))
                Toggle(_selectedId);
        }

        private void MoveSelectedToRoot()
        {
            if (_selectedId < 0 || !_tree.Contains(_selectedId))
            {
                _lastOperation = "Select a node before moving it.";
                UpdateStatus();
                return;
            }

            var changes = _tree.BeginUpdate().MoveToRoot(_selectedId).Commit();
            _lastOperation = "Moved the selected subtree to the root level.";
            RefreshAfterStructureChange(changes);
        }

        private void RemoveSelected()
        {
            if (_selectedId < 0 || !_tree.Contains(_selectedId))
            {
                _lastOperation = "Select a node before removing it.";
                UpdateStatus();
                return;
            }

            var changes = _tree.BeginUpdate().Remove(_selectedId).Commit();
            _selectedId = -1;
            _lastOperation = "Removed the selected subtree.";
            RefreshAfterStructureChange(changes);
        }

        private void SwapWithNext(int id)
        {
            if (!_tree.Contains(id) || _tree.VisibleCount < 2)
                return;

            var firstIndex = _tree.IndexOfVisible(id);
            var secondId = -1;
            for (var offset = 1; offset < _tree.VisibleCount; offset++)
            {
                var candidateIndex = (firstIndex + offset) % _tree.VisibleCount;
                var candidateId = _tree.GetVisibleRow(candidateIndex).Id;
                if (IsAncestor(id, candidateId) || IsAncestor(candidateId, id))
                    continue;
                secondId = candidateId;
                break;
            }

            if (secondId < 0)
            {
                _lastOperation = $"No unrelated node can be swapped with {id}.";
                UpdateStatus();
                return;
            }

            var changes = _tree.BeginUpdate()
                .SwapNodes(id, 20)
                .Commit();
            _selectedId = id;
            _lastOperation = $"Swapped nodes {id} and {secondId}.";
            RefreshAfterStructureChange(changes);
        }

        private bool IsAncestor(int possibleAncestorId, int nodeId)
        {
            var currentId = nodeId;
            while (_tree.TryGetParentId(currentId, out var parentId))
            {
                if (parentId == possibleAncestorId)
                    return true;
                currentId = parentId;
            }
            return false;
        }

        private void BuildInitialTree()
        {
            var records = new[]
            {
                TreeNodeRecord<int, string>.Child(8, 7, "Nested Item 1", 0),
                TreeNodeRecord<int, string>.Child(3, 1, "Item 2", 1),
                TreeNodeRecord<int, string>.Root(5, "Group 2", 1, true),
                TreeNodeRecord<int, string>.Child(6, 5, "Item 4", 0),
                TreeNodeRecord<int, string>.Root(1, "Group 1", 0, true),
                TreeNodeRecord<int, string>.Child(10, 5, "Item 5", 2),
                TreeNodeRecord<int, string>.Child(7, 5, "Nested Group", 1, true),
                TreeNodeRecord<int, string>.Child(2, 1, "Item 1", 0),
                TreeNodeRecord<int, string>.Child(9, 7, "Nested Item 2", 1),
                TreeNodeRecord<int, string>.Child(4, 1, "Item 3", 2),
                TreeNodeRecord<int, string>.Root(11, "Group 11", 2, true),
                TreeNodeRecord<int, string>.Root(12, "Group 11", 3, true),
                TreeNodeRecord<int, string>.Root(13, "Group 11", 4, true),
                TreeNodeRecord<int, string>.Root(14, "Group 11", 5, true),
                TreeNodeRecord<int, string>.Root(15, "Group 11", 6, true),
                TreeNodeRecord<int, string>.Root(16, "Group 11",7, true),
                TreeNodeRecord<int, string>.Root(17, "Group 11", 8, true),
                TreeNodeRecord<int, string>.Root(18, "Group 11", 9, true),
                TreeNodeRecord<int, string>.Root(19, "Group 11", 10, true),
                TreeNodeRecord<int, string>.Root(20, "Group 11", 11, true),
            };

            _tree.LoadSnapshot(records);
            _nextId = 11;
            _selectedId = 1;
        }

        private void RefreshAfterStructureChange(TreeChangeSet<int> changes)
        {
            RebuildLayout();
            _contentHeightFrom = _content.rect.height;
            _nodeAnimation.Retarget(_nodeTargets, AnimationDuration, changes);
            _groupAnimation.Retarget(_groupTargets, AnimationDuration);
            Canvas.ForceUpdateCanvases();
            RefreshVisibleViews();
        }

        private void RebuildLayout()
        {
            _nodeLayouts.Clear();
            _groupLayouts.Clear();
            _nodeTargets.Clear();
            _groupTargets.Clear();

            var y = ContentPadding;
            for (var i = 0; i < _tree.RootCount; i++)
            {
                LayoutNode(_tree.GetRootId(i), 0, ref y);
                if (i + 1 < _tree.RootCount)
                    y += RootSpacing;
            }

            y += ContentPadding;
            _contentHeightTo = Mathf.Max(0f, y);
        }

        private void LayoutNode(int id, int depth, ref float y)
        {
            var isGroup = _tree.GetChildCount(id) > 0;
            if (!isGroup)
            {
                AddNodeLayout(new NodeLayout(id, depth, y, ItemHeight, false,
                    _tree.GetItem(id)));
                y += ItemHeight + NodeSpacing;
                return;
            }

            var frameIndex = _groupLayouts.Count;
            var frameTop = y;
            _groupLayouts.Add(default);
            y += GroupPaddingTop;
            AddNodeLayout(new NodeLayout(id, depth, y, GroupHeaderHeight, true,
                _tree.GetItem(id)));
            y += GroupHeaderHeight + NodeSpacing;

            if (_tree.IsExpanded(id))
            {
                var childCount = _tree.GetChildCount(id);
                for (var i = 0; i < childCount; i++)
                    LayoutNode(_tree.GetChildId(id, i), depth + 1, ref y);
            }

            y += GroupPaddingBottom;
            var groupLayout = new GroupLayout(id, depth, frameTop, y - frameTop);
            _groupLayouts[frameIndex] = groupLayout;
            _groupTargets.Add(new TreeMotionLayout<int>(id, frameTop, groupLayout.Height));
            _groupVisuals[id] = groupLayout;
            y += NodeSpacing;
        }

        private void AddNodeLayout(NodeLayout layout)
        {
            _nodeLayouts.Add(layout);
            _nodeTargets.Add(new TreeMotionLayout<int>(layout.Id, layout.Top, layout.Height));
            _nodeVisuals[layout.Id] = layout;
        }

        private void RefreshVisibleViews()
        {
            var scrollTop = Mathf.Max(0f, _content.anchoredPosition.y);
            var viewportHeight = Mathf.Max(0f, _viewport.rect.height);
            var visibleTop = scrollTop;
            var visibleBottom = scrollTop + viewportHeight;

            CollectVisibleGroupIds(visibleTop, visibleBottom);
            var visibleFrameCount = 0;
            for (var i = 0; i < _renderIds.Count; i++)
            {
                var id = _renderIds[i];
                if (!_groupAnimation.TryGetValue(id, out var value) ||
                    !_groupVisuals.TryGetValue(id, out var layout))
                    continue;

                EnsureGroupFramePool(visibleFrameCount + 1);
                BindGroupFrame(_groupFramePool[visibleFrameCount],
                    _groupFrameCanvasGroups[visibleFrameCount], layout, value, visibleFrameCount);
                visibleFrameCount++;
            }

            for (var i = visibleFrameCount; i < _groupFramePool.Count; i++)
                _groupFramePool[i].gameObject.SetActive(false);
            StoreRenderedIds(_previousRenderedGroupIds);

            CollectVisibleNodeIds(visibleTop, visibleBottom);
            var visibleRowCount = 0;
            for (var i = 0; i < _renderIds.Count; i++)
            {
                var id = _renderIds[i];
                if (!_nodeAnimation.TryGetValue(id, out var value) ||
                    !_nodeVisuals.TryGetValue(id, out var layout))
                    continue;

                EnsureRowPool(visibleRowCount + 1);
                var role = value.Kind == TreeMotionAnimationKind.Insert
                    ? TreeMotionPresentationRole.Entering
                    : value.Kind == TreeMotionAnimationKind.Remove
                        ? TreeMotionPresentationRole.Exiting
                        : TreeMotionPresentationRole.Current;
                BindRow(_rowPool[visibleRowCount], layout, value, role);
                visibleRowCount++;
            }

            for (var i = visibleRowCount; i < _rowPool.Count; i++)
                _rowPool[i].gameObject.SetActive(false);
            StoreRenderedIds(_previousRenderedNodeIds);

            UpdateStatus();
            WriteDiagnostics(scrollTop, viewportHeight, visibleTop, visibleBottom,
                visibleFrameCount, visibleRowCount);
        }

        private void InitializeDiagnostics()
        {
            _diagnosticsPath = Path.Combine(Application.persistentDataPath, DiagnosticsFileName);
            try
            {
                File.WriteAllText(_diagnosticsPath,
                    $"TreeMotion diagnostics started {DateTime.Now:O}{Environment.NewLine}");
                Debug.Log($"TreeMotion diagnostics: {_diagnosticsPath}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not initialize TreeMotion diagnostics: {exception.Message}");
                _diagnosticsPath = null;
            }
        }

        private void WriteDiagnostics(float scrollTop, float viewportHeight, float visibleTop,
            float visibleBottom, int visibleFrameCount, int visibleRowCount)
        {
            if (string.IsNullOrEmpty(_diagnosticsPath))
                return;

            var text = new StringBuilder(2048);
            text.AppendLine();
            text.AppendLine($"[{DateTime.Now:O}] frame={Time.frameCount}");
            text.AppendLine($"scrollTop={scrollTop:F2} viewportHeight={viewportHeight:F2} " +
                            $"viewport=[{scrollTop:F2},{scrollTop + viewportHeight:F2})");
            text.AppendLine($"overscan=0.00 renderRange=[{visibleTop:F2},{visibleBottom:F2}) " +
                            $"contentHeight={_content.rect.height:F2} targetContentHeight={_contentHeightTo:F2}");
            text.AppendLine($"nodeAnimation={_nodeAnimation.IsAnimating} " +
                            $"groupAnimation={_groupAnimation.IsAnimating} " +
                            $"renderedRows={visibleRowCount} pooledRows={_rowPool.Count} " +
                            $"renderedFrames={visibleFrameCount} pooledFrames={_groupFramePool.Count}");

            text.Append("renderedNodeIds=");
            for (var i = 0; i < _previousRenderedNodeIds.Count; i++)
            {
                if (i > 0)
                    text.Append(',');
                text.Append(_previousRenderedNodeIds[i]);
            }
            text.AppendLine();

            for (var i = 0; i < _rowPool.Count; i++)
            {
                var row = _rowPool[i];
                var id = i < _previousRenderedNodeIds.Count
                    ? _previousRenderedNodeIds[i].ToString()
                    : "-";
                var rect = row.RectTransform;
                var top = -rect.offsetMax.y;
                var bottom = -rect.offsetMin.y;
                var overlapsViewport = bottom > scrollTop && top < scrollTop + viewportHeight;
                var overlapsRenderRange = bottom > visibleTop && top < visibleBottom;
                text.AppendLine($"{row.name} id={id} active={row.gameObject.activeSelf} " +
                                $"range=[{top:F2},{bottom:F2}) viewport={overlapsViewport} " +
                                $"renderRange={overlapsRenderRange}");
            }

            try
            {
                File.AppendAllText(_diagnosticsPath, text.ToString());
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not write TreeMotion diagnostics: {exception.Message}");
                _diagnosticsPath = null;
            }
        }

        private void BindRow(TreeMotionSampleRow rowView, NodeLayout layout,
            TreeMotionAnimationValue<int> value, TreeMotionPresentationRole role)
        {
            rowView.gameObject.SetActive(true);
            var isExpanded = layout.IsGroup && _tree.Contains(layout.Id) &&
                             _tree.IsExpanded(layout.Id);
            rowView.Bind(layout.Id, layout.Label, layout.IsGroup, isExpanded,
                layout.Id == _selectedId);
            PositionRow(rowView.RectTransform, layout.Depth, value.Offset, value.Size);
            rowView.SetPresentation(new TreeMotionPresentation(value.Kind, role,
                value.Progress, value.IsAnimating));
            rowView.transform.SetAsLastSibling();
        }

        private void CollectVisibleNodeIds(float visibleTop, float visibleBottom)
        {
            BeginRenderIds();
            var first = FindFirstVisibleNode(visibleTop);
            for (var i = first; i < _nodeLayouts.Count; i++)
            {
                var layout = _nodeLayouts[i];
                if (layout.Top >= visibleBottom)
                    break;
                AddRenderId(layout.Id);
            }
            AddPreviouslyRenderedIds(_previousRenderedNodeIds, _nodeAnimation,
                visibleTop, visibleBottom);
        }

        private void CollectVisibleGroupIds(float visibleTop, float visibleBottom)
        {
            BeginRenderIds();
            var first = FindFirstVisibleGroup(visibleTop);
            for (var i = first; i < _groupLayouts.Count; i++)
            {
                var layout = _groupLayouts[i];
                if (layout.Top >= visibleBottom)
                    break;
                AddRenderId(layout.Id);
            }
            AddPreviouslyRenderedIds(_previousRenderedGroupIds, _groupAnimation,
                visibleTop, visibleBottom);
        }

        private void BeginRenderIds()
        {
            _renderIds.Clear();
            _renderIdSet.Clear();
        }

        private void AddRenderId(int id)
        {
            if (_renderIdSet.Add(id))
                _renderIds.Add(id);
        }

        private void AddPreviouslyRenderedIds(List<int> previous,
            TreeMotionAnimation<int> animation, float visibleTop, float visibleBottom)
        {
            if (!animation.IsAnimating)
                return;
            for (var i = 0; i < previous.Count; i++)
            {
                var id = previous[i];
                if (animation.TryGetValue(id, out var value) &&
                    value.Offset + value.Size > visibleTop && value.Offset < visibleBottom)
                    AddRenderId(id);
            }
        }

        private void StoreRenderedIds(List<int> destination)
        {
            destination.Clear();
            destination.AddRange(_renderIds);
        }

        private int FindFirstVisibleNode(float visibleTop)
        {
            var low = 0;
            var high = _nodeLayouts.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (_nodeLayouts[middle].Bottom <= visibleTop)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        private int FindFirstVisibleGroup(float visibleTop)
        {
            var low = 0;
            var high = _groupLayouts.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (_groupLayouts[middle].Bottom <= visibleTop)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        private void EnsureRowPool(int count)
        {
            while (_rowPool.Count < count)
            {
                var row = Instantiate(_itemPrefab, _content);
                row.name = $"PooledCard{_rowPool.Count}";
                row.Initialize(this);
                _rowPool.Add(row);
            }
        }

        private void EnsureGroupFramePool(int count)
        {
            while (_groupFramePool.Count < count)
            {
                var frame = Instantiate(_groupFramePrefab, _content);
                frame.name = $"PooledGroupFrame{_groupFramePool.Count}";
                _groupFramePool.Add(frame);
                var canvasGroup = frame.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                    canvasGroup = frame.gameObject.AddComponent<CanvasGroup>();
                _groupFrameCanvasGroups.Add(canvasGroup);
            }
        }

        private void BindGroupFrame(UnityEngine.UI.Image frame, CanvasGroup canvasGroup,
            GroupLayout layout, TreeMotionAnimationValue<int> value, int siblingIndex)
        {
            frame.gameObject.SetActive(true);
            frame.color = layout.Depth % 2 == 0
                ? new Color(0.05f, 0.62f, 0.63f, 0.96f)
                : new Color(0.06f, 0.45f, 0.55f, 0.98f);
            var inset = ContentPadding + layout.Depth * DepthInset;
            PositionRect(frame.rectTransform, inset, inset, value.Offset, value.Size);
            canvasGroup.alpha = value.Kind == TreeMotionAnimationKind.Insert
                ? value.Progress
                : value.Kind == TreeMotionAnimationKind.Remove
                    ? 1f - value.Progress
                    : 1f;
            canvasGroup.blocksRaycasts = false;
            frame.rectTransform.SetSiblingIndex(siblingIndex);
        }

        private static void PositionRow(RectTransform rect, int depth, float top, float height)
        {
            var frameInset = ContentPadding + depth * DepthInset;
            var inset = frameInset + FrameInnerPadding;
            PositionRect(rect, inset, inset, top, height);
        }

        private void SetContentHeight(float height)
            => _content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

        private void RemoveStaleVisuals()
        {
            _staleIds.Clear();
            foreach (var pair in _nodeVisuals)
            {
                if (!_tree.Contains(pair.Key))
                    _staleIds.Add(pair.Key);
            }
            for (var i = 0; i < _staleIds.Count; i++)
                _nodeVisuals.Remove(_staleIds[i]);

            _staleIds.Clear();
            foreach (var pair in _groupVisuals)
            {
                if (!_tree.Contains(pair.Key) || _tree.GetChildCount(pair.Key) == 0)
                    _staleIds.Add(pair.Key);
            }
            for (var i = 0; i < _staleIds.Count; i++)
                _groupVisuals.Remove(_staleIds[i]);
        }

        private static void PositionRect(RectTransform rect, float left, float right, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private void UpdateStatus()
        {
            _statusLabel.text = $"{_tree.Count} nodes / {_nodeLayouts.Count} visible / " +
                                $"{_rowPool.Count} pooled cards\n{_lastOperation}";
        }

        private static void EnsureInputModule()
        {
            var eventSystem = UnityEngine.Object.FindObjectOfType<EventSystem>();
            if (eventSystem == null || eventSystem.GetComponent<BaseInputModule>() != null)
                return;

            var inputSystemType = Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSystemType != null)
                eventSystem.gameObject.AddComponent(inputSystemType);
            else
                eventSystem.gameObject.AddComponent<StandaloneInputModule>();
        }

        private readonly struct NodeLayout
        {
            internal readonly int Id;
            internal readonly int Depth;
            internal readonly float Top;
            internal readonly float Height;
            internal readonly bool IsGroup;
            internal readonly string Label;
            internal float Bottom => Top + Height;

            internal NodeLayout(int id, int depth, float top, float height, bool isGroup,
                string label)
            {
                Id = id;
                Depth = depth;
                Top = top;
                Height = height;
                IsGroup = isGroup;
                Label = label;
            }
        }

        private readonly struct GroupLayout
        {
            internal readonly int Id;
            internal readonly int Depth;
            internal readonly float Top;
            internal readonly float Height;
            internal float Bottom => Top + Height;

            internal GroupLayout(int id, int depth, float top, float height)
            {
                Id = id;
                Depth = depth;
                Top = top;
                Height = height;
            }
        }
    }
}
