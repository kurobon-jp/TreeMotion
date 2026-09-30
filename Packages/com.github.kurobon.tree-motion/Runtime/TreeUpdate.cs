using System;
using System.Collections.Generic;

namespace TreeMotion
{
    public sealed class TreeUpdate<TId, TItem>
    {
        private readonly TreeStore<TId, TItem> _store;
        private readonly List<TreeMutation<TId, TItem>> _mutations = new List<TreeMutation<TId, TItem>>();
        private bool _committed;

        internal TreeUpdate(TreeStore<TId, TItem> store)
        {
            _store = store;
        }

        public TreeUpdate<TId, TItem> InsertRoot(TId id, TItem item, int index = -1, bool isExpanded = false)
        {
            Add(TreeMutation<TId, TItem>.InsertRoot(id, item, index, isExpanded));
            return this;
        }

        public TreeUpdate<TId, TItem> Insert(TId parentId, TId id, TItem item, int index = -1,
            bool isExpanded = false)
        {
            Add(TreeMutation<TId, TItem>.Insert(parentId, id, item, index, isExpanded));
            return this;
        }

        public TreeUpdate<TId, TItem> Remove(TId id)
        {
            Add(TreeMutation<TId, TItem>.Remove(id));
            return this;
        }

        public TreeUpdate<TId, TItem> MoveToRoot(TId id, int index = -1)
        {
            Add(TreeMutation<TId, TItem>.MoveToRoot(id, index));
            return this;
        }

        public TreeUpdate<TId, TItem> Move(TId id, TId parentId, int index = -1)
        {
            Add(TreeMutation<TId, TItem>.Move(id, parentId, index));
            return this;
        }

        public TreeUpdate<TId, TItem> SetExpanded(TId id, bool isExpanded)
        {
            Add(TreeMutation<TId, TItem>.SetExpanded(id, isExpanded));
            return this;
        }

        public TreeUpdate<TId, TItem> UpdateItem(TId id, TItem item)
        {
            Add(TreeMutation<TId, TItem>.UpdateItem(id, item));
            return this;
        }

        public TreeUpdate<TId, TItem> SwapNodes(TId firstId, TId secondId)
        {
            Add(TreeMutation<TId, TItem>.SwapNodes(firstId, secondId));
            return this;
        }

        public TreeChangeSet<TId> Commit()
        {
            if (_committed)
                throw new InvalidOperationException("This update has already been committed.");

            _committed = true;
            return _store.Apply(_mutations);
        }

        private void Add(TreeMutation<TId, TItem> mutation)
        {
            if (_committed)
                throw new InvalidOperationException("A committed update cannot be modified.");
            _mutations.Add(mutation);
        }
    }

    internal enum TreeMutationKind
    {
        Insert,
        Remove,
        Move,
        SetExpanded,
        UpdateItem,
        SwapNodes
    }

    internal readonly struct TreeMutation<TId, TItem>
    {
        internal TreeMutationKind Kind { get; }
        internal TId Id { get; }
        internal TId ParentId { get; }
        internal TId OtherId { get; }
        internal TItem Item { get; }
        internal int Index { get; }
        internal bool HasParent { get; }
        internal bool BoolValue { get; }

        private TreeMutation(TreeMutationKind kind, TId id, TId parentId, TId otherId,
            TItem item, int index, bool hasParent, bool boolValue)
        {
            Kind = kind;
            Id = id;
            ParentId = parentId;
            OtherId = otherId;
            Item = item;
            Index = index;
            HasParent = hasParent;
            BoolValue = boolValue;
        }

        internal static TreeMutation<TId, TItem> InsertRoot(TId id, TItem item, int index, bool isExpanded)
            => new TreeMutation<TId, TItem>(TreeMutationKind.Insert, id, default, default, item,
                index, false, isExpanded);

        internal static TreeMutation<TId, TItem> Insert(TId parentId, TId id, TItem item, int index,
            bool isExpanded)
            => new TreeMutation<TId, TItem>(TreeMutationKind.Insert, id, parentId, default, item,
                index, true, isExpanded);

        internal static TreeMutation<TId, TItem> Remove(TId id)
            => new TreeMutation<TId, TItem>(TreeMutationKind.Remove, id, default, default, default,
                -1, false, false);

        internal static TreeMutation<TId, TItem> MoveToRoot(TId id, int index)
            => new TreeMutation<TId, TItem>(TreeMutationKind.Move, id, default, default, default,
                index, false, false);

        internal static TreeMutation<TId, TItem> Move(TId id, TId parentId, int index)
            => new TreeMutation<TId, TItem>(TreeMutationKind.Move, id, parentId, default, default,
                index, true, false);

        internal static TreeMutation<TId, TItem> SetExpanded(TId id, bool isExpanded)
            => new TreeMutation<TId, TItem>(TreeMutationKind.SetExpanded, id, default, default,
                default, -1, false, isExpanded);

        internal static TreeMutation<TId, TItem> UpdateItem(TId id, TItem item)
            => new TreeMutation<TId, TItem>(TreeMutationKind.UpdateItem, id, default, default, item,
                -1, false, false);

        internal static TreeMutation<TId, TItem> SwapNodes(TId firstId, TId secondId)
            => new TreeMutation<TId, TItem>(TreeMutationKind.SwapNodes, firstId, default,
                secondId, default, -1, false, false);
    }
}
