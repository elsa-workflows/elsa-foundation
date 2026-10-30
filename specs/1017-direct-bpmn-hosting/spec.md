# Feature Specification: Direct and embedded BPMN production hosting

**Feature Branch**: `agent/codex/2898-direct-bpmn-hosting`

**Created**: 2026-10-28

**Status**: Implemented — prepared for PR3410 under the spec lifecycle after round3 correction/actual recovery/Studio qualification; shipped status begins at merge. Clean full-PR review and owner/scan/CI/protected delivery gates remain open.

**Input**: Deliver existing [#2898](https://github.com/elsa-workflows/elsa-foundation/issues/2898) within [program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892), after verified #2735 adoption: direct and embedded BPMN hosting, restart and operational races, real recoverable pipeline invocation, and supported human-task/incident commands. Preserve authoring and saved executions. Broader operational and Studio acceptance remains [#2907](https://github.com/elsa-workflows/elsa-foundation/issues/2907).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Resume a directly hosted business process (Priority: P1)

As an integrator, I can start and inspect a BPMN process as its own execution, wait for parallel work, stop the host, and resume the same execution without losing accepted work or repeating logical progression.

**Why this priority**: First-class hosting must work independently of native composition before its extension contracts are considered proven.

**Independent Test**: Publish a parallel process, retain a partially completed fork, terminate the host, and restart against the same stores. Complete remaining work and inspect the persisted execution and join.

**Acceptance Scenarios**:

1. **Given** a supported published process, **When** started directly, **Then** it has its own execution identity and model-owned progression without an enclosing native workflow or native-root BPMN activity.
2. **Given** a fork with one accepted completion, **When** a fresh host resumes it, **Then** that branch remains complete, the other remains owed, and the join advances once after the remaining completion is accepted.
3. **Given** an accepted continuation with pending dispatch, **When** the host exits before dispatch or after recipient acceptance before acknowledgement, **Then** restart retains delivery and replay does not duplicate logical progression.
4. **Given** execution ownership has changed, **When** its old owner submits a late decision, **Then** it cannot change model state or accept new owed work. Delivery already committed by a valid owner remains recoverable and may physically finish under its declared replay boundary.

---

### User Story 2 - Resolve human, timer and worker races safely (Priority: P1)

As an operator, I can complete human work or observe interruption, trusting retries and late results cannot revive a cancelled path or complete a different task.

**Why this priority**: These races determine whether a process remains safe after delays and recovery.

**Independent Test**: Use supported commands to exercise human/timer and worker/timer acceptance in both orders, with restart and late obsolete inputs.

**Acceptance Scenarios**:

1. **Given** a human wait with an interrupting timer, **When** human completion is accepted first, **Then** its route advances once and later timer delivery has no semantic effect.
2. **Given** the same wait, **When** interruption is accepted first, **Then** the timer route advances once and late human/worker success cannot reopen that invocation.
3. **Given** a worker result and interrupting timer competing for one invocation, **When** each is durably accepted first in separate runs, **Then** each retained winner survives restart and obsolete inputs cannot advance it.
4. **Given** an accepted completion, **When** an identical retry arrives, **Then** it returns the retained outcome; conflicting content, foreign identities and unauthorized requests cannot change the execution or disclose another scope's result.
5. **Given** broadcast matching selected recipients on first delivery, **When** dispatch is retried after a listener re-arms, **Then** retry addresses only the original recipients. A listener created after publication but before first delivery remains eligible under delivery-time matching.

---

### User Story 3 - Embed the same process in native composition (Priority: P1)

As a workflow author, I can embed supported BPMN and receive completion, business error, fault or cancellation under the native composite's established rules.

**Why this priority**: Direct hosting is insufficient if embedding changes semantics or native compatibility.

**Independent Test**: Compare equivalent direct/embedded semantic traces; qualify Sequence, Flowchart and CompensationScope parent outcomes and restart before parent delivery.

**Acceptance Scenarios**:

1. **Given** equivalent direct/embedded processes, **When** they receive the same ordered accepted inputs, **Then** element progression, work requests, catches, terminal outcome and business result agree after only documented host-identity normalization.
2. **Given** a durably accepted BPMN terminal outcome, **When** the host exits before its native parent consumes it, **Then** recovery delivers it once logically under that parent's existing continuation/fault/cancellation rules.
3. **Given** a parent-cancelled embedded invocation, **When** previously dispatched work reports success, **Then** that invocation and parent remain cancelled and no successor consumes the old result.
4. **Given** existing native-only definitions, **When** they run on the new host, **Then** their progression and inspection remain compatible, including composite-specific fault projection.

---

### User Story 4 - Call a recoverable pipeline and resolve incidents (Priority: P2)

As an integrator, I can bind a service task to a real finite pipeline and resolve human waits or incidents through supported commands, without editing stored state.

**Why this priority**: A service bridge and operational commands establish useful behavior beyond token-only examples.

**Independent Test**: Invoke a finite ETL pipeline from direct and embedded BPMN; inspect physical output and success/business-error/fault/cancel outcomes. Retry a recoverable incident and recover its retained result after restart.

**Acceptance Scenarios**:

1. **Given** accepted pipeline invocation, **When** the host exits before dispatch or after pipeline completion before BPMN delivery, **Then** recovery preserves its identity without admitting a second logical pipeline run.
2. **Given** success, modeled business error, technical fault and pipeline-originated cancellation in separate runs, **When** each is accepted, **Then** success advances, a matching handler catches the error, unhandled fault produces an incident, and cancellation retains its distinct cancelled outcome. Unmatched business errors follow the declared profile rather than silently succeeding.
3. **Given** a retryable incident, **When** an authorized retry is accepted, **Then** one new distinguishable attempt is recorded; replay creates no additional attempt and obsolete results cannot overwrite it.
4. **Given** a durable human wait or incident, **When** an operator inspects and completes/retries/cancels through supported commands after restart, **Then** commands address the exact execution and invocation and return reproducible outcomes without storage edits.
5. **Given** BPMN interruption or native-parent cancellation before pipeline admission or while its work/result is pending, **When** cancellation is delivered and the host restarts, **Then** an uncompleted pipeline is durably cancelled, its remaining partition/work obligations are settled or retained with an explicit cancellation disposition, and no new work is admitted after cancellation. Already-permitted physical work may finish under the declared external-effect boundary without advancing cancelled BPMN. A pipeline already terminal before cancellation keeps its truthful terminal result; cancellation does not rewrite completed work as cancelled.

---

### User Story 5 - Keep published and saved work usable on upgrade (Priority: P1)

As an existing user, I can retain published definitions and saved executions, upgrade, and continue the existing Studio authoring and inspection journey.

**Why this priority**: New capabilities must not invalidate work the current host already accepted.

**Independent Test**: Before the relevant writer changes, publish and park actual native and BPMN runs on the current host. Retain their durable inputs, upgrade over those stores, finish them through normal commands, and inspect the existing Studio journey.

**Acceptance Scenarios**:

1. **Given** actual pre-change saved runs, **When** the upgraded host continues them, **Then** identities, accepted work, waits, pinned definitions and outcomes remain valid. Generated state probes alone do not establish this.
2. **Given** the frozen authored/published BPMN contract, **When** an existing definition is opened, edited, exported, published, run and inspected in Studio, **Then** the frozen authoring fingerprint remains unchanged and supported bindings/diagram data are preserved.
3. **Given** saved work requiring an older supported contract, **When** new hosting capabilities are enabled, **Then** compatible behavior remains available without silent migration. Unsupported new placement is refused before acceptance; accepted supported work remains recoverable.
4. **Given** new capability admission is disabled for rollback, **When** accepted runs still require it, **Then** their readers and completion workers remain available and owed results are retained.

### Edge Cases

- Restart after partial fork completion, before dispatch, after external effect, after result acceptance and before parent acknowledgement.
- Competing timeout/human/worker inputs; multiple broadcast listeners; re-arming between first delivery and retry.
- Old invocation results; conflicting retry content; missing or foreign scope, execution, invocation or attempt identity.
- Lease expiry followed by ownership replacement; delayed old-owner decisions; independent worker-attempt and execution ownership changes.
- Parent cancellation during pipeline admission/result delivery; external effects that cannot be undone; child terminal acceptance before parent delivery.
- Missing bindings, unsupported constructs/versions, immutable definition pins and the same authored process published at different versions.
- Corrupt/incompatible versus valid historical state; absent historical serializer fields; rollback with new-capability work still pending.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Direct BPMN MUST have its own execution identity and model-owned progression without a native workflow/root-BPMN activity substitute. The same interpreter MUST support native embedding (US1, US3).
- **FR-002**: Direct/embedded execution MUST use the selected released semantics and the same declared supported construct/binding profile; equivalent accepted inputs MUST produce equivalent traces with explicitly limited host-identity normalization (US3).
- **FR-003**: Progression MUST remain model-owned. Shared hosting MUST provide durable coordination without BPMN token policy or changed native composition semantics (US1, US3).
- **FR-004**: Decision acceptance MUST atomically retain resulting model state, owed work, accepted-input identity and replay outcome under the current owner. Failure MUST expose the complete accepted decision or no accepted decision (US1).
- **FR-005**: Owed work MUST remain recoverable until recipient acceptance is proven. Duplicate dispatch/acknowledgement loss MUST NOT duplicate logical progression; physical external effects retain their declared at-least-once boundary (US1, US4).
- **FR-006**: Replaced owners MUST NOT accept state decisions or new owed work. Previously committed delivery remains owed and may physically finish after ownership changes under FR-005; ownership loss does not revoke an external send atomically. Worker-attempt validity MUST be checked independently so a current execution owner cannot advance the model with an obsolete result (US1, US2).
- **FR-007**: Commands/deliveries MUST identify authorized scope, execution and exact invocation, with distinguishable attempts. Authorization MUST precede replay-result disclosure. Identical retries MUST replay; conflicting retries and foreign identities MUST have no effect (US2, US4).
- **FR-008**: Fork/join MUST survive actual termination and fresh-process recovery, retaining accepted branches and advancing the join once logically (US1).
- **FR-009**: Human completion, interrupting timer and worker-result races MUST follow the first durably accepted decision. Both human/timer and worker/timer orders MUST be qualified with restart and late obsolete inputs (US2).
- **FR-010**: Broadcasts MUST preserve delivery-time matching by durably retaining the first delivery's recipient snapshot before dispatch. Retries MUST NOT match newly re-armed listeners; publication-time matching MUST NOT replace the settled rule (US2).
- **FR-011**: Human completion, execution cancellation, execution/task inspection, incident inspection and incident retry MUST operate through supported product commands with durable identities and replay behavior, without qualification depending on stored-state edits (US2, US4).
- **FR-012**: Service tasks MUST invoke a real recoverable finite ETL pipeline with one logical identity through admission/dispatch/completion/callback recovery. Physical destination behavior and replay limits MUST be declared and observed (US4).
- **FR-013**: The bridge MUST preserve success, matching/unmatched business error, technical fault and pipeline-originated cancellation. BPMN interruption/native-parent cancellation MUST also reach the actual pipeline, including before admission and during pending work/result delivery. After restart, qualification MUST inspect the pipeline's durable terminal/cancellation disposition and remaining work; stopping BPMN advancement while leaving an uncompleted pipeline Running is insufficient. Already-terminal pipeline results MUST remain truthful, and already-permitted physical effects retain FR-005's boundary. Direct/embedded qualification MUST exercise these outcomes and native-parent mappings without fault/cancellation becoming success (US3, US4).
- **FR-014**: Incident retry MUST create one distinguishable new attempt, retain previous evidence, reject obsolete results, and replay identical retry commands without creating another attempt (US4).
- **FR-015**: Sequence, Flowchart and CompensationScope parent outcomes and persisted child delivery MUST remain compatible, including composite-specific fault/inspection behavior. This work MUST NOT settle separate owner decision #3242 (US3).
- **FR-016**: Before modifying/deleting an affected saved-execution writer, qualification MUST capture actual current-host native/BPMN checkpoints through normal publication/execution commands. Upgrade proof MUST resume and finish those actual inputs; synthetic probes/baseline passes MUST remain separate controls (US5).
- **FR-017**: Authored/published BPMN contracts and the frozen authoring fingerprint MUST remain unchanged. Compatible historical payloads, pins, bindings and pending delivery MUST retain recovery paths; absent new optional data MUST NOT change historical replay identity (US5).
- **FR-018**: Existing Studio open/edit/export/publish/run/inspection MUST be qualified against the changed host with an actual saved definition/execution. This MUST NOT be represented as first-class direct-host Studio acceptance under #2907 (US5).
- **FR-019**: Unsupported constructs/bindings/versions MUST be refused before accepting new work, with useful process/element context. Upgrade MUST NOT permanently refuse previously accepted supported artifacts or silently migrate active instances (US5).
- **FR-020**: Rollback MUST stop new capability admissions while retaining readers/workers for already accepted instances and results; execution versions and limitations MUST remain inspectable (US5).
- **FR-021**: Qualification MUST declare construct/binding/operation support, XML/diagram boundaries and unsupported cuts, distinguishing actual-host proof from library conformance, experiments and broader #2907 acceptance (US1–US5).
- **FR-022**: Every claimed recovery/race/compatibility/outcome guarantee MUST have source-bound actual-host evidence and meaningful failure controls where safeguards change. Record versions, durable inputs, identities, observable outputs and limitations. Library tests/state probes MUST NOT substitute for upgraded-host behavior (US1–US5).

### Key Entities *(include if feature involves data)*

- **Published process**: Immutable authored process, supported profile, version and bindings used by an execution.
- **Model execution**: Scope-bound process identity, retained model state, owner/revision and optional native parent.
- **Accepted input**: Authorized command/delivery bound to an invocation, with retained replay identity and outcome.
- **Owed work**: Accepted dispatch, timer, signal-recipient delivery or parent outcome pending proven recipient acceptance.
- **Task invocation and attempt**: One logical human/service obligation with distinguishable attempts and optional pipeline identity.
- **Incident**: Inspectable technical failure with retained cause, attempt and authorized resolution history.
- **Compatibility capture**: Actual old-host published artifacts, saved stores and observations retained before writer changes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The direct parallel restart journey retains both branch completions and exactly one logical join/terminal outcome; its embedded counterpart has the same normalized trace.
- **SC-002**: All four race orders (human first, timer before human, worker first, timer before worker) and the broadcast recipient/re-armed-listener scenario retain their accepted outcomes through restart, with zero obsolete-input advancements.
- **SC-003**: Direct/embedded pipeline runs demonstrate success, business error, fault and pipeline-originated cancellation with physical output or the retained non-success outcome and no duplicate logical admission after recovery. Business-error coverage includes matched/unmatched cases. Separate BPMN/parent-interruption runs inspect actual pipeline cancellation and remaining-work disposition across admission/result races and restart; zero uncompleted pipelines are left Running after cancellation settles, and previously terminal results are not rewritten.
- **SC-004**: Operators inspect/complete an actual human wait, inspect/retry an incident, and cancel execution through supported commands after restart. Identical/conflicting retries, unauthorized requests and foreign identities produce the specified outcomes without state edits.
- **SC-005**: Actual pre-change native/BPMN runs finish on the upgraded host; the existing Studio author/export/publish/run/inspect journey passes; the frozen fingerprint is unchanged; all declared native-parent outcome cases retain prior behavior.
- **SC-006**: Each #2898 criterion links to reproducible production evidence and limits. #2907 criteria are checked only against their own operational/Studio evidence; remaining program journeys stay open.

## Assumptions

- The [18 October continuation](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6249319967), [Orleans deferral](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6287993256) and current #2898/#2907 dispositions govern this one active task. No competing interpreter, issue or architecture-research restart is needed.
- BPMN 0.5.0/payload 1.10.0 is qualified and adopted by #2735. The frozen authoring fingerprint is `41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`. [Delivered qualification](../1006-bpmn-library-adoption/evidence/resulting-main-qualification.md) supplies the existing boundary, not a substitute for fresh pre-writer captures.
- Reuse the existing fenced checkpoint/outbox authority and qualified built-in host. Do not create an independent durability authority, select a new actor framework or put semantic policies in shared hosting. The plan must demonstrate the smallest shared state/dispatch boundary with real native and BPMN consumers before any stable neutral-extension claim.
- The initial profile covers constructs/bindings required by these parallel, human/timer, pipeline/error and incident journeys. Review its complete inventory in the plan; it does not imply full BPMN conformance. Respect import ownership #2736 and existing unsupported-import refusal.
- Existing finite ETL supplies the service witness. Continuous processing, SQL/RabbitMQ connectors and wider persistence-provider qualification remain #2908 obligations. The plan must declare selected persistence and avoid projecting its guarantees onto unqualified providers.
- Full task administration, first-class direct-host Studio and broader operational extension acceptance remain #2907 work beyond this minimal supported-command journey. Arbitrary alteration, live migration, full operator-console UX, further Orleans and the task-runner follow-up remain deferred.
- Neutral contracts are not presumed stable. Provisional activity-versioning/artifact-policy provisions retain their constitutional status; implementing this task does not ratify draft material.
