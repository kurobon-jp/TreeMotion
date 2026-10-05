namespace TreeMotion.Samples.ScrollView
{
    public enum SampleItemType
    {
        Item,
        Card,
        Group
    }

    public struct SampleItem
    {
        public string Label { get; }
        public SampleItemType Type { get; }
        public float Size { get; }

        public SampleItem(string label, SampleItemType type = SampleItemType.Item, float size = 32f)
        {
            Label = label;
            Type = type;
            Size = size;
        }
    }
}
