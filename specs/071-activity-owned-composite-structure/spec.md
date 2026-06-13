# Feature Specification: Activity-Owned Composite Structure

**Feature Branch**: `codex/activity-owned-composite-structure`
**Created**: 2026-06-13
**Status**: Draft
**Input**: Clean up composite activity modeling so child slots are traversal projections and relationship semantics belong to the owning activity.

## Context

The workflow root-activity contract established that workflows own one root activity and that
composite behavior belongs to activity contracts such as Sequence, Flowchart, If, ForEach, and
For. The remaining ambiguity was that `ActivityChildSlot` and `ExecutableChildSlot` could still
carry metadata, which made it tempting to model Flowchart connections, start nodes, If branch
meaning, loop bodies, and similar relationship semantics in generic core slot records.

That makes child slots more than traversal projections and recreates a weak generic composition
model. The design and runtime cores need a generic way to discover nested activities for
validation, publishing, hashing, indexing, and scheduling, but they must not own the meaning of
those relationships.

## Requirements

- **FR-001**: `ActivityNode` MUST expose optional activity-owned authored structure separate from child-slot traversal projections.
- **FR-002**: `ExecutableNode` MUST expose optional activity-owned compiled structure separate from child-slot traversal projections.
- **FR-003**: `ActivityChildSlot` MUST be projection-only: slot name plus child activities. It MUST NOT expose generic metadata.
- **FR-004**: `ExecutableChildSlot` MUST be projection-only: slot name plus child executable nodes. It MUST NOT expose generic metadata.
- **FR-005**: Core design/runtime packages MUST NOT define activity-specific slot-name constants, metadata-key constants, edge records, branch records, connection records, or loop-body semantics.
- **FR-006**: Publishing MUST copy opaque activity-owned authored structure into the executable artifact without depending on the owning activity module.
- **FR-007**: Activity modules MUST own interpretation and validation of their structure kind, schema version, and payload.
- **FR-008**: Flowchart connections and start-node selection MUST be represented as Flowchart-owned structure, not slot metadata.
- **FR-009**: Sequence may continue to use an ordered child projection when its activity contract defines the order, but core MUST NOT interpret that order as generic sequence semantics.
- **FR-010**: Future If, ForEach, For, StateMachine, and custom composite activities MUST model their relationship semantics in activity-owned structure or activity-specific contracts, not generic slot metadata.

## Non-Goals

- Implementing If, ForEach, For, or StateMachine activities.
- Replacing runtime child projection traversal with an adapter registry.
- Changing the activity construction descriptor contract.
- Migrating persisted draft/version JSON from earlier provisional shapes.

## Acceptance Criteria

- Design contract tests prove `ActivityNode.Structure` exists and `ActivityChildSlot` has no `Metadata`.
- Runtime contract tests prove `ExecutableNode.Structure` exists and `ExecutableChildSlot` has no `Metadata`.
- Publishing tests prove activity-owned structure is copied into `WorkflowExecutable`.
- Flowchart tests prove connections and start-node selection are read from Flowchart-owned structure.
- Flowchart extension-point docs no longer describe slot metadata as the graph contract.
