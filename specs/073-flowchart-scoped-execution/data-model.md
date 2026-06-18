# Data Model: Flowchart Scoped Execution

## FlowchartExecutionState

Parent state object owned by a running Flowchart activity execution.

Fields:

- `rootScopeId`: identifier of the root execution scope.
- `scopes`: collection of `ExecutionScope`.
- `executionPaths`: collection of `ExecutionPath`.
- `arrivals`: optional immutable `FlowchartArrival` records.
- `activeChildren`: collection of `FlowchartActiveChild` bindings.
- `diagnostics`: collection of `FlowchartDiagnosticEvent`.

Validation rules:

- Exactly one root scope exists.
- Every execution path references an existing scope.
- Every active child references an existing execution path and scope.
- A child activity execution may be bound to only one execution path.

## ExecutionScope

Logical control-flow boundary for Flowchart execution.

Fields:

- `scopeId`
- `parentScopeId`
- `kind`: `root`, `branch`, `join`, `loopIteration`, or `race`
- `createdByNodeId`
- `startConnectionId`
- `ownerNodeId`
- `loopIterationKey`
- `status`: `active`, `waiting`, `completed`, `canceled`, or `faulted`
- `metadata`: policy-specific key/value values

Relationships:

- Root scope has no parent.
- Branch, join, loop iteration, and race scopes have a parent scope.
- Execution paths are attached to exactly one current scope.

State transitions:

- `active` → `waiting`
- `active` → `completed`
- `active` → `canceled`
- `waiting` → `completed`
- `waiting` → `canceled`
- any non-terminal → `faulted`

## ExecutionPath

Single path of control through the Flowchart graph.

Fields:

- `executionPathId`
- `parentExecutionPathId`
- `executionScopeId`
- `currentNodeId`
- `incomingConnectionId`
- `schedulingActivityExecutionId`
- `status`: `active`, `waiting`, `completed`, `canceled`, or `faulted`
- `iterationKey`
- `lastOutcomeNames`

Validation rules:

- Every active or waiting execution path has a current node.
- A completed/canceled/faulted execution path does not schedule new children.
- Execution paths from one loop iteration cannot satisfy joins in another loop iteration.

## FlowchartArrival

Immutable evidence that an execution path arrived at a target through a connection.

Fields:

- `arrivalId`
- `executionPathId`
- `executionScopeId`
- `sourceNodeId`
- `targetNodeId`
- `connectionId`
- `sourcePort`
- `producingActivityExecutionId`
- `status`: `arrived` or `consumed`

Validation rules:

- Arrivals are append-only except for `arrived` → `consumed`.
- Consumed arrivals cannot be consumed again.

## FlowchartActiveChild

Binding between a scheduled child activity execution and Flowchart control-flow state.

Fields:

- `childActivityExecutionId`
- `nodeId`
- `executionPathId`
- `executionScopeId`
- `schedulingCause`

Validation rules:

- Each child activity execution id appears at most once.
- Active child bindings are removed or closed when the child completes, cancels, or faults.

## FlowchartDiagnosticEvent

User-facing explanation of a meaningful Flowchart decision or failure.

Fields:

- `diagnosticId`
- `kind`: scheduled, waiting, joined, deadPath, loopIteration, canceled, policyFailure, completed
- `nodeId`
- `connectionId`
- `executionPathId`
- `executionScopeId`
- `message`
- `details`

Validation rules:

- Diagnostics for joins include the target node and the expected/missing branches when known.
- Diagnostics for dead paths include the reason the path can no longer arrive.

## FlowchartPolicyMetadata

Versioned structure metadata keyed by node and connection identifiers.

Fields:

- `nodePolicies`: node id to policy kind/configuration.
- `connectionPolicies`: connection id to outcome, condition, default flag, and label metadata.

Validation rules:

- Policy kind must resolve to a registered policy.
- Connection metadata must reference an existing connection.
- Node metadata must reference an existing child node.

## FlowchartPolicyDecision

Result returned by a gateway policy.

Fields:

- `commands`: ordered list of policy commands.
- `diagnostics`: optional policy decision diagnostics.

Validation rules:

- Decisions cannot contain conflicting commands for the same execution path.
- Decisions cannot schedule missing nodes.
- Decisions cannot mutate state directly; the engine applies commands.
