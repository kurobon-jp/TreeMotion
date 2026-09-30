# Changelog

## [0.1.0] - 2026-09-29

- Created the initial package structure.
- Added the stable-ID tree store and incremental visible-row index.
- Added batched insert, remove, move, expand, collapse, and item update operations.
- Added correctness tests and opt-in large-tree benchmarks.
- Added allocation-free fixed-height visible-range calculations.
- Added chunked variable-height indexing with stable-ID measurement updates and prefix queries.
- Added randomized correctness coverage and opt-in 100,000-row layout benchmarks.
- Added an interactive uGUI sample scene with nested group cards and pooled viewport rendering.
- Replaced runtime-created sample visuals with reusable item and group-frame prefabs.
- Added retargetable, stable-ID animation tracks for insert, remove, move, and resize operations.
- Added allocation-free per-frame animation lookup and interrupted-transition continuity tests.
- Animated sample rows, content height, and nested group frames without parent tick propagation.
- Added stable-ID item swap changes and paired movement animation support.
- Added atomic flat snapshot loading for API/master data with parent, order, and cycle validation.
- Updated the sample to initialize from unordered `TreeNodeRecord` data instead of chained inserts.
- Added `TreeMotionScrollView`, a prefab-backed fixed-height view layer that owns pooling, culling,
  layout, and structural animation.
- Changed `TreeMotionScrollView` to lease views by stable ID and bind only rows entering the
  viewport or receiving presentation data changes; removed the overscan setting.
- Added a minimal Basic Tree Sample and renamed the feature-rich sample listing to Advanced
  TreeMotion Sample.
- Moved hierarchy indentation out of `TreeMotionScrollView`; item views now own their visual
  indentation using `VisibleRow.Depth`.
- Replaced library-defined opacity and scale presentation with animation kind, role, and normalized
  progress so item views own their visual effects.
- Split item updates from swaps: `UpdateItem` changes one node's item, while `SwapNodes` exchanges
  two nodes' tree positions together with their complete child subtrees and expanded state.

