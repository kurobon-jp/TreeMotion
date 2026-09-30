using System.Diagnostics;
using NUnit.Framework;

namespace TreeMotion.Tests
{
    public sealed class TreeStoreBenchmarks
    {
        [Test, Explicit("Run manually when changing TreeStore data structures.")]
        public void InsertOneHundredThousandVisibleRoots()
        {
            const int count = 100_000;
            var tree = new TreeStore<int, int>();
            var update = tree.BeginUpdate();
            for (var i = 0; i < count; i++)
                update.InsertRoot(i, i);

            var stopwatch = Stopwatch.StartNew();
            var changes = update.Commit();
            stopwatch.Stop();

            TestContext.WriteLine($"Inserted {count:N0} roots in {stopwatch.Elapsed.TotalMilliseconds:N2} ms.");
            Assert.That(tree.VisibleCount, Is.EqualTo(count));
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Count, Is.EqualTo(count));
        }

        [Test, Explicit("Run manually when changing TreeStore data structures.")]
        public void LoadSnapshotWithOneHundredThousandNodes()
        {
            const int count = 100_000;
            var records = new TreeNodeRecord<int, int>[count];
            records[0] = TreeNodeRecord<int, int>.Root(0, 0, isExpanded: true);
            for (var i = 1; i < count; i++)
                records[i] = TreeNodeRecord<int, int>.Child(i, 0, i, i - 1);

            var tree = new TreeStore<int, int>();
            var stopwatch = Stopwatch.StartNew();
            tree.LoadSnapshot(records);
            stopwatch.Stop();

            TestContext.WriteLine($"Loaded {count:N0} snapshot nodes in " +
                                  $"{stopwatch.Elapsed.TotalMilliseconds:N2} ms.");
            Assert.That(tree.Count, Is.EqualTo(count));
            Assert.That(tree.VisibleCount, Is.EqualTo(count));
        }

        [Test, Explicit("Run manually when changing TreeStore data structures.")]
        public void CollapseAndExpandTenThousandDescendants()
        {
            const int count = 10_000;
            var tree = new TreeStore<int, int>();
            var setup = tree.BeginUpdate().InsertRoot(0, 0, isExpanded: true);
            for (var i = 1; i <= count; i++)
                setup.Insert(0, i, i);
            setup.Commit();

            var stopwatch = Stopwatch.StartNew();
            tree.BeginUpdate().SetExpanded(0, false).Commit();
            tree.BeginUpdate().SetExpanded(0, true).Commit();
            stopwatch.Stop();

            TestContext.WriteLine($"Collapsed and expanded {count:N0} rows in " +
                                  $"{stopwatch.Elapsed.TotalMilliseconds:N2} ms.");
            Assert.That(tree.VisibleCount, Is.EqualTo(count + 1));
        }

        [Test, Explicit("Run manually when changing TreeStore data structures.")]
        public void InsertOneThousandRowsIntoTheMiddleOfTenThousand()
        {
            const int initialCount = 10_000;
            const int insertedCount = 1_000;
            var tree = new TreeStore<int, int>();
            var setup = tree.BeginUpdate();
            for (var i = 0; i < initialCount; i++)
                setup.InsertRoot(i, i);
            setup.Commit();

            var update = tree.BeginUpdate();
            for (var i = 0; i < insertedCount; i++)
                update.InsertRoot(initialCount + i, initialCount + i, initialCount / 2 + i);

            var stopwatch = Stopwatch.StartNew();
            update.Commit();
            stopwatch.Stop();

            TestContext.WriteLine($"Inserted {insertedCount:N0} rows into the middle of " +
                                  $"{initialCount:N0} rows in {stopwatch.Elapsed.TotalMilliseconds:N2} ms.");
            Assert.That(tree.VisibleCount, Is.EqualTo(initialCount + insertedCount));
        }
    }
}
