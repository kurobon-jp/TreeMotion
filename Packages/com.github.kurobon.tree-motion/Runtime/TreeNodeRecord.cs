using System;

namespace TreeMotion
{
    /// <summary>
    /// A flat snapshot record suitable for mapping API or master data into a TreeStore.
    /// Records may be supplied in any order; SiblingIndex defines root and child ordering.
    /// </summary>
    public readonly struct TreeNodeRecord<TId, TItem>
    {
        public TId Id { get; }
        public TId ParentId { get; }
        public bool HasParent { get; }
        public int SiblingIndex { get; }
        public TItem Item { get; }
        public bool IsExpanded { get; }

        private TreeNodeRecord(TId id, TId parentId, bool hasParent, TItem item,
            int siblingIndex, bool isExpanded)
        {
            if (id is null)
                throw new ArgumentNullException(nameof(id));
            if (hasParent && parentId is null)
                throw new ArgumentNullException(nameof(parentId));
            if (siblingIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(siblingIndex));

            Id = id;
            ParentId = parentId;
            HasParent = hasParent;
            SiblingIndex = siblingIndex;
            Item = item;
            IsExpanded = isExpanded;
        }

        public static TreeNodeRecord<TId, TItem> Root(TId id, TItem item,
            int siblingIndex = 0, bool isExpanded = false)
            => new TreeNodeRecord<TId, TItem>(id, default, false, item, siblingIndex,
                isExpanded);

        public static TreeNodeRecord<TId, TItem> Child(TId id, TId parentId, TItem item,
            int siblingIndex = 0, bool isExpanded = false)
            => new TreeNodeRecord<TId, TItem>(id, parentId, true, item, siblingIndex,
                isExpanded);
    }
}
