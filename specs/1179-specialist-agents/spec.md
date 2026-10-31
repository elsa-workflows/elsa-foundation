# Feature Specification: User-defined specialist agents

**Feature Branch**: `agent/codex/3538-specialist-design`

**Created**: 2026-10-31

**Status**: Approved — design reviewed; product implementation incomplete

**Input**: Program [#3519](https://github.com/elsa-workflows/elsa-foundation/issues/3519):
deliver user-defined specialist agents through native Elsa workflows and BPMN, with real models,
explicit continuity and bounded collaboration. [M1 #3521](https://github.com/elsa-workflows/elsa-foundation/issues/3521)
and [design leaf #3538](https://github.com/elsa-workflows/elsa-foundation/issues/3538) own this design packet.
The owner authorized end-to-end delivery and recommended defaults. This specification describes
the product to implement, not behavior established by M0 research or by merging the design packet.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Create, test and publish a specialist (Priority: P1)

An authorized author creates Copywriter, chooses an available model connection, writes instructions
and defines a JSON brief/result contract. They test a saved draft snapshot, inspect its result and
usage, then review and publish a version that others can select as an activity.

**Why this priority**: This delivers the smallest complete specialist workflow, beyond a saved prompt.

**Independent Test**: Create → save → test → review → publish → select the named activity. Reloading
retains the saved authoring state and exact published identity.

**Acceptance Scenarios**:

1. **Given** create permission and a supported model connection, **when** the author saves Copywriter,
   **then** its instructions, schemas and valid settings survive reload and an exact draft revision is shown.
2. **Given** two authors at the same revision, **when** one saves first, **then** the other's save identifies
   the conflict and preserves their working content without overwriting the winner.
3. **Given** a saved candidate, **when** it is tested, **then** the same specialist execution path used by
   workflows runs that exact candidate and records input, output, model identity, usage and failures.
4. **Given** an accepted publication review, **when** the author publishes, **then** a named immutable
   activity with usable inputs, result projections and outcomes becomes discoverable to authorized users.
5. **Given** the published version, **when** instructions or settings change in another draft, **then**
   existing workflows continue to reference the earlier behavior until an explicit upgrade is accepted.

### User Story 2 — Compose an editorial process (Priority: P1)

A process author places Copywriter and Proofreader in a native workflow or BPMN process, binds the
article between them, branches on the structured review verdict, and routes accepted copy to human approval.

**Why this priority**: Composition in Elsa is the product's central promise.

**Independent Test**: Run equivalent editorial processes from saved native and BPMN definitions and
compare accepted specialist identities, inputs, results, outcomes and parent continuation.

**Acceptance Scenarios**:

1. **Given** published specialists, **when** a native Sequence/Flowchart runs the editorial example,
   **then** Proofreader receives the committed Copywriter output and the process follows the validated verdict.
2. **Given** the equivalent BPMN service tasks, **when** the process is saved/reloaded and executed,
   **then** it retains the exact specialist versions and reaches the corresponding human approval.
3. **Given** supported BPMN interchange, **when** exported content is imported again, **then** the
   exact task bindings survive; unsupported content produces an actionable refusal and retains authoring data.
4. **Given** a duplicate or late completion, **when** it reaches the process, **then** it cannot repeat
   an accepted specialist result or logically continue the parent twice.
5. **Given** a changes_requested verdict, **when** another round is allowed, **then** Copywriter receives
   the previous article and review comments; at most three total rounds run before explicit exhaustion.
6. **Given** an exact article awaiting human review, **when** the host restarts and an authorized person
   approves or denies it, **then** both process forms resume the exact wait once; denial never publishes
   the article, and duplicate, foreign, changed-article or late decisions cannot approve another wait.

### User Story 3 — Continue an explicit conversation (Priority: P1)

A caller normally runs an isolated specialist call. A caller who wants continuity supplies an explicit
conversation reference and expected revision, and can inspect or stop the resulting turn.

**Why this priority**: Continuity is a deliberate capability with visible ownership and concurrency rules.

**Independent Test**: Fresh calls share no history; two admitted turns on one conversation restore accepted
history across a process restart, while foreign scope/version and overlapping stale turns are refused.

**Acceptance Scenarios**:

1. **Given** no conversation reference, **when** two invocations run, **then** neither receives the other's history.
2. **Given** a permitted conversation and exact specialist version, **when** a subsequent turn starts,
   **then** it uses only the last accepted session/result revision and makes the continuity choice visible.
3. **Given** competing callers at one revision, **when** both request a turn, **then** at most one is admitted;
   the other receives a conflict with an explicit safe next action.
4. **Given** an accepted result followed by host failure, **when** execution recovers, **then** it returns that
   result without asking the model again. A response not durably accepted is visibly unresolved.
5. **Given** a cancelled/superseded turn, foreign tenant/owner or different specialist version, **when**
   continuation/completion is attempted, **then** it cannot replace or disclose the accepted conversation state.

### User Story 4 — Use tools and knowledge with exact approval (Priority: P2)

A specialist author attaches authorized knowledge and tools. A process owner requires human approval
for an effect. The person reviewing it sees the exact proposed action, arguments and scope.

**Why this priority**: It adds useful capabilities while preserving process authority over effects.

**Independent Test**: Exercise one workflow tool and one MCP tool, scoped knowledge, denial/revocation,
approval restart, changed arguments and an effect whose acknowledgement is lost.

**Acceptance Scenarios**:

1. **Given** scoped resources, **when** a specialist requests them, **then** only permitted observations
   and capabilities are available; the accepted decision records the resource versions it used.
2. **Given** an approval-required proposal, **when** approval is pending, denied or revoked,
   **then** no protected effect occurs. Approval for different arguments authorizes no work.
3. **Given** an approved action and a lost acknowledgement, **when** recovery runs, **then** it reconciles
   the operation's effect/receipt using the connector's supported identity rather than claiming universal exactly-once delivery.

### User Story 5 — Bound work and configure teams (Priority: P2)

An author configures a sequential team, concurrent specialist cohort or bounded refinement pattern.
The team appears as a reusable activity with explicit inputs/results and execution limits.

**Why this priority**: It extends the specialist model into collaboration without requiring another designer.

**Independent Test**: Run Copywriter/Proofreader refinement and a concurrent review cohort inside a parent
process, including exhausted budgets, cancellation, restart, duplicate messages and a delayed participant.

**Acceptance Scenarios**:

1. **Given** a concurrent cohort, **when** participants finish at different times, **then** review receives
   the complete intended round once and cannot mistake the first arrival for the whole cohort.
2. **Given** explicit round/step/token/time limits, **when** further work exceeds an enforceable limit,
   **then** it is not dispatched and the process receives a clear exhausted/cancelled outcome.
3. **Given** unknown provider usage or work already in flight, **when** limits/cancellation are inspected,
   **then** the uncertainty and remaining exposure are explicit rather than reported as zero cost or guaranteed interruption.
4. **Given** a team nested in a parent, **when** it terminates, **then** the parent receives one accepted
   result/outcome, preserving its own composition semantics.

### User Story 6 — Evaluate candidate changes (Priority: P2)

An author saves input cases and assertions/rubrics, compares candidate and published versions, and
uses those results when deciding whether to publish.

**Why this priority**: Repeatable evidence makes model-driven changes reviewable.

**Independent Test**: Save an evaluation set, run it against two exact candidates and reload its report.

**Acceptance Scenarios**:

1. **Given** saved cases, **when** evaluated, **then** results identify the exact candidate, inputs,
   model/capability configuration, assertions, observed output, usage and failure status.
2. **Given** deterministic contract checks and model-quality rubrics, **when** results are compared,
   **then** their evidence is distinct; a nondeterministic quality score cannot substitute for schema correctness.
3. **Given** cancelled/skipped/failed cases, **when** a report is displayed, **then** those states retain
   their meaning and are not summarized as passed.

### User Story 7 — Manage and inspect specialists in Studio (Priority: P1)

Authors discover specialists, manage drafts/history, test and publish, then navigate from a workflow run
to the exact specialist invocation and permitted observable details.

**Why this priority**: The module must be usable through ordinary Elsa Studio journeys.

**Independent Test**: Complete the create/test/publish/use/inspect/history journey with mouse and keyboard,
at normal and narrow widths, with edit, read-only and denied permissions.

**Acceptance Scenarios**:

1. **Given** an authoring page, **when** settings or schemas are invalid/unsupported, **then** fields explain
   the issue and offer valid choices; credentials are never displayed as prompt configuration.
2. **Given** a published/historical or source-owned specialist, **when** opened, **then** content authority
   and read-only state are clear, with explicit copy/fork/upgrade actions where permitted.
3. **Given** an invocation, **when** inspected, **then** permitted inputs/results, calls, approvals, usage,
   incidents and links are visible without secrets or private model reasoning.
4. **Given** unsaved edits, undo/redo, navigation and reload, **when** used on each editable surface,
   **then** the declared working-state/save boundary is consistent and no accepted server write is silently undone.

### User Story 8 — Operate supported deployments (Priority: P1)

Operators install a supported authoring/runtime composition and provider, upgrade it, and recover work
without changing already published specialist semantics.

**Why this priority**: Runtime-only and independently packaged deployments are required product shapes.

**Independent Test**: Qualify the named store/provider/host matrix, including an independent package
consumer, process restart, migration/rollback and disabling new admission while retaining in-flight support.

**Acceptance Scenarios**:

1. **Given** a published artifact and its required runtime capabilities, **when** loaded in a runtime-only
   consumer, **then** it executes without Design/Publishing assemblies or mutable authoring reads, including BPMN.
2. **Given** a supported upgrade/disable operation, **when** new admission is disabled, **then** existing
   artifacts/readers and in-flight executions retain their declared interpretation and recovery support.
3. **Given** authorized real-model access, **when** both editorial examples run, **then** live model results
   are recorded separately from deterministic/protocol fixtures and satisfy the finite acceptance checks.

### Edge Cases

- Draft conflict or publication review invalidated by an intervening edit; double-click/retry after an ambiguous save.
- Invalid/truncated JSON, undeclared result members, missing required fields, model refusal and provider disconnection.
- Connection revocation, lost capability information or a configuration that the selected model no longer supports.
- Conversation replay, delimiter collisions, owner/tenant change, explicit fork, version upgrade and retention purge.
- Lease loss, late result, duplicate approval, changed tool arguments and crash after effect before acknowledgement.
- Partial team cohort, bounded feedback exhaustion, cancelled child and a parent waiting on the team's completion.
- Evaluation case failure/cancellation, history access without edit permission and redacted inspection.
- Mixed package versions, disabled features, migrations interrupted between modules and an in-flight old artifact.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Authorized authors MUST create, edit, find and copy/fork specialists, and retire exact published versions through the existing lifecycle, with conflict-safe saves and explicit content authority.
- **FR-002**: Authoring MUST retain instructions, explicit JSON input/result schemas, outcomes, model selection, supported settings and resource policies by draft revision.
- **FR-003**: Model settings MUST be limited to verified capabilities/ranges of the selected connection/model; unknown support MUST not be presented as available.
- **FR-004**: Playground execution MUST use the workflow specialist runtime and record the exact saved candidate or published version used.
- **FR-005**: Publication MUST create an immutable discoverable activity with stable member keys, usable result projections and an exact version reference.
- **FR-006**: Draft changes, retirement and recommended-version changes MUST not retarget existing published workflow bindings.
- **FR-007**: Native Sequence/Flowchart and BPMN MUST bind, persist and execute the same specialist behavior and accepted result semantics.
- **FR-008**: Supported BPMN import/export MUST retain exact task bindings; unsupported interchange MUST fail explicitly without inventing a binding.
- **FR-009**: Model output MUST satisfy the exact published result contract before acceptance; invalid output MUST never become an accepted workflow result.
- **FR-010**: Calls MUST start fresh unless an explicit authorized conversation and expected revision are supplied.
- **FR-011**: Continuity MUST bind tenant, owner, exact specialist version and conversation identity, admit at most one turn at a revision and fence stale completion.
- **FR-012**: Accepted session/result state MUST recover together; replay MUST return accepted work without invoking the model again.
- **FR-013**: Missing acceptance after a potentially dispatched operation MUST expose uncertainty and a deliberate recovery action, without automatic duplicate effects.
- **FR-014**: Cancellation, supersession and retention MUST preserve authority fences, resource policy and truthful observable status.
- **FR-015**: Tools/knowledge MUST use execution-time authorization and record the exact permitted resource/capability references used in accepted decisions.
- **FR-016**: Required approval MUST identify the exact proposed tool, canonical arguments, target/scope and invocation; changed arguments MUST invalidate it.
- **FR-017**: Supported workflow and MCP tool paths MUST reconcile duplicate/conflicting/late receipts and effect-before-ack loss within their declared idempotency guarantees.
- **FR-018**: Enforceable step, round, token and time budgets MUST gate further dispatch; unknown usage and in-flight limits MUST be explicit.
- **FR-019**: Pattern-configured teams MUST be reusable activities with bounded feedback, complete-cohort barriers, scoped message visibility and one logical parent completion.
- **FR-020**: Saved evaluations MUST compare exact candidates/versions and distinguish deterministic contract checks, model-quality rubrics and incomplete execution states.
- **FR-021**: Studio MUST support discovery, create/edit/test/publish/use/inspect/history with consistent permissions, content authority, working-state/save behavior, keyboard and narrow layouts.
- **FR-022**: Inspection MUST expose permitted observable execution evidence and redact credentials, protected content and private model reasoning.
- **FR-023**: Runtime-only consumers MUST execute supported published native/BPMN artifacts without authoring data or Design/Publishing assembly dependencies.
- **FR-024**: Supported deployment/provider/store combinations MUST qualify installation, restart, migration, rollback, retained old-artifact interpretation and independent package consumption.
- **FR-025**: Delivered editorial examples MUST run with an authorized real model through native and BPMN workflows, with evidence distinguished from fixtures.
- **FR-026**: Users, module authors and operators MUST receive usable documentation, configuration examples, extension contracts and failure/recovery guidance.

### Key Entities *(include if feature involves data)*

The [glossary](../../docs/glossary/elsa.md) owns meanings. This feature relates specialist behavior
to the existing activity draft/version/publication, workflow executable, connection and execution
identities; adds scoped specialist conversations/turns and evaluation records; and treats teams as
specialist compositions. The [data model](data-model.md) will own their proposed state/transaction shapes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every Program acceptance row A1–A15 has its own observed passing product evidence before Program completion; research receipts alone close none of them.
- **SC-002**: The complete author journey and both editorial process journeys pass with exact specialist/result bindings and one logical parent continuation under duplicate/late delivery.
- **SC-003**: Fresh/continued, scope/version conflict, restart, cancellation and unknown-outcome cases preserve the declared session/result authority, with zero unauthorized accepted replacements.
- **SC-004**: Protected-tool denial/revocation/changed-approval cases produce zero protected effects; supported effect-recovery cases report their actual outcome and idempotency limit.
- **SC-005**: Every named supported provider/store/host cell has a retained qualification result; native and BPMN runtime-only package graphs contain zero Design/Publishing assemblies.
- **SC-006**: Every primary Studio journey passes keyboard, read-only/denied, save/reload and 320px overflow checks on the real product surfaces.
- **SC-007**: Evaluation reports retain exact candidate identities and distinct passed/failed/skipped/cancelled/unknown statuses; unsupported settings and malformed outputs never appear as accepted success.

## Assumptions

- Specialist-first, fresh-by-default continuity, explicit JSON I/O and native bounded team patterns are owner-approved direction.
- Existing activity publishing, permission/connection systems and workflow composition remain the owning product surfaces.
- Initial archive behavior means retiring a selected published version and explicitly updating its recommendation where needed; it is not deletion of history or a new definition-wide archive state. Existing bindings remain interpretable.
- Weaver remains a separately configured assistant experience; user-defined specialists do not enable multiple assistant providers.
- Model credentials and external access require an authorized existing connection or separately completed provisioning; no purchase is implied.
- Support is finite and named in the plan. Unsupported combinations are visible; the product scope and Program A1–A15 are not reduced by a research limitation.
- The reviewed design is approved to implement; contract signatures remain proposed until their first implementing slice. Constitution §E2.8 versioning/Model X, §E2.9 and framework §2.24 retain their recorded provisional status; this work ratifies none of them.
