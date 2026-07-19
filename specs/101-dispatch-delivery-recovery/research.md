# Research: Recover Failed Dispatch Delivery

## Decision 1: Persist finite retry policy on the existing start contribution

**Decision**: Add validated DispatchWorkflow host feature options for total child-start delivery attempts and positive delay. Snapshot those values into the existing `RuntimePostCommitIntentHandlerContribution` for the child-start kind.

**Rationale**: Outbox items already persist the contribution policy at checkpoint creation. This provides replay-stable host policy without activity inputs or a second queue.

**Alternatives rejected**: Scheduler retry policy owns scheduler poison handling, not post-commit delivery. Activity inputs are prohibited. Pump-only configuration would not be part of committed work.

## Decision 2: Classify delivery failures without persisting raw reasons

**Decision**: Introduce a provider-neutral delivery exception/classification carrying retryable/permanent meaning and fixed safe classification only. Explicit start rejection is permanent; deferred/unavailable delivery and ordinary infrastructure exceptions are transient. Child business faults remain terminal child checkpoints.

**Rationale**: The processor must choose immediate final failure versus retry, while dispatcher reasons and exception summaries may contain secrets.

## Decision 3: Use the failed start outbox item as the durable dead letter

**Decision**: Keep the original child-start outbox item in terminal `FailedFinal` state and link its deterministic ID plus safe incident/generation/attempt evidence from the `DispatchFailed` record. Do not add a second incident/dead-letter document kind.

**Rationale**: The terminal item already holds the exact intent, retry policy, attempts, and fencing state. The issue requires a durable incident or dead letter; duplicating it would add reconciliation and retention obligations.

## Decision 4: Project exhaustion through an additive DispatchWorkflow-owned seam

**Decision**: Add an optional Runtime Core final-failure projector contract. The DispatchWorkflow implementation validates the start intent and canonical dispatch, creates safe dead-letter metadata, and—for wait mode—constructs the deterministic `DispatchFailed` parent-resume item.

**Rationale**: Runtime Core owns generic outbox processing but must not depend on activity-specific payload types.

## Decision 5: Commit final failure and wait resumption atomically

**Decision**: Extend `RuntimePostCommitOutboxClaimCompletion` additively with an optional pending follow-up outbox item. In-memory and Groundwork stores atomically persist the final start item, `DispatchFailed` record/dead-letter evidence, and follow-up item. Fire-and-forget carries no follow-up.

**Replay rules**: Stale owner/fencing fails; deterministic equivalent follow-up is idempotent; conflicting follow-up fails closed; terminal child/acknowledged start outranks late failure; terminal parent makes resume delivery a no-op.

## Decision 6: Reuse the existing parent bookmark route for DispatchFailed

**Decision**: Extend the parent resume result/payload/activity mapping to include `DispatchFailed`. The result has no outputs and only fixed `child-start-delivery-failed` / `delivery` / safe summary plus one deterministic incident ID.

**Rationale**: #679/#680 already provide deterministic intent delivery and bookmark consumption.

## Decision 7: Make redrive one explicit atomic lifecycle exception

**Decision**: Add `IWorkflowDispatchRedriveStore` implemented by the shared in-memory and Groundwork outbox owners. It loads all canonical context and atomically changes only eligible fire-and-forget `DispatchFailed` + matching `FailedFinal` start state into Pending/current generation. The same item/intent/payload/retry policy and dispatch/child/idempotency identities remain. Fence and generation advance.

**Dispositions**: `Accepted`, `AlreadyApplied`, `ActiveConflict`, `NotFound`, and `NotEligible`. Wait mode is always `NotEligible` and abandoned.

## Decision 8: Reuse read/manage authorization split

**Decision**: Extend dispatch GET views under `workflow-runtime.read`; add POST redrive under `workflow-runtime.manage`. The request accepts no tenant, child, intent, executable, idempotency, or payload fields. Provider access context supplies tenant scope.

## Decision 9: Emit structured allowlisted operational events

**Decision**: Add events for failed attempt, retry schedule, permanent failure/dead letter, wait resume queued, and redrive disposition. Include only stable IDs/classifications/times.

## Compatibility and Scope Findings

- `RuntimePostCommitRetryPolicy.MaxAttempts` means total attempts, not retries.
- Existing constructors and base store interfaces remain; projector/redrive capabilities and completion fields are additive.
- Missing optional generation metadata means zero; missing dead-letter fields means not redrive eligible.
- Redrive is the only sanctioned `DispatchFailed -> Pending` transition and is unavailable through ordinary `SaveAsync`.
- No broker, Studio, TestRun, distributed transport, activity-authored retry, or WorkflowDefinitionActivity work is included.
