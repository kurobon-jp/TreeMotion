using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TreeMotion.Samples
{
    public sealed class TreeMotionSampleController : MonoBehaviour, ITreeMotionDataSource<int, string>
    {
        [SerializeField] private TreeMotionScrollView _scrollView;
        [SerializeField] private TreeMotionSampleRow _itemPrefab;
        [SerializeField] private TreeMotionGroupView _groupPrefab;
        [SerializeField] private UnityEngine.UI.Button _addRootButton;
        [SerializeField] private UnityEngine.UI.Button _addChildButton;
        [SerializeField] private UnityEngine.UI.Button _toggleButton;
        [SerializeField] private UnityEngine.UI.Button _moveButton;
        [SerializeField] private UnityEngine.UI.Button _removeButton;
        [SerializeField] private TextMeshProUGUI _statusLabel;
        private readonly TreeStore<int, string> _tree = new TreeStore<int, string>();
        private readonly HashSet<int> _groupIds = new HashSet<int>();
        private TreeMotionViewController<int> _view;
        private int _nextId = 1, _selectedId = -1, _lastClickedId = -1;
        private int _presentedSelectedId = -1;
        private float _lastClickTime = float.NegativeInfinity;
        private string _lastOperation = "Select a card, then try the controls.";

        private void Start()
        {
            _addRootButton.onClick.AddListener(AddRoot);
            _addChildButton.onClick.AddListener(AddChild);
            _toggleButton.onClick.AddListener(ToggleSelected);
            _moveButton.onClick.AddListener(MoveSelectedToRoot);
            _removeButton.onClick.AddListener(RemoveSelected);
            BuildInitialTree();
            _view = _scrollView.SetDataSource(_tree, this);
            _presentedSelectedId = _selectedId;
            UpdateStatus();
        }

        public int GetItemType(int id, string item) => _groupIds.Contains(id) ? 1 : 0;
        public GameObject GetItemPrefab(int itemType) => itemType == 0 ? _itemPrefab.gameObject : _groupPrefab.gameObject;
        public float GetItemHeight(int itemType) => itemType == 1 ? 46f : 68f;
        public void Bind(GameObject view, int id, string item, VisibleRow<int> row)
        {
            var card = view.TryGetComponent<TreeMotionGroupView>(out var group) ? group.GetComponentInChildren<TreeMotionSampleRow>() : view.GetComponent<TreeMotionSampleRow>();
            card.Initialize(this);
            card.Bind(id, item, group != null, row.IsExpanded, id == _selectedId);
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
            var previous = _selectedId;
            _selectedId = id;
            if (previous >= 0 && _tree.Contains(previous)) _view.Refresh(previous);
            _view.Refresh(id);
            _presentedSelectedId = _selectedId;
            _lastOperation = $"Selected '{_tree.GetItem(id)}'.";
            UpdateStatus();
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
            _groupIds.Add(_selectedId);
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
                .SwapNodes(id, secondId)
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
                new TreeNodeRecord<int, string>(8, "Nested Item 1", parentId: 7, siblingIndex: 0),
                new TreeNodeRecord<int, string>(3, "Item 2", parentId: 1, siblingIndex: 1),
                new TreeNodeRecord<int, string>(5, "Group 2", siblingIndex: 1, isExpanded: true),
                new TreeNodeRecord<int, string>(6, "Item 4", parentId: 5, siblingIndex: 0),
                new TreeNodeRecord<int, string>(1, "Group 1", siblingIndex: 0, isExpanded: true),
                new TreeNodeRecord<int, string>(10, "Item 5", parentId: 5, siblingIndex: 2),
                new TreeNodeRecord<int, string>(7, "Nested Group", parentId: 5, siblingIndex: 1, isExpanded: true),
                new TreeNodeRecord<int, string>(2, "Item 1", parentId: 1, siblingIndex: 0),
                new TreeNodeRecord<int, string>(9, "Nested Item 2", parentId: 7, siblingIndex: 1),
                new TreeNodeRecord<int, string>(4, "Item 3", parentId: 1, siblingIndex: 2),
                new TreeNodeRecord<int, string>(11, "Group 11", siblingIndex: 2, isExpanded: true),
                new TreeNodeRecord<int, string>(12, "Group 11", siblingIndex: 3, isExpanded: true),
                new TreeNodeRecord<int, string>(13, "Group 11", siblingIndex: 4, isExpanded: true),
                new TreeNodeRecord<int, string>(14, "Group 11", siblingIndex: 5, isExpanded: true),
                new TreeNodeRecord<int, string>(15, "Group 11", siblingIndex: 6, isExpanded: true),
                new TreeNodeRecord<int, string>(16, "Group 11", siblingIndex: 7, isExpanded: true),
                new TreeNodeRecord<int, string>(17, "Group 11", siblingIndex: 8, isExpanded: true),
                new TreeNodeRecord<int, string>(18, "Group 11", siblingIndex: 9, isExpanded: true),
                new TreeNodeRecord<int, string>(19, "Group 11", siblingIndex: 10, isExpanded: true),
                new TreeNodeRecord<int, string>(20, "Group 11", siblingIndex: 11, isExpanded: true),
            };

            _tree.LoadSnapshot(records);
            foreach (var record in records)
                if (!record.HasParent || _tree.GetChildCount(record.Id) > 0) _groupIds.Add(record.Id);
            _nextId = 21;
            _selectedId = 1;
        }

        private void RefreshAfterStructureChange(TreeChangeSet<int> changes)
        {
            _view.Apply(changes);
            if (_presentedSelectedId != _selectedId)
            {
                if (_tree.Contains(_presentedSelectedId)) _view.Refresh(_presentedSelectedId);
                if (_tree.Contains(_selectedId)) _view.Refresh(_selectedId);
                _presentedSelectedId = _selectedId;
            }
            UpdateStatus();
        }
        private void UpdateStatus()
            => _statusLabel.text = $"{_tree.Count} nodes / {_tree.VisibleCount} visible\n{_lastOperation}";
    }
}
