# TreeMotion

TreeMotion provides a virtualized tree view for Unity uGUI with animated structural updates.

The first core milestone provides stable node identity, arbitrary nesting, incremental visible-row
maintenance, batched changes, and change records suitable for driving view animations.

## Quick start

Import **ScrollView Tree Sample** for nested Group prefabs and a repeatable recording showcase of
expand/collapse, Group moves and swaps, batch inserts/removals and updates. All samples use `TreeMotionScrollView`.
**Basic Tree Sample** shows the simple single-type data source; **Advanced TreeMotion Sample**
adds selection and toolbar editing.

```csharp
tree.LoadSnapshot(records);
view = scrollView.SetDataSource(tree, dataSource);
view.Apply(tree.BeginUpdate().UpdateItem(id, updatedItem).Commit());
```

Implement `ITreeMotionDataSource<TId, TItem>`:

```csharp
public int GetItemType(int id, Item item) => item.Type;
public GameObject GetItemPrefab(int itemType) => prefabs[itemType];
public float GetItemHeight(int itemType) => heights[itemType];
public void Bind(GameObject view, int id, Item item, VisibleRow<int> row)
{
    // Bind the concrete View selected by ItemType.
}
```

Each type selects a GameObject prefab with RectTransform. Items require no TreeMotion component;
DataSource supplies their fixed height. Groups use TreeMotionGroupView and a stretched child
ChildrenFrame. Frame insets reserve optional authored UI; the library does not reference Header.
Child spacing and padding belong to the parent Group. Content itself has TreeMotionGroupView,
with ChildrenFrame pointing to Content: this RootGroup controls top-level spacing and padding.
Group identity comes from the prefab, independent of child count. GetItemHeight is not called
for Groups; whole Group height is computed from frame insets and children.

Pools are separated by ItemType and leased by stable node ID. Visible nodes retain their instances;
a type change returns the old view to its own pool and binds a view of the new type. Data changes
must be followed by `Apply`; `Refresh(id)` rebinds presentation-only state such as selection.

Group and child Views are siblings under Content. Each Group instance contains only its authored
Header, ChildrenFrame and decorations, not the dynamically generated child Views. The viewport
clips them all. See [Prefab authoring](Documentation~/group-prefabs.md) for setup and constraints.

The renderer animates vertical geometry and horizontal insets, including current positions when
retargeted. Groups remain leased while their frame intersects the viewport, even when their header
is offscreen. A Group's descendants are independently culled. Each Item type has a fixed
height; dynamic measured heights remain a separate layout utility below.

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

Initial API or master data should be normalized into flat snapshot records. When `siblingIndex`
is omitted, roots and children under each parent follow their order in the input array. Children
may still appear before their parents. Specify `siblingIndex` to restore ordering from unordered data:

Use `new TreeNodeRecord<TId, TItem>(id, item)` for a root, and pass `parentId` for a child.
There is no reserved root ID: `0` is valid for both node IDs and parent IDs. For reference-type
IDs, a null `parentId` also means a root. Use named `parentId` and `siblingIndex` arguments to
make their meaning clear.

```csharp
var records = new[]
{
    new TreeNodeRecord<int, Item>(2, childItem, parentId: 1, siblingIndex: 0),
    new TreeNodeRecord<int, Item>(1, rootItem, siblingIndex: 0, isExpanded: true)
};

tree.LoadSnapshot(records);
```

When omitted and explicit indices are mixed, explicit values are reserved first. Omitted values
receive the lowest unused nonnegative indices in encounter order, independently for each parent.
`SiblingIndex` is nullable; `null` means no explicit order was specified.

`LoadSnapshot` validates duplicate IDs, missing parents, duplicate explicit sibling indices, and cycles
before atomically replacing the store. It builds the visible sequence once and is intended for
initial or complete reloads. Use `BeginUpdate` for subsequent animated deltas.

The current visible sequence uses `List<T>` behind an internal boundary. Benchmarks are included so
it can be replaced with a chunked sequence if large middle insertions become a measured bottleneck.

`TreeMotionAnimation<TId>` provides flat transition state. Retarget it after a structural
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

`TreeMotionAnimationValue` contains layout and normalized progress only. `TreeMotionScrollView`
does not add CanvasGroup or change alpha, scale or interaction flags. Optional root components
implementing `ITreeMotionPresentationHandler` receive animation kind, role and eased progress.
Implement `ResetPresentation` to restore authored state when pooled Views are returned or reused.
Multiple handlers can compose effects, provided they do not write the same properties.
Add `TreeMotionFade` explicitly for fading and `TreeMotionInteractionBlocker` explicitly to prevent
interaction during entry/exit. Each owns its CanvasGroup settings; neither is required for Groups.

For virtualized rendering, the renderer queries current animation tracks so offscreen nodes moving
into the viewport are also included. A Group's whole extent is interpolated directly; Authored decoration remains untouched and ChildrenFrame size follows the current group extent, without propagating child
animation ticks up the hierarchy.
`UpdateItem` changes the item stored by one node. `SwapNodes` exchanges the tree positions of two
nodes. Each node keeps its stable ID, item, complete child subtree, and expanded state. Swapping an
ancestor with its own descendant is rejected because it would create a cycle. Its `TreeChange`
exposes `FirstId` and `SecondId`, allowing renderers to animate both stable-ID subtrees toward their
new positions without replacing their views.

Await layout completion with `ApplyAsync` on Unity's main thread:

```csharp
await view.ApplyAsync(tree.BeginUpdate().Move(nodeId, parentId).Commit(), cancellationToken);
PlayNextEffect();
```

The returned standard `Task` can be converted with UniTask's `AsUniTask()` in projects that use
UniTask; TreeMotion does not depend on UniTask. Completion occurs after the final layout and
presentation frame is applied. Empty changes or changes with no animation complete immediately.
Every subsequent `Apply`/`ApplyAsync` (including empty changes), `Reload`, data-source replacement,
or View destruction cancels the previous wait. A supplied token cancels only the wait, leaving
an already started animation running; a pre-canceled token skips applying the changes to the View.
The TreeStore commit has already happened and is never rolled back. Independent effects started
by presentation handlers are not awaited. Use `Apply` when no completion wait is needed.

