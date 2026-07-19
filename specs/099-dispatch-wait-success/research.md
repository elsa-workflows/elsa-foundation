# Research: Wait for a Successful Child and Return Safe Outputs

## Decision 1: Commit wait state directly on the activity invocation boundary

**Decision**: Extend the existing workflow-dispatch staging request with an optional wait bookmark. When wait mode is staged, the invoke handler builds one mandatory bookmark-created checkpoint containing the suspended activity state, bookmark, Pending dispatch, child-start intent, inspection projection, and any durable write-back. It does not enqueue the ordinary two-step CreateBookmark work item first.

**Rationale**: The current ordinary bookmark path enqueues CreateBookmark after activity invocation. Staging dispatch plus bookmark is currently rejected, and adapting that two-step path would leave either child-start responsibility or the parent wait visible first. One direct commit is the only existing provider-neutral unit that satisfies the issue’s atomicity and “child not visible before wait” requirements.

**Alternatives rejected**:

- Commit the dispatch, then create a bookmark: a child can become visible before the parent wait.
- Create the bookmark, then commit dispatch: a crash can strand the parent forever with no child-start responsibility.
- Add a broker transaction: forbidden by the program guardrails and redundant with the runtime checkpoint/outbox transaction.

## Decision 2: Derive all wait and resume identities from `WorkflowDispatchIdentity`

**Decision**: Add deterministic bookmark ID, stimulus hash, parent-resume intent ID, and parent-resume idempotency key derivations to the existing versioned dispatch identity utility. The bookmark has one DispatchWorkflow-owned stimulus type, exact parent/activity linkage, the DispatchWorkflow completion resume target, and no expiry.

**Rationale**: Parent checkpoint replay, duplicate terminal observation, and duplicate resume delivery all need the same identities after process recreation. Keeping one digest and distinct prefixes follows the existing child/start identity contract and prevents accidental field drift.

**Alternatives rejected**:

- Generate bookmark or resume IDs at delivery time: process recreation would create different work.
- Use only child execution ID as the bookmark: it loses parent activity ownership and makes cross-parent collision defense weaker.
- Add an authored timeout: #679 explicitly requires no built-in timeout.

## Decision 3: Reuse ordinary bookmark consumption for parent activity completion

**Decision**: Contribute a parent-resume post-commit handler that validates the deterministic payload, calls `IBookmarkResumeDispatcher`, and rechecks bookmark/activity/workflow state. DispatchWorkflow exposes a context-shaped private `[ResumeTarget]` callback, reads the serialized payload from the existing `IExecutionExpressionState.ResumeInput` carrier, validates it, sets both outputs, and selects `Completed`. The existing resume work handler then atomically consumes the bookmark, persists the completed activity state and outputs, and records ordinary parent graph completion work.

**Rationale**: The existing dispatcher selects the configured actor provider and the existing bookmark-consumption checkpoint already provides idempotent single consumption and completion propagation. Reusing it keeps delivery outside actor mailboxes and avoids a DispatchWorkflow-specific parent mutation path.

**Alternatives rejected**:

- Directly update the parent activity from the child checkpoint: violates the per-execution single-writer/actor boundary.
- Add a second resume queue or bespoke parent dispatcher: duplicates the global resumption stack.
- Acknowledge after enqueue alone: a crash can occur before bookmark consumption; the criterion requires retry until consumed or terminal.

## Decision 4: Add an explicit unbounded retry policy only to the resume contribution

**Decision**: Extend `RuntimePostCommitIntentHandlerContribution` with an optional persisted outbox retry policy. Preserve `AddRuntimePostCommitIntentHandler<THandler>(kind)` as the source-compatible `RetryPolicy.None` overload and add a policy-bearing overload. Add an unbounded `RetryUntilAcknowledged` policy mode with positive fixed backoff and saturating attempt accounting. Only the DispatchWorkflow parent-resume contribution opts into it.

**Rationale**: Generated outbox items currently use `RetryPolicy.None`; merely throwing from the handler would make the first miss final. Handler contribution is already the authoritative intent-kind registration and therefore the narrowest single source for delivery policy. Missing contributions and unsupported kinds continue to receive `None`, so they fail through the existing safe path and are never silently acknowledged.

**Alternatives rejected**:

- Change the global default to retry: changes unsupported-kind semantics and can strand arbitrary invalid intents.
- Use `int.MaxValue` attempts: it still has an exhaustion boundary and can overflow.
- Implement finite exhaustion or dead-letter now: owned by #681.

Retry recording also emits one structured warning suitable for log-based alerting. Its fields are limited to the outbox item ID, intent ID/kind, dispatch ID when present, saturated attempt count, and next availability; it never includes the serialized intent payload, child outputs, captured exception detail, or actor-envelope content. This satisfies the parent program's operational-alerting requirement without introducing #681 exhaustion or incident/dead-letter semantics.

## Decision 5: Capture safe child workflow outputs in terminal enrichment

**Decision**: Add a Runtime Core output-source contract returning the existing safe workflow-output projection for one execution after applying the terminal commit’s pending durable-value changes. The Runtime implementation reuses `RuntimeWorkflowOutputStateProjection` and `IRuntimePayloadCapturePolicy`. A DispatchWorkflow completion enricher selects only Completed wait-mode dispatches and serializes the resulting safe entries into the parent-resume payload.

**Rationale**: Durable workflow outputs—not activity inspection values—are the authored result channel. The existing projection already enforces JSON values, declared type, explicit redaction markers, and configured capture policy. A Core contract lets the DispatchWorkflow feature consume that behavior without adding a Runtime implementation reference or duplicating security logic.

**Alternatives rejected**:

- Read raw durable values in the DispatchWorkflow module: duplicates capture/redaction policy and risks disclosure.
- Read activity inspection outputs: those are diagnostic evidence, not declared workflow results.
- Query the child after committing Completed: creates a terminal-without-resume crash gap.

## Decision 6: Reuse a committed terminal intent on checkpoint replay

**Decision**: Add an optional `IPostCommitOutboxLookupStore` capability implemented by built-in in-memory and Groundwork outbox stores. Before creating a Completed parent-resume intent, the enricher derives its exact outbox item ID from child commit ID and resume intent ID. If a committed item exists, it validates and reuses that exact intent; otherwise it captures outputs and creates the first intent.

**Rationale**: Capture policy or configuration can change after an uncertain acknowledgement. Recapturing on replay could produce a different payload and checkpoint fingerprint even though the first commit succeeded. Reusing the committed outbox payload preserves replay identity without storing raw result values on the dispatch inspection record.

**Alternatives rejected**:

- Recompute outputs on every replay: not byte-stable across policy changes.
- Store the result on `WorkflowDispatchRecord`: expands an inspection/lifecycle record with raw result values and complicates lifecycle validation.
- Query only deliverable items: a delivered item is still needed for uncertain checkpoint replay but is excluded from deliverable queries.

## Decision 7: Terminal enrichment fails closed

**Decision**: On a child Completed checkpoint, inability to query the dispatch, read/project required output state, or perform committed-outbox lookup fails the checkpoint before persistence. The same checkpoint always appends both the Completed lifecycle projection and resume intent for a wait-mode record.

**Rationale**: Committing Completed while losing resume responsibility violates the feature’s central invariant. Temporary read/provider failure is recoverable by checkpoint redelivery; a partially committed success is not.

## Decision 8: Treat consumption, terminal parent, or removed parent as acknowledgement

**Decision**: The parent-resume handler acknowledges when the bookmark is absent and the parent activity is completed/terminal, the parent workflow is terminal, or retained parent state has already been removed. If the parent is nonterminal and its DispatchWorkflow activity remains suspended but the bookmark is absent, delivery remains retryable. Accepted, Duplicate, Deferred, and Rejected dispatcher results are all followed by authoritative state rechecks.

**Rationale**: Dispatcher status alone does not prove the bookmark was consumed, especially for asynchronous actor providers. State rechecks distinguish harmless duplicate delivery from a still-unmet wait.

## Decision 9: Version the additive outbox retry shape

**Decision**: Replace the Groundwork post-commit outbox baseline with v3 for the unbounded-policy flag. Before GA, v3 is both current and minimum-readable, the v2 fixture is removed, no Elsa upcaster or registry is added, and deployments with older persisted rows require the documented datastore reset. Lookup uses the same document kind and introduces no new storage unit.

**Rationale**: Retry policy is durable delivery behavior. A current-only clean baseline matches the repository's pre-GA serialization policy and prevents older rows from silently acquiring unbounded behavior.

## Compatibility and Scope Findings

- Preserve every existing `WorkflowDispatchCheckpointRequest`, `RuntimePostCommitIntentHandlerContribution`, `RuntimePostCommitRetryPolicy`, and `RuntimeCheckpointCommitter` constructor through forwarding overloads.
- Preserve `IRuntimePostCommitOutboxStore`; lookup is a separate additive capability.
- Preserve the handler vocabulary established in #675: handlers use `HandleAsync`; the aggregate uses `DispatchAsync`; the scheduler adapter retains its legacy dispatcher interface.
- Keep fire-and-forget activity completion and `Dispatched` semantics byte-compatible.
- Child Faulted/Cancelled and parent cancellation behavior are deliberately not handled until #680.
- Retry exhaustion, dead-letter, and redrive remain #681 even though #679 adds one explicitly unbounded successful-resume policy.
- TestRun and distributed placement remain #682 and #683 respectively.
