# Advanced TreeMotion Sample

Open `TreeMotionSample.unity` and enter Play mode.

The scene demonstrates:

- Arbitrarily nested, expandable groups.
- API-style flat snapshot loading with parent IDs and sibling ordering; the sample records are
  intentionally unordered before loading.
- Batched insert, remove, and move operations using stable integer IDs.
- Nested group cards: every group provides a visual container for its children.
- Variable-height layout caching and pooling: only cards near the viewport have views.
- Prefab-backed pooling: item cards and group frames are authored as reusable prefab assets;
  runtime code only instantiates, binds, positions, and recycles them.
- Animated insert, remove, move, expand, and collapse operations. Rows fade and scale while
  offsets and group-frame sizes interpolate in the flat viewport hierarchy.
- Interrupted transitions retarget from their current displayed values, so repeated input does not
  snap rows back to an earlier position.
- A live node, visible-row, and pooled-view count in the footer.

Select a row and use the toolbar to edit the tree. The row arrow also expands or collapses a group.
Double-click a row to replace its data while preserving its stable ID and cross-fading the old and
new prefab views.

