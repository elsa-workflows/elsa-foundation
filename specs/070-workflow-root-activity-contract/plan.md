# Implementation Plan: Workflow Root Activity Contract

**Branch**: `codex/workflow-root-activity-contract` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

## Summary

Correct the workflow boundary from a flowchart-shaped graph to a single root activity. Design state
will own one authored root activity. Publishing will compile that root into one runtime-owned
executable root activity. Runtime start scheduling will schedule the root activity only. Existing
runtime state, scheduler, checkpoint, bookmark, and activity invocation contracts continue to use
executable activity ids where needed.

## Technical Context

- `WorkflowDefinitionState` currently carries `Activities` and `ActivityConnections`.
- `ActivityNode` currently carries `IsStart`, `IsTerminal`, and `ChildActivities`.
- `WorkflowExecutable` currently carries `Nodes`, `Edges`, and `StartNodeIds`.
- Scheduler payloads and activity execution state already reference `ExecutableNodeId`.
- Bookmark and activity invocation paths need a fast artifact-owned lookup by executable activity id.

## Constitution Check

| Gate | Status | Notes |
| --- | --- | --- |
| Workflows.Design / Workflows.Runtime split | PASS | Publishing remains the bridge; Runtime still does not reference Design. |
| Artifact-only runtime | PASS | Runtime uses the executable artifact only. |
| Architectural triplet | PASS | State, read projections, and executable remain separate. |
| Elsa Core alignment | PASS | Workflow has one root activity; Flowchart/Sequence/StateMachine are activity kinds. |
| Unit tests for logic-bearing changes | REQUIRED | Design model, publishing, runtime start, and completion scheduling need focused tests. |

## Data Model Direction

- `WorkflowDefinitionState.RootActivity : ActivityNode?`
- `ActivityNode` carries identity and argument state only; no generic composition property.
- Activity-specific child slots expose child activities according to the activity contract:
  `Sequence.Activities`, `If.Then` / `If.Else`, `ForEach.Body`, `Composite.Root`, and
  `Flowchart` activities/connections/start/join state.
- Those names are examples owned by activity modules or importers. Workflows Design Core and
  Workflows Runtime Core must carry only opaque slot names/metadata and must not define reserved
  constants for activity-specific concepts.
- `WorkflowExecutable.RootActivity : ExecutableNode`
- `ExecutableNode` carries compiled activity identity, descriptor payload, and binding state only; no
  generic executable composition property.
- Executable child references are compiled into the activity-specific descriptor/binding payload that
  owns them.
- `WorkflowExecutable.NodesById` remains as a derived index over `RootActivity` and descendants
  discovered through opaque child slots or activity-specific adapters.

Generic composition records are forbidden. Runtime behavior for interpreting
Flowchart/Sequence/StateMachine remains owned by those activity implementations.

Elsa Core alignment note: `Flowchart` is a container activity with its own `Start` and
`Connections` properties. It schedules child activities through the Flowchart activity execution
context and reacts to child completion in Flowchart-owned logic. This slice preserves that
responsibility boundary by removing generic workflow-level edge traversal from completion
scheduling.

## Implementation Steps

1. Add/update architecture docs and superseding Speckit artifacts.
2. Update design model records and XML documentation.
3. Update design API projections, draft creation/clone, diffing, validators, and test helpers.
4. Add activity-specific child-slot traversal so design validation and publishing can find children without a generic composition carrier.
5. Update publishing to compile one root activity recursively through child slots.
6. Update runtime executable model to own one root activity and derive lookup tables through child slots.
7. Update runtime start scheduling to schedule the root activity only.
8. Remove workflow-level/generic executable edge traversal from completion scheduling.
9. Rewrite focused tests and delete/mark graph-shaped/generic-composition assertions as superseded.
10. Run targeted design, publishing, runtime, and architecture tests.
11. Refresh generated maps if source inputs changed and commit the completed work unit.

## Risks

- Existing vertical-slice tests encode the wrong graph-shaped behavior and must be rewritten rather than preserved.
- Composite activity behavior is not implemented by this slice; tests should verify artifact shape and primitive-root execution, not Flowchart execution.
- Existing JSON data from the superseded shape is not migrated by this slice.
