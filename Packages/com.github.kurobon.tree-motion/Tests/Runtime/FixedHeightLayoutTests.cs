using System;
using NUnit.Framework;

namespace TreeMotion.Tests
{
    public sealed class FixedHeightLayoutTests
    {
        [Test]
        public void ContentSize_IncludesSpacingAndPadding()
        {
            var layout = new FixedHeightLayout(20f, 5f, 10f, 15f);

            Assert.That(layout.GetContentSize(3), Is.EqualTo(95f));
            Assert.That(layout.GetContentSize(0), Is.EqualTo(25f));
        }

        [Test]
        public void VisibleRange_IsEndExclusiveAndIncludesOverscan()
        {
            var layout = new FixedHeightLayout(20f, 5f, 10f, 15f);

            var range = layout.GetVisibleRange(35f, 40f, 100, overscan: 1);

            Assert.That(range.Start, Is.EqualTo(0));
            Assert.That(range.End, Is.EqualTo(4));
            Assert.That(range.Count, Is.EqualTo(4));
        }

        [Test]
        public void ViewportInsideSpacing_DoesNotIncludeThePreviousRow()
        {
            var layout = new FixedHeightLayout(20f, 10f);

            var range = layout.GetVisibleRange(21f, 2f, 10, overscan: 0);

            Assert.That(range.IsEmpty, Is.True);
        }

        [Test]
        public void InvalidDimensions_AreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FixedHeightLayout(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FixedHeightLayout(10f, -1f));
        }

        [Test]
        public void VisibleRowReads_DoNotAllocateAfterWarmup()
        {
            const int count = 10_000;
            var tree = new TreeStore<int, int>();
            var update = tree.BeginUpdate();
            for (var i = 0; i < count; i++)
                update.InsertRoot(i, i);
            update.Commit();

            _ = tree.GetVisibleRow(0);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var checksum = 0;
            for (var i = 0; i < count; i++)
                checksum += tree.GetVisibleRow(i).Id;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(checksum, Is.GreaterThan(0));
            Assert.That(allocated, Is.Zero);
        }
    }
}
