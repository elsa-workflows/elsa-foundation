# Data Model: Recover Failed Dispatch Delivery

## Host child-start delivery policy

- `MaxDeliveryAttempts`: positive total attempt count.
- `RetryDelay`: positive delay persisted into each start outbox item.

No activity input is added.

## Delivery failure classification

- `Kind`: transient or permanent infrastructure.
- `Code` and `Summary`: fixed safe values selected internally, never provider/exception text.

## Dispatch delivery generation

Generation zero is the original committed start. Each accepted redrive advances by one. Missing optional generation metadata resolves to zero. Stable derived identities per generation are incident ID and wait failure-resume outbox ID. Start intent/outbox, child, dispatch, and idempotency identities never change.

## Durable dead-letter evidence

The original `RuntimePostCommitOutboxItem` remains the dead letter in `FailedFinal`. The linked `WorkflowDispatchRecord` in `DispatchFailed` contains only:

- dead-letter/outbox item ID;
- deterministic incident ID;
- delivery generation;
- final generation attempt count;
- final failure time;
- fixed code `child-start-delivery-failed` and category `delivery`.

It excludes raw failure text, payload, values, authority, tenant/partition, exception/provider reason, and arbitrary metadata.

## Final failure projection

One fenced completion aggregate contains final safe delivery result, canonical `DispatchFailed` record, and optional pending parent-resume item (wait only). The follow-up reuses existing parent-resume/bookmark identities and `RetryUntilAcknowledged`.

## Parent DispatchFailed result

- original deterministic child ID;
- `Status=DispatchFailed`;
- empty outputs;
- fixed code/category/summary and deterministic incident ID.

## Redrive request and result

Request has `DispatchId`, operator `RequestId`, and host `RequestedAt`. Result dispositions are `Accepted`, `AlreadyApplied`, `ActiveConflict`, `NotFound`, and `NotEligible`. Safe results expose only dispatch ID, disposition, status, generation, and incident/dead-letter ID.

## Lifecycle sequences

```text
Transient: Pending -> Delivering -> FailedRetryable -> ... -> Delivered

Wait exhaustion (one transaction):
start outbox Delivering -> FailedFinal
dispatch Pending/Started -> DispatchFailed(abandoned, incident)
create Pending parent-resume -> one DispatchFailed activity outcome

Detached exhaustion/redrive:
start outbox Delivering -> FailedFinal
dispatch Pending/Started -> DispatchFailed(redrive eligible)
authorized redrive -> same outbox Pending(generation+1), same dispatch Pending
ordinary deterministic admission/start
```

## Provider compatibility

Metadata additions use safe absence defaults. Groundwork finalization uses one cross-unit transaction over workflow-execution visibility, outbox, dispatch, and optional follow-up state; redrive uses one cross-unit transaction over the existing outbox and dispatch kinds.
