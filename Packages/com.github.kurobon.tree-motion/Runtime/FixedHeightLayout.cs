using System;

namespace TreeMotion
{
    public readonly struct VisibleRange
    {
        public int Start { get; }
        public int End { get; }
        public int Count => End - Start;
        public bool IsEmpty => Count == 0;

        internal VisibleRange(int start, int end)
        {
            Start = start;
            End = end;
        }
    }

    /// <summary>
    /// Allocation-free index calculations for a fixed-height virtualized list.
    /// End indexes are exclusive.
    /// </summary>
    public readonly struct FixedHeightLayout
    {
        public float RowHeight { get; }
        public float Spacing { get; }
        public float PaddingStart { get; }
        public float PaddingEnd { get; }
        public float Stride => RowHeight + Spacing;

        public FixedHeightLayout(float rowHeight, float spacing = 0f, float paddingStart = 0f,
            float paddingEnd = 0f)
        {
            if (rowHeight <= 0f)
                throw new ArgumentOutOfRangeException(nameof(rowHeight), "Row height must be positive.");
            if (spacing < 0f)
                throw new ArgumentOutOfRangeException(nameof(spacing), "Spacing cannot be negative.");
            if (paddingStart < 0f)
                throw new ArgumentOutOfRangeException(nameof(paddingStart), "Padding cannot be negative.");
            if (paddingEnd < 0f)
                throw new ArgumentOutOfRangeException(nameof(paddingEnd), "Padding cannot be negative.");

            RowHeight = rowHeight;
            Spacing = spacing;
            PaddingStart = paddingStart;
            PaddingEnd = paddingEnd;
        }

        public float GetContentSize(int rowCount)
        {
            ValidateRowCount(rowCount);
            if (rowCount == 0)
                return PaddingStart + PaddingEnd;
            return PaddingStart + PaddingEnd + rowCount * RowHeight + (rowCount - 1) * Spacing;
        }

        public float GetRowOffset(int index)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            return PaddingStart + index * Stride;
        }

        public VisibleRange GetVisibleRange(float scrollOffset, float viewportSize, int rowCount,
            int overscan = 1)
        {
            ValidateRowCount(rowCount);
            if (viewportSize < 0f)
                throw new ArgumentOutOfRangeException(nameof(viewportSize));
            if (overscan < 0)
                throw new ArgumentOutOfRangeException(nameof(overscan));
            if (rowCount == 0 || viewportSize == 0f)
                return new VisibleRange(0, 0);

            var viewportStart = Math.Max(0f, scrollOffset);
            var viewportEnd = viewportStart + viewportSize;
            var start = (int)Math.Floor((viewportStart - PaddingStart) / Stride);
            start = Clamp(start, 0, rowCount - 1);
            if (GetRowOffset(start) + RowHeight <= viewportStart)
                start++;

            var end = (int)Math.Ceiling((viewportEnd - PaddingStart) / Stride);
            end = Clamp(end, 0, rowCount);
            if (start >= end)
                return new VisibleRange(0, 0);

            start = Math.Max(0, start - overscan);
            end = Math.Min(rowCount, end + overscan);
            return new VisibleRange(start, end);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }

        private static void ValidateRowCount(int rowCount)
        {
            if (rowCount < 0)
                throw new ArgumentOutOfRangeException(nameof(rowCount));
        }
    }
}
