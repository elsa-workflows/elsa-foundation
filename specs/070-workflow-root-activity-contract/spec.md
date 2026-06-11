# Feature Specification: Workflow Root Activity Contract

**Feature Branch**: `codex/workflow-root-activity-contract`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Correct the workflow design/runtime boundary so a workflow carries one root activity, taking strong cues from elsa-core.

## Context

The current foundation runtime slices accidentally model both `WorkflowDefinitionState` and
`WorkflowExecutable` as flowchart-like containers: design state owns workflow-level activities and
connections, while runtime executable artifacts own workflow-level executable nodes, edges, and start
node ids. That conflicts with elsa-core's workflow model, where the workflow owns one root
`IActivity`; the root may itself be `Flowchart`, `Sequence`, `StateMachine`, a primitive activity, or
another activity kind.

This slice supersedes graph-shaped workflow-boundary requirements in earlier runtime slices. Flowchart
connections, sequence ordering, state transitions, and start-child choices are activity-owned
composition state, not workflow-level state.

Elsa Core cue: `Flowchart` is a composite/container activity. It carries its own `Start` activity
and `Connections`, then schedules child activities from the Flowchart activity execution context.
The foundation runtime boundary should therefore schedule the workflow root activity and leave
Flowchart edge interpretation to the Flowchart activity implementation.

## Scenarios & Tests

1. Given an authored workflow definition, when design state is inspected, then it contains exactly one optional `RootActivity` and no workflow-level `Activities` or `ActivityConnections` members.
2. Given an authored root activity with nested composition state, when the workflow is published, then the bridge produces a `WorkflowExecutable` with exactly one compiled `RootActivity`.
3. Given a start command for a pinned workflow executable, when runtime start scheduling runs, then it schedules the executable root activity only.
4. Given a workflow whose root activity is a primitive activity, when it is published and executed through the existing scheduler path, then the primitive root can execute without requiring workflow-level start nodes or edges.

## Requirements

- **FR-001**: `WorkflowDefinitionState` MUST carry one authored `RootActivity` member instead of workflow-level `Activities` and `ActivityConnections`.
- **FR-002**: `ActivityNode` MUST represent an authored activity. Child activities and connections, when present, MUST be activity-owned composition state.
- **FR-003**: `ActivityNode` MUST NOT expose workflow-level `IsStart` or `IsTerminal` flags. Start/terminal semantics are owned by the relevant composite activity or runtime behavior.
- **FR-004**: Design validators, draft diffing, API projections, and layout joins MUST use the root activity tree instead of workflow-level activity collections.
- **FR-005**: `WorkflowExecutable` MUST carry one compiled `RootActivity` member instead of workflow-level `Edges` and `StartNodeIds`.
- **FR-006**: Runtime lookup tables such as `NodesById` MAY exist as derived indexes over the executable root activity tree, but MUST NOT be the authoritative executable shape.
- **FR-007**: Publishing MUST compile the design root activity into the executable root activity without making Runtime depend on Design.
- **FR-008**: Runtime start scheduling MUST schedule the executable root activity only.
- **FR-009**: Completion propagation MUST NOT traverse workflow-level executable edges from `WorkflowExecutable`. Composite activity continuation behavior belongs to activity-specific runtime behavior.
- **FR-010**: Flowchart-like edge traversal MUST be implemented by a Flowchart activity/runtime module that owns its child activities and connections, not by generic workflow completion scheduling.
- **FR-011**: Superseded specs/tests that assert workflow-level start nodes or executable edges MUST be updated, removed, or marked superseded.

## Non-Goals

- Implementing full `Flowchart`, `Sequence`, or `StateMachine` runtime behavior.
- Implementing workflow-as-activity nested execution.
- Redesigning expression/variable binding beyond the model changes required by the root activity contract.
- Changing the Design/Runtime bounded-context split.
- Adding migration logic for persisted draft/version JSON created with the superseded graph shape.

## Acceptance Criteria

- Focused design tests prove `WorkflowDefinitionState` exposes `RootActivity` and no workflow-level `Activities` or `ActivityConnections` properties.
- Focused runtime tests prove `WorkflowExecutable` exposes `RootActivity` and no workflow-level `Edges` or `StartNodeIds` properties.
- Publishing tests prove a primitive root activity compiles into the executable root activity.
- Runtime start scheduling tests prove only the root activity is scheduled.
- Runtime completion scheduling tests no longer rely on `WorkflowExecutable.Edges`.
- Dependency-boundary tests still prove Workflows Runtime does not depend on Workflows Design.
