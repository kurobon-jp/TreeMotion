# ScrollView Tree Sample

Import **ScrollView Tree Sample** from Package Manager, open `ScrollViewTreeSample.unity`,
and enter Play mode. This sample is independent of Basic Tree Sample.

The automatic demo is a deterministic showcase intended for recording. It opens with a settled
hierarchy, then performs nested Group collapse/expand, whole-Group Swap, moving an expanded nested Group between parents,
three simultaneous alternate-card inserts, Item Swap, card expansion/contraction through Update, and three simultaneous
removals. It restores the initial ordering, IDs, labels and expanded states through animated
operations, then repeats without a snapshot reload or abrupt visual reset.

Each transition completes before **Beat Duration** (default 0.9 seconds) holds its settled frame.
**Opening Hold** and **Loop Hold** default to 2 seconds. With a 0.25-second animation duration, one
loop takes about 16 seconds, plus the opening hold on first playback. Timing follows actual
animation completion rather than assuming a fixed renderer duration. For recording, use a Game
view tall enough to show both Groups and the added cards. Only the nested Group briefly collapses
at the beginning; it expands on the next step. Every Group retains children throughout the showcase.

Optionally assign a TMP Text to **Caption** to describe the current operation without creating
extra UI automatically. Clicking a Group stops the automatic showcase and gives manual control.
Use the controller's **Restart Showcase** context menu in Play mode for a fresh take. Disable
**Animate Changes** before Play mode for manual-only operation.

The showcase runs as an async Task, awaits `ApplyAsync` for each transition, and uses cancellable
`Task.Delay` for real-time holds (independent of Time.timeScale). Manual control, restart, disabling,
or destroying the controller cancels the current sequence. Restart creates a fresh cancellation
source; an older sequence cannot continue into the new take. Cancellation is handled at the Unity
entry point, while unexpected failures are logged.

`ScrollViewTreeSampleController` loads unordered snapshot records, binds the item prefab,
and awaits committed changes through `TreeMotionViewController.ApplyAsync`. It implements `ITreeMotionDataSource<int, SampleItem>` to select
ItemType, GameObject prefab and height and to bind the concrete View. Parent Group settings own spacing.
`TreeMotionScrollView` owns layout, viewport culling, prefab pooling, content sizing, and animation.
The row component is an ordinary MonoBehaviour with an optional presentation handler. It owns
label alignment, color and custom entry/exit scale. The prefabs explicitly include TreeMotionFade
and TreeMotionInteractionBlocker for fade and interaction rules. The scroll view
owns hierarchy indentation and group-frame geometry.

The scene uses whole Group prefabs containing Header and ChildrenFrame. Ordinary and nested
Group types select separate prefabs. Each `SampleItem` explicitly stores its `Label` and
`SampleItemType`: Item, Card, Group, NestedGroup, or ExpandedCard. IDs identify nodes only;
neither ID values nor label text determine the prefab or height. Inserted Cards select the
alternate Item prefab. The expanded card uses the same alternate prefab in a separate ItemType with an
80-pixel height (normally 40). Update keeps its node ID and animates its height and surrounding
layout; changing ItemType exchanges the pooled view. A label-only Update does not change layout.
Pools are separated by type. Empty Groups retain their Group appearance in manual operation.

Update replaces the item's data with a new `SampleItem`, changing Card to ExpandedCard and back.
The data is immutable so changes go through `UpdateItem` instead of silently mutating stored items.
Leaf heights are 32, 40, and 80 pixels respectively. Group height is calculated by TreeMotion from
ChildrenFrame and children; the data source does not return a placeholder height for Group types.

The showcase only moves nodes into explicitly defined Groups and never promotes arbitrary Items.
The renderer does not infer Group identity from child count. Header and ChildrenFrame
remain inside the Group instance; child Views are independent siblings under Content.
