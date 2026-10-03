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

        internal static TreeNodeRecord<int, string>[] InitialSnapshot() => new[]
        {
            new TreeNodeRecord<int, string>(1, "Group 1", isExpanded: true),
            new TreeNodeRecord<int, string>(2, "Item 1", parentId: 1),
            new TreeNodeRecord<int, string>(3, "Item 2", parentId: 1),
            new TreeNodeRecord<int, string>(4, "Item 3", parentId: 1),
            new TreeNodeRecord<int, string>(5, "Group 2", isExpanded: true),
            new TreeNodeRecord<int, string>(6, "Item 4", parentId: 5),
            new TreeNodeRecord<int, string>(7, "Nested Group", parentId: 5, isExpanded: true),
            new TreeNodeRecord<int, string>(8, "Nested Item 1", parentId: 7),
            new TreeNodeRecord<int, string>(9, "Nested Item 2", parentId: 7),
            new TreeNodeRecord<int, string>(10, "Item 5", parentId: 5),
        };

        internal static TreeChangeSet<int> ApplyStep(TreeStore<int, string> tree, int step)
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
                        .Insert(1, newCardOffset + 1, "New card · A", 0)
                        .Insert(1, newCardOffset + 3, "New card · B", 1)
                        .Insert(1, newCardOffset + 5, "New card · C", 2); break;
                case 7: update.SwapNodes(2, 10); break;
                case 8: update.UpdateItem(newCardOffset + 3, "Expanded card · Same identity"); break;
                case 9: update.UpdateItem(newCardOffset + 3, "New card · B"); break;
                case 10: update
                    .Remove(newCardOffset + 1)
                    .Remove(newCardOffset + 3)
                    .Remove(newCardOffset + 5); break;
                case 11: update.SwapNodes(2, 10); break;
                default: throw new ArgumentOutOfRangeException(nameof(step));
            }

            return update.Commit();
        }
    }
}
