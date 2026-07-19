# Data Model: Wait for a Successful Child and Return Safe Outputs

## WorkflowDispatchIdentity additions

The existing digest over parent workflow execution ID and parent activity execution ID remains unchanged. New distinct prefixes derive:

- `WaitBookmarkId`
- `WaitStimulusHash`
- `ParentResumeIntentId`
- `ParentResumeIdempotencyKey`

The existing dispatch, child, start-intent, and start-idempotency values are unchanged. All additions use identity version `v1` so the digest input remains one logical dispatch identity while textual prefixes separate namespaces.

## WorkflowDispatchCheckpointRequest additions

Existing fire-and-forget shape:

- `Record`
- `StartIntent`
- no wait bookmark

Wait-mode shape adds:

- one `ActivityBookmarkRequest`
- record mode `WaitForCompletion`
- bookmark ID and stimulus hash matching the deterministic identity
- DispatchWorkflow-owned stimulus type and resume target
- `ExpiresAt = null`

Validation requires fire-and-forget records to omit the wait bookmark and wait-mode records to carry exactly one matching bookmark. The existing two-argument constructor forwards to the fire-and-forget shape.

## Wait-mode parent checkpoint

One checkpoint contains:

- suspended `ActivityExecutionState` for the DispatchWorkflow activity
- one `BookmarkState`
- one Pending `WorkflowDispatchRecord` with `Mode = WaitForCompletion`
- one child-start `RuntimePostCommitIntent`
- any existing activity inspection/durable write-back required by the invoke path

The activity state remains `Suspended` with the bookmark ID and normal bookmark-waiting substatus. The parent workflow uses existing engine waiting semantics; this feature does not introduce a new workflow status.

## WorkflowDispatchParentResumePayload

Safe immutable payload recorded by the child Completed checkpoint:

| Field | Meaning |
|---|---|
| DispatchId | Deterministic dispatch linkage |
| ParentWorkflowExecutionId | Exact waiting parent |
| ParentActivityExecutionId | Exact suspended DispatchWorkflow activity |
| ChildWorkflowExecutionId | Completed child |
| BookmarkId | Exact parent bookmark to consume |
| StimulusType / StimulusHash | Deterministic bookmark match |
| Result | `DispatchWorkflowResult` with Completed and safe outputs |

Payload validation recomputes the dispatch identity and rejects any mismatch before actor acquisition or output assignment.

## Safe child output entry

The stable `DispatchWorkflowOutput` surface remains:

- `Name`
- `DeclaredType`
- `IsRedacted`
- optional `JsonElement Value`

Projection rules:

- name is always retained;
- declared runtime type is formatted deterministically into the existing string field as `Id` when present, otherwise `Kind` (schema remains validation detail rather than a second result payload);
- redacted entries have `IsRedacted = true` and `Value = null`;
- disclosed entries contain a cloned JSON value;
- names are unique and ordinally ordered;
- only `WorkflowDispatchStatus.Completed` may carry outputs.

## RuntimePostCommitIntentHandlerContribution addition

- `IntentKind`
- `HandlerType`
- `RetryPolicy`

The existing constructor assigns `RuntimePostCommitRetryPolicy.None`. Duplicate contributions for one kind must agree on handler type and retry policy; disagreement fails composition deterministically.

## RuntimePostCommitRetryPolicy addition

Existing bounded fields remain:

- `MaxAttempts`
- `Delay`
- `Metadata`

New field:

- `RetryUntilAcknowledged`

Validation:

- `None`: `MaxAttempts = 0`, no delay, unbounded flag false.
- bounded: positive `MaxAttempts`, positive delay, unbounded flag false.
- until acknowledged: positive delay, unbounded flag true; finite exhaustion is disabled.
- unbounded attempt count saturates at `int.MaxValue` rather than overflowing.

State transition for an unconsumed resume:

```text
Delivering(claim owner/token)
  -> FailedRetryable(AvailableAt = now + delay, attempt = saturating increment)
  -> Delivering(new claim/token)
  -> ... until acknowledgement
```

Every committed `FailedRetryable` transition for the parent-resume kind also produces a structured operational warning. Its schema is deliberately non-persistent and payload-free: outbox item ID, intent ID/kind, optional dispatch ID, saturated attempt count, and next `AvailableAt`. It carries no intent payload, child output, exception detail, stack trace, or actor envelope.

## IPostCommitOutboxLookupStore

Additive capability:

- `FindAsync(outboxItemId)` returns any status, including Delivered or Failed, under the active access scope.

It does not change delivery queries, claim ownership, or the existing base outbox contract.

## Successful lifecycle sequence

```text
Parent DispatchWorkflow Running
  -> atomic wait checkpoint:
       Parent activity Suspended + Bookmark
       Dispatch Pending/WaitForCompletion
       ChildStart intent Pending
  -> child materialization:
       Dispatch Started
  -> child terminal checkpoint:
       Child Completed
       Dispatch Completed
       ParentResume intent with safe result
  -> global parent-resume delivery:
       exact bookmark command (retry until consumed)
  -> bookmark-consumption checkpoint:
       Bookmark deleted
       Parent activity Completed
       Result + ChildWorkflowExecutionId recorded
       ordinary graph completion intent
  -> ParentResume outbox acknowledged
```

Replay may observe later states at any step. Equivalent terminal and consumption observations are idempotent; no transition regresses dispatch lifecycle.

## Groundwork wire changes

- post-commit outbox document version becomes v3;
- v3 is the clean pre-GA current and minimum-readable baseline;
- older fixtures are removed, no Elsa upcaster is registered, and older persisted rows require a datastore reset;
- the v3 fixture captures an unbounded parent-resume item;
- bookmark, dispatch, and durable output documents reuse their existing kinds and indexes.
