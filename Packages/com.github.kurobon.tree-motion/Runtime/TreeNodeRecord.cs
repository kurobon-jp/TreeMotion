using System;

namespace TreeMotion
{
    /// <summary>
    /// A flat snapshot record suitable for mapping API or master data into a TreeStore.
    /// Omitted sibling indices use encounter order within each parent. Explicit indices reserve
    /// their ordering values; omitted indices fill unused values in encounter order.
    /// </summary>
    public readonly struct TreeNodeRecord<TId, TItem>
    {
        public TId Id { get; }
        public TId ParentId { get; }
        public bool HasParent { get; }
        public int? SiblingIndex { get; }
        public TItem Item { get; }
        public bool IsExpanded { get; }

        private TreeNodeRecord(TId id, TId parentId, bool hasParent, TItem item,
            int? siblingIndex, bool isExpanded)
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

        /// <summary>Creates a root record when no parent ID is supplied.</summary>
        public TreeNodeRecord(TId id, TItem item,
            int? siblingIndex = null, bool isExpanded = false)
            : this(id, default, false, item, siblingIndex, isExpanded)
        {
        }

        /// <summary>Creates a child record; a null reference-type parent ID means no parent.</summary>
        public TreeNodeRecord(TId id, TItem item, TId parentId,
            int? siblingIndex = null, bool isExpanded = false)
            : this(id, parentId, !(parentId is null), item, siblingIndex, isExpanded)
        {
        }
    }
}
