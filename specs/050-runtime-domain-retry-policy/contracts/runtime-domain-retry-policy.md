# Contract: Runtime Domain Retry Policy

`IRuntimeDomainRetryPolicy` decides workflow/activity domain retry behavior after a domain failure request.

## Default Rule

1. Receive `RuntimeDomainRetryRequest`.
2. Return `RuntimeDomainRetryDecision` with `RuntimeDomainRetryMode.DoNotRetry`.
3. Include diagnostic metadata:
   - `runtime.domainRetry.policy`
   - `runtime.domainRetry.workflowExecutionId`
   - `runtime.domainRetry.activityExecutionId` when present
4. Do not inspect operational recovery candidates, execution leases, heartbeats, interrupted execution state, or post-commit outbox delivery state.

## Separation Rule

Operational recovery may requeue from a checkpoint after a lost lease or stopped host. That is not a domain retry decision and must not increment domain retry counters.
