# Research: Workflow Instance Inspection

## Decision: Runtime instance details stay runtime-only

**Rationale**: The constitution requires Runtime packages to remain independent of Workflows.Design. Instance details already expose runtime execution identity, activity execution records, and incidents. Visualization can use the instance's `definitionVersionId` to request the corresponding design-version snapshot from Workflows.Design.

**Alternatives considered**:
- Add design state/layout directly to runtime instance details. Rejected because it would make Runtime API depend on design-side entities.
- Duplicate layout metadata into runtime execution records. Rejected because layout is design-owned metadata and would create synchronization risk.

## Decision: Extend workflow definition version details with layout

**Rationale**: `WorkflowDefinitionVersionLayout` is already the write-once sibling for version designer metadata. Returning it with version details gives Studio the exact authored state and layout for the version that produced the instance.

**Alternatives considered**:
- Use current draft layout by definition id. Rejected because drafts can diverge from the version that produced an instance.
- Add a separate layout-only endpoint. Rejected for this slice because consumers need state and layout together to render a coherent canvas.

## Decision: Reuse the Studio designer adapter in read-only instance mode

**Rationale**: `workflowAdapter.ts` already converts authored state and layout into React Flow nodes/edges for Flowchart and Sequence roots. Reusing it avoids a parallel graph renderer and keeps definition and instance visualization consistent.

**Alternatives considered**:
- Build a separate instance graph model. Rejected because it duplicates activity, layout, and edge interpretation logic.
- Show only a timeline. Rejected because the user specifically needs the instance shown within the designer layout.

## Decision: Deep-linkable instance route

**Rationale**: A dedicated route gives the inspection UI enough horizontal space and supports direct sharing/reloading. The list remains optimized for scanning and filtering.

**Alternatives considered**:
- Expand the current inline side panel. Rejected because it cannot provide a wide graph plus timeline without making the list unusable.
- Modal overlay. Rejected because instance inspection is a primary diagnostic workspace, not a transient confirmation flow.
