using System.Diagnostics;
using NUnit.Framework;

namespace TreeMotion.Tests
{
    public sealed class VariableHeightLayoutIndexBenchmarks
    {
        [Test, Explicit("Run manually when changing variable-height indexing.")]
        public void BuildAndQueryOneHundredThousandRows()
        {
            const int count = 100_000;
            const int queryCount = 100_000;
            var ids = new int[count];
            var heights = new float[count];
            for (var i = 0; i < count; i++)
            {
                ids[i] = i;
                heights[i] = 20f + i % 61;
            }

            var index = new VariableHeightLayoutIndex<int>(44f, 2f);
            var stopwatch = Stopwatch.StartNew();
            index.Reset(ids, heights);
            stopwatch.Stop();
            TestContext.WriteLine($"Built {count:N0} variable-height rows in " +
                                  $"{stopwatch.Elapsed.TotalMilliseconds:N2} ms.");

            var checksum = 0;
            stopwatch.Restart();
            for (var i = 0; i < queryCount; i++)
            {
                var row = i * 7919 % count;
                checksum ^= index.FindIndex(index.GetOffset(row) + heights[row] * 0.5f);
            }
            stopwatch.Stop();
            TestContext.WriteLine($"Ran {queryCount:N0} offset/index query pairs in " +
                                  $"{stopwatch.Elapsed.TotalMilliseconds:N2} ms.");

            Assert.That(index.Count, Is.EqualTo(count));
            Assert.That(checksum, Is.Not.EqualTo(int.MinValue));
        }

        [Test, Explicit("Run manually when changing variable-height indexing.")]
        public void ApplyTenThousandMeasurementsToOneHundredThousandRows()
        {
            const int count = 100_000;
            const int measurementCount = 10_000;
            var ids = new int[count];
            for (var i = 0; i < count; i++)
                ids[i] = i;

            var index = new VariableHeightLayoutIndex<int>(44f, 2f);
            index.Reset(ids);

            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < measurementCount; i++)
            {
                var id = i * 7919 % count;
                index.SetHeight(id, 20f + i % 61);
            }
            stopwatch.Stop();

            TestContext.WriteLine($"Applied {measurementCount:N0} measured heights in " +
                                  $"{stopwatch.Elapsed.TotalMilliseconds:N2} ms.");
            Assert.That(index.Count, Is.EqualTo(count));
        }
    }
}
