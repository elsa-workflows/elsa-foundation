# Feature Specification: Standalone durable jobs

**Feature Branch**: `agent/codex/3586-background-jobs`

**Created**: 2026-11-06

**Status**: In progress — scoped domain admission adopted 6 November 2026; M1 packaged checkpoint verified; M2 scheduling and full qualification remain in progress.

**Input**: [Canonical feature #3586](https://github.com/elsa-workflows/elsa-foundation/issues/3586),
owner-authorized end-to-end delivery on 5 November 2026, and this delivery chat's selected decisions.

## User Scenarios & Testing

### User Story 1 — Run a durable handler from an ordinary application (Priority: P1)

A developer registers a Generate Report handler, submits its input from an ordinary .NET application,
and processes it in a separately hosted worker. No workflow graph, workflow authoring installation or
Studio is needed. The developer can recover the accepted invocation and its useful result after restart.

**Why this priority**: standalone usage defines the product and its dependency boundary.

**Independent Test**: a clean packaged consumer and worker run the same registered handler immediately,
at a future time and on a recurring schedule; restart either process and inspect the original identity.

**Acceptance Scenarios**:

1. **Given** a valid submission, **when** durable acceptance is acknowledged, **then** its pinned handler,
   inputs, policy and identity survive restart and can be inspected without a workflow installation.
2. **Given** a lost acknowledgement within protected key recognition, **when** the producer resubmits the
   same key/request, **then** it recovers the same job; changed content conflicts. Expired-key recovery
   follows the separately disclosed retention contract and cannot assume indefinite recognition.
3. **Given** two independent workers, **when** one loses ownership or stops, **then** supported recovery
   preserves the invocation, attempts and retained outcomes under the documented provider guarantee.

### User Story 2 — Control an outage without losing work (Priority: P1)

An operator pauses Generate Report during a downstream outage. New submissions remain durable, running
reports finish, and other job types on the same queue continue. Queue, definition and instance holds
remain effective independently when the operator resumes the job type.

**Why this priority**: selective durable pause and recovery are required first-release behavior.

**Independent Test**: race pause/resume, start permissions, retry and restart across separate workers,
including all four scopes and overlapping holds while proving unrelated shared-queue progress.

**Acceptance Scenarios**:

1. **Given** any applicable pause, **when** a new attempt requests permission, **then** no permission or
   execution capacity is consumed; an unrelated eligible job on that queue can start.
2. **Given** an attempt whose durable start permission precedes pause, **when** the handler enters later,
   **then** it is shown as already Starting/Running and may finish normally with its result retained.
3. **Given** two holds, **when** one is resumed, **then** the other still prevents starts, including retries
   and future invocations covered by the logical scope, across worker and store restart.
4. **Given** a recurring schedule paused or offline for two days, **when** processing resumes under the
   default policy, **then** one catch-up invocation is created and ordinary scheduling resumes; an
   explicit replay policy preserves every missed occurrence. Previously accepted jobs remain distinct.

### User Story 3 — Invoke the same job from native workflows and BPMN (Priority: P1)

Order Fulfillment invokes the same Generate Report handler and waits for the result. If that instance
or definition is paused, physical completion remains durable and result-driven continuation waits.
The workflow can also submit independent work with an explicit cancellation/association policy.

**Why this priority**: orchestration integration is part of the initial release, with each model retaining
its own continuation and result-consumption semantics.

**Independent Test**: real native and genuine BPMN consumers invoke the same packaged handler; prove
result/failure, pause/resume, parent cancellation and restart without duplicating physical retry ownership.

**Acceptance Scenarios**:

1. **Given** an awaited job, **when** it finishes during an instance/definition pause, **then** its result
   remains inspectable and the model grants no later result-driven continuation permission until resume.
2. **Given** a committed result, **when** delivery or continuation is retried, **then** the same result
   reaches the exact saved consumer and no new physical handler attempt is created merely for redelivery.
3. **Given** a terminated parent or an independent submission, **when** cancellation/delivery is recovered,
   **then** the declared ownership policy is applied without resurrecting that parent or erasing results.

### User Story 4 — Operate jobs through authorized API and Studio (Priority: P1)

An operator locates a held or failed job, sees its queue, schedule, attempts, progress, retained result
and every hold reason, then performs an authorized retry, cancel, pause or resume with clear feedback.

**Why this priority**: operations are required first-release product behavior, independently optional
for applications that process jobs without Studio.

**Independent Test**: real API and composed Studio journeys cover two tenants, denied operations,
failure/recovery, persistence, keyboard/focus, themes and narrow layouts with inspected artifacts.

**Acceptance Scenarios**:

1. **Given** a paused job, **when** an operator inspects it, **then** all remaining reasons and permitted
   actions are visible, including a result already completed while continuation remains held.
2. **Given** another tenant's identity or insufficient permissions, **when** data or a mutation is requested,
   **then** the server enforces scope without leaking inputs, results, secret values or job existence.
3. **Given** an operator retries a terminal failure, **when** the request is accepted, **then** prior attempt
   history/results remain intact, the new run is linked explicitly and no completed parent is rewritten.

### Edge Cases

Lost acceptance/completion acknowledgements; concurrent conflicting idempotency keys; missing old handler
versions; paused queue heads; overlapping holds; due retries; schedule edits during an owed occurrence;
worker death before and after physical effects; stale completion after handover; cooperative handlers that
ignore cancellation; clock skew; parent cancellation before admission; duplicate model result delivery;
retention during an owed result; cross-tenant cursors/associations; raw exception and secret disclosure.

## Requirements

### Functional Requirements

- **FR-001**: Developers MUST register stable, explicitly versioned handler identities and bounded input/result
  schemas, submit durable invocations and inspect their accepted identities without authoring workflows.
- **FR-002**: Producers and workers MUST be independently hostable. Standalone packages MUST exclude
  unrelated native/BPMN workflow, design, expression-authoring and Studio dependencies.
- **FR-003**: Acceptance MUST acknowledge only committed responsibility and advertise its key-recognition
  horizon. Within that horizon, and while job/delivery responsibility remains pending, matching request/key
  replay returns the same identity and conflicting reuse fails. After eligible recognition cleanup, key reuse
  may create new work; receipts/documentation MUST disclose that boundary for acknowledgement recovery.
- **FR-004**: Accepted handler/schema/policy/input pins MUST survive deployment changes. Missing support
  leaves work visibly unavailable and recoverable, without silent repinning or burning handler attempts.
- **FR-005**: Immediate, delayed and recurring jobs MUST use named queues and horizontally scalable workers.
  Scheduled occurrence identity MUST be independent of delivery, worker and retry identity.
- **FR-006**: Recurring schedules MUST default to one catch-up occurrence after a pause/outage, then return
  to schedule. Explicit replay-every-occurrence uses bounded draining. Already accepted jobs are never
  merged or discarded. Operators can inspect missed/coalesced counts and retained scheduling responsibility.
- **FR-007**: The runner MUST retain attempts, failures, useful typed results and bounded progress, with
  recovery after supported worker/store restart and handover and explicit at-least-once physical execution.
- **FR-008**: One authority MUST own automatic retry/backoff and attempt budgets. Result/continuation
  redelivery MUST NOT spend physical execution attempts or introduce a second automatic retry policy.
- **FR-009**: Cancellation, timeouts, shutdown and lost ownership MUST distinguish requested stopping,
  durable terminal selection and actual physical exit. Late physical results remain identifiable.
- **FR-010**: Queue, job-type, workflow-instance and workflow-definition pauses MUST durably prevent every
  later attempt start permission across nodes and restart, including automatic/manual retries and recovery.
- **FR-011**: An attempt starts at its durable start-permission commit, ordered against pause writes.
  A prior grant can enter handler code after pause returns and MUST be visible as Starting/Running.
- **FR-012**: Work MUST remain durably acceptable while paused. Held work consumes no execution capacity;
  unrelated eligible work on the same shared physical queue progresses without a dedicated queue per workflow.
- **FR-013**: Every applicable pause scope MUST permit execution. Resuming one hold MUST NOT release another.
  Logical job-type/definition holds cover their versions and future invocations; exact instance scope remains distinct.
- **FR-014**: Pause/resume MUST be authorized, revisioned, idempotent and audited. Operations display all
  active reasons and distinguish a pause from cancellation, process suspension or rollback of effects.
- **FR-015**: Worker capacity and queue/type/workflow-associated concurrency MUST state their local versus
  deployment-wide semantics. Admission limits MUST NOT falsely promise a physical bound after lease loss.
- **FR-016**: Ownership/fencing MUST prevent stale workers from advancing authoritative state or consuming
  a successor's permits. Lease expiry alone MUST NOT be described as proof of physical worker termination.
- **FR-017**: Native and BPMN consumers MUST invoke the same handler capability through optional adapters,
  supporting awaiting a result and explicit independent submission, without a job graph language.
- **FR-018**: Workflow instance/definition pauses MUST hold new result-driven continuation permissions.
  Already granted continuation follows the same disclosed cutoff as attempt starts. Results stay durable;
  queue/job-type pauses govern attempt starts rather than holding completed result delivery.
- **FR-019**: Each model MUST own its continuation, association and consumption proof. Delivery is retired
  only after durable consumption/equivalence or exact terminal-parent proof, never mere transport acceptance.
- **FR-019a**: Durable model/schedule/outbox origins MUST retain admission recognition independently of
  ordinary producer-key expiry, including independent jobs with no result consumer. Safe retirement MUST
  cover retained retries and in-flight duplicates or leave a compact tombstone rejecting late origin replay.
- **FR-020**: Association identity MUST cover model, tenant, definition/version, exact instance and saved
  invocation. Nested/detached jobs and parent completion/cancellation MUST follow explicit ownership policies.
- **FR-021**: Authorized manual retry MUST be idempotent, audited, linked to the original run and preserve
  prior results/history. It MUST NOT silently reattach to a consumed result or resurrect a terminated workflow.
- **FR-022**: API/code operations MUST expose queues, schedules, jobs, attempts, useful results, progress,
  failures, pause state and operational recovery without requiring Studio.
- **FR-023**: The optional Studio module MUST expose those operations through existing shared controls,
  useful collection/detail navigation, clear status/action feedback and bounded inspection.
- **FR-024**: Tenant/permission boundaries MUST apply to admission, queries, cursors, associations, workers,
  results and every mutation. Inputs/results and detailed diagnostics require their appropriate permissions.
- **FR-025**: Durable work MUST use declared secret/connection references; ordinary diagnostics, lists and
  exported evidence MUST redact sensitive data. Retention MUST protect all still-owed results and receipts.
- **FR-026**: Scheduling and reusable delivery mechanics MUST coordinate with #3582/#3583 and current shared
  Runtime work without duplicating their product or making either entire program a prerequisite.
- **FR-027**: Documentation MUST distinguish durable acceptance, authorized start, physical effects, fenced
  settlement and model consumption, including idempotency obligations and provider/deployment limits.
- **FR-028**: Product acceptance MUST include real packaged standalone/native/BPMN consumers, multi-worker
  failure/recovery/pause controls, actual authorized API/Studio journeys, reviewed merged changes and resulting main.

### Key Entities

- **Handler registration**: stable type/version and supported input/result contracts.
- **Job invocation**: immutable scoped request, acceptance identity, policy and optional model association.
- **Attempt**: physical execution identity, owner/fence/deadline, outcome and retained completion evidence.
- **Result**: immutable outcome/payload identity, distinct from delivery/consumer acknowledgement.
- **Schedule and occurrence**: revisioned recurrence and durable owed scheduled instant.
- **Pause hold and concurrency permit**: scoped admission controls with distinct release/ownership identities.
- **Model association and delivery**: exact parent/wait identity, cancellation mode and owed consumption proof.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A clean packaged application and separate worker execute and inspect immediate/delayed/recurring
  jobs with zero unrelated workflow or Studio packages in the standalone dependency closure.
- **SC-002**: Real native and genuine BPMN consumers invoke the same handler and recover success/failure,
  saved results and model-owned continuation under the agreed pause/cancellation contract.
- **SC-003**: Every supported provider/deployment passes its acceptance, handover, restart, stale-owner,
  duplicate-delivery and failure matrix; unexecuted profiles are explicitly unqualified.
- **SC-004**: All four pause scopes, overlaps, retries, future/version coverage and the selected start cutoff
  pass across independent nodes and restart while unrelated shared-queue work demonstrably progresses.
- **SC-005**: Physical work finishing during a parent pause retains its result; resume consumes the same result
  without an unnecessary new handler execution, including lost delivery acknowledgements.
- **SC-006**: Real API/Studio operations pass tenant/permission and applicable empty/populated, loading/failure,
  denied, keyboard/focus, theme and narrow journeys with inspected downloadable current-commit evidence.
- **SC-007**: Each original #3586 acceptance criterion has reviewed merged/resulting-main evidence and usable
  integrator/operator documentation; no milestone-only or unmerged prototype is reported as product delivery.

## Assumptions

- ADR0096 records the owner's explicit scoped adoption of Jobs admission; no other unratified proposal becomes authority.
- First-party persistence follows current EF policy; claimed engine/deployment profiles require their own proof.
- UTC interval/cron schedules are the initial scheduling surface; any additional civil-time contract needs explicit design.
- Awaited jobs inherit validated workflow association and cancellation responsibility; independent submission states its
  association/cancellation mode. Manual recovery does not rewrite completed model state.
- External effects can repeat. Integrators provide appropriate downstream idempotency; no exactly-once effect is promised.
- Deployment, production credentials and package release are excluded from this delivery authorization.
