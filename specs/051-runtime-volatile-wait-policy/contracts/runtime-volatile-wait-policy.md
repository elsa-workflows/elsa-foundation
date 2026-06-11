# Contract: Runtime Volatile Wait Policy

`IRuntimeVolatileWaitPolicy` decides whether an activity-scoped volatile wait can remain in in-memory scheduler continuation state for the current host/runtime context.

## Default Rule

1. Receive `RuntimeVolatileWaitPolicyRequest`.
2. If `HostSupportsInMemoryContinuation` is true, return an allowed decision.
3. If `HostSupportsInMemoryContinuation` is false, return a denied decision with a reason.
4. Preserve requested host shutdown behavior, cancellation behavior, durable fallback policy, and requested duration as decision guardrails.
5. Include diagnostic metadata:
   - `runtime.volatileWait.policy`
   - `runtime.volatileWait.workflowExecutionId`
   - `runtime.volatileWait.activityExecutionId`
   - `runtime.volatileWait.awaitableKind`

## Separation Rule

Volatile wait policy decisions do not create durable bookmarks and do not carry bookmark IDs, resume target IDs, or C# callback method names.
