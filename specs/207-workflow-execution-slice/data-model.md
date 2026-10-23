# Data Model: Workflow Execution Vertical Slice

> Supersession note (2026-06-11): the `Edges` and `StartNodeIds` fields described here are
> superseded by
> [070-workflow-root-activity-contract](../070-workflow-root-activity-contract/spec.md). Composite
> activity composition state is activity-owned.

## WorkflowExecutable

Runtime-owned runnable artifact.

Existing fields:
- `Identity`
- `Nodes`
- `NodesById`
- `ResumeTargets`
- `CreatedAt`
- `PublishedAt`
- `CompatibilityMetadata`

New fields:
- `Edges`: ordered collection of `ExecutableEdge`.
- `StartNodeIds`: ordered collection of executable node ids.

Validation:
- Node ids are unique.
- Edge source and target ids exist in `NodesById`.
- Start node ids exist in `NodesById`.
- The vertical slice publisher emits exactly one start node.

## ExecutableNode

Runtime-owned compiled activity node.

Existing fields remain:
- `ExecutableNodeId`
- `AuthoredActivityId`
- `ActivityType`
- `ActivityTypeVersion`
- `DescriptorType`
- `DescriptorPayload`
- `InputBindings`
- `OutputCaptures`
- `Metadata`

Metadata required for this slice:
- Authored `NodeId`.
- Optional authored terminal marker.

## ExecutableEdge

Runtime-owned sequential control-flow link.

Fields:
- `SourceNodeId`
- `SourcePort`
- `TargetNodeId`
- `TargetPort`

Validation:
- Source and target ids are non-empty.
- Source and target ids refer to executable nodes.
- Publisher rejects more than one outgoing edge from a node for this slice.

## RuntimeInputBinding

Existing runtime input binding with literal support.

Metadata required for this slice:
- `typeName`: CLR type name from the design input definition.
- Optional `referenceKey`: design argument reference key for diagnostics.

Validation:
- Literal source must include one JSON literal.
- Publisher rejects expression, variable, activity-output, and durable-value sources for this slice.

## WorkflowExecutableStore

Runtime-owned artifact store contract.

Operations:
- Save a `WorkflowExecutable`.
- Find a `WorkflowExecutable` by artifact id.
- Optionally list artifacts for diagnostics/demo tooling.

Invariants:
- Store keys by `WorkflowExecutable.Identity.ArtifactId`.
- A later save for the same artifact id replaces the same artifact snapshot only if ids match.

## WorkflowExecutionResult

REST-friendly runtime execution summary.

Fields:
- `WorkflowExecutionId`
- `ArtifactId`
- `Status`
- `StartedAt`
- `CompletedAt`
- `Activities`
- `Error`

Status values:
- `Completed`
- `Faulted`

## ActivityExecutionResult

Runtime execution summary for one activity.

Fields:
- `ActivityExecutionId`
- `ExecutableNodeId`
- `ActivityType`
- `Status`
- `StartedAt`
- `CompletedAt`
- `Error`

Status values:
- `Completed`
- `Faulted`
