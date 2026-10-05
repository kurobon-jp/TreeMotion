using System;
using System.Collections.Generic;

namespace TreeMotion
{
    public enum TreeMotionAnimationKind
    {
        Stable,
        Insert,
        Remove,
        Move,
        Swap,
        Resize
    }

    public readonly struct TreeMotionLayout<TId>
    {
        public TId Id { get; }
        public float Offset { get; }
        public float Size { get; }

        public TreeMotionLayout(TId id, float offset, float size)
        {
            if (id is null)
                throw new ArgumentNullException(nameof(id));
            ValidateFinite(offset, nameof(offset));
            ValidateNonNegativeFinite(size, nameof(size));
            Id = id;
            Offset = offset;
            Size = size;
        }

        private static void ValidateFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static void ValidateNonNegativeFinite(float value, string parameterName)
        {
            ValidateFinite(value, parameterName);
            if (value < 0f)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public readonly struct TreeMotionAnimationValue<TId>
    {
        public TId Id { get; }
        public float Offset { get; }
        public float Size { get; }
        public float Progress { get; }
        public TreeMotionAnimationKind Kind { get; }
        public bool IsExiting => Kind == TreeMotionAnimationKind.Remove;
        public bool IsAnimating => Kind != TreeMotionAnimationKind.Stable;

        internal TreeMotionAnimationValue(TId id, float offset, float size, float progress,
            TreeMotionAnimationKind kind)
        {
            Id = id;
            Offset = offset;
            Size = size;
            Progress = progress;
            Kind = kind;
        }
    }

    /// <summary>
    /// Retargetable, allocation-free-per-frame transition state for a flat layout. Nested group
    /// sizes should be supplied as independent layout entries so animation ticks never need to
    /// propagate through the tree.
    /// </summary>
    public sealed class TreeMotionAnimation<TId>
    {
        private static readonly bool IdCanBeNull = default(TId) is null;
        private struct Track
        {
            internal TId Id;
            internal float FromOffset;
            internal float ToOffset;
            internal float FromSize;
            internal float ToSize;
            internal float FromProgress;
            internal float ToProgress;
            internal TreeMotionAnimationKind Kind;
            internal bool HasTarget;
        }

        private readonly List<Track> _tracks = new();
        private readonly List<TreeMotionAnimationValue<TId>> _current = new();
        private readonly Dictionary<TId, int> _targetIndices;
        private readonly Dictionary<TId, int> _trackIndices;
        private readonly HashSet<TId> _sourceIds;
        private readonly HashSet<TId> _swapIds;
        private float _elapsed;
        private float _duration;

        public TreeMotionAnimation(IEqualityComparer<TId> comparer = null)
        {
            var resolvedComparer = comparer ?? EqualityComparer<TId>.Default;
            _targetIndices = new Dictionary<TId, int>(resolvedComparer);
            _trackIndices = new Dictionary<TId, int>(resolvedComparer);
            _sourceIds = new HashSet<TId>(resolvedComparer);
            _swapIds = new HashSet<TId>(resolvedComparer);
        }

        public int Count => _tracks.Count;
        public bool IsAnimating => _elapsed < _duration;
        public float Progress => _duration <= 0f ? 1f : Math.Min(1f, _elapsed / _duration);

        // Responsive geometry changes immediately without restarting the transition clock.
        // Preserve exiting tracks and their presentation progress until the transition ends.
        internal void SnapGeometry(IReadOnlyList<TreeMotionLayout<TId>> layout)
        {
            ValidateLayout(layout);
            BuildTargetIndex(layout);
            for (var i = 0; i < _tracks.Count; i++)
            {
                var track = _tracks[i];
                if (!_targetIndices.TryGetValue(track.Id, out var index)) continue;
                var target = layout[index];
                track.FromOffset = track.ToOffset = target.Offset;
                track.FromSize = track.ToSize = target.Size;
                _tracks[i] = track;
            }
        }

        public void Clear()
        {
            _tracks.Clear();
            _current.Clear();
            _targetIndices.Clear();
            _trackIndices.Clear();
            _sourceIds.Clear();
            _swapIds.Clear();
            _elapsed = 0f;
            _duration = 0f;
        }

        public void Snap(IReadOnlyList<TreeMotionLayout<TId>> layout)
        {
            ValidateLayout(layout);
            _tracks.Clear();
            for (var i = 0; i < layout.Count; i++)
            {
                var item = layout[i];
                _tracks.Add(new Track
                {
                    Id = item.Id,
                    FromOffset = item.Offset,
                    ToOffset = item.Offset,
                    FromSize = item.Size,
                    ToSize = item.Size,
                    FromProgress = 1f,
                    ToProgress = 1f,
                    Kind = TreeMotionAnimationKind.Stable,
                    HasTarget = true
                });
            }
            RebuildTrackIndex();

            _elapsed = 0f;
            _duration = 0f;
        }

        /// <summary>
        /// Starts a transition from the currently displayed values. Calling this during an active
        /// transition is continuous: no item jumps back to an earlier layout.
        /// </summary>
        public void Retarget(IReadOnlyList<TreeMotionLayout<TId>> target, float duration)
        {
            _swapIds.Clear();
            RetargetCore(target, duration);
        }

        /// <summary>
        /// Retargets the layout and marks swapped node roots with the swap animation kind.
        /// </summary>
        public void Retarget(IReadOnlyList<TreeMotionLayout<TId>> target, float duration,
            IReadOnlyList<TreeChange<TId>> changes)
        {
            if (changes == null)
                throw new ArgumentNullException(nameof(changes));
            _swapIds.Clear();
            for (var i = 0; i < changes.Count; i++)
            {
                if (changes[i].Kind == TreeChangeKind.Swap)
                {
                    _swapIds.Add(changes[i].FirstId);
                    _swapIds.Add(changes[i].SecondId);
                }
            }
            RetargetCore(target, duration);
        }

        private void RetargetCore(IReadOnlyList<TreeMotionLayout<TId>> target, float duration)
        {
            ValidatePositiveFinite(duration, nameof(duration));
            BuildTargetIndex(target);
            CaptureCurrent();
            _tracks.Clear();
            _sourceIds.Clear();

            for (var i = 0; i < _current.Count; i++)
            {
                var current = _current[i];
                _sourceIds.Add(current.Id);
                if (_targetIndices.TryGetValue(current.Id, out var targetIndex))
                {
                    var destination = target[targetIndex];
                    _tracks.Add(CreateTrack(current, destination));
                }
                else
                {
                    _tracks.Add(new Track
                    {
                        Id = current.Id,
                        FromOffset = current.Offset,
                        ToOffset = current.Offset,
                        FromSize = current.Size,
                        ToSize = current.Size,
                        FromProgress = current.Kind == TreeMotionAnimationKind.Insert
                            ? 1f - current.Progress
                            : 0f,
                        ToProgress = 1f,
                        Kind = TreeMotionAnimationKind.Remove,
                        HasTarget = false
                    });
                }
            }

            for (var i = 0; i < target.Count; i++)
            {
                var destination = target[i];
                if (_sourceIds.Contains(destination.Id))
                    continue;
                _tracks.Add(new Track
                {
                    Id = destination.Id,
                    FromOffset = destination.Offset,
                    ToOffset = destination.Offset,
                    FromSize = destination.Size,
                    ToSize = destination.Size,
                    FromProgress = 0f,
                    ToProgress = 1f,
                    Kind = TreeMotionAnimationKind.Insert,
                    HasTarget = true
                });
            }

            RebuildTrackIndex();

            _elapsed = 0f;
            _duration = 0f;
            for (var i = 0; i < _tracks.Count; i++)
                if (_tracks[i].Kind != TreeMotionAnimationKind.Stable)
                {
                    _duration = duration;
                    break;
                }
        }

        public void Advance(float deltaTime)
        {
            ValidateNonNegativeFinite(deltaTime, nameof(deltaTime));
            if (!IsAnimating)
                return;
            _elapsed = Math.Min(_duration, _elapsed + deltaTime);
            if (_elapsed < _duration)
                return;

            var write = 0;
            for (var read = 0; read < _tracks.Count; read++)
            {
                var track = _tracks[read];
                if (!track.HasTarget)
                    continue;
                track.FromOffset = track.ToOffset;
                track.FromSize = track.ToSize;
                track.FromProgress = 1f;
                track.ToProgress = 1f;
                track.Kind = TreeMotionAnimationKind.Stable;
                _tracks[write++] = track;
            }
            if (write < _tracks.Count)
                _tracks.RemoveRange(write, _tracks.Count - write);
            RebuildTrackIndex();
        }

        public TreeMotionAnimationValue<TId> GetValue(int index)
        {
            if ((uint)index >= (uint)_tracks.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return Evaluate(_tracks[index], EasedProgress());
        }

        /// <summary>Looks up one stable ID without scanning or allocating.</summary>
        public bool TryGetValue(TId id, out TreeMotionAnimationValue<TId> value)
        {
            if (IdCanBeNull && id is null)
                throw new ArgumentNullException(nameof(id));
            if (_trackIndices.TryGetValue(id, out var index))
            {
                value = Evaluate(_tracks[index], EasedProgress());
                return true;
            }
            value = default;
            return false;
        }

        private void RebuildTrackIndex()
        {
            _trackIndices.Clear();
            for (var i = 0; i < _tracks.Count; i++)
                _trackIndices.Add(_tracks[i].Id, i);
        }

        private Track CreateTrack(TreeMotionAnimationValue<TId> current,
            TreeMotionLayout<TId> destination)
        {
            var swapped = _swapIds.Contains(current.Id);
            var moved = current.Offset != destination.Offset;
            var resized = current.Size != destination.Size;
            var continuesInsert = current.Kind == TreeMotionAnimationKind.Insert && !swapped;
            var kind = swapped ? TreeMotionAnimationKind.Swap :
                continuesInsert ? TreeMotionAnimationKind.Insert :
                moved ? TreeMotionAnimationKind.Move :
                resized ? TreeMotionAnimationKind.Resize : TreeMotionAnimationKind.Stable;
            return new Track
            {
                Id = current.Id,
                FromOffset = current.Offset,
                ToOffset = destination.Offset,
                FromSize = current.Size,
                ToSize = destination.Size,
                FromProgress = continuesInsert ? current.Progress : kind == TreeMotionAnimationKind.Stable
                    ? 1f
                    : 0f,
                ToProgress = 1f,
                Kind = kind,
                HasTarget = true
            };
        }

        private void CaptureCurrent()
        {
            _current.Clear();
            var progress = EasedProgress();
            for (var i = 0; i < _tracks.Count; i++)
                _current.Add(Evaluate(_tracks[i], progress));
        }

        private void BuildTargetIndex(IReadOnlyList<TreeMotionLayout<TId>> target)
        {
            ValidateLayout(target);
            _targetIndices.Clear();
            for (var i = 0; i < target.Count; i++)
            {
                if (!_targetIndices.TryAdd(target[i].Id, i))
                    throw new ArgumentException("Duplicate IDs are not allowed.", nameof(target));
            }
        }

        private void ValidateLayout(IReadOnlyList<TreeMotionLayout<TId>> layout)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));
            _targetIndices.Clear();
            for (var i = 0; i < layout.Count; i++)
            {
                var item = layout[i];
                if (item.Id is null)
                    throw new ArgumentException("Layout IDs cannot be null.", nameof(layout));
                ValidateFinite(item.Offset, nameof(layout));
                ValidateNonNegativeFinite(item.Size, nameof(layout));
                if (!_targetIndices.TryAdd(item.Id, i))
                    throw new ArgumentException("Duplicate IDs are not allowed.", nameof(layout));
            }
        }

        private float EasedProgress()
        {
            var t = Progress;
            return t * t * (3f - 2f * t);
        }

        private TreeMotionAnimationValue<TId> Evaluate(Track track, float progress)
            => new(track.Id,
                Lerp(track.FromOffset, track.ToOffset, progress),
                Lerp(track.FromSize, track.ToSize, progress),
                Lerp(track.FromProgress, track.ToProgress, Progress), track.Kind);

        private static float Lerp(float from, float to, float progress)
            => from + (to - from) * progress;

        private static void ValidatePositiveFinite(float value, string parameterName)
        {
            ValidateFinite(value, parameterName);
            if (value <= 0f)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static void ValidateNonNegativeFinite(float value, string parameterName)
        {
            ValidateFinite(value, parameterName);
            if (value < 0f)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static void ValidateFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
