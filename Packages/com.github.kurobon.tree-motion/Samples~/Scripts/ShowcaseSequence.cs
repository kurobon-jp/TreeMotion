using System;

namespace TreeMotion.Samples
{
    // The same IDs and final ordering are restored every cycle: no random or reload transitions.
    internal static class ShowcaseSequence
    {
        internal const int StepCount = 12;
        
        internal static TreeNodeRecord<int, SampleItem>[] InitialSnapshot() => new[]
        {
            new TreeNodeRecord<int, SampleItem>(1, new SampleItem("Group 1", SampleItemType.Group)),
            new TreeNodeRecord<int, SampleItem>(2, new SampleItem("Item 1"), parentId: 1),
            new TreeNodeRecord<int, SampleItem>(3, new SampleItem("Item 2"), parentId: 1),
            new TreeNodeRecord<int, SampleItem>(4, new SampleItem("Item 3"), parentId: 1),
            new TreeNodeRecord<int, SampleItem>(5, new SampleItem("Item 4")),
            new TreeNodeRecord<int, SampleItem>(6, new SampleItem("Group 2", SampleItemType.Group)),
            new TreeNodeRecord<int, SampleItem>(7, new SampleItem("Item 5"), parentId: 6),
            new TreeNodeRecord<int, SampleItem>(8, new SampleItem("Group 3", SampleItemType.Group), parentId: 6),
            new TreeNodeRecord<int, SampleItem>(9, new SampleItem("Item 6"), parentId: 8),
            new TreeNodeRecord<int, SampleItem>(10, new SampleItem("Item 7"), parentId: 8),
            new TreeNodeRecord<int, SampleItem>(11, new SampleItem("Item 8")),
        };

        internal static TreeChangeSet<int> ApplyStep(TreeStore<int, SampleItem> tree, int step)
        {
            var update = tree.BeginUpdate();
            const int newCardOffset = 100;
            switch (step)
            {
                case 0: update.Expand(1, false); break;
                case 1: update.Expand(1, true); break;
                case 2: update.Swap(5, 11); break;
                case 3: update.Swap(5, 11); break;
                case 4: update.Move(8, 1, 1); break;
                case 5: update.Move(8, 6, 1); break;
                case 6:
                    update
                        .Insert(1, newCardOffset + 1, new SampleItem("New card · A", SampleItemType.Card, 40f), 0)
                        .Insert(1, newCardOffset + 2, new SampleItem("New card · B", SampleItemType.Card, 40f), 1)
                        .Insert(1, newCardOffset + 3, new SampleItem("New card · C", SampleItemType.Card, 40f), 2); break;
                case 7: update.Swap(3, 10); break;
                case 8:
                    update.Update(newCardOffset + 2,
                        new SampleItem("Resized\nNew card · B", SampleItemType.Card, 80f)); break;
                case 9: update.Update(newCardOffset + 2, new SampleItem("New card · B", SampleItemType.Card)); break;
                case 10:
                    update
                        .Remove(newCardOffset + 1)
                        .Remove(newCardOffset + 2)
                        .Remove(newCardOffset + 3); break;
                case 11: update.Swap(3, 10); break;
                default: throw new ArgumentOutOfRangeException(nameof(step));
            }

            return update.Commit();
        }
    }
}
