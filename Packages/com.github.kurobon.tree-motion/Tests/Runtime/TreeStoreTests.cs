using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace TreeMotion.Tests
{
    public sealed class TreeStoreTests
    {
        [Test]
        public void LoadSnapshot_AcceptsUnorderedRecordsAndSortsEverySiblingSet()
        {
            var tree = new TreeStore<int, string>();
            var records = new[]
            {
                TreeNodeRecord<int, string>.Child(4, 3, "nested", 0),
                TreeNodeRecord<int, string>.Child(2, 1, "second", 1),
                TreeNodeRecord<int, string>.Root(5, "root-b", 1, true),
                TreeNodeRecord<int, string>.Child(3, 1, "group", 0, true),
                TreeNodeRecord<int, string>.Root(1, "root-a", 0, true),
                TreeNodeRecord<int, string>.Child(6, 5, "b-child", 0)
            };

            tree.LoadSnapshot(records);

            Assert.That(tree.Count, Is.EqualTo(6));
            Assert.That(tree.GetRootId(0), Is.EqualTo(1));
            Assert.That(tree.GetRootId(1), Is.EqualTo(5));
            Assert.That(tree.GetChildId(1, 0), Is.EqualTo(3));
            Assert.That(tree.GetChildId(1, 1), Is.EqualTo(2));
            AssertVisible(tree, (1, 0), (3, 1), (4, 2), (2, 1), (5, 0), (6, 1));
        }

        [Test]
        public void LoadSnapshot_CollapsedNodesStillValidateButHideDescendants()
        {
            var tree = new TreeStore<int, string>();
            tree.LoadSnapshot(new[]
            {
                TreeNodeRecord<int, string>.Child(2, 1, "child", 0),
                TreeNodeRecord<int, string>.Root(1, "root", 0, false)
            });

            Assert.That(tree.Count, Is.EqualTo(2));
            AssertVisible(tree, (1, 0));
        }

        [Test]
        public void LoadSnapshot_RejectsInvalidInputWithoutChangingExistingTree()
        {
            var tree = CreateNestedTree();
            var before = new List<(int id, int depth)>();
            for (var i = 0; i < tree.VisibleCount; i++)
            {
                var row = tree.GetVisibleRow(i);
                before.Add((row.Id, row.Depth));
            }

            Assert.Throws<ArgumentException>(() => tree.LoadSnapshot(new[]
            {
                TreeNodeRecord<int, string>.Root(10, "duplicate-a", 0),
                TreeNodeRecord<int, string>.Root(10, "duplicate-b", 1)
            }));
            Assert.Throws<ArgumentException>(() => tree.LoadSnapshot(new[]
            {
                TreeNodeRecord<int, string>.Child(10, 999, "orphan", 0)
            }));
            Assert.Throws<InvalidOperationException>(() => tree.LoadSnapshot(new[]
            {
                TreeNodeRecord<int, string>.Child(10, 11, "cycle-a", 0),
                TreeNodeRecord<int, string>.Child(11, 10, "cycle-b", 0)
            }));
            Assert.Throws<ArgumentException>(() => tree.LoadSnapshot(new[]
            {
                TreeNodeRecord<int, string>.Root(10, "same-order-a", 0),
                TreeNodeRecord<int, string>.Root(11, "same-order-b", 0)
            }));

            Assert.That(tree.Count, Is.EqualTo(4));
            AssertVisible(tree, before.ToArray());
        }

        [Test]
        public void LoadSnapshot_HandlesDeepNestingWithoutRecursion()
        {
            const int depth = 2000;
            var records = new TreeNodeRecord<int, int>[depth];
            records[0] = TreeNodeRecord<int, int>.Root(0, 0, isExpanded: true);
            for (var i = 1; i < depth; i++)
                records[i] = TreeNodeRecord<int, int>.Child(i, i - 1, i,
                    isExpanded: true);

            var tree = new TreeStore<int, int>();
            tree.LoadSnapshot(records);

            Assert.That(tree.VisibleCount, Is.EqualTo(depth));
            Assert.That(tree.GetVisibleRow(depth - 1).Depth, Is.EqualTo(depth - 1));
        }

        [Test]
        public void LoadSnapshot_CanBeFollowedByIncrementalAnimatedUpdates()
        {
            var tree = new TreeStore<int, string>();
            tree.LoadSnapshot(new[]
            {
                TreeNodeRecord<int, string>.Root(1, "root", 0, true),
                TreeNodeRecord<int, string>.Child(2, 1, "existing", 0)
            });

            var insert = tree.BeginUpdate().Insert(1, 3, "inserted").Commit();
            var move = tree.BeginUpdate().MoveToRoot(2).Commit();
            var update = tree.BeginUpdate().UpdateItem(3, "updated").Commit();

            AssertVisible(tree, (1, 0), (3, 1), (2, 0));
            Assert.That(tree.GetItem(3), Is.EqualTo("updated"));
            Assert.That(insert[0].Kind, Is.EqualTo(TreeChangeKind.Insert));
            Assert.That(move[0].Kind, Is.EqualTo(TreeChangeKind.Move));
            Assert.That(update[0].Kind, Is.EqualTo(TreeChangeKind.Update));
        }

        [Test]
        public void NestedGroups_AreFlattenedInDepthFirstOrder()
        {
            var tree = new TreeStore<int, string>();

            tree.BeginUpdate()
                .InsertRoot(1, "A", isExpanded: true)
                .Insert(1, 2, "A-1")
                .Insert(1, 3, "B", isExpanded: true)
                .Insert(3, 4, "B-1")
                .Commit();

            AssertVisible(tree, (1, 0), (2, 1), (3, 1), (4, 2));
        }

        [Test]
        public void CollapseAndExpand_ChangesOnlyTheDescendantRange()
        {
            var tree = CreateNestedTree();

            var collapse = tree.BeginUpdate().SetExpanded(1, false).Commit();

            AssertVisible(tree, (1, 0));
            Assert.That(collapse.Count, Is.EqualTo(2));
            Assert.That(collapse[1].Kind, Is.EqualTo(TreeChangeKind.Remove));
            Assert.That(collapse[1].FromIndex, Is.EqualTo(1));
            Assert.That(collapse[1].Count, Is.EqualTo(3));

            var expand = tree.BeginUpdate().SetExpanded(1, true).Commit();

            AssertVisible(tree, (1, 0), (2, 1), (3, 1), (4, 2));
            Assert.That(expand[1].Kind, Is.EqualTo(TreeChangeKind.Insert));
            Assert.That(expand[1].ToIndex, Is.EqualTo(1));
            Assert.That(expand[1].Count, Is.EqualTo(3));
        }

        [Test]
        public void MoveAcrossParents_PreservesIdentityAndUpdatesDepth()
        {
            var tree = new TreeStore<int, string>();
            tree.BeginUpdate()
                .InsertRoot(1, "A", isExpanded: true)
                .InsertRoot(5, "C", isExpanded: true)
                .Insert(1, 2, "B", isExpanded: true)
                .Insert(2, 3, "leaf")
                .Commit();

            var changes = tree.BeginUpdate().Move(2, 5).Commit();

            AssertVisible(tree, (1, 0), (5, 0), (2, 1), (3, 2));
            Assert.That(tree.GetItem(2), Is.EqualTo("B"));
            Assert.That(changes[0].Kind, Is.EqualTo(TreeChangeKind.Move));
            Assert.That(changes[0].FromIndex, Is.EqualTo(1));
            Assert.That(changes[0].ToIndex, Is.EqualTo(2));
            Assert.That(changes[0].Count, Is.EqualTo(2));
        }

        [Test]
        public void MoveIntoCollapsedParent_RemovesVisibleSubtree()
        {
            var tree = new TreeStore<int, string>();
            tree.BeginUpdate()
                .InsertRoot(1, "A", isExpanded: true)
                .InsertRoot(4, "B", isExpanded: false)
                .Insert(1, 2, "group", isExpanded: true)
                .Insert(2, 3, "leaf")
                .Commit();

            var changes = tree.BeginUpdate().Move(2, 4).Commit();

            AssertVisible(tree, (1, 0), (4, 0));
            Assert.That(changes[0].Kind, Is.EqualTo(TreeChangeKind.Remove));
            Assert.That(changes[0].Count, Is.EqualTo(2));
        }

        [Test]
        public void Remove_RemovesTheWholeSubtree()
        {
            var tree = CreateNestedTree();

            var changes = tree.BeginUpdate().Remove(3).Commit();

            AssertVisible(tree, (1, 0), (2, 1));
            Assert.That(tree.Count, Is.EqualTo(2));
            Assert.That(tree.Contains(3), Is.False);
            Assert.That(tree.Contains(4), Is.False);
            Assert.That(changes[0].Count, Is.EqualTo(2));
        }

        [Test]
        public void DuplicateIds_AreRejected()
        {
            var tree = new TreeStore<int, string>();
            tree.BeginUpdate().InsertRoot(1, "first").Commit();

            Assert.Throws<ArgumentException>(() =>
                tree.BeginUpdate().InsertRoot(1, "duplicate").Commit());
        }

        [Test]
        public void AdjacentInserts_AreReportedAsOneRange()
        {
            var tree = new TreeStore<int, int>();

            var changes = tree.BeginUpdate()
                .InsertRoot(1, 1)
                .InsertRoot(2, 2)
                .InsertRoot(3, 3)
                .Commit();

            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Kind, Is.EqualTo(TreeChangeKind.Insert));
            Assert.That(changes[0].ToIndex, Is.Zero);
            Assert.That(changes[0].Count, Is.EqualTo(3));
        }

        [Test]
        public void AdjacentChildInserts_UpdateTheParentOnce()
        {
            var tree = new TreeStore<int, int>();
            tree.BeginUpdate().InsertRoot(1, 1, isExpanded: true).Commit();

            var changes = tree.BeginUpdate()
                .Insert(1, 2, 2)
                .Insert(1, 3, 3)
                .Insert(1, 4, 4)
                .Commit();

            Assert.That(tree.VisibleCount, Is.EqualTo(4));
            Assert.That(tree.GetVisibleRow(1).Id, Is.EqualTo(2));
            Assert.That(tree.GetVisibleRow(2).Id, Is.EqualTo(3));
            Assert.That(tree.GetVisibleRow(3).Id, Is.EqualTo(4));
            Assert.That(changes.Count, Is.EqualTo(2));
            Assert.That(changes[0].Kind, Is.EqualTo(TreeChangeKind.Insert));
            Assert.That(changes[0].Count, Is.EqualTo(3));
            Assert.That(changes[1].Kind, Is.EqualTo(TreeChangeKind.Update));
            Assert.That(changes[1].FirstId, Is.EqualTo(1));
        }

        [Test]
        public void UpdateItem_PreservesIdentityAndReportsUpdate()
        {
            var tree = new TreeStore<int, string>();
            tree.BeginUpdate().InsertRoot(1, "before").Commit();

            var changes = tree.BeginUpdate().UpdateItem(1, "after").Commit();

            Assert.That(tree.GetItem(1), Is.EqualTo("after"));
            Assert.That(tree.GetVisibleRow(0).Id, Is.EqualTo(1));
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Kind, Is.EqualTo(TreeChangeKind.Update));
        }

        [Test]
        public void SwapNodes_ExchangesSiblingSubtrees()
        {
            var tree = new TreeStore<int, string>();
            tree.BeginUpdate()
                .InsertRoot(1, "root", isExpanded: true)
                .Insert(1, 2, "first", isExpanded: true)
                .Insert(2, 3, "child")
                .Insert(1, 4, "second")
                .Commit();

            var changes = tree.BeginUpdate().SwapNodes(2, 4).Commit();

            Assert.That(tree.GetItem(2), Is.EqualTo("first"));
            Assert.That(tree.GetItem(4), Is.EqualTo("second"));
            Assert.That(tree.GetChildId(1, 0), Is.EqualTo(4));
            Assert.That(tree.GetChildId(1, 1), Is.EqualTo(2));
            Assert.That(tree.GetChildId(2, 0), Is.EqualTo(3));
            Assert.That(tree.IsExpanded(2), Is.True);
            AssertVisible(tree, (1, 0), (4, 1), (2, 1), (3, 2));
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Kind, Is.EqualTo(TreeChangeKind.Swap));
            Assert.That(changes[0].FirstId, Is.EqualTo(2));
            Assert.That(changes[0].SecondId, Is.EqualTo(4));
        }

        [Test]
        public void SwapNodes_ExchangesPositionsAcrossParents()
        {
            var tree = new TreeStore<int, string>();
            tree.BeginUpdate()
                .InsertRoot(1, "first root", isExpanded: true)
                .Insert(1, 2, "first child", isExpanded: true)
                .Insert(2, 3, "grandchild")
                .InsertRoot(4, "second root", isExpanded: true)
                .Insert(4, 5, "second child")
                .Commit();

            tree.BeginUpdate().SwapNodes(2, 5).Commit();

            Assert.That(tree.GetChildId(1, 0), Is.EqualTo(5));
            Assert.That(tree.GetChildId(4, 0), Is.EqualTo(2));
            Assert.That(tree.GetChildId(2, 0), Is.EqualTo(3));
            Assert.That(tree.TryGetParentId(5, out var parentOfFive), Is.True);
            Assert.That(parentOfFive, Is.EqualTo(1));
            Assert.That(tree.TryGetParentId(2, out var parentOfTwo), Is.True);
            Assert.That(parentOfTwo, Is.EqualTo(4));
            AssertVisible(tree, (1, 0), (5, 1), (4, 0), (2, 1), (3, 2));
        }

        [Test]
        public void SwapNodes_RejectsTheSameIdentity()
        {
            var tree = new TreeStore<int, string>();
            tree.BeginUpdate().InsertRoot(1, "one").InsertRoot(2, "two").Commit();

            Assert.Throws<ArgumentException>(() =>
                tree.BeginUpdate().SwapNodes(1, 1).Commit());
        }

        [Test]
        public void SwapNodes_RejectsAncestorAndDescendantWithoutChangingTheTree()
        {
            var tree = CreateNestedTree();

            Assert.Throws<InvalidOperationException>(() =>
                tree.BeginUpdate().SwapNodes(1, 4).Commit());

            AssertVisible(tree, (1, 0), (2, 1), (3, 1), (4, 2));
        }

        [Test]
        public void MoveBelowDescendant_IsRejectedWithoutChangingTheTree()
        {
            var tree = CreateNestedTree();

            Assert.Throws<InvalidOperationException>(() =>
                tree.BeginUpdate().Move(1, 4).Commit());

            AssertVisible(tree, (1, 0), (2, 1), (3, 1), (4, 2));
        }

        [Test]
        public void DeepNesting_DoesNotUseRecursiveTraversal()
        {
            const int depth = 2000;
            var tree = new TreeStore<int, int>();
            var update = tree.BeginUpdate().InsertRoot(0, 0, isExpanded: true);
            for (var i = 1; i < depth; i++)
                update.Insert(i - 1, i, i, isExpanded: true);

            update.Commit();

            Assert.That(tree.VisibleCount, Is.EqualTo(depth));
            Assert.That(tree.GetVisibleRow(depth - 1).Depth, Is.EqualTo(depth - 1));
        }

        private static TreeStore<int, string> CreateNestedTree()
        {
            var tree = new TreeStore<int, string>();
            tree.BeginUpdate()
                .InsertRoot(1, "A", isExpanded: true)
                .Insert(1, 2, "A-1")
                .Insert(1, 3, "B", isExpanded: true)
                .Insert(3, 4, "B-1")
                .Commit();
            return tree;
        }

        private static void AssertVisible(TreeStore<int, string> tree, params (int id, int depth)[] expected)
        {
            Assert.That(tree.VisibleCount, Is.EqualTo(expected.Length));
            for (var i = 0; i < expected.Length; i++)
            {
                var row = tree.GetVisibleRow(i);
                Assert.That(row.Id, Is.EqualTo(expected[i].id), $"Unexpected ID at visible index {i}.");
                Assert.That(row.Depth, Is.EqualTo(expected[i].depth), $"Unexpected depth at visible index {i}.");
            }
        }
    }
}
