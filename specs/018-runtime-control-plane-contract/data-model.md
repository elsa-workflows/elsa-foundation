# Data Model: Runtime Control Plane Contract

## ControlPlaneState

Runtime-owned administrative state for pause/unpause and drain-like holds. It is not workflow continuation state and does not replace `WorkflowExecutionState`.

Fields:

- `ControlPlaneStateId`
- `WorkflowExecutionId` when the state is scoped to one workflow execution
- `ActiveHolds`
- `ReleasedHolds`
- `Metadata`

## ControlPlaneHold

One administrative hold. A hold has a scope and the target IDs required by that scope.

Scopes:

- `Ingress`
- `WorkflowExecution`
- `ActivityExecution`
- `Generator`
- `WorkerDispatcher`
- `HostDrain`

Required target IDs:

- Ingress: `IngressSourceId`
- Workflow execution: `WorkflowExecutionId`
- Activity execution: `WorkflowExecutionId` and `ActivityExecutionId`
- Generator: `WorkflowExecutionId` and `GeneratorId`
- Worker/dispatcher: `WorkerId`
- Host drain: `HostId`

## SchedulerPauseDecision

Result of evaluating a safe pause boundary.

Fields:

- `CanAdvance`
- `Boundary`
- `ContinuationPolicy`
- `HoldId`
- `Reason`
- `Metadata`

## IngressPausePolicy

Default ingress behavior while paused.

Defaults:

- HTTP endpoint: reject with 503.
- Timer: skip while paused.
- Message/queue: stop fetching/locking through broker-native backpressure.
- Request/response webhook: reject with 503.
- Durable event stream: pause subscription or stop advancing/checkpointing offset where supported.
- Manual/API start: reject with paused-state error.
