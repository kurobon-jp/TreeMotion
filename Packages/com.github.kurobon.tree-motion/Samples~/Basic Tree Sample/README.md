# Basic Tree Sample

Open `BasicTreeSample.unity` and enter Play mode.

The current sample loads 100 root records, binds one reusable prefab through
`ITreeMotionDataSource<int, string>`, and delegates layout and viewport pooling to
`TreeMotionScrollView`. The prefab owns its 56-unit height.

For nested groups, multiple ItemTypes and random structural animation, see ScrollView Tree Sample.
For interactive selection, toolbar editing and double-click swaps, see Advanced TreeMotion Sample.
