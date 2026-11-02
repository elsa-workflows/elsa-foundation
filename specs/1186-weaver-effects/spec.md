# Feature Specification: Weaver effects and effective session autonomy

**Feature Branch**: `agent/codex/3440-weaver-effects`

**Created**: 2026-11-01

**Status**: Implemented

**Input**: [Feature #3440](https://github.com/elsa-workflows/elsa-foundation/issues/3440), M2 of [Program #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428). The owner authorizes autonomous delivery of the existing five acceptance criteria, their lifecycle follow-through and the remaining M0–M5 roadmap.

**Review**: Independent correctness and acceptance/standards specification reviews completed on 2026-11-01. The identified policy-preservation requirement is included in story1 scenario6 and FR-006 and was independently rechecked. Approval is within the owner's existing Program delivery authorization; it neither freezes an implementation contract nor completes an acceptance criterion.

## User Scenarios & Testing

### User Story 1 - Use the acknowledged autonomy of the current conversation (Priority: P1)

A user changes Weaver's autonomy setting in an existing conversation. The displayed setting agrees with the server's acknowledged policy for subsequent work, while the conversation, context, history and previous effect receipts remain intact.

**Why this priority**: A local setting that disagrees with execution policy can perform an unintended automatic change or ask for review when automatic execution was acknowledged.

**Independent Test**: In one established conversation, switch in both directions between review-first and an allowed automatic mode. Ask a separately packaged module to perform a genuinely eligible low-risk server operation. Inspect the actual owning resource and the policy attributed to the accepted turn. Keep the existing review-required operations as controls through both supported provider paths.

**Acceptance Scenarios**:

1. **Given** an idle established conversation and an allowed requested mode, **When** the change is acknowledged, **Then** the same conversation retains its identity/history/receipts and later work uses the acknowledged effective mode.
2. **Given** an administrator's ceiling or current authority that restricts a requested mode, **When** the user changes the setting, **Then** the effective acknowledged policy remains within current limits and the display reports the actual outcome.
3. **Given** a committed mode change whose response is lost, **When** the client reads authoritative state or retries the same intent, **Then** it can resolve the setting without another conversation or duplicated effects.
4. **Given** an unresolved setting request, **When** another submission or setting change arrives, **Then** each receives a deterministic disposition and no turn silently uses an unacknowledged setting.
5. **Given** an accepted turn, **When** the user later chooses another setting, **Then** the result remains attributed to the policy accepted for that turn.
6. **Given** an established conversation with denied capabilities, redaction or context limits and other server policy restrictions, **When** only its autonomy setting changes, **Then** those unrelated restrictions remain effective and are not replaced with defaults.

### User Story 2 - Keep active and reviewed work bound to its original operation (Priority: P1)

A user reviews an exact module-owned operation. Policy changes, concurrent decisions, cancellation, lost confirmations or expiry do not silently change its target, repeat its effect or turn uncertain application into false completion.

**Why this priority**: Policy and review must remain coherent across the entire operation, including client working-state application and its acknowledgement.

**Independent Test**: Hold work at accepted-message, foreground, review, approved dispatch, application, unconfirmed result and receipt-continuation boundaries. Compete mode changes, decisions and cancellation in both orders. Observe authoritative policy, original invocation data, actual owner state and retained receipts.

**Acceptance Scenarios**:

1. **Given** accepted/active work, pending review, approved dispatch, unresolved client application or receipt continuation, **When** an incompatible mode change is requested, **Then** it receives an explicit busy/conflict outcome without changing the active policy or operation.
2. **Given** approval for exact target/revision/arguments, **When** those values or current module/host authority, validation or risk change, **Then** execution retains the exact reviewed binding or refuses before application.
3. **Given** concurrent approve/deny or execute/deny/cancel decisions, **When** one transition wins, **Then** the losing transition has an explicit correlated outcome, and no operation applies more than once.
4. **Given** cancellation or deadline after client dispatch, **When** application cannot be confirmed, **Then** the outcome explicitly preserves uncertainty and a truthful later acknowledgement can be reconciled without replay.
5. **Given** a supported working-state change, **When** the owner applies it, **Then** validation, one atomic visible batch, dirty state and whole-batch Undo remain correct; persistence and publication require their normal explicit actions.

### User Story 3 - Admit concurrent requests and policy changes in a defined order (Priority: P1)

A user or module can send overlapping requests. Collection, accepted-message insertion, foreground ownership, review and continuation agree on whether the conversation is busy and which acknowledged policy governs the accepted work.

**Why this priority**: Independent checks around asynchronous work do not establish one ordering of policy and operation admission.

**Independent Test**: Use controlled barriers across collection-to-insertion, session lookup-to-foreground registration, terminal status-to-lease release, and pause/receipt handoff. Exercise distinct, identical and conflicting requests while inspecting accepted user messages, provider entries, policy attribution and actual effects.

**Acceptance Scenarios**:

1. **Given** two distinct simultaneous user deliveries, **When** admission resolves, **Then** at most one is admitted as pending/active work and the other receives an explicit disposition.
2. **Given** identical repeated delivery identity/content, **When** requests overlap, **Then** they retain their original correlated identity without a second effect or provider turn; conflicting content is refused.
3. **Given** collection began under one acknowledged policy, **When** a competing policy change wins or loses before insertion, **Then** the accepted context/policy/turn has one consistent ordering and retains request/source/client/provenance.
4. **Given** a terminal message whose foreground ownership has not been released, **When** another transition arrives, **Then** terminal display alone does not create an idle admission gap.
5. **Given** cancellation, contributor failure or cleanup failure, **When** admission ends, **Then** owned reservations are released or remain explicitly unresolved without manufacturing an accepted turn or losing an accepted receipt.

### User Story 4 - Inspect truthful lifecycle and owner-scoped audit outcomes (Priority: P2)

A user can understand expiry, denial, application and uncertainty through correlated records for their own work. The audit query returns eligible records before its limit is applied.

**Why this priority**: Status alone cannot establish an effect, and an audit limit must not hide the caller's relevant records behind unrelated owners.

**Independent Test**: Expire a preparation before approval/execution and after dispatch with the existing clock. Repeat delivery and inspect owner state, response and correlation. Query mixed actor/tenant and legacy-owner records with a limit of one through the owning reader boundary.

**Acceptance Scenarios**:

1. **Given** expiry before dispatch, **When** approval/execution is requested repeatedly, **Then** no effect occurs and a stable correlated expiry record describes the accepted lifecycle outcome.
2. **Given** a deadline after dispatch, **When** the result is unconfirmed, **Then** audit and transport report uncertainty instead of claiming no effect or replaying work.
3. **Given** mixed owners and a small result limit, **When** a user queries audit records, **Then** current actor/tenant authorization and eligible ordering apply before the limit; missing legacy ownership grants no access.
4. **Given** a replacement module, provider or store, **When** required execution/admission/query support is absent, **Then** the result refuses explicitly rather than silently bypassing the owning boundary.

### Edge Cases

- Current session authority, tenant, module availability, target revision, host ceiling or domain risk changes during an await.
- An acknowledged policy update loses its response; two updates compete; an unchanged-mode request arrives while work is busy.
- An automatic approval recorded by existing execution policy is not a new human decision after a later policy change.
- A foreground lease remains held after a terminal message; a paused review or accepted receipt remains owned after that lease is released.
- Client acknowledgement is lost, duplicated, late, conflicting or received after authority/module/target drift. An uncertain effect cannot be replayed to acquire a receipt.
- A context contributor ignores cancellation or faults while an admission transition is pending.
- A module legitimately declares a low-risk automatic operation; existing review-required workflow edits and Home visibility changes remain review-required.
- Unsupported executor, missing renderer, stale prepared arguments and changed SDK/module contracts retain explicit outcomes.
- Legacy audit entries lack owner metadata; another owner's newer entries would otherwise consume the requested limit.
- Supported stop or a deliberately new conversation remains explicit; no silent transcript copying or conversation rotation hides policy disagreement.

## Requirements

### Functional Requirements

- **FR-001**: Route allowed/reviewed operations through the same installed owning module, with explicit unsupported execution and no success-reporting fallback.
- **FR-002**: Preserve exact prepared target, revision, arguments, originating client and invocation identity across review, application and acknowledgement.
- **FR-003**: Preserve proposed, approved, locally applied, persisted, refused and uncertain outcomes distinctly in transport, display and audit.
- **FR-004**: Enforce current actor/tenant authority, installed module/host policy, native validation and module-owned risk before execution and supported completion.
- **FR-005**: Support an acknowledged effective-autonomy change for an existing idle conversation without changing its identity, history, provenance or genuine effect receipts.
- **FR-006**: Keep every acknowledged setting within the administrator's current allowed modes/ceiling, preserve unrelated server policy restrictions during an autonomy-only change, and expose authoritative session policy readback for lost-response resolution.
- **FR-007**: Define one ordering of policy updates and message/foreground/review/application/continuation admission; explicit busy/conflict outcomes prevent stale-policy accepted work.
- **FR-008**: Keep unresolved policy delivery explicit and attribute accepted turns/results to the acknowledged policy that admitted them.
- **FR-009**: Preserve deterministic distinct, identical and conflicting delivery semantics with at most one pending/active user delivery, provider entry and effect where applicable.
- **FR-010**: Preserve original review requirements when qualifying an eligible module-owned automatic operation; the generic assistant must not encode resource-specific risk or effects.
- **FR-011**: Return correlated owner-validated effect receipts and resume the originating turn with real results/refusals through both existing provider loops.
- **FR-012**: Preserve truthful accepted receipts and no replay across duplicates, cancellation, late acknowledgement, module removal and authority/target/risk drift.
- **FR-013**: Record correlated pre-dispatch approval/execution expiry with zero dispatch/effect, and preserve post-dispatch uncertainty as a different outcome.
- **FR-014**: Preserve one atomic validated working-state batch, dirty state and whole-batch Undo where supported; normal save/publish remains explicit.
- **FR-015**: Apply actor/tenant ownership and eligible ordering before audit limits through the owning query boundary, including missing legacy ownership.
- **FR-016**: Keep installed-module discovery, context, guidance, schemas, validation, risk, rendering and application semantics with their owners; generic orchestration/transport/policy/review contains no domain allowlist or copied domain implementation.
- **FR-017**: Qualify supported composed Studio/backend and independent package consumers with actual owner readback, current policy, exact operation/receipt correlations and controlled races before finalizing shared contracts.
- **FR-018**: Distinguish deterministic/component, actual-adapter protocol and opt-in live-provider evidence; preserve failures, skips, retries, quarantine and unsupported boundaries accurately.

### Key Entities

- **Effective conversation policy**: the server-acknowledged allowed autonomy and its current administrative limits, retained with one conversation.
- **Accepted operation**: correlated request/turn/preparation with its accepted policy, immutable target/arguments and current authority requirements.
- **Lifecycle ownership**: the owner of pending, foreground, paused/review, dispatched, unconfirmed and receipt-continuation work whose handoffs define eligibility for other transitions.
- **Observed outcome**: an owner-validated receipt, explicit refusal or uncertainty, distinct from a status or assistant assertion.
- **Owner-scoped audit query**: eligible actor/tenant records and ordering evaluated before a bounded result limit.

## Success Criteria

### Measurable Outcomes

- **SC-001**: On both provider paths, every acknowledged mode change in each direction on one established conversation governs the next eligible operation, while all existing conversation/receipt identities remain unchanged.
- **SC-002**: Every controlled admission/foreground/review/application/continuation race has one explicit ordering and correlated losing disposition; no case yields a duplicate user admission, provider turn or observed effect.
- **SC-003**: Every successful application has independent owning-resource evidence; required review, original binding, current authority and whole-batch Undo/save/publish controls remain effective.
- **SC-004**: Each pre-dispatch expiry has zero dispatch/effect and correlated audit; every post-dispatch unconfirmed case remains explicit uncertainty with no replay.
- **SC-005**: Mixed-owner/tenant and legacy audit queries with a result limit of one return only eligible correctly ordered records.
- **SC-006**: Supported normal and package-composed journeys, affected complete suites, independent review, required exact-head checks and resulting-main verification pass with their actual evidence categories before issue/Project completion.

## Assumptions and Boundaries

- Prerequisite #3439 is verified Done on actual main `c0b47ceb19c0123a2a75f1475c3b2595ec3506e3` through PR3514. Existing module dispatch, refusal, binding, owner authority, receipt validation, client completion and Undo are reused.
- The current default message-insertion lock is preserved; the historical two-pending-request hypothesis is not treated as a reproduced defect. The current audit API already filters ownership before limiting; tests establish any remaining query-boundary concern.
- A mode request is acknowledged only under a demonstrated safe boundary. Unsupported/uncertain work has an explicit outcome; no design may assume a terminal label proves the conversation is idle.
- Contracts follow causal composed journeys. No new policy/admission schema, replacement-owner port or second execution engine is selected by this specification.
- Durable process-restart/distributed session recovery remains #3441; broad workflow and current-module journeys remain #3442/#3443. These boundaries do not defer this leaf's five acceptance criteria or lifecycle follow-through.
- No live-provider purchase, credential extraction, release, deployment or production publisher activation is authorized by this feature.

## Issue Traceability

| #3440 acceptance | Requirements and stories |
| --- | --- |
| Real owning executors/unsupported refusal | FR-001/004/010/016/017; stories1/2 |
| Exact review and distinct application states | FR-002/003/012/014; story2 |
| Current authority/risk/effective server autonomy | FR-004–010; stories1–3 |
| Authoritative receipts and both loops | FR-011/012/017/018; stories1/2 |
| Atomic Undo, races, duplicates and cancellation | FR-007/009/012–015; stories2–4 |

The original candidate follow-through is retained in FR-007/009 (admission), FR-013 (expiry) and FR-015 (scoped audit).

### Acceptance evidence

There are three evidence categories:
- **Component:** deterministic tests on real default services.
- **Protocol:** deterministic provider-protocol native evidence, using the actual Copilot and Anthropic adapters with packaged consumers.
- **Live:** live-provider evidence. None was claimed.

| #3440 acceptance | Evidence | Category |
| --- | --- | --- |
| Owning executors; explicit refusal when unsupported | `AgentProposalTests`, `AgentToolInvokerTests`; native size, visibility and workflow effects; `authority-refusal-continuation` and `stale-target-continuation` | Component + Protocol |
| Exact review binding; distinct proposed, approved, applied and persisted states | `AgentProposalTests` (T013), `AgentProposalOrderingTests`; `reviewed-size-control`, `visibility-review-control` and `workflow-review-control` | Component + Protocol |
| Current authority, policy, risk and effective server autonomy; client mode cannot disagree with session policy | `AgentSessionPolicyTests`, `AgentSessionAutonomyApiTests`, `AgentLifecycleOutcomeDisclosureTests`, Studio `WeaverSurface`/`agentClient` tests; four mode cases per adapter (T006 causal baseline, then T012), `busy-policy-lifecycle` | Component + Protocol |
| Correlated receipts; results and denials return to the originating turn on both loops | `denied-result-continuation`, `interrupted-after-effect`, `duplicate-effect-delivery` and the automatic receipt controls on both adapters | Protocol |
| Atomic Undo, races, duplicates, cancellation | `AgentProposalOrderingTests`, `AgentSessionAdmissionTests`, `AgentTurnOrchestratorTests` (T016–T018); `cancel-before-effect`, `duplicate-effect-delivery`, `workflow-review-control` | Component + Protocol |
| Deterministic message admission (follow-through) | `AgentSessionAdmissionTests`, `AgentApiCurrentSurfaceTests` (T016) | Component |
| Correlated expiry evidence (follow-through) | `AgentProposalTests` expiry cases (T020) | Component |
| Owner-scoped audit (follow-through) | `AgentAuditOwnershipTests`, `AgentAuditApiTests` (T021/T022) | Component |

The complete independent package witness requires every native case above. Exact heads, counts
and hashes are recorded on #3440. The normal composed Studio gate is the complete Studio browser
suite (T024).
