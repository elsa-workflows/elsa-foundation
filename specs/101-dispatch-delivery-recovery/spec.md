# Feature Specification: Dispatch Delivery Recovery

**Feature Branch**: `codex/dispatch-workflow-program`

**Created**: 2026-07-16

**Status**: Draft

**Input**: GitHub issue #681, “Retry, dead-letter, and redrive failed dispatch delivery”

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Recover transient child-start delivery failures (Priority: P1)

As an operator, I can rely on host policy to retry transient child-start infrastructure failures without an author adding retry inputs and without creating replacement child executions.

**Why this priority**: A durable dispatch responsibility is useful only if ordinary transport, provider, and availability failures can recover without changing workflow semantics or logical child identity.

**Independent Test**: Fail child-start delivery transiently for several attempts, then allow it to succeed, and prove every attempt uses the original dispatch, child execution, start intent, and idempotency identities while no activity-level retry input exists.

**Acceptance Scenarios**:

1. **Given** a committed child-start responsibility and a transient infrastructure failure, **When** host retry policy permits another attempt, **Then** delivery is rescheduled with positive backoff and the original logical identities.
2. **Given** duplicate or crash-recovered delivery of the same responsibility, **When** one attempt materializes the child, **Then** all later attempts converge on that same child rather than creating a replacement.
3. **Given** an admitted child later reports a business fault, **When** the child terminal result is processed, **Then** infrastructure delivery retry is not scheduled.

---

### User Story 2 - Resolve exhausted waited delivery exactly once (Priority: P1)

As a workflow author waiting for a child, I receive one safe `DispatchFailed` result when child-start delivery exhausts host policy, and the failed dispatch can never be restarted after my workflow handles that result.

**Why this priority**: A waited parent must not hang forever or later observe a child after it has already made a control-flow decision from permanent delivery failure.

**Independent Test**: Exhaust a wait-mode child-start responsibility, crash and replay every boundary around failure projection and parent resumption, and prove one durable incident, one safe parent resume, one logical activity completion, and permanent redrive rejection.

**Acceptance Scenarios**:

1. **Given** a wait-mode child-start responsibility whose infrastructure retries are exhausted, **When** permanent failure is committed, **Then** the dispatch becomes `DispatchFailed`, a durable safe incident is recorded, and one replay-stable parent-resume responsibility is created.
2. **Given** the parent consumes that responsibility, **When** `DispatchWorkflow` resumes, **Then** it completes normally through the `DispatchFailed` outcome with only the child ID, incident ID, and fixed safe diagnostic classification.
3. **Given** duplicate exhaustion, resume, or acknowledgement delivery, **When** state is replayed after a crash, **Then** the parent handles at most one logical `DispatchFailed` completion.
4. **Given** a wait-mode dispatch that exhausted delivery, **When** any operator requests redrive before or after parent consumption, **Then** redrive is rejected and the abandoned dispatch remains terminal.

---

### User Story 3 - Inspect and redrive an exhausted detached dispatch (Priority: P1)

As an authorized operator, I can inspect a safe dead letter for an exhausted fire-and-forget dispatch and redrive it without changing the already-completed parent or the original child identity.

**Why this priority**: Detached delivery has no waiting workflow to resolve operational failure, so recovery requires a durable operator surface with explicit authorization and strong identity preservation.

**Independent Test**: Exhaust a fire-and-forget start, inspect it under read authorization, reject unauthorized and cross-tenant access, redrive under the distinct management authorization, and prove convergence on the original dispatch and child while the parent is never reopened.

**Acceptance Scenarios**:

1. **Given** exhausted fire-and-forget child-start delivery, **When** permanent failure is committed, **Then** the dispatch becomes `DispatchFailed` and one durable safe dead letter/incident is available without changing the completed parent.
2. **Given** an authenticated caller with failed-dispatch read authorization and the matching tenant scope, **When** the caller inspects failures, **Then** only safe lifecycle, identity, attempt, timing, and fixed diagnostic fields are returned.
3. **Given** a caller with lifecycle read authorization but without redrive authorization, **When** redrive is requested, **Then** it is denied without changing durable state.
4. **Given** an authorized redrive of a permanently failed fire-and-forget dispatch, **When** recovery is accepted, **Then** the original dispatch record, child execution ID, start intent, and idempotency identity are reused and only one active delivery responsibility exists.
5. **Given** concurrent or duplicate redrive requests, **When** they race with delivery or another redrive, **Then** they converge on one redrive generation and cannot create multiple logical children.

### Edge Cases

- An infrastructure failure before child admission is retryable; a child business fault after admission is a terminal child outcome and never a delivery retry signal.
- Retry timing and exhaustion thresholds come from host policy, not authored activity inputs, and every scheduled retry uses positive backoff.
- A successful, duplicate, durably forwarded, cancelled-before-admission, or already-terminal start delivery cannot be converted into a dead letter by a stale failure report.
- Wait-mode exhaustion permanently abandons start delivery even if the parent-resume responsibility has not yet been consumed; it is never operator-redrivable.
- Fire-and-forget redrive is allowed only while the dispatch is `DispatchFailed` for exhausted start delivery. Completed, Faulted, Cancelled, active, wait-mode, or non-delivery failures are rejected.
- A redrive racing with another caller, an expired claim, stale fencing, process restart, or late delivery result must preserve one current delivery generation and reject stale writers.
- Incident, API, and telemetry surfaces exclude serialized inputs, outputs, authority metadata, exception messages/types, stack traces, provider payloads, and arbitrary metadata.
- Cross-tenant lookup, enumeration, and redrive fail closed. Read and redrive authorization are separate decisions.
- Redrive does not reopen, resume, or otherwise mutate the fire-and-forget parent.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Child-start responsibilities MUST use a host-configured infrastructure retry policy; `DispatchWorkflow` MUST NOT add activity-level retry inputs.
- **FR-002**: Retry policy MUST distinguish retryable delivery failure from acknowledged delivery and from child business-terminal behavior after admission.
- **FR-003**: Every retry MUST preserve dispatch ID, child execution ID, start intent ID, outbox identity, and start idempotency identity and MUST use positive policy-selected backoff.
- **FR-004**: Claimed, duplicate, and crash-recovered attempts MUST be fenced so stale results cannot overwrite a newer attempt, successful admission, terminal lifecycle state, or redrive generation.
- **FR-005**: Exhaustion MUST be a durable provider-atomic transition that records the final delivery result, moves the dispatch to `DispatchFailed`, and creates one stable safe incident/dead letter before operational acknowledgement.
- **FR-006**: A delivery-failure incident MUST have a stable incident ID, fixed safe code/category/summary, attempt count, first/last attempt timestamps, and no sensitive or free-form failure content.
- **FR-007**: Wait-mode exhaustion MUST atomically create one deterministic parent-resume responsibility carrying the existing dispatch/child identity and safe incident reference.
- **FR-008**: A wait-mode exhausted result MUST complete `DispatchWorkflow` normally, expose zero child outputs, and emit only the `DispatchFailed` graph outcome.
- **FR-009**: Duplicate wait-mode exhaustion and parent-resume delivery MUST converge on one logical parent activity completion through the existing durable bookmark route.
- **FR-010**: Every wait-mode dispatch that reaches exhausted `DispatchFailed` MUST be permanently non-redrivable, regardless of whether the parent has consumed the resume responsibility.
- **FR-011**: Fire-and-forget exhaustion MUST leave the already-completed parent unchanged and MUST create no parent bookmark or resume responsibility.
- **FR-012**: Authenticated runtime inspection MUST expose bounded, tenant-scoped failed-dispatch lookup/listing with safe dispatch, child, mode, status, attempt, incident, and timing fields only.
- **FR-013**: Failed-dispatch read authorization and redrive authorization MUST be distinct, and both lookup and mutation MUST fail closed for missing authorization or tenant mismatch.
- **FR-014**: Redrive MUST be accepted only for a fire-and-forget dispatch in `DispatchFailed` specifically because child-start delivery exhausted.
- **FR-015**: Accepted redrive MUST reuse the original dispatch record, child execution ID, start intent, and idempotency identity while creating at most one current delivery generation.
- **FR-016**: Duplicate/concurrent redrive requests MUST be idempotent or explicitly conflict without creating duplicate delivery responsibility; stale pre-redrive claims/results MUST be rejected.
- **FR-017**: Successful redrive MUST follow the ordinary admission/start path and MUST NOT reopen, resume, or rewrite the parent workflow.
- **FR-018**: Observability MUST cover each delivery attempt, retry scheduling, exhaustion, incident creation, wait-parent failure resumption, redrive acceptance/rejection, and eventual delivery result using safe identifiers and classifications only.
- **FR-019**: In-memory behavior MUST provide semantic parity, while Groundwork MUST persist retry, exhaustion, incident, redrive, claim-expiry, and fencing state across process recreation.
- **FR-020**: Crash and duplicate tests MUST cover failure before/after attempt recording, retry scheduling, exhaustion, incident/dead-letter creation, parent failure resumption, redrive, successful redrive admission, acknowledgement uncertainty, expired claims, and stale writers.
- **FR-021**: Existing successful start, durable-forwarding acknowledgement, child business Faulted/Cancelled behavior, parent cancellation, safe output handling, tenant/partition/authority inheritance, and unsupported-intent failure MUST remain unchanged.
- **FR-022**: Provider wire changes MUST keep each document kind on a clean current-only baseline: when the serialized shape changes before GA, bump and replace that kind's current fixture, keep minimum-readable equal to current, and require a datastore reset rather than adding Elsa upcasters or historical fixture support. Missing optional delivery metadata MUST receive safe defaults.
- **FR-023**: This work unit MUST NOT add broker-specific contracts, activity-authored retry controls, Studio UI, TestRun behavior (#682), distributed placement/transport (#683), or WorkflowDefinitionActivity changes.

### Key Entities

- **Delivery attempt state**: Fenced operational state for the current child-start delivery generation, including safe attempt/timing and policy scheduling data.
- **Delivery-failure incident/dead letter**: Durable safe operator record tied to one exhausted fire-and-forget or wait-mode dispatch without preserving sensitive failure content.
- **Redrive generation**: Provider-atomic reactivation of the original fire-and-forget start responsibility that invalidates stale pre-redrive delivery results.
- **Failed-dispatch inspection**: Tenant-scoped safe read model protected by lifecycle-read authorization.
- **Redrive request/result**: Separately authorized operator command and deterministic outcome for an eligible dead letter.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Three transient delivery failures followed by success create exactly one child execution and preserve every deterministic dispatch/start identity across all attempts.
- **SC-002**: Replaying exhaustion and its checkpoint at least three times yields one `DispatchFailed` projection and one stable incident for each mode.
- **SC-003**: Wait-mode exhaustion delivered at least three times completes the parent activity once through `DispatchFailed`, with zero child outputs and zero activity faults.
- **SC-004**: Every wait-mode redrive attempt is rejected before mutation, both before and after parent-resume consumption.
- **SC-005**: At least 100 concurrent duplicate redrive races yield one accepted/current redrive generation, one logical child identity, and no stale result overwrites.
- **SC-006**: Restart tests at every named durability boundary converge on the same attempt, incident, lifecycle, and redrive state with no lost or duplicate responsibility.
- **SC-007**: Authorization tests prove read-only callers cannot redrive, unauthorized and cross-tenant callers receive no failure data, and authorized matching-tenant callers can redrive only eligible detached failures.
- **SC-008**: A sensitive-data corpus produces zero input/output values, authority metadata, exception text/type, stack traces, provider payloads, or arbitrary metadata in incidents, APIs, logs, metrics, or traces.
- **SC-009**: Regression suites report no behavior changes for successful/duplicate/durably-forwarded starts, child business faults/cancellation, parent cancellation, and normal fire-and-forget completion.
- **SC-010**: Architecture audits report no broker, Studio, TestRun, distributed transport, activity-authored retry, or WorkflowDefinitionActivity scope expansion.

## Assumptions

- #675’s runtime post-commit outbox remains the delivery substrate; #681 extends its policy and provider transitions rather than adding a second queue.
- #678’s Groundwork claim/fencing and authenticated dispatch inspection are the durability and API foundations.
- #679’s deterministic parent-resume route and #680’s safe `DispatchFailed` result/lifecycle projection are reused.
- Host policy supplies retry count/backoff defaults and can vary by deployment; correctness does not depend on one hard-coded number.
- A stable fixed delivery-failure classification plus incident identity is sufficient for workflow/API consumers; detailed provider exceptions remain only in access-controlled host diagnostics and are never copied into durable incident or telemetry payloads.
- The broader constitution remains draft/provisional; accepted checkpoint, persistence, single-writer, authentication, and actor-boundary decisions govern this work.

## Scope Boundaries

### Included

- Host-policy retry for child-start infrastructure delivery.
- Durable exhaustion and safe incident/dead-letter state for wait and fire-and-forget modes.
- Exactly-once safe wait-parent `DispatchFailed` resumption.
- Authenticated, tenant-scoped failed-dispatch inspection with separate read/redrive authorization.
- Idempotent identity-preserving fire-and-forget redrive.
- In-memory semantic coverage, Groundwork crash convergence, and safe observability.

### Excluded

- Activity-authored retry inputs or policies.
- Child business-fault retry or replacement children.
- TestRun execution scope (#682).
- Distributed placement, remote transport, and two-node execution (#683).
- Broker/service-bus contracts, Studio operational UI, and WorkflowDefinitionActivity.
