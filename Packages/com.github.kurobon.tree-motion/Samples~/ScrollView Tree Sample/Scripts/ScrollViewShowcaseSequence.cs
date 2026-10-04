using System;

namespace TreeMotion.Samples.ScrollView
{
    // The same IDs and final ordering are restored every cycle: no random or reload transitions.
    internal static class ScrollViewShowcaseSequence
    {
        internal static readonly string[] Captions =
        {
            "Collapse · A nested group", "Expand · Reveal the children",
            "Swap · Entire groups", "Swap · Back in place", "Move · A nested group",
            "Move · Across parents", "Insert · Three cards at once",
            "Swap · Stable item identities", "Update · Expand a card", "Update · Restore card height",
            "Remove · Three cards at once", "Swap · Restore the order"
        };

        internal static TreeNodeRecord<int, SampleItem>[] InitialSnapshot() => new[]
        {
            new TreeNodeRecord<int, SampleItem>(1, new SampleItem("Group 1", SampleItemType.Group)),
            new TreeNodeRecord<int, SampleItem>(2, new SampleItem("Item 1"), parentId: 1),
            new TreeNodeRecord<int, SampleItem>(3, new SampleItem("Item 2"), parentId: 1),
            new TreeNodeRecord<int, SampleItem>(4, new SampleItem("Item 3"), parentId: 1),
            new TreeNodeRecord<int, SampleItem>(5, new SampleItem("Group 2", SampleItemType.Group)),
            new TreeNodeRecord<int, SampleItem>(6, new SampleItem("Item 4"), parentId: 5),
            new TreeNodeRecord<int, SampleItem>(7, new SampleItem("Nested Group", SampleItemType.NestedGroup), parentId: 5),
            new TreeNodeRecord<int, SampleItem>(8, new SampleItem("Nested Item 1"), parentId: 7),
            new TreeNodeRecord<int, SampleItem>(9, new SampleItem("Nested Item 2"), parentId: 7),
            new TreeNodeRecord<int, SampleItem>(10, new SampleItem("Item 5"), parentId: 5),
        };

        internal static TreeChangeSet<int> ApplyStep(TreeStore<int, SampleItem> tree, int step)
        {
            var update = tree.BeginUpdate();
            const int newCardOffset = 100;
            switch (step)
            {
                case 0: update.SetExpanded(1, false); break;
                case 1: update.SetExpanded(1, true); break;
                case 2: update.SwapNodes(1, 5); break;
                case 3: update.SwapNodes(1, 5); break;
                case 4: update.Move(7, 1, 1); break;
                case 5: update.Move(7, 5, 1); break;
                case 6:
                    update
                        .Insert(1, newCardOffset + 1, new SampleItem("New card · A", SampleItemType.Card), 0)
                        .Insert(1, newCardOffset + 2, new SampleItem("New card · B", SampleItemType.Card), 1)
                        .Insert(1, newCardOffset + 3, new SampleItem("New card · C", SampleItemType.Card), 2); break;
                case 7: update.SwapNodes(2, 10); break;
                case 8: update.UpdateItem(newCardOffset + 2, new SampleItem("Expanded card · Same identity", SampleItemType.ExpandedCard)); break;
                case 9: update.UpdateItem(newCardOffset + 2, new SampleItem("New card · B", SampleItemType.Card)); break;
                case 10: update
                    .Remove(newCardOffset + 1)
                    .Remove(newCardOffset + 2)
                    .Remove(newCardOffset + 3); break;
                case 11: update.SwapNodes(2, 10); break;
                default: throw new ArgumentOutOfRangeException(nameof(step));
            }

            return update.Commit();
        }
    }
}
