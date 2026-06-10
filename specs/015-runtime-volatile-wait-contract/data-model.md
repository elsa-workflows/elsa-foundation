# Data Model: Runtime Volatile Wait Contract

## `VolatileWaitRegistration`

In-memory scheduler continuation state for an activity-scoped volatile wait.

- `WaitId`
- `WorkflowExecutionId`
- `ActivityExecutionId`
- `BranchId`
- `RegisteredAt`
- `ExpiresAt`
- `AwaitableKind`
- `Status`
- `HostShutdownBehavior`
- `CancellationBehavior`
- `Metadata`

It deliberately does not contain bookmark IDs, resume target IDs, or method names.

## `SchedulerContinuationWorkItem`

Deterministic scheduler work emitted by internal runtime events such as volatile wait completion.

- `WorkItemId`
- `WorkflowExecutionId`
- `ActivityExecutionId`
- `BranchId`
- `Kind`
- `VolatileWaitId`
- `EnqueuedAt`
- `Reason`
- `Metadata`

## `RuntimeVolatileWaitPolicyRequest`

Policy input describing a proposed volatile wait and host capability.

- `WorkflowExecutionId`
- `ActivityExecutionId`
- `BranchId`
- `AwaitableKind`
- `RequestedDuration`
- `HostSupportsInMemoryContinuation`
- `RequestedHostShutdownBehavior`
- `RequestedCancellationBehavior`
- `DurableFallbackPolicy`
- `Metadata`

## `RuntimeVolatileWaitPolicyDecision`

Policy output declaring whether a volatile wait is allowed and which host shutdown, cancellation, and durable fallback rules apply.
