# Feature Specification: Activity-Owned Composite Structure

**Feature Branch**: `codex/activity-owned-composite-structure`
**Created**: 2026-06-13
**Status**: Draft
**Input**: Clean up composite activity modeling so persisted design state is activity-owned structure and runtime child slots are compiled traversal projections.

## Context

The workflow root-activity contract established that workflows own one root activity and that
composite behavior belongs to activity contracts such as Sequence, Flowchart, If, ForEach, and
For. The remaining ambiguity was that design-time `ActivityNode` could persist child slots, which
made a traversal projection look like the authored composition model. That recreated a weak
generic composition model and made Sequence ordering, Flowchart connections, If branches, and loop
bodies look like core-owned concepts.

Design-time workflow state must store activity-owned structure only. Generic design flows still
need to discover nested activities for validation, publishing, hashing, indexing, and mutation, so
that discovery is provided by activity-owned structure handlers. Runtime keeps materialized
`ExecutableNode.ChildSlots` because scheduling needs a fast compiled projection of executable
children.

## Requirements

- **FR-001**: `ActivityNode` MUST expose optional activity-owned authored structure and MUST NOT expose persisted child slots.
- **FR-002**: `ExecutableNode` MUST expose optional activity-owned compiled structure separate from child-slot traversal projections.
- **FR-003**: Design-time child traversal MUST use a non-persisted projection read model: slot name plus child activities. It MUST NOT expose generic metadata.
- **FR-004**: `ExecutableChildSlot` MUST be projection-only: slot name plus child executable nodes. It MUST NOT expose generic metadata.
- **FR-005**: Core design/runtime packages MUST NOT define activity-specific slot-name constants, metadata-key constants, edge records, branch records, connection records, or loop-body semantics.
- **FR-006**: Activity modules MUST register structure handlers keyed by structure kind and schema version when their authored payload contains child activities.
- **FR-007**: Structure handlers MUST project authored child activities, replace projected children for draft mutation, and compile authored structure into executable structure without embedding design `ActivityNode` payloads in the executable payload.
- **FR-008**: Publishing MUST use structure handlers to flatten design activities, compile executable child slots, and compile executable structure. Unknown structure kinds MUST be copied as opaque non-composite structure.
- **FR-009**: Flowchart authored structure MUST contain child activities, connections, and start node; executable Flowchart structure MUST contain connections and start node only.
- **FR-010**: Sequence authored structure MUST contain ordered child activities; executable Sequence structure MUST contain ordered child node ids; Sequence runtime MUST schedule by executable structure order, not child-slot array order.
- **FR-011**: Future If, ForEach, For, StateMachine, and custom composite activities MUST model their relationship semantics in activity-owned structure or activity-specific contracts, not generic slot metadata.

## Non-Goals

- Implementing If, ForEach, For, or StateMachine activities.
- Adding a compatibility bridge for older design-time `childSlots` payloads.
- Changing the activity construction descriptor contract.
- Migrating persisted draft/version JSON from earlier provisional shapes.

## Acceptance Criteria

- Design contract tests prove `ActivityNode.Structure` exists and `ActivityNode.ChildSlots` does not.
- Design projection tests prove nested activities are discovered through structure handlers.
- Runtime contract tests prove `ExecutableNode.Structure` exists and `ExecutableChildSlot` has no `Metadata`.
- Publishing tests prove Sequence and Flowchart authored structures compile into executable child slots plus executable structure.
- Publishing tests prove unknown opaque structure is copied into `WorkflowExecutable` and treated as non-composite.
- Sequence tests prove runtime scheduling follows executable structure order and rejects missing or duplicate child references.
- Flowchart tests prove connections and start-node selection are read from Flowchart-owned structure.
- Flowchart extension-point docs no longer describe slot metadata as the graph contract.
