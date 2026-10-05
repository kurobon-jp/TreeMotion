![TreeMotion — Animated trees for Unity UI](docs/images/tree-motion-banner.png)

# Tree Motion

[English](README.md) | [日本語](README.ja.md)

A virtualized tree view for Unity uGUI, with animated changes to nested Groups and Items.

Insert, remove, move, swap, and expand nodes without replacing the entire tree.
Unique IDs identify data, and views are reused per prefab.

<img src="docs/images/tree-motion-demo.gif" alt="TreeMotion animation demo" height="512">

## Features

- Groups can be nested at multiple levels, with prefabs, child spacing, and padding configured per Group.
- Animated insert, remove, move, swap, expand/collapse, and item size changes.
- Viewport culling and pools keyed by prefab reference.
- Fixed-width and stretched prefabs using their authored Anchors and Pivot.
- Batched updates and per-node sizes supplied by your adapter.
- Per-operation duration and awaitable completion using standard Task.
- Optional fade, interaction blocking, and custom presentation handlers.

## Installation

In Unity's Package Manager, select **Add package from git URL** and enter:

```text
https://github.com/kurobon-jp/TreeMotion.git?path=Assets/
```

## Set up a ScrollView

```text
ScrollView                  ScrollRect + TreeMotionScrollView
└─ Viewport                 RectTransform + viewport mask
   └─ Content               RectTransform + TreeMotionGroupView
```

Assign ScrollRect, Viewport, and Content to TreeMotionScrollView. On Content's TreeMotionGroupView,
assign **Content itself** as ChildrenFrame. Its ChildrenSpacing and ChildrenPadding control the
top-level layout; Content is a layout container, not a data node.

<img src="docs/images/scrollview.png" width="300"/> <img src="docs/images/content1.png" width="300"/>

Leaf prefabs need a RectTransform and your own binding components. Group prefabs also need
TreeMotionGroupView and a **ChildrenFrame** stretched on the vertical axis.

```text
Group prefab                TreeMotionGroupView
├─ Header                   Optional authored UI
└─ ChildrenFrame            RectTransform stretched on both axes
```

Frame insets reserve space for headings and decorations. Child spacing and padding are configured
on the parent Group. Group size is calculated from frame insets and child extents.

At runtime, Group views and child views are all placed directly under Content. ChildrenFrame
defines the layout region; it does not become the parent of generated child views.

## Provide data and prefabs

Implement `ITreeMotionAdapter<TId, TItem>` to select prefabs, supply sizes, and bind your UI.
TreeStore holds the data, the Adapter maps it to UI, and TreeMotionBinding applies changes to
the connected TreeMotionScrollView.
This example uses an enum stored in each item and assumes one TMP label per prefab:

```csharp
public enum EntryKind { Item, Group }

public sealed class Entry
{
    public string Label;
    public EntryKind Kind;
    public float Size;
}

public sealed class EntryAdapter : TreeMotion.ITreeMotionAdapter<int, Entry>
{
    public UnityEngine.GameObject ItemPrefab;
    public UnityEngine.GameObject GroupPrefab;

    public UnityEngine.GameObject GetItemPrefab(int id, Entry item)
        => item.Kind == EntryKind.Group ? GroupPrefab : ItemPrefab;
    public float GetItemSize(int id, Entry item) => item.Size;

    public void Bind(UnityEngine.GameObject view, int id, Entry item,
        TreeMotion.VisibleRow<int> row)
    {
        // Bind your own UI components here.
        view.GetComponentInChildren<TMPro.TMP_Text>().text = item.Label;
    }
}
```

GetItemPrefab selects the prefab directly; node IDs identify data and should not determine appearance.
`GetItemSize` is called per leaf during layout, including after updates and moves. Nodes sharing
the same prefab can have different sizes. Group sizes are calculated by TreeMotion, so
`GetItemSize` is not called for Group prefabs.

Load the initial tree and connect it to the ScrollView:

```csharp
using TreeMotion;

var tree = new TreeStore<int, Entry>();
tree.LoadSnapshot(new[]
{
    new TreeNodeRecord<int, Entry>(1,
        new Entry { Label = "Group", Kind = EntryKind.Group }, isExpanded: true),
    new TreeNodeRecord<int, Entry>(2,
        new Entry { Label = "Item", Size = 40f }, parentId: 1)
});

var adapter = new EntryAdapter { ItemPrefab = itemPrefab, GroupPrefab = groupPrefab };
var binding = scrollView.Bind(tree, adapter);
```

Bind returns a `TreeMotionBinding<TId>`: a connection handle for Apply, Reload, Refresh, and
animation completion, rather than a UI View. Rebinding replaces the connection and cancels the
previous binding's pending waits; subsequent updates use the new handle.

`scrollView`, `itemPrefab`, and `groupPrefab` are your assigned scene/prefab references.
Omit parentId for a root; pass it for a child. ID `0` is valid and has no special meaning.
Sibling order follows input order unless siblingIndex is specified. Records can appear before
their parents. Duplicate IDs, missing parents, and cycles are rejected.

## Update the tree

Commit changes to TreeStore, then apply them to the view:

```csharp
var changes = tree.BeginUpdate()
    .Insert(1, 3, new Entry { Label = "New item", Size = 56f })
    .Update(2, new Entry { Label = "Updated item", Size = 64f })
    .Commit();

binding.Apply(changes, duration: 0.4f);
```

Other operations include `Remove(id)`, `Move(id, parentId, index)`, `MoveToRoot(id, index)`,
`Swap(firstId, secondId)`, and `Expanded(id, expanded)`. Move/Swap carries a Group's subtree.
Swap preserves each node's ID, item, and expanded state; ancestor/descendant swaps are rejected.
Move/Swap targets and their descendants render in front during the transition.

Use LoadSnapshot for initial/full reloads, and BeginUpdate for subsequent changes. Call
`binding.Reload()` after replacing a snapshot. `binding.Refresh(id)` rebinds presentation-only state,
such as selection; change the item through Update to update its size or content.

## Operations

Create a batch with `tree.BeginUpdate()`, call the operations below, then `Commit()` once.
Pass the returned changes to `binding.Apply` or `binding.ApplyAsync` to update the display.
Committing alone changes data, not the connected UI.

| Operation | API | Behavior | Animation and notes |
| --- | --- | --- | --- |
| **Insert** | `Insert(parentId, id, item, index = -1, isExpanded = false)` <br/> `InsertRoot(id, item, index = -1, isExpanded = false)` | Adds a child or top-level node. IDs must be unique. Omitting index appends; `0` inserts at the beginning. A Group and its children can be inserted in one batch. | Visible nodes receive entering presentation; surrounding nodes and Group extents adjust. Children inserted into a collapsed Group stay hidden until it expands. |
| **Remove** | `Remove(id)` | Deletes a node **and all its descendants** from TreeStore. | Visible views receive exiting presentation, then return to their pools. Surrounding nodes close the gap. During exit, the view can remain visible after its data has been removed. |
| **Move** | `Move(id, parentId, index = -1)` / `MoveToRoot(id, index = -1)` | Changes parent/order while retaining ID, item, expanded state, and descendants. Index refers to the destination list after removing the node from its previous position. | Visible views interpolate to the destination, including horizontal geometry changes when depth changes. The target subtree renders in front. Moving into/out of a collapsed region hides/reveals nodes. Moving under one's own descendant is rejected. |
| **Expanded** | `Expanded(id, isExpanded)` | `true` reveals children; `false` hides them **without deleting data**. Descendants retain their own expanded states. | Group extents and surrounding positions animate. Children entering/leaving the visible sequence receive Entering/Exiting presentation roles. |
| **Update** | `Update(id, item)` | Replaces the item while retaining ID, parent, children, and expanded state. The Adapter re-evaluates the prefab and leaf size, then binds the view again. | Size changes animate; content-only changes are reflected immediately. A prefab change replaces the view with one from the new prefab's pool: **appearance switches immediately**, without automatic entry/exit or crossfade. |
| **Swap** | `Swap(firstId, secondId)` | Exchanges two nodes' positions, including across parents. Each carries its item, expanded state, and subtree. | Both target subtrees render in front while moving. Views retain their identity when their prefab is unchanged. Identical IDs and ancestor/descendant pairs are rejected. |

Prefab definitions and pools are keyed by prefab reference. Updates that return the same prefab retain the view.
A node with children must resolve to a Group prefab.

## Await animation completion

```csharp
await binding.ApplyAsync(changes, duration: 0.4f, cancellationToken: token);
PlayNextEffect();
```

- Omitting duration uses the ScrollView's default. Zero applies immediately.
- Empty changes and operations with no animation return a completed Task.
- A later Apply/ApplyAsync, Reload, adapter replacement, or View destruction cancels the wait.
- Token cancellation stops waiting; an already started animation continues.
- A pre-canceled token skips View application. It does not undo a committed TreeStore update.

UniTask users can convert the returned Task with `.AsUniTask()`; the package has no UniTask dependency.

## Customize presentation

TreeMotion manages geometry and pooling. It does not automatically add a CanvasGroup or change
alpha, scale, or interaction flags. Add **TreeMotionFade** to the prefab when needed;
it fades entering/exiting views and disables raycasts while animating.

To add custom effects, attach a component that implements `ITreeMotionPresentationHandler` to the prefab's root GameObject:

```csharp
using TreeMotion;
using UnityEngine;

public sealed class Scaling : MonoBehaviour, ITreeMotionPresentationHandler
{
    public void ResetPresentation()
    {
        transform.localScale = Vector3.one;
    }

    public void SetTreeMotionPresentation(in TreeMotionPresentation presentation)
    {
        var scale = presentation.Cause switch
        {
            TreeChangeKind.Expand => presentation.Progress,
            TreeChangeKind.Collapse => 1f - presentation.Progress,
            _ => 1f
        };

        transform.localScale = new Vector3(1f, scale, 1f);
    }
}
```

Add Scaling to the prefab's root GameObject.
This example changes the vertical scale when a Group expands or collapses and uses unit scale otherwise.

Handlers receive kind, Entering/Visible/Exiting role, and linear progress.
`presentation.Cause` uses `TreeChangeKind` to distinguish Insert, Remove, Expand, Collapse,
Move, Swap, and Update. It is null when there is no cause.
For example, insertion uses `Role = Entering, Cause = Insert`, while children revealed by
expanding a Group use `Role = Entering, Cause = Expand`.
ResetPresentation restores
state when views are pooled. Multiple handlers can combine effects if they write different
properties.

## Current scope

- Layout is vertical; horizontal geometry follows authored prefab Anchors/Pivot.
- Text wrapping is not measured automatically; your adapter supplies leaf sizes.
- Parent-child relationships in the data do not match the GameObject hierarchy in Unity.
