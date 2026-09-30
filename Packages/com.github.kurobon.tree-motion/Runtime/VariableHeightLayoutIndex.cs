using System;
using System.Collections.Generic;

namespace TreeMotion
{
    /// <summary>
    /// Maintains stable-ID rows with variable heights in chunked storage. Prefix queries inspect
    /// at most one chunk, while structural edits only relocate entries in affected chunks.
    /// </summary>
    public sealed class VariableHeightLayoutIndex<TId>
    {
        private readonly struct Entry
        {
            internal readonly TId Id;
            internal readonly float Height;

            internal Entry(TId id, float height)
            {
                Id = id;
                Height = height;
            }
        }

        private sealed class Chunk
        {
            internal readonly List<Entry> Entries;
            internal int Order;
            internal float TotalHeight;

            internal Chunk(int capacity)
            {
                Entries = new List<Entry>(capacity);
            }
        }

        private readonly struct Location
        {
            internal readonly Chunk Chunk;
            internal readonly int LocalIndex;

            internal Location(Chunk chunk, int localIndex)
            {
                Chunk = chunk;
                LocalIndex = localIndex;
            }
        }

        private readonly List<Chunk> _chunks = new List<Chunk>();
        private readonly Dictionary<TId, Location> _locations;
        private readonly HashSet<TId> _validationIds;
        private readonly List<Entry> _mutationBuffer = new List<Entry>();
        private readonly int _chunkCapacity;
        private int[] _countTree = new int[1];
        private float[] _extentTree = new float[1];
        private int _treeLength = 1;
        private int _count;
        private float _totalHeight;

        public int Count => _count;
        public float DefaultHeight { get; }
        public float Spacing { get; }
        public float PaddingStart { get; }
        public float PaddingEnd { get; }
        public float ContentSize => PaddingStart + PaddingEnd + _totalHeight +
                                    (_count > 0 ? (_count - 1) * Spacing : 0f);

        public VariableHeightLayoutIndex(float defaultHeight, float spacing = 0f,
            float paddingStart = 0f, float paddingEnd = 0f, int chunkCapacity = 128,
            IEqualityComparer<TId> comparer = null)
        {
            ValidatePositiveFinite(defaultHeight, nameof(defaultHeight));
            ValidateNonNegativeFinite(spacing, nameof(spacing));
            ValidateNonNegativeFinite(paddingStart, nameof(paddingStart));
            ValidateNonNegativeFinite(paddingEnd, nameof(paddingEnd));
            if (chunkCapacity < 16)
                throw new ArgumentOutOfRangeException(nameof(chunkCapacity),
                    "Chunk capacity must be at least 16.");

            DefaultHeight = defaultHeight;
            Spacing = spacing;
            PaddingStart = paddingStart;
            PaddingEnd = paddingEnd;
            _chunkCapacity = chunkCapacity;
            var resolvedComparer = comparer ?? EqualityComparer<TId>.Default;
            _locations = new Dictionary<TId, Location>(resolvedComparer);
            _validationIds = new HashSet<TId>(resolvedComparer);
        }

        public void Clear()
        {
            _chunks.Clear();
            _locations.Clear();
            _validationIds.Clear();
            _mutationBuffer.Clear();
            _count = 0;
            _totalHeight = 0f;
            Array.Clear(_countTree, 0, _countTree.Length);
            Array.Clear(_extentTree, 0, _extentTree.Length);
            _treeLength = 1;
        }

        public void Reset(IReadOnlyList<TId> ids, IReadOnlyList<float> heights = null)
        {
            if (ids == null)
                throw new ArgumentNullException(nameof(ids));
            if (heights != null && heights.Count != ids.Count)
                throw new ArgumentException("The height count must match the ID count.", nameof(heights));

            Clear();
            if (ids.Count == 0)
                return;

            for (var start = 0; start < ids.Count; start += _chunkCapacity)
            {
                var chunk = new Chunk(_chunkCapacity);
                var end = Math.Min(ids.Count, start + _chunkCapacity);
                for (var i = start; i < end; i++)
                {
                    var id = ids[i];
                    ValidateId(id, nameof(ids));
                    if (_locations.ContainsKey(id))
                        throw new ArgumentException("Duplicate IDs are not allowed.", nameof(ids));
                    var height = heights == null ? DefaultHeight : heights[i];
                    ValidateNonNegativeFinite(height, nameof(heights));
                    chunk.Entries.Add(new Entry(id, height));
                    chunk.TotalHeight += height;
                    _locations.Add(id, new Location(chunk, chunk.Entries.Count - 1));
                }

                _chunks.Add(chunk);
            }

            _count = ids.Count;
            _totalHeight = SumChunkHeights();
            RebuildTrees();
        }

        public void Insert(int index, TId id, float height = float.NaN)
        {
            _mutationBuffer.Clear();
            ValidateId(id, nameof(id));
            if (_locations.ContainsKey(id))
                throw new ArgumentException("Duplicate IDs are not allowed.", nameof(id));
            height = ResolveHeight(height, nameof(height));
            _mutationBuffer.Add(new Entry(id, height));
            InsertBuffered(index);
        }

        public void InsertRange(int index, IReadOnlyList<TId> ids, IReadOnlyList<float> heights = null)
        {
            ValidateInsertIndex(index);
            if (ids == null)
                throw new ArgumentNullException(nameof(ids));
            if (heights != null && heights.Count != ids.Count)
                throw new ArgumentException("The height count must match the ID count.", nameof(heights));
            if (ids.Count == 0)
                return;

            _mutationBuffer.Clear();
            _validationIds.Clear();
            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                ValidateId(id, nameof(ids));
                if (_locations.ContainsKey(id) || !_validationIds.Add(id))
                    throw new ArgumentException("Duplicate IDs are not allowed.", nameof(ids));
                var height = heights == null ? DefaultHeight : heights[i];
                ValidateNonNegativeFinite(height, nameof(heights));
                _mutationBuffer.Add(new Entry(id, height));
            }

            InsertBuffered(index);
        }

        public void RemoveRange(int index, int count)
        {
            ValidateRange(index, count);
            if (count == 0)
                return;

            var chunkIndex = FindChunkByItemIndex(index, out var localIndex);
            var remaining = count;
            while (remaining > 0)
            {
                var chunk = _chunks[chunkIndex];
                var removeCount = Math.Min(remaining, chunk.Entries.Count - localIndex);
                for (var i = localIndex; i < localIndex + removeCount; i++)
                {
                    var entry = chunk.Entries[i];
                    _locations.Remove(entry.Id);
                    chunk.TotalHeight -= entry.Height;
                    _totalHeight -= entry.Height;
                }

                chunk.Entries.RemoveRange(localIndex, removeCount);
                _count -= removeCount;
                remaining -= removeCount;
                if (chunk.Entries.Count == 0)
                {
                    _chunks.RemoveAt(chunkIndex);
                }
                else
                {
                    ReindexChunk(chunk, localIndex);
                    chunkIndex++;
                }

                localIndex = 0;
            }

            MergeSparseChunks();
            RebuildTrees();
        }

        /// <summary>
        /// Moves a contiguous range. The destination index is expressed in the collection after
        /// the range has been removed.
        /// </summary>
        public void MoveRange(int fromIndex, int count, int destinationIndex)
        {
            ValidateRange(fromIndex, count);
            if (destinationIndex < 0 || destinationIndex > _count - count)
                throw new ArgumentOutOfRangeException(nameof(destinationIndex));
            if (count == 0 || destinationIndex == fromIndex)
                return;

            _mutationBuffer.Clear();
            CopyRangeToBuffer(fromIndex, count);
            RemoveRange(fromIndex, count);
            InsertBuffered(destinationIndex);
        }

        public bool Contains(TId id)
        {
            ValidateId(id, nameof(id));
            return _locations.ContainsKey(id);
        }

        public int IndexOf(TId id)
        {
            var location = GetLocation(id);
            return GetCountBeforeChunk(location.Chunk.Order) + location.LocalIndex;
        }

        public TId GetId(int index)
        {
            var chunkIndex = FindChunkByItemIndex(index, out var localIndex);
            return _chunks[chunkIndex].Entries[localIndex].Id;
        }

        public float GetHeight(int index)
        {
            var chunkIndex = FindChunkByItemIndex(index, out var localIndex);
            return _chunks[chunkIndex].Entries[localIndex].Height;
        }

        public float GetHeight(TId id)
        {
            var location = GetLocation(id);
            return location.Chunk.Entries[location.LocalIndex].Height;
        }

        /// <summary>Sets an actual measured height and returns the applied delta.</summary>
        public float SetHeight(TId id, float height)
        {
            ValidateNonNegativeFinite(height, nameof(height));
            var location = GetLocation(id);
            var previous = location.Chunk.Entries[location.LocalIndex];
            var delta = height - previous.Height;
            if (delta == 0f)
                return 0f;

            location.Chunk.Entries[location.LocalIndex] = new Entry(previous.Id, height);
            location.Chunk.TotalHeight += delta;
            _totalHeight += delta;
            AddExtent(location.Chunk.Order, delta);
            return delta;
        }

        public float GetOffset(int index)
        {
            if (index < 0 || index > _count)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (index == _count)
                return PaddingStart + GetExtentTotal();

            var chunkIndex = FindChunkByItemIndex(index, out var localIndex);
            var offset = PaddingStart + GetExtentBeforeChunk(chunkIndex);
            var entries = _chunks[chunkIndex].Entries;
            for (var i = 0; i < localIndex; i++)
                offset += entries[i].Height + Spacing;
            return offset;
        }

        public float GetRangeSize(int index, int count)
        {
            ValidateRange(index, count);
            if (count == 0)
                return 0f;
            return GetOffset(index + count) - GetOffset(index) - Spacing;
        }

        /// <summary>Returns the row at, or immediately before, the supplied content offset.</summary>
        public int FindIndex(float offset)
        {
            if (_count == 0)
                return -1;
            if (offset <= PaddingStart)
                return 0;

            var localOffset = offset - PaddingStart;
            var totalExtent = GetExtentTotal();
            if (localOffset >= totalExtent)
                return _count - 1;

            var chunkIndex = FindChunkByExtent(localOffset);
            var remaining = localOffset - GetExtentBeforeChunk(chunkIndex);
            var entries = _chunks[chunkIndex].Entries;
            var localIndex = 0;
            while (localIndex < entries.Count - 1)
            {
                var extent = entries[localIndex].Height + Spacing;
                if (remaining < extent)
                    break;
                remaining -= extent;
                localIndex++;
            }

            return GetCountBeforeChunk(chunkIndex) + localIndex;
        }

        public VisibleRange GetVisibleRange(float scrollOffset, float viewportSize,
            float overscan = 0f)
        {
            ValidateNonNegativeFinite(viewportSize, nameof(viewportSize));
            ValidateNonNegativeFinite(overscan, nameof(overscan));
            if (_count == 0 || viewportSize == 0f)
                return new VisibleRange(0, 0);

            var visibleStart = Math.Max(0f, scrollOffset - overscan);
            var visibleEnd = Math.Max(visibleStart, scrollOffset + viewportSize + overscan);
            var start = FindIndex(visibleStart);
            while (start >= 0 && start < _count &&
                   GetOffset(start) + GetHeight(start) <= visibleStart)
                start++;
            if (start >= _count)
                return new VisibleRange(0, 0);

            var endCandidate = FindIndex(visibleEnd);
            var end = GetOffset(endCandidate) < visibleEnd ? endCandidate + 1 : endCandidate;
            if (end < start)
                return new VisibleRange(0, 0);
            return new VisibleRange(start, Math.Min(_count, end));
        }

        private void InsertBuffered(int index)
        {
            ValidateInsertIndex(index);
            if (_mutationBuffer.Count == 0)
                return;

            Chunk chunk;
            int chunkIndex;
            int localIndex;
            if (_count == 0)
            {
                chunk = new Chunk(Math.Max(_chunkCapacity, _mutationBuffer.Count));
                _chunks.Add(chunk);
                chunkIndex = 0;
                localIndex = 0;
            }
            else if (index == _count)
            {
                chunkIndex = _chunks.Count - 1;
                chunk = _chunks[chunkIndex];
                localIndex = chunk.Entries.Count;
            }
            else
            {
                chunkIndex = FindChunkByItemIndex(index, out localIndex);
                chunk = _chunks[chunkIndex];
            }

            chunk.Entries.InsertRange(localIndex, _mutationBuffer);
            for (var i = 0; i < _mutationBuffer.Count; i++)
            {
                var entry = _mutationBuffer[i];
                chunk.TotalHeight += entry.Height;
                _totalHeight += entry.Height;
            }

            _count += _mutationBuffer.Count;
            ReindexChunk(chunk, localIndex);
            SplitOversizedChunks(chunkIndex);
            RebuildTrees();
        }

        private void SplitOversizedChunks(int startChunkIndex)
        {
            var chunkIndex = startChunkIndex;
            while (chunkIndex < _chunks.Count)
            {
                var chunk = _chunks[chunkIndex];
                if (chunk.Entries.Count <= _chunkCapacity)
                    break;

                var movedCount = chunk.Entries.Count - _chunkCapacity;
                var next = new Chunk(Math.Max(_chunkCapacity, movedCount));
                for (var i = _chunkCapacity; i < chunk.Entries.Count; i++)
                    next.Entries.Add(chunk.Entries[i]);
                chunk.Entries.RemoveRange(_chunkCapacity, movedCount);
                RecalculateChunk(chunk);
                RecalculateChunk(next);
                _chunks.Insert(chunkIndex + 1, next);
                ReindexChunk(chunk, 0);
                ReindexChunk(next, 0);
                chunkIndex++;
            }
        }

        private void MergeSparseChunks()
        {
            for (var i = 0; i + 1 < _chunks.Count;)
            {
                var current = _chunks[i];
                var next = _chunks[i + 1];
                if (current.Entries.Count + next.Entries.Count > _chunkCapacity)
                {
                    i++;
                    continue;
                }

                current.Entries.AddRange(next.Entries);
                current.TotalHeight += next.TotalHeight;
                _chunks.RemoveAt(i + 1);
                ReindexChunk(current, 0);
            }
        }

        private void CopyRangeToBuffer(int index, int count)
        {
            var chunkIndex = FindChunkByItemIndex(index, out var localIndex);
            var remaining = count;
            while (remaining > 0)
            {
                var entries = _chunks[chunkIndex].Entries;
                var copyCount = Math.Min(remaining, entries.Count - localIndex);
                for (var i = 0; i < copyCount; i++)
                    _mutationBuffer.Add(entries[localIndex + i]);
                remaining -= copyCount;
                chunkIndex++;
                localIndex = 0;
            }
        }

        private Location GetLocation(TId id)
        {
            ValidateId(id, nameof(id));
            if (_locations.TryGetValue(id, out var location))
                return location;
            throw new KeyNotFoundException($"No row exists for ID '{id}'.");
        }

        private int FindChunkByItemIndex(int index, out int localIndex)
        {
            if (index < 0 || index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index));
            var low = 0;
            var high = _chunks.Count - 1;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (GetCountBeforeChunk(middle + 1) <= index)
                    low = middle + 1;
                else
                    high = middle;
            }

            localIndex = index - GetCountBeforeChunk(low);
            return low;
        }

        private int FindChunkByExtent(float offset)
        {
            var low = 0;
            var high = _chunks.Count - 1;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (GetExtentBeforeChunk(middle + 1) <= offset)
                    low = middle + 1;
                else
                    high = middle;
            }

            return low;
        }

        private void ReindexChunk(Chunk chunk, int start)
        {
            for (var i = start; i < chunk.Entries.Count; i++)
                _locations[chunk.Entries[i].Id] = new Location(chunk, i);
        }

        private void RecalculateChunk(Chunk chunk)
        {
            chunk.TotalHeight = 0f;
            for (var i = 0; i < chunk.Entries.Count; i++)
                chunk.TotalHeight += chunk.Entries[i].Height;
        }

        private float SumChunkHeights()
        {
            var result = 0f;
            for (var i = 0; i < _chunks.Count; i++)
                result += _chunks[i].TotalHeight;
            return result;
        }

        private void RebuildTrees()
        {
            _treeLength = _chunks.Count + 1;
            EnsureTreeCapacity(_treeLength);
            Array.Clear(_countTree, 0, _countTree.Length);
            Array.Clear(_extentTree, 0, _extentTree.Length);
            for (var i = 0; i < _chunks.Count; i++)
            {
                var chunk = _chunks[i];
                chunk.Order = i;
                AddCount(i, chunk.Entries.Count);
                AddExtent(i, chunk.TotalHeight + chunk.Entries.Count * Spacing);
            }
        }

        private void AddCount(int chunkIndex, int delta)
        {
            for (var i = chunkIndex + 1; i < _treeLength; i += i & -i)
                _countTree[i] += delta;
        }

        private void AddExtent(int chunkIndex, float delta)
        {
            for (var i = chunkIndex + 1; i < _treeLength; i += i & -i)
                _extentTree[i] += delta;
        }

        private void EnsureTreeCapacity(int requiredLength)
        {
            if (_countTree.Length >= requiredLength)
                return;

            var capacity = Math.Max(requiredLength, _countTree.Length * 2);
            _countTree = new int[capacity];
            _extentTree = new float[capacity];
        }

        private int GetCountBeforeChunk(int chunkIndex)
        {
            var result = 0;
            for (var i = chunkIndex; i > 0; i -= i & -i)
                result += _countTree[i];
            return result;
        }

        private float GetExtentBeforeChunk(int chunkIndex)
        {
            var result = 0f;
            for (var i = chunkIndex; i > 0; i -= i & -i)
                result += _extentTree[i];
            return result;
        }

        private float GetExtentTotal() => GetExtentBeforeChunk(_chunks.Count);

        private float ResolveHeight(float height, string parameterName)
        {
            if (float.IsNaN(height))
                return DefaultHeight;
            ValidateNonNegativeFinite(height, parameterName);
            return height;
        }

        private void ValidateInsertIndex(int index)
        {
            if (index < 0 || index > _count)
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        private void ValidateRange(int index, int count)
        {
            if (index < 0 || count < 0 || index > _count - count)
                throw new ArgumentOutOfRangeException(index < 0 ? nameof(index) : nameof(count));
        }

        private static void ValidateId(TId id, string parameterName)
        {
            if (ReferenceEquals(id, null))
                throw new ArgumentNullException(parameterName);
        }

        private static void ValidatePositiveFinite(float value, string parameterName)
        {
            if (!(value > 0f) || float.IsInfinity(value) || float.IsNaN(value))
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static void ValidateNonNegativeFinite(float value, string parameterName)
        {
            if (value < 0f || float.IsInfinity(value) || float.IsNaN(value))
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
