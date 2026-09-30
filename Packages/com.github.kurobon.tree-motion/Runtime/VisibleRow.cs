namespace TreeMotion
{
    public readonly struct VisibleRow<TId>
    {
        public TId Id { get; }
        public int Depth { get; }
        public bool HasChildren { get; }
        public bool IsExpanded { get; }

        internal VisibleRow(TId id, int depth, bool hasChildren, bool isExpanded)
        {
            Id = id;
            Depth = depth;
            HasChildren = hasChildren;
            IsExpanded = isExpanded;
        }
    }
}
