using System.Collections.Generic;
using UnityEngine;

namespace TreeMotion.Samples
{
    public class BasicDemo : MonoBehaviour, ITreeMotionAdapter<int, SampleItem>
    {
        [SerializeField] private TreeMotionScrollView _scrollView;
        [SerializeField] private GameObject _itemPrefab;
        [SerializeField] private GameObject _groupPrefab;

        private const int GroupAId = 1;
        private const int GroupBId = 2;
        private const int Item1Id = 3;
        private const int Item2Id = 4;
        private const int AddedItemStartId = 5;

        private readonly TreeStore<int, SampleItem> _tree = new();
        private readonly Stack<int> _addedIds = new();
        private TreeMotionBinding<int> _binding;
        private int _nextId = AddedItemStartId;

        private void Start()
        {
            _tree.LoadSnapshot(new[]
            {
                new TreeNodeRecord<int, SampleItem>(GroupAId, new SampleItem("Group A", SampleItemType.Group)),
                new TreeNodeRecord<int, SampleItem>(Item1Id, new SampleItem("Item 1"), parentId: GroupAId),
                new TreeNodeRecord<int, SampleItem>(GroupBId, new SampleItem("Group B", SampleItemType.Group)),
                new TreeNodeRecord<int, SampleItem>(Item2Id, new SampleItem("Item 2"), parentId: GroupBId)
            });
            _binding = _scrollView.Bind(_tree, this);
        }

        public GameObject GetItemPrefab(int id, SampleItem item)
            => item.Type == SampleItemType.Group ? _groupPrefab : _itemPrefab;

        public float GetItemSize(int id, SampleItem item) => item.Size;

        public void Bind(GameObject go, int id, SampleItem item, VisibleRow<int> row)
        {
            if (go.TryGetComponent<SampleView>(out var view))
                view.Bind(id, item, OnClick, row.IsExpanded);
        }

        #region Button Events

        private void OnClick(int id)
        {
            if (_tree.GetItem(id).Type != SampleItemType.Group) return;
            _binding.Apply(_tree.BeginUpdate().Expand(id, !_tree.IsExpanded(id)).Commit());
        }

        // Connect these methods to the corresponding buttons in the Inspector.
        // Insert adds to Group A; Remove removes the most recently added item.
        public void Insert()
        {
            if (_binding == null) return;
            var id = _nextId++;
            var changes = _tree.BeginUpdate()
                .Insert(GroupAId, id, new SampleItem($"Added Item {id - AddedItemStartId + 1}"))
                .Commit();
            _addedIds.Push(id);
            _binding.Apply(changes);
        }

        public void Remove()
        {
            if (_binding == null || _addedIds.Count == 0) return;
            var id = _addedIds.Peek();
            var changes = _tree.BeginUpdate().Remove(id).Commit();
            _addedIds.Pop();
            _binding.Apply(changes);
        }

        // Move Item 1 back and forth between Group A and Group B.
        public void Move()
        {
            if (_binding == null) return;
            _tree.TryGetParentId(Item1Id, out var parentId);
            var destinationId = parentId == GroupAId ? GroupBId : GroupAId;
            _binding.Apply(_tree.BeginUpdate().Move(Item1Id, destinationId).Commit());
        }

        // Swap Item 1 and Item 2, including when they share the same Group.
        public void Swap()
        {
            if (_binding == null) return;
            _binding.Apply(_tree.BeginUpdate().Swap(Item1Id, Item2Id).Commit());
        }

        #endregion
    }
}