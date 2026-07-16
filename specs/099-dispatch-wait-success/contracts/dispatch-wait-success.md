# Contract: Successful DispatchWorkflow Wait and Resume

## Stable activity surface

The #676 surface is unchanged:

- Inputs: `WorkflowDefinitionId`, `Inputs`, `WaitForCompletion=false`, `CancelChildOnParentCancellation=true`, `CorrelationId`.
- Outputs: `ChildWorkflowExecutionId`, `Result`.
- Outcomes: `Dispatched`, `Completed`, `Faulted`, `Cancelled`, `DispatchFailed`.

For `WaitForCompletion=true`, this slice emits only `Completed` after successful bookmark resume. It does not emit `Dispatched` at the initial wait checkpoint.

## Atomic wait checkpoint

The runtime accepts a staged wait dispatch only when:

1. record mode is `WaitForCompletion`;
2. record/start identities match the deterministic parent/activity identity;
3. exactly one non-expiring bookmark matches the deterministic bookmark/stimulus identity;
4. bookmark activity and executable-node ownership match the invocation being suspended.

The provider commits suspended activity state, bookmark, Pending dispatch, child-start outbox, and checkpoint marker atomically. No child-start handler runs inline.

## Child Completed checkpoint

Before persistence fingerprinting, the DispatchWorkflow completion enricher:

1. identifies wait-mode dispatches linked to the exact child execution;
2. accepts only a `WorkflowExecutionStatus.Completed` terminal change;
3. derives the deterministic parent-resume intent/outbox identity;
4. reuses a previously committed exact intent on replay, otherwise projects safe child outputs;
5. appends the parent-resume intent while the runtime lifecycle enricher appends the Completed dispatch transition.

Any required lookup or projection failure rejects the checkpoint. Faulted/Cancelled child behavior remains #680.

## Output contract

The result is built only from durable workflow outputs projected through the configured runtime capture policy. Every entry includes name, declared type, and redaction state. A redacted entry has no value. The parent-resume payload and result never include exception details, stack traces, arbitrary child metadata, activity inspection payloads, or redacted values.

## Parent-resume intent contract

Stable intent kind: `Elsa.Activities.DispatchWorkflow.ResumeParent`.

The handler:

- validates payload and deterministic identity;
- calls `IBookmarkResumeDispatcher.DispatchAsync` with the exact parent execution, stimulus, safe result input, and deterministic idempotency key;
- re-reads parent workflow, activity, and bookmark state after dispatch;
- returns success only when the bookmark is consumed, the activity/workflow is terminal, or retained parent state is gone;
- otherwise throws a stable safe deferral failure so the outbox reschedules with backoff.

`Dispatched` or `Duplicate` from the actor dispatcher is not by itself acknowledgement; consumption state is authoritative.

## Retry policy contract

Intent-handler registration remains the sole source of intent kind ownership. The existing registration overload assigns `RetryPolicy.None`. A new overload may associate a retry policy with the same contribution.

Only the parent-resume kind uses `RetryUntilAcknowledged` with positive backoff. Its attempt count saturates and never triggers finite final failure. Other contributed and unsupported kinds keep their existing policy. Unsupported kinds throw through the dispatcher and follow their existing failed/final outbox path; they are not acknowledged.

Each recorded retryable parent-resume attempt emits one alertable structured warning carrying only stable work/dispatch identifiers, intent kind, saturated attempt count, and next-availability timing. The warning contract excludes intent payload JSON, child output values, exception details, stack traces, and actor-envelope content.

Finite exhaustion, dead-letter, and redrive are not part of this contract.

## Resume callback contract

The DispatchWorkflow resume target is context-shaped and reads only the safe parent-resume payload from `IExecutionExpressionState.ResumeInput`. It validates parent/activity/child/bookmark identity, sets `ChildWorkflowExecutionId`, sets the provided Completed result, and selects `Completed`. The generic resume handler owns activity completion, bookmark deletion, durable output capture, and graph propagation in one bookmark-consumption checkpoint.

Duplicate resume work after completion does not invoke the callback again. Missing bookmark plus nonterminal suspended activity remains retryable; missing bookmark plus terminal activity/workflow is idempotent success.

## Groundwork contract

Groundwork must prove atomic and restart-safe behavior at these boundaries:

1. before/after parent wait checkpoint commit;
2. before/during child-start outbox claim and materialization;
3. before/after child Completed checkpoint and parent-resume outbox creation;
4. before/during parent-resume claim and command dispatch;
5. before/after bookmark-consumption checkpoint;
6. before/after ordinary parent completion propagation;
7. before outbox acknowledgement and after uncertain acknowledgement.

Every recovery path converges on one child execution, one bookmark consumption, one parent activity completion, and one equivalent safe result.

## Compatibility contract

- Existing public constructors remain available and forward to new complete forms.
- `IWorkflowDispatchStore` and `IRuntimePostCommitOutboxStore` remain unchanged.
- Lookup is additive through `IPostCommitOutboxLookupStore`.
- Handler vocabulary stays `HandleAsync`; aggregate dispatcher stays `DispatchAsync`; scheduler adapter preserves its legacy dispatcher interface.
- Fire-and-forget checkpoint/outcome and unsupported-kind behavior remain unchanged.
