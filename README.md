# TreeMotion

TreeMotion is a virtualized, animated tree view for Unity uGUI.

The project focuses on two capabilities:

- Arbitrarily nested groups represented as a flattened visible-row model.
- Animated insertion, removal, and movement while preserving stable item identity.

## Project layout

- `Packages/com.github.kurobon.tree-motion/Runtime`: public runtime API and implementation.
- `Packages/com.github.kurobon.tree-motion/Editor`: editor-only tooling.
- `Packages/com.github.kurobon.tree-motion/Tests`: package tests.
- `Packages/com.github.kurobon.tree-motion/Samples~`: examples imported through Package Manager.

## Initial design constraints

- Data nodes use stable IDs; visible indexes are treated as transient layout positions.
- Hierarchical data is flattened before virtualization and layout.
- Logical layout positions are kept separate from visual animation offsets.
- Removed views remain alive until their exit animation completes.
- The package does not depend on SimpleScroll, allowing both APIs to evolve independently.

## Development

Open the repository root with Unity `6000.4.11f1`.

