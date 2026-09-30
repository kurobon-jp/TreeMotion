using System;
using System.Collections;
using System.Collections.Generic;

namespace TreeMotion
{
    public enum TreeChangeKind
    {
        Insert,
        Remove,
        Move,
        Update,
        Swap
    }

    public readonly struct TreeChange<TId>
    {
        public TreeChangeKind Kind { get; }
        public TId FirstId { get; }
        public TId SecondId { get; }
        public int FromIndex { get; }
        public int ToIndex { get; }
        public int Count { get; }

        internal TreeChange(TreeChangeKind kind, TId id, int fromIndex, int toIndex, int count)
            : this(kind, id, default, fromIndex, toIndex, count)
        {
        }

        internal TreeChange(TreeChangeKind kind, TId firstId, TId secondId, int fromIndex,
            int toIndex, int count)
        {
            Kind = kind;
            FirstId = firstId;
            SecondId = secondId;
            FromIndex = fromIndex;
            ToIndex = toIndex;
            Count = count;
        }
    }

    public sealed class TreeChangeSet<TId> : IReadOnlyList<TreeChange<TId>>
    {
        private static readonly TreeChange<TId>[] EmptyChanges = Array.Empty<TreeChange<TId>>();
        private readonly TreeChange<TId>[] _changes;

        internal TreeChangeSet(List<TreeChange<TId>> changes)
        {
            _changes = changes.Count == 0 ? EmptyChanges : changes.ToArray();
        }

        public int Count => _changes.Length;
        public TreeChange<TId> this[int index] => _changes[index];
        public IEnumerator<TreeChange<TId>> GetEnumerator() => ((IEnumerable<TreeChange<TId>>)_changes).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _changes.GetEnumerator();
    }
}
