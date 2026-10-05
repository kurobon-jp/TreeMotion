# ScrollView Tree Sample

Import **ScrollView Tree Sample** from Package Manager, open `ScrollViewTreeSample.unity`,
and enter Play mode. This sample is independent of Basic Tree Sample.

The automatic demo is a deterministic showcase intended for recording. It opens with a settled
hierarchy, then performs Group collapse/expand, whole-Group Swap, moving an expanded nested Group between parents,
three simultaneous alternate-card inserts, Item Swap, card expansion/contraction through Update, and three simultaneous
removals. It restores the initial ordering, IDs, labels and expanded states through animated
operations, then repeats without a snapshot reload or abrupt visual reset.

Each transition completes before **Beat Duration** (default 0.9 seconds) holds its settled frame.
**Opening Hold** and **Loop Hold** default to 2 seconds. With a 0.25-second animation duration, one
loop takes about 16 seconds, plus the opening hold on first playback. Timing follows actual
animation completion rather than assuming a fixed renderer duration. For recording, use a Game
view tall enough to show both Groups and the added cards. Group 1 briefly collapses
at the beginning; it expands on the next step. Every Group retains children throughout the showcase.

Clicking a Group stops the automatic showcase and gives manual control.
Use the controller's **Restart Showcase** context menu in Play mode for a fresh take. Disable
**Animate Changes** before Play mode for manual-only operation.

The showcase runs as an async Task, awaits `ApplyAsync` for each transition, and uses cancellable
`Task.Delay` for real-time holds (independent of Time.timeScale). Manual control, restart,
or destroying the controller cancels the current sequence. Restart creates a fresh cancellation
source; an older sequence cannot continue into the new take. Cancellation is handled at the Unity
entry point, while unexpected failures are logged.

`ScrollViewTreeSampleController` loads unordered snapshot records, binds the item prefab,
and awaits committed changes through `TreeMotionBinding.ApplyAsync`. It implements `ITreeMotionAdapter<int, SampleItem>` to select
GameObject prefab and per-node size and to bind the concrete View. Parent Group settings own spacing.
`TreeMotionScrollView` owns layout, viewport culling, prefab pooling, content sizing, and animation.
The row component is an ordinary MonoBehaviour with an optional presentation handler. It owns
label binding and optional Card slide presentation. The prefabs explicitly include TreeMotionFade
for fading and raycast blocking while animating. The scroll view
owns hierarchy indentation and group-frame geometry.

Leaf prefabs contain a stretched `Visual` child holding the background, button and labels.
The showcase's inserted Cards slide in from the right and slide out to the right while fading.
The row's **Slide Distance** defaults to 120 UI units; a negative value selects the left side.
Only Visual moves, leaving TreeMotion's Root layout untouched. Slide and Fade use the same
presentation progress, so `ApplyAsync` waits for both. Pool reset restores the authored Visual
position. Ordinary Items and Groups do not slide when revealed by Expand.

The scene uses whole Group prefabs containing Header and ChildrenFrame. Ordinary and nested
Groups select their prefab directly. Each `SampleItem` explicitly stores its `Label` and
`SampleItemType`: Item, Card, or Group. `Resized` records the Card's size state. IDs identify nodes only;
neither ID values nor label text determine the prefab or height. Inserted Cards select the
alternate Item prefab. A Card with `Resized = true` uses the same alternate prefab with an
80-pixel height (normally 40). Update keeps its node ID and animates its height and surrounding
layout; changing the prefab exchanges the pooled view. A label-only Update does not change layout.
Pools are keyed by prefab reference. Changing `Resized` retains the same View.

Update replaces the item's data with a new `SampleItem`, toggling `Resized` without changing its type.
The data is immutable so changes go through `Update` instead of silently mutating stored items.
Leaf heights are 32, 40, and 80 pixels respectively. Group height is calculated by TreeMotion from
ChildrenFrame and children; the adapter does not return a placeholder height for Group types.

The showcase only moves nodes into explicitly defined Groups and never promotes arbitrary Items.
The renderer does not infer Group identity from child count. Header and ChildrenFrame
remain inside the Group instance; child Views are independent siblings under Content.
