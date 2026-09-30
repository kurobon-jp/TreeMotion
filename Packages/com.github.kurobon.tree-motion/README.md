# TreeMotion

TreeMotion provides a virtualized tree view for Unity uGUI with animated structural updates.

The first core milestone provides stable node identity, arbitrary nesting, incremental visible-row
maintenance, batched changes, and change records suitable for driving view animations.

## Quick start

Import **Basic Tree Sample** from Package Manager first. Its controller only loads data, binds the
item prefab, and forwards expand/collapse changes to `TreeMotionScrollView`:

```csharp
tree.LoadSnapshot(records);

view = scrollView.SetDataSource(
    tree,
    itemPrefab,
    itemHeight: 56f,
    (rowView, id, item, row) => rowView.Bind(id, item, row, Toggle));

// After changing the store:
view.Apply(changes);
```

`TreeMotionScrollView` owns viewport culling, prefab pooling, row positioning, content sizing, and
insert/remove/move/replace animation. Pooled views are leased by stable ID, so scrolling only binds
rows entering the viewport; rows that remain visible keep their existing `MonoBehaviour` instance.
Use **Advanced TreeMotion Sample** when you need an example of variable-height rows, nested group
frames, structural editing, and diagnostics.

## Data model

```csharp
var tree = new TreeStore<int, Item>();

TreeChangeSet<int> changes = tree.BeginUpdate()
    .InsertRoot(1, rootItem, isExpanded: true)
    .Insert(1, 2, childItem)
    .Move(2, anotherParentId)
    .UpdateItem(1, updatedItem)
    .SwapNodes(2, anotherNodeId)
    .Commit();
```

`TreeStore` does not rebuild or compare a full snapshot when a batch is committed. It updates the
affected contiguous visible range and reports `Insert`, `Remove`, `Move`, and `Update` changes.
Consumers can read visible rows by index without allocating:

```csharp
VisibleRow<int> row = tree.GetVisibleRow(index);
```

Initial API or master data should be normalized into flat snapshot records. Input order does not
matter; `SiblingIndex` defines ordering within each parent:

```csharp
var records = new[]
{
    TreeNodeRecord<int, Item>.Child(2, 1, childItem, siblingIndex: 0),
    TreeNodeRecord<int, Item>.Root(1, rootItem, siblingIndex: 0, isExpanded: true)
};

tree.LoadSnapshot(records);
```

`LoadSnapshot` validates duplicate IDs, missing parents, duplicate sibling indices, and cycles
before atomically replacing the store. It builds the visible sequence once and is intended for
initial or complete reloads. Use `BeginUpdate` for subsequent animated deltas.

The current visible sequence uses `List<T>` behind an internal boundary. Benchmarks are included so
it can be replaced with a chunked sequence if large middle insertions become a measured bottleneck.

`FixedHeightLayout` maps scroll offsets to an end-exclusive `VisibleRange` without allocating.

`VariableHeightLayoutIndex<TId>` supports rows whose height is only known after their pooled
`MonoBehaviour` view has been bound and measured. Add rows with an estimated height, then report the
actual height by stable ID:

```csharp
var layout = new VariableHeightLayoutIndex<int>(defaultHeight: 72f, spacing: 8f);
layout.Reset(visibleNodeIds);

// After binding a pooled view and rebuilding that view's local layout:
float delta = layout.SetHeight(nodeId, view.RectTransform.rect.height);
```

The index stores rows in bounded chunks and keeps Fenwick prefix trees over chunk counts and extents.
Offset lookup, ID lookup, measured-height updates, and visible-range queries do not allocate. A group
does not need to receive every child animation tick: its visual size can be derived from the indexed
range, while temporary animation deltas are composed by the renderer in the flat viewport hierarchy.

`TreeMotionAnimation<TId>` provides that flat transition state. Retarget it after a structural
update; stable IDs automatically produce insert, remove, move, and resize tracks. Retargeting an
animation already in progress continues from the currently displayed values.

```csharp
var motion = new TreeMotionAnimation<int>();
motion.Snap(initialLayout);

// After the TreeStore and layout index have been updated:
motion.Retarget(targetLayout, duration: 0.25f, changes);

// Per frame. GetValue and TryGetValue do not allocate after warmup.
motion.Advance(unscaledDeltaTime);
if (motion.TryGetValue(nodeId, out var value))
{
    view.SetLayout(value.Offset, value.Size);
    view.SetProgress(value.Kind, value.Progress);
}
```

`TreeMotionAnimationValue` contains layout and normalized progress only; it does not prescribe
opacity, scale, color, or any other visual effect. `TreeMotionScrollView` passes the animation kind,
presentation role, and eased progress to `ITreeMotionItemView.SetTreeMotionPresentation`, leaving
the visual treatment to each item prefab.

For virtualized rendering, query only target IDs inside the visible range plus any already-leased
exiting views. Parent group frames use a separate animation instance keyed by the same stable IDs;
their sizes are interpolated directly, without propagating child animation ticks up the hierarchy.
`UpdateItem` changes the item stored by one node. `SwapNodes` exchanges the tree positions of two
nodes. Each node keeps its stable ID, item, complete child subtree, and expanded state. Swapping an
ancestor with its own descendant is rejected because it would create a cycle. Its `TreeChange`
exposes `FirstId` and `SecondId`, allowing renderers to animate both stable-ID subtrees toward their
new positions without replacing their views.

