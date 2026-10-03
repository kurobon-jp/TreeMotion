# Advanced TreeMotion Sample

Open `TreeMotionSample.unity` and enter Play mode.

Select a row and use the toolbar to add roots or children, move the selected subtree to the root,
remove it, or toggle expansion. The arrow toggles a Group. Double-click a row to swap it with the
next visible unrelated node.

The controller loads unordered snapshot records, chooses Item/Group types, binds selection and
labels, and commits data changes. `TreeMotionScrollView` owns layout, per-type pools, culling,
Group sizing and animation. `TreeMotionGroup.prefab` contains Header and ChildrenFrame;
child Views remain siblings under Content. Empty Groups retain their Group type.

Items use a 68-unit height and Groups a 46-unit Header height. The footer reports total and
visible node counts and the last operation. ScrollView Tree Sample additionally demonstrates
multiple Item and Group prefab types and random insert/move/update/swap/remove operations.
