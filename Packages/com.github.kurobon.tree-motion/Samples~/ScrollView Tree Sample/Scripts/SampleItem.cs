namespace TreeMotion.Samples.ScrollView
{
    public enum SampleItemType
    {
        Item,
        Card,
        Group,
        NestedGroup,
        ExpandedCard
    }

    public sealed class SampleItem
    {
        public string Label { get; }
        public SampleItemType Type { get; }

        public SampleItem(string label, SampleItemType type = SampleItemType.Item)
        {
            Label = label;
            Type = type;
        }
    }
}
