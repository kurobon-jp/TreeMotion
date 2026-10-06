using System;
using System.Collections.Generic;

namespace TreeMotion
{
    /// <summary>
    /// Stores a mutable tree and an incrementally maintained list of its visible rows.
    /// Structural changes are submitted through <see cref="TreeUpdate{TId,TItem}"/>.
    /// </summary>
    public sealed class TreeStore<TId, TItem>
    {
        private sealed class Node
        {
            internal readonly TId Id;
            internal readonly List<TId> Children = new();
            internal TItem Item;
            internal TId ParentId;
            internal bool HasParent;
            internal bool IsExpanded;

            internal Node(TId id, TItem item, TId parentId, bool hasParent, bool isExpanded)
            {
                Id = id;
                Item = item;
                ParentId = parentId;
                HasParent = hasParent;
                IsExpanded = isExpanded;
            }
        }

        private Dictionary<TId, Node> _nodes;
        private struct NodeUndo
        {
            internal Node Original;
            internal TItem Item;
            internal TId ParentId;
            internal bool HasParent, IsExpanded;
            internal List<TId> Children;
        }

        private readonly Dictionary<TId, NodeUndo> _undoNodes;
        private readonly List<TId> _undoRoots = new();
        private readonly Stack<List<TId>> _undoChildrenPool = new();
        private bool _rootOrderSaved;
        private List<TId> _roots = new();
        private List<VisibleRow<TId>> _visibleRows = new();
        private Dictionary<TId, int> _visibleIndices;
        private readonly List<VisibleRow<TId>> _rowBuffer = new();
        private readonly List<TId> _idBuffer = new();
        private readonly HashSet<TId> _idSetBuffer;
        private readonly Stack<TraversalFrame> _traversal = new();
        private readonly Stack<TId> _idStack = new();

        public TreeStore() : this(null)
        {
        }

        public TreeStore(IEqualityComparer<TId> comparer)
        {
            _nodes = new Dictionary<TId, Node>(comparer ?? EqualityComparer<TId>.Default);
            _undoNodes = new Dictionary<TId, NodeUndo>(_nodes.Comparer);
            _visibleIndices = new Dictionary<TId, int>(comparer ?? EqualityComparer<TId>.Default);
            _idSetBuffer = new HashSet<TId>(comparer ?? EqualityComparer<TId>.Default);
        }

        public int Count => _nodes.Count;
        public int RootCount => _roots.Count;
        public int VisibleCount => _visibleRows.Count;
        public IEqualityComparer<TId> Comparer => _nodes.Comparer;

        public TreeUpdate<TId, TItem> BeginUpdate() => new(this);

        /// <summary>
        /// Atomically replaces the complete tree from flat, arbitrarily ordered records. This is
        /// intended for initial API/master snapshots; use BeginUpdate for animated deltas.
        /// </summary>
        public void LoadSnapshot(IReadOnlyList<TreeNodeRecord<TId, TItem>> records)
        {
            if (records == null)
                throw new ArgumentNullException(nameof(records));

            var comparer = _nodes.Comparer;
            var nodes = new Dictionary<TId, Node>(records.Count, comparer);
            var roots = new List<SnapshotOrder>();
            var childOrders = new Dictionary<TId, List<SnapshotOrder>>(comparer);

            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                ValidateId(record.Id, nameof(records));
                if (record.HasParent)
                    ValidateId(record.ParentId, nameof(records));
                if (!nodes.TryAdd(record.Id, new Node(record.Id, record.Item,
                        record.ParentId, record.HasParent, record.IsExpanded)))
                    throw new ArgumentException($"Duplicate node ID '{record.Id}'.",
                        nameof(records));
            }

            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                var order = new SnapshotOrder(record.Id, record.SiblingIndex);
                if (!record.HasParent)
                {
                    roots.Add(order);
                    continue;
                }

                if (!nodes.ContainsKey(record.ParentId))
                    throw new ArgumentException(
                        $"Parent '{record.ParentId}' for node '{record.Id}' does not exist.",
                        nameof(records));
                if (!childOrders.TryGetValue(record.ParentId, out var children))
                {
                    children = new List<SnapshotOrder>();
                    childOrders.Add(record.ParentId, children);
                }
                children.Add(order);
            }

            SortAndValidateOrder(roots, "root", records);
            foreach (var node in nodes.Values)
                if (node.HasParent) node.ParentId = nodes[node.ParentId].Id;
            foreach (var pair in childOrders)
            {
                SortAndValidateOrder(pair.Value, $"parent '{pair.Key}'", records);
                var children = nodes[pair.Key].Children;
                for (var i = 0; i < pair.Value.Count; i++)
                    children.Add(pair.Value[i].Id);
            }

            var rootIds = new List<TId>(roots.Count);
            for (var i = 0; i < roots.Count; i++)
                rootIds.Add(roots[i].Id);

            var visibleRows = new List<VisibleRow<TId>>(records.Count);
            var visibleIndices = new Dictionary<TId, int>(records.Count, comparer);
            var visited = new HashSet<TId>(comparer);
            var traversal = new Stack<TraversalFrame>();
            for (var i = rootIds.Count - 1; i >= 0; i--)
                traversal.Push(new TraversalFrame(rootIds[i], 0));

            while (traversal.Count > 0)
            {
                var frame = traversal.Pop();
                if (!visited.Add(frame.Id))
                    throw new InvalidOperationException(
                        $"Snapshot contains a cycle involving node '{frame.Id}'.");

                var node = nodes[frame.Id];
                for (var i = node.Children.Count - 1; i >= 0; i--)
                    traversal.Push(new TraversalFrame(node.Children[i], frame.Depth + 1));
            }

            if (visited.Count != nodes.Count)
                throw new InvalidOperationException(
                    "Snapshot contains a parent cycle and cannot be rooted.");

            traversal.Clear();
            for (var i = rootIds.Count - 1; i >= 0; i--)
                traversal.Push(new TraversalFrame(rootIds[i], 0));
            while (traversal.Count > 0)
            {
                var frame = traversal.Pop();
                var node = nodes[frame.Id];
                visibleIndices.Add(node.Id, visibleRows.Count);
                visibleRows.Add(CreateVisibleRow(node, frame.Depth));
                if (!node.IsExpanded)
                    continue;
                for (var i = node.Children.Count - 1; i >= 0; i--)
                    traversal.Push(new TraversalFrame(node.Children[i], frame.Depth + 1));
            }

            _nodes = nodes;
            _roots = rootIds;
            _visibleRows = visibleRows;
            _visibleIndices = visibleIndices;
        }

        public bool Contains(TId id)
        {
            ValidateId(id, nameof(id));
            return _nodes.ContainsKey(id);
        }

        public TItem GetItem(TId id) => GetNode(id).Item;
        
        public bool TryGetItem(TId id, out TItem item)
        {
            item = default;
            if (!TryGetNode(id, out var node)) return false;
            item = node.Item;
            return true;
        }

        public int GetChildCount(TId id) => GetNode(id).Children.Count;

        public TId GetChildId(TId id, int childIndex) => GetNode(id).Children[childIndex];

        public TId GetRootId(int rootIndex) => _roots[rootIndex];

        public bool TryGetParentId(TId id, out TId parentId)
        {
            var node = GetNode(id);
            parentId = node.ParentId;
            return node.HasParent;
        }

        public bool IsExpanded(TId id) => GetNode(id).IsExpanded;

        public VisibleRow<TId> GetVisibleRow(int index) => _visibleRows[index];

        public int IndexOfVisible(TId id)
        {
            ValidateId(id, nameof(id));
            return _visibleIndices.GetValueOrDefault(id, -1);
        }

        internal TreeChangeSet<TId> Apply(IReadOnlyList<TreeMutation<TId, TItem>> mutations)
        {
            if (mutations.Count == 0) return new TreeChangeSet<TId>(new List<TreeChange<TId>>());

            try
            {
                return ApplyCore(mutations);
            }
            catch
            {
                Rollback();
                throw;
            }
            finally
            {
                foreach (var saved in _undoNodes.Values)
                {
                    if (saved.Children == null) continue;
                    saved.Children.Clear();
                    _undoChildrenPool.Push(saved.Children);
                }
                _undoNodes.Clear();
                _undoRoots.Clear();
                _rootOrderSaved = false;
            }
        }

        private void SaveNode(Node node, bool saveChildren = false)
        {
            if (!_undoNodes.TryGetValue(node.Id, out var saved))
                saved = new NodeUndo
                {
                    Original = node, Item = node.Item, ParentId = node.ParentId,
                    HasParent = node.HasParent, IsExpanded = node.IsExpanded
                };
            // A node added in this batch only needs to be removed on rollback.
            if (saved.Original != null && saveChildren && saved.Children == null)
            {
                saved.Children = _undoChildrenPool.Count > 0 ? _undoChildrenPool.Pop() : new List<TId>();
                saved.Children.AddRange(saved.Original.Children);
            }
            _undoNodes[node.Id] = saved;
        }

        private void SaveSiblingOrder(Node parent)
        {
            if (parent != null) SaveNode(parent, saveChildren: true);
            else if (!_rootOrderSaved)
            {
                _undoRoots.AddRange(_roots);
                _rootOrderSaved = true;
            }
        }

        private void Rollback()
        {
            foreach (var pair in _undoNodes)
            {
                var saved = pair.Value;
                if (saved.Original == null)
                {
                    _nodes.Remove(pair.Key);
                    continue;
                }
                var node = saved.Original;
                node.Item = saved.Item;
                node.ParentId = saved.ParentId;
                node.HasParent = saved.HasParent;
                node.IsExpanded = saved.IsExpanded;
                if (saved.Children != null)
                {
                    node.Children.Clear();
                    node.Children.AddRange(saved.Children);
                }
                _nodes[pair.Key] = node;
            }
            if (_rootOrderSaved)
            {
                _roots.Clear();
                _roots.AddRange(_undoRoots);
            }
            RebuildVisibleRows();
        }

        private TreeChangeSet<TId> ApplyCore(IReadOnlyList<TreeMutation<TId, TItem>> mutations)
        {
            var changes = new List<TreeChange<TId>>(mutations.Count);
            for (var i = 0; i < mutations.Count; i++)
            {
                var mutation = mutations[i];
                switch (mutation.Kind)
                {
                    case TreeMutationKind.Insert:
                        var insertCount = GetInsertRunLength(mutations, i);
                        if (insertCount == 1)
                        {
                            Insert(mutation, changes);
                        }
                        else
                        {
                            InsertRange(mutations, i, insertCount, changes);
                            i += insertCount - 1;
                        }
                        break;
                    case TreeMutationKind.Remove:
                        Remove(mutation.Id, changes);
                        break;
                    case TreeMutationKind.Move:
                        Move(mutation, changes);
                        break;
                    case TreeMutationKind.Expand:
                        Expand(mutation.Id, mutation.IsExpanded, changes);
                        break;
                    case TreeMutationKind.Update:
                        Update(mutation.Id, mutation.Item, changes);
                        break;
                    case TreeMutationKind.Swap:
                        Swap(mutation.Id, mutation.OtherId, changes);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return new TreeChangeSet<TId>(changes);
        }

        private int GetInsertRunLength(IReadOnlyList<TreeMutation<TId, TItem>> mutations, int start)
        {
            var first = mutations[start];
            var count = 1;
            for (var i = start + 1; i < mutations.Count; i++)
            {
                var current = mutations[i];
                if (current.Kind != TreeMutationKind.Insert || current.HasParent != first.HasParent)
                    break;
                if (first.HasParent && !_nodes.Comparer.Equals(current.ParentId, first.ParentId))
                    break;
                if (first.Index == -1 ? current.Index != -1 : current.Index != first.Index + count)
                    break;
                count++;
            }

            return count;
        }

        private void InsertRange(IReadOnlyList<TreeMutation<TId, TItem>> mutations, int start, int count,
            List<TreeChange<TId>> changes)
        {
            var first = mutations[start];
            Node parent = null;
            List<TId> siblings;
            if (first.HasParent)
            {
                parent = GetNode(first.ParentId);
                siblings = parent.Children;
            }
            else
            {
                siblings = _roots;
            }

            var childIndex = NormalizeInsertionIndex(first.Index, siblings.Count);
            _idBuffer.Clear();
            _idSetBuffer.Clear();
            for (var i = 0; i < count; i++)
            {
                var mutation = mutations[start + i];
                ValidateId(mutation.Id, nameof(mutation.Id));
                if (_nodes.ContainsKey(mutation.Id) || !_idSetBuffer.Add(mutation.Id))
                    throw new ArgumentException("A node with the same ID already exists.", nameof(mutation.Id));
                _idBuffer.Add(mutation.Id);
            }

            var visibleIndex = GetVisibleInsertionIndex(parent, siblings, childIndex);
            SaveSiblingOrder(parent);
            for (var i = 0; i < count; i++)
            {
                var mutation = mutations[start + i];
                _undoNodes.TryAdd(mutation.Id, default);
                _nodes.Add(mutation.Id,
                    new Node(mutation.Id, mutation.Item, parent == null ? default : parent.Id, mutation.HasParent,
                        mutation.IsExpanded));
            }

            siblings.InsertRange(childIndex, _idBuffer);
            if (visibleIndex >= 0)
            {
                var depth = parent == null ? 0 : _visibleRows[_visibleIndices[parent.Id]].Depth + 1;
                _rowBuffer.Clear();
                for (var i = 0; i < count; i++)
                {
                    var node = _nodes[_idBuffer[i]];
                    _rowBuffer.Add(CreateVisibleRow(node, depth));
                }

                InsertVisibleRange(visibleIndex, _rowBuffer);
                AddChange(changes,
                    new TreeChange<TId>(TreeChangeKind.Insert, first.Id, -1, visibleIndex, count));
            }

            RefreshVisibleRow(parent, changes);
        }

        private void Insert(TreeMutation<TId, TItem> mutation, List<TreeChange<TId>> changes)
        {
            ValidateId(mutation.Id, nameof(mutation.Id));
            if (_nodes.ContainsKey(mutation.Id))
                throw new ArgumentException("A node with the same ID already exists.", nameof(mutation.Id));

            Node parent = null;
            List<TId> siblings;
            if (mutation.HasParent)
            {
                parent = GetNode(mutation.ParentId);
                siblings = parent.Children;
            }
            else
            {
                siblings = _roots;
            }

            var childIndex = NormalizeInsertionIndex(mutation.Index, siblings.Count);
            var node = new Node(mutation.Id, mutation.Item, parent == null ? default : parent.Id, mutation.HasParent,
                mutation.IsExpanded);
            SaveSiblingOrder(parent);
            _undoNodes.TryAdd(mutation.Id, default);
            _nodes.Add(mutation.Id, node);
            siblings.Insert(childIndex, mutation.Id);

            var visibleIndex = GetVisibleInsertionIndex(parent, siblings, childIndex);
            if (visibleIndex >= 0)
            {
                var depth = parent == null ? 0 : _visibleRows[_visibleIndices[parent.Id]].Depth + 1;
                InsertVisibleRow(visibleIndex, new VisibleRow<TId>(node.Id, depth, false, node.IsExpanded));
                AddChange(changes, new TreeChange<TId>(TreeChangeKind.Insert, node.Id, -1, visibleIndex, 1));
            }

            RefreshVisibleRow(parent, changes);
        }

        private void Remove(TId id, List<TreeChange<TId>> changes)
        {
            var node = GetNode(id);
            var parent = node.HasParent ? GetNode(node.ParentId) : null;
            var visibleIndex = IndexOfVisible(id);
            var visibleCount = visibleIndex < 0 ? 0 : GetVisibleSubtreeEnd(visibleIndex) - visibleIndex;

            var siblings = node.HasParent ? parent.Children : _roots;
            SaveSiblingOrder(parent);
            siblings.RemoveAt(FindSiblingIndex(siblings, node.Id));
            RemoveNodeAndDescendants(id);

            if (visibleCount > 0)
            {
                RemoveVisibleRange(visibleIndex, visibleCount);
                AddChange(changes, new TreeChange<TId>(TreeChangeKind.Remove, id, visibleIndex, -1,
                    visibleCount));
            }

            RefreshVisibleRow(parent, changes);
        }

        private void Move(TreeMutation<TId, TItem> mutation, List<TreeChange<TId>> changes)
        {
            var node = GetNode(mutation.Id);
            var oldParent = node.HasParent ? GetNode(node.ParentId) : null;
            Node newParent = null;
            if (mutation.HasParent)
            {
                newParent = GetNode(mutation.ParentId);
                EnsureCanMove(node, newParent);
            }

            var oldVisibleIndex = IndexOfVisible(node.Id);
            var oldVisibleCount = oldVisibleIndex < 0 ? 0 : GetVisibleSubtreeEnd(oldVisibleIndex) - oldVisibleIndex;

            var oldSiblings = node.HasParent ? oldParent.Children : _roots;
            SaveNode(node);
            SaveSiblingOrder(oldParent);
            SaveSiblingOrder(newParent);
            var oldSiblingIndex = FindSiblingIndex(oldSiblings, node.Id);
            oldSiblings.RemoveAt(oldSiblingIndex);

            var newSiblings = newParent == null ? _roots : newParent.Children;
            var newSiblingIndex = NormalizeInsertionIndex(mutation.Index, newSiblings.Count);
            newSiblings.Insert(newSiblingIndex, node.Id);
            node.ParentId = newParent == null ? default : newParent.Id;
            node.HasParent = mutation.HasParent;

            if (oldVisibleCount > 0)
                RemoveVisibleRange(oldVisibleIndex, oldVisibleCount);

            var newVisibleIndex = GetVisibleInsertionIndex(newParent, newSiblings, newSiblingIndex);
            var newVisibleCount = 0;
            if (newVisibleIndex >= 0)
            {
                var depth = newParent == null ? 0 : _visibleRows[_visibleIndices[newParent.Id]].Depth + 1;
                FlattenVisibleSubtree(node.Id, depth, _rowBuffer);
                newVisibleCount = _rowBuffer.Count;
                InsertVisibleRange(newVisibleIndex, _rowBuffer);
            }

            if (oldVisibleCount > 0 && newVisibleCount > 0)
            {
                AddChange(changes, new TreeChange<TId>(TreeChangeKind.Move, node.Id, oldVisibleIndex,
                    newVisibleIndex, newVisibleCount));
            }
            else if (oldVisibleCount > 0)
            {
                AddChange(changes, new TreeChange<TId>(TreeChangeKind.Remove, node.Id, oldVisibleIndex, -1,
                    oldVisibleCount));
            }
            else if (newVisibleCount > 0)
            {
                AddChange(changes, new TreeChange<TId>(TreeChangeKind.Insert, node.Id, -1, newVisibleIndex,
                    newVisibleCount));
            }

            RefreshVisibleRow(oldParent, changes);
            if (newParent != oldParent)
                RefreshVisibleRow(newParent, changes);
        }

        private void Expand(TId id, bool isExpanded, List<TreeChange<TId>> changes)
        {
            var node = GetNode(id);
            if (node.IsExpanded == isExpanded)
                return;

            SaveNode(node);
            node.IsExpanded = isExpanded;
            var visibleIndex = IndexOfVisible(id);
            if (visibleIndex < 0)
                return;

            ReplaceVisibleRow(visibleIndex, node);

            if (isExpanded)
            {
                _rowBuffer.Clear();
                var depth = _visibleRows[visibleIndex].Depth + 1;
                for (var i = 0; i < node.Children.Count; i++)
                    AppendVisibleSubtree(node.Children[i], depth, _rowBuffer);

                InsertVisibleRange(visibleIndex + 1, _rowBuffer);
                AddChange(changes, new TreeChange<TId>(TreeChangeKind.Expand, id, -1, visibleIndex + 1,
                    _rowBuffer.Count));
            }
            else
            {
                var count = GetVisibleSubtreeEnd(visibleIndex) - visibleIndex - 1;
                RemoveVisibleRange(visibleIndex + 1, count);
                AddChange(changes, new TreeChange<TId>(TreeChangeKind.Collapse, id, visibleIndex + 1, -1,
                    count));
            }
        }

        private void Update(TId id, TItem item, List<TreeChange<TId>> changes)
        {
            var node = GetNode(id);
            SaveNode(node);
            node.Item = item;
            var visibleIndex = IndexOfVisible(id);
            if (visibleIndex >= 0)
                AddChange(changes,
                    new TreeChange<TId>(TreeChangeKind.Update, id, visibleIndex, visibleIndex, 1));
        }

        private void Swap(TId firstId, TId secondId, List<TreeChange<TId>> changes)
        {
            if (_nodes.Comparer.Equals(firstId, secondId))
                throw new ArgumentException("Swap IDs must differ.", nameof(secondId));

            var first = GetNode(firstId);
            var second = GetNode(secondId);
            if (IsAncestor(first, second) || IsAncestor(second, first))
                throw new InvalidOperationException(
                    "An ancestor and its descendant cannot exchange tree positions.");

            var firstVisibleIndex = IndexOfVisible(firstId);
            var firstParent = first.HasParent ? GetNode(first.ParentId) : null;
            var secondParent = second.HasParent ? GetNode(second.ParentId) : null;
            var firstSiblings = firstParent == null ? _roots : firstParent.Children;
            var secondSiblings = secondParent == null ? _roots : secondParent.Children;
            var firstSiblingIndex = FindSiblingIndex(firstSiblings, first.Id);
            var secondSiblingIndex = FindSiblingIndex(secondSiblings, second.Id);
            SaveNode(first);
            SaveNode(second);
            SaveSiblingOrder(firstParent);
            SaveSiblingOrder(secondParent);

            if (ReferenceEquals(firstSiblings, secondSiblings))
            {
                firstSiblings[firstSiblingIndex] = second.Id;
                firstSiblings[secondSiblingIndex] = first.Id;
            }
            else
            {
                firstSiblings[firstSiblingIndex] = second.Id;
                secondSiblings[secondSiblingIndex] = first.Id;
            }

            var firstParentId = first.ParentId;
            var firstHasParent = first.HasParent;
            first.ParentId = second.ParentId;
            first.HasParent = second.HasParent;
            second.ParentId = firstParentId;
            second.HasParent = firstHasParent;

            RebuildVisibleRows();
            AddChange(changes, new TreeChange<TId>(TreeChangeKind.Swap, firstId, secondId,
                firstVisibleIndex, IndexOfVisible(firstId), 2));
        }

        private int FindSiblingIndex(List<TId> siblings, TId id)
        {
            for (var i = 0; i < siblings.Count; i++)
                if (Comparer.Equals(siblings[i], id)) return i;
            throw new InvalidOperationException("Node is missing from its sibling list.");
        }

        private bool IsAncestor(Node possibleAncestor, Node node)
        {
            var current = node;
            while (current.HasParent)
            {
                current = GetNode(current.ParentId);
                if (_nodes.Comparer.Equals(current.Id, possibleAncestor.Id))
                    return true;
            }
            return false;
        }

        private void RebuildVisibleRows()
        {
            _visibleRows.Clear();
            _visibleIndices.Clear();
            _traversal.Clear();
            for (var i = _roots.Count - 1; i >= 0; i--)
                _traversal.Push(new TraversalFrame(_roots[i], 0));

            while (_traversal.Count > 0)
            {
                var frame = _traversal.Pop();
                var node = _nodes[frame.Id];
                _visibleIndices.Add(node.Id, _visibleRows.Count);
                _visibleRows.Add(CreateVisibleRow(node, frame.Depth));
                if (!node.IsExpanded)
                    continue;
                for (var i = node.Children.Count - 1; i >= 0; i--)
                    _traversal.Push(new TraversalFrame(node.Children[i], frame.Depth + 1));
            }
        }

        private int GetVisibleInsertionIndex(Node parent, List<TId> siblings, int childIndex)
        {
            if (parent != null)
            {
                if (!parent.IsExpanded || !_visibleIndices.TryGetValue(parent.Id, out var parentIndex))
                    return -1;
                if (childIndex == 0)
                    return parentIndex + 1;
            }
            else if (childIndex == 0)
            {
                return 0;
            }

            var previousId = siblings[childIndex - 1];
            return _visibleIndices.TryGetValue(previousId, out var previousIndex)
                ? GetVisibleSubtreeEnd(previousIndex)
                : -1;
        }

        private int GetVisibleSubtreeEnd(int start)
        {
            var depth = _visibleRows[start].Depth;
            var end = start + 1;
            while (end < _visibleRows.Count && _visibleRows[end].Depth > depth)
                end++;
            return end;
        }

        private void FlattenVisibleSubtree(TId id, int depth, List<VisibleRow<TId>> output)
        {
            output.Clear();
            AppendVisibleSubtree(id, depth, output);
        }

        private void AppendVisibleSubtree(TId id, int depth, List<VisibleRow<TId>> output)
        {
            _traversal.Clear();
            _traversal.Push(new TraversalFrame(id, depth));
            while (_traversal.Count > 0)
            {
                var frame = _traversal.Pop();
                var node = _nodes[frame.Id];
                output.Add(CreateVisibleRow(node, frame.Depth));
                if (!node.IsExpanded)
                    continue;

                for (var i = node.Children.Count - 1; i >= 0; i--)
                    _traversal.Push(new TraversalFrame(node.Children[i], frame.Depth + 1));
            }
        }

        private void InsertVisibleRange(int index, IReadOnlyList<VisibleRow<TId>> rows)
        {
            if (rows.Count == 0)
                return;

            if (rows is List<VisibleRow<TId>> list)
            {
                _visibleRows.InsertRange(index, list);
            }
            else
            {
                for (var i = 0; i < rows.Count; i++)
                    _visibleRows.Insert(index + i, rows[i]);
            }

            ReindexVisibleRows(index);
        }

        private void InsertVisibleRow(int index, VisibleRow<TId> row)
        {
            _visibleRows.Insert(index, row);
            ReindexVisibleRows(index);
        }

        private void RemoveVisibleRange(int index, int count)
        {
            for (var i = index; i < index + count; i++)
                _visibleIndices.Remove(_visibleRows[i].Id);
            _visibleRows.RemoveRange(index, count);
            ReindexVisibleRows(index);
        }

        private void ReindexVisibleRows(int start)
        {
            for (var i = start; i < _visibleRows.Count; i++)
                _visibleIndices[_visibleRows[i].Id] = i;
        }

        private void RefreshVisibleRow(Node node, List<TreeChange<TId>> changes)
        {
            if (node == null || !_visibleIndices.TryGetValue(node.Id, out var visibleIndex))
                return;
            ReplaceVisibleRow(visibleIndex, node);
            AddChange(changes,
                new TreeChange<TId>(TreeChangeKind.Update, node.Id, visibleIndex, visibleIndex, 1));
        }

        private static void AddChange(List<TreeChange<TId>> changes, TreeChange<TId> change)
        {
            if (changes.Count > 0)
            {
                var lastIndex = changes.Count - 1;
                var previous = changes[lastIndex];
                if (previous.Kind == TreeChangeKind.Insert && change.Kind == TreeChangeKind.Insert &&
                    previous.ToIndex + previous.Count == change.ToIndex)
                {
                    changes[lastIndex] = new TreeChange<TId>(TreeChangeKind.Insert,
                        previous.FirstId, -1,
                        previous.ToIndex, previous.Count + change.Count);
                    return;
                }

                if (previous.Kind == TreeChangeKind.Remove && change.Kind == TreeChangeKind.Remove &&
                    previous.FromIndex == change.FromIndex)
                {
                    changes[lastIndex] = new TreeChange<TId>(TreeChangeKind.Remove,
                        previous.FirstId,
                        previous.FromIndex, -1, previous.Count + change.Count);
                    return;
                }
            }

            changes.Add(change);
        }

        private void ReplaceVisibleRow(int visibleIndex, Node node)
        {
            var previous = _visibleRows[visibleIndex];
            _visibleRows[visibleIndex] = CreateVisibleRow(node, previous.Depth);
        }

        private static VisibleRow<TId> CreateVisibleRow(Node node, int depth) =>
            new(node.Id, depth, node.Children.Count > 0, node.IsExpanded);

        private void RemoveNodeAndDescendants(TId id)
        {
            _idStack.Clear();
            _idStack.Push(id);
            while (_idStack.Count > 0)
            {
                var currentId = _idStack.Pop();
                var current = _nodes[currentId];
                SaveNode(current);
                for (var i = 0; i < current.Children.Count; i++)
                    _idStack.Push(current.Children[i]);
                _nodes.Remove(currentId);
            }
        }

        private void EnsureCanMove(Node node, Node newParent)
        {
            var current = newParent;
            while (current != null)
            {
                if (_nodes.Comparer.Equals(current.Id, node.Id))
                    throw new InvalidOperationException("A node cannot be moved below itself or one of its descendants.");
                current = current.HasParent ? GetNode(current.ParentId) : null;
            }
        }

        private Node GetNode(TId id)
        {
            ValidateId(id, nameof(id));
            if (TryGetNode(id, out var node))
                return node;
            throw new KeyNotFoundException($"No node exists for ID '{id}'.");
        }

        private bool TryGetNode(TId id, out Node node)
        {
            return _nodes.TryGetValue(id, out node);
        }

        private static int NormalizeInsertionIndex(int index, int count)
        {
            if (index == -1)
                return count;
            if (index < 0 || index > count)
                throw new ArgumentOutOfRangeException(nameof(index), index,
                    $"The index must be between 0 and {count}, or -1 to append.");
            return index;
        }

        private static void ValidateId(TId id, string parameterName)
        {
            if (ReferenceEquals(id, null))
                throw new ArgumentNullException(parameterName);
        }

        private static void SortAndValidateOrder(List<SnapshotOrder> items, string owner,
            IReadOnlyList<TreeNodeRecord<TId, TItem>> records)
        {
            var reserved = new HashSet<int>();
            for (var i = 0; i < items.Count; i++)
            {
                var index = items[i].SiblingIndex;
                if (index.HasValue && !reserved.Add(index.Value))
                    throw new ArgumentException(
                        $"Duplicate sibling index {index.Value} under {owner}.",
                        nameof(records));
            }
            var nextIndex = 0;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].SiblingIndex.HasValue)
                    continue;
                while (reserved.Contains(nextIndex))
                    nextIndex++;
                items[i] = new SnapshotOrder(items[i].Id, nextIndex++);
            }
            items.Sort((left, right) => left.SiblingIndex.Value.CompareTo(right.SiblingIndex.Value));
        }

        private readonly struct SnapshotOrder
        {
            internal readonly TId Id;
            internal readonly int? SiblingIndex;

            internal SnapshotOrder(TId id, int? siblingIndex)
            {
                Id = id;
                SiblingIndex = siblingIndex;
            }
        }

        private readonly struct TraversalFrame
        {
            internal readonly TId Id;
            internal readonly int Depth;

            internal TraversalFrame(TId id, int depth)
            {
                Id = id;
                Depth = depth;
            }
        }
    }
}
