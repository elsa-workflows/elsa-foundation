# Contract: Durable Dispatch Persistence and Inspection

## Store contract

`IWorkflowDispatchStore` remains unchanged as the domain-owned source of dispatch lifecycle truth.

- `SaveAsync(record)` performs idempotent insert/update with immutable identity and monotonic lifecycle validation.
- `FindAsync(dispatchId)` returns one access-scoped record.
- `ListAsync(parentExecutionId)` remains source compatible.
- `IWorkflowDispatchQueryStore.QueryAsync(query)` additively supports bounded parent, child, status, and intersection filtering with deterministic order.
- `IWorkflowDispatchDeleteStore.DeleteAsync(dispatchId)` additively removes only the requested record; retention policy belongs to the collector.

Provider implementations must not silently ignore checkpoint dispatch changes.

Pending creation commits must identify the parent workflow execution. Non-Pending lifecycle commits must identify the exact linked child workflow execution. The trusted lifecycle service may write Started directly only after deterministic child admission. All unrelated commit ownership is rejected.

## Atomic parent checkpoint contract

For a checkpoint that stages detached dispatch, one provider transaction includes:

1. parent workflow/activity/scheduler state,
2. Pending workflow-dispatch document,
3. child-start post-commit outbox document,
4. checkpoint commit marker.

The commit is all-or-nothing. Replay with the same fingerprint returns the recorded outbox IDs. Replay conflict fails closed.

## Child-start lifecycle contract

The handler accepts only its contributed intent kind and deterministic identity. The dispatcher derives deterministic internal command, envelope, scheduler-work, and root activity IDs from the committed server-owned dispatch. Accepted or same-identity Duplicate child admission advances the record to Started. Rejected or unsafe deferred admission remains on the existing outbox failure path. A process failure after materialization is repaired by byte-equivalent outbox redelivery.

## Outbox claim contract

The processor must claim through additive `IRuntimePostCommitOutboxClaimStore` before invoking a handler. Claim selects only Pending or expired Delivering items, assigns a new positive fencing token and visibility deadline, and is atomic per item. Claim-aware acknowledge/failure operations require the exact current owner/token. Stale operations fail without changing the item. Process failure leaves a Delivering item reclaimable after visibility expiry. Existing `IRuntimePostCommitOutboxStore` implementers remain source compatible.

## Child terminal checkpoint contract

Before persistence fingerprinting, the runtime finds dispatches whose child execution ID matches a terminal workflow state change and appends the corresponding lifecycle change with `UpdatedAt` equal to the child checkpoint timestamp. Replay appends the identical change even if already applied. The provider commits dispatch and child workflow terminal state atomically. A terminal state supersedes a later Started attempt. This is observation only, not propagation policy.

## Inspection contract

- Routes require `WorkflowRuntimeRead`.
- List is bounded and accepts parent, child, and status filters.
- Get uses deterministic dispatch ID.
- Handlers return dedicated safe views, never domain records.
- Store access context enforces tenant scope; absence and cross-scope access both surface as not found/empty according to existing runtime conventions.
- Diagnostic fields are allowlisted stable classifications only.

## Retention contract

The collector never cascades from one execution deletion. It deletes only a terminal dispatch after two successful rounds of reads prove both linked executions absent. Nonterminal state, failure, or cancellation retains.

## Readiness contract

Production-safe detached dispatch requires durable implementations for:

- checkpoint commit,
- workflow-dispatch store,
- post-commit outbox,
- scheduler/continuation queue,
- background runtime resumption.

In-memory composition is explicitly ProcessLocal. Any partial production composition is Unsafe and reports stable missing-component codes without configuration secrets.

Durability evidence is contributed by composition modules and aggregated provider-neutrally; Runtime Core and Activities do not inspect Groundwork concrete types.

`IWorkflowDispatchReadinessAssessor.AssessAsync` is the stable reporting contract. The DispatchWorkflow shell initializer reports Unsafe, ProcessLocal, or DurableReady through the host readiness/logging path without changing existing startup behavior.
