# Feature Specification: Weaver session, approval and outcome recovery

**Feature Branch**: `agent/claude/3441-weaver-recovery`

**Created**: 2026-11-02

**Status**: Implemented

**Input**: [Feature #3441](https://github.com/elsa-workflows/elsa-foundation/issues/3441), M2 ([#3431](https://github.com/elsa-workflows/elsa-foundation/issues/3431)) of [Program #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428). The owner authorizes autonomous delivery of the five acceptance criteria, the #3514 Code Review follow-through recorded on the issue, and the remaining M0–M5 roadmap. Prerequisite #3440 is verified Done ([closeout](https://github.com/elsa-workflows/elsa-foundation/issues/3440#issuecomment-6570028483)).

**Review**: Independent correctness/acceptance and repository-standards specification reviews completed on 2026-11-02. A second independent round verified those fixes; its findings are also incorporated: paused-turn state and pause fingerprints, receipt-versus-owner-statement precedence, the owner-only authoritative reread, the definition of an authorization pass, and the definition of workspace. Round-one findings: stream-coupled turns, the approval basis and deadline, provider resume, the shipped proposal statuses, settlement timing, orphaned claims, both history limits, the persistence family, fingerprint stability, module-owned availability and new-contract registration.

**Terminology**: A *Weaver session* (shown in Studio as a conversation) is the existing Agent session. *Proposal* and *Tool Invocation* keep their glossary meanings ([Studio/CONTEXT.md](../../Studio/CONTEXT.md)); proposal statuses are the shipped `AgentActionProposalStatus` values. A *disclosure snapshot* is an entry of the existing disclosure history from [spec 1176](../1176-weaver-turn-context/spec.md).

## User Scenarios & Testing

### User Story 1 - Return to the same session after a reload or disconnect (Priority: P1)

A user reloads Studio, closes and reopens the Weaver dock, or loses the network for a moment. They return to the same session and see its history, acknowledged autonomy setting, pending reviews and recorded outcomes. Anything interrupted by the disconnect is shown with its true outcome, and they can continue.

**Why this priority**: Today a reload or dock close loses the session from the UI and can leave the user unsure whether an operation was applied. This is the most frequent recovery need and requires no server restart.

**Independent Test**: Establish a session with an executed effect, a pending review, a non-default acknowledged autonomy setting and an accepted prompt that has not started. Reload the page, close and reopen the dock, open a second tab, and drop the event stream mid-turn. Compare what Studio shows with the server's authoritative records and the owning module's actual state.

**Acceptance Scenarios**:

1. **Given** an active session, **When** the user reloads Studio or reopens the dock in the same workspace, **Then** Studio offers that session, and reopening it shows its history, acknowledged setting, pending reviews and outcomes without creating a new session.
2. **Given** a turn whose event stream drops, **When** the user returns, **Then** Studio shows the turn's recorded outcome. A turn runs only while its stream is open, so the turn shows as Cancelled, and an operation dispatched without a receipt shows as Uncertain. Nothing is resubmitted automatically.
3. **Given** an accepted prompt that never started, **When** the user returns, **Then** Studio shows it as not started and offers to start it or withdraw it. Starting it runs the same accepted message once; it is never a new submission.
4. **Given** a pending review, **When** the user decides it after returning, **Then** the decision applies to the exact reviewed operation once, subject to its deadline.
5. **Given** a client operation that was not yet dispatched and whose unsaved target no longer exists, or whose module adapter is no longer present, **When** the session is recovered, **Then** the operation is refused with zero effect: the owning module's adapter reports the target unavailable, or the missing adapter is reported. A client operation already dispatched without a receipt stays uncertain. Studio never reconstructs vanished unsaved state.
6. **Given** a client operation left uncertain because its originating tab or page is gone, **When** the owner returns, **Then** they can resolve it explicitly. Their resolution is recorded as the owner's statement, not as a receipt, and that operation stops keeping the session busy. A paused turn that awaits the operation's outcome stays busy until the owner continues it. A truthful receipt that arrives later still reconciles the operation and supersedes the statement; the statement stays in the history.
7. **Given** two tabs on one session, **When** tab B reopens it while tab A still holds an operation awaiting the client, **Then** tab B neither refuses nor marks tab A's operation. Tab A can still complete it.
8. **Given** another actor or tenant, **When** they list or reopen the session, **Then** it is not listed, and reopening returns the same not-found or forbidden outcome as any foreign session, with no content disclosed.

### User Story 2 - Survive a server restart without repeating or losing accepted work (Priority: P1)

The host restarts while sessions are idle, waiting for review, approved but not yet executed, or executed but not yet confirmed. After the restart, nothing accepted is silently repeated, nothing recorded is lost, and anything whose outcome is unknown is shown as uncertain.

**Why this priority**: A restart is where effects can be repeated or lost. Truthful recovery is the core promise of this feature.

**Independent Test**: With durable recovery configured, restart the host at each lifecycle boundary: idle, pending review, approved before dispatch, dispatched before the receipt, receipt recorded before provider continuation, and mid-provider-turn. Control the clock to place each case before and after its deadline. After the restart, inspect the session, the owning module's actual state, the audit records and how many times each effect executed.

**Acceptance Scenarios**:

1. **Given** an idle session, **When** the host restarts, **Then** the same session, history, acknowledged policy and revision, disclosure provenance and receipts are available to its owner and can be continued.
2. **Given** a pending review, **When** the host restarts, **Then** it remains decidable against the same exact operation until its deadline (5 minutes for a prepared operation today). After the deadline it is recorded once as Expired, with zero effect.
3. **Given** an Approved operation that was not dispatched, **When** the host restarts, **Then** recovery does not execute it. It executes once only when the owner explicitly executes it, and only while all of these still hold: its deadline has not passed, the session's policy revision equals the one stamped on it, its approval binding is unchanged, the owning module confirms the exact target and revision, and current authority, validation, risk and policy all still permit it. Otherwise it is recorded as Expired or as a zero-effect refusal.
4. **Given** an operation that was dispatched but not confirmed, **When** the host restarts, **Then** it is recorded as Uncertain and never replayed. A later truthful receipt for it, arriving while the operation's prepared lifetime is still open, is reconciled without a second execution. After that lifetime has passed, the operation stays Uncertain for the owner to resolve.
5. **Given** a recorded receipt whose paused turn had not yet continued with the provider, **When** the host restarts, **Then** the paused turn and its receipt are retained. When the owner returns, the turn continues once and reports the recorded receipt to the provider without re-executing the operation.
6. **Given** a provider turn that was running, **When** the host restarts, **Then** the turn is recorded as interrupted. No hidden reasoning or provider runtime object is restored, and the user can continue the session. Each provider's handling of its own stored conversation state after an interruption is explicit (FR-021).
7. **Given** a repeated, duplicated or late result after cancellation or restart, **When** it arrives, **Then** it receives one correlated disposition and creates no second effect or provider turn. An identical retry of an accepted message returns the original message.

### User Story 3 - Keep long sessions bounded and continuable (Priority: P2)

A user works in one session for a long time. Recorded history grows with what was actually disclosed, not with every checkpoint. When the session reaches its documented limit, Studio says so and offers a supported way to continue.

**Why this priority**: Repeating cumulative disclosure snapshots on every checkpoint makes storage and owner re-admission grow quadratically, and a durable store would make that cost permanent. An undocumented hard stop is a poor outcome.

**Independent Test**: Drive a session through many checkpoints that disclose the same and different context. Measure recorded snapshot volume and owner admissions per authorization pass. Then reach each limit and follow Studio's continuation.

**Acceptance Scenarios**:

1. **Given** checkpoints that repeat an already recorded snapshot, **When** they are recorded, **Then** the snapshot is stored once and referenced afterwards. The provenance ledger still shows every disclosure boundary, in order.
2. **Given** one authorization pass that covers the same snapshot several times, **When** owners are asked for admission, **Then** each owner is asked once per distinct snapshot in that pass. Admission is never reused across checkpoints or turns.
3. **Given** a session at its snapshot limit (64 distinct snapshots) or its size limit (8 MiB of recorded disclosure, with less than 64 KiB left, so the session is treated as full), **When** work would exceed either limit, including in the middle of a turn, **Then** Studio shows "session limit reached" and offers to continue in a new session. The original stays readable with its provenance intact. One disclosure larger than any session can record (`snapshot-too-large`), or larger than the room left in a session that is not full (`snapshot-does-not-fit`), is refused on its own: the session is not marked and no continuation is offered.
4. **Given** a continuation session, **When** it starts, **Then** nothing is carried over except a link to the original. Any context it needs is disclosed again under fresh owner admission.
5. **Given** either limit, **When** any recovery path runs, **Then** it never erases provenance, resets the budget, or replays an accepted operation to make room.

### User Story 4 - Recover only under the returning user's current authority (Priority: P1)

Only a present, authenticated user's current permissions can cause disclosure or execution during recovery. Housekeeping after a restart records only zero-effect lifecycle outcomes.

**Why this priority**: The current tool authorizer uses the authenticated request's identity. A background process has no such identity, and borrowing or synthesizing one would bypass authorization.

**Independent Test**: Restart with pending, approved, dispatched and running work. Let recovery run with no user present. Then return as the original user, as that user after a permission was revoked, and as a different user. Observe disclosure, execution and audit outcomes.

**Acceptance Scenarios**:

1. **Given** recovered work and no authenticated user, **When** recovery runs, **Then** nothing is disclosed or executed. Only lifecycle outcomes that cause no new effect are recorded, attributed to the recovery process: Expired with zero effect, and Uncertain or interrupted, which disclose a possible earlier effect without causing one.
2. **Given** the original user returns after one of their permissions was revoked, **When** they continue, **Then** the affected operations are refused with an explicit outcome and nothing is executed.
3. **Given** a user other than the owner, **When** they request the session, **Then** they receive the shared session-authorization outcome and nothing is disclosed.

### Edge Cases

- **Durable recovery not configured.** The host keeps today's in-memory behavior. Studio still recovers across reloads while the process lives, and states that a restart ends the session.
- **Recorded state is corrupt, from an incompatible version, or unavailable.** The session is shown as unavailable with a reason. Recovery does not invent a state, and no effect executes.
- **Two host processes see the same durable session.** Only one can own its active work at a time, and a superseded owner's writes are refused.
- **An expired session is found after a restart.** It is closed, not reopened.
- **A receipt arrives for an operation the restarted host recorded as Uncertain.** It is reconciled to the original operation. A receipt that conflicts with the recorded binding is refused.
- **The user reconnects while settlement of the same session is in progress.** Both see one consistent state, and neither duplicates work.
- **The host has replaced the session, streaming, invoker or proposal service.** Recovery is supported only through the documented boundary. Otherwise it reports itself unsupported and creates no shadow state; with a durable store selected, such a composition fails activation rather than run partly durable.
- **An autonomy change is requested while restored claims are still held.** It receives the documented busy outcome until the claims settle.

## Requirements

### Functional Requirements

- **FR-001**: The system MUST document which Weaver state is recoverable:
  - session identity, ownership, status and expiry;
  - history;
  - acknowledged policy and revision;
  - disclosure snapshots and the provenance ledger;
  - proposals with their exact authorized tool requests, approval bindings, statuses and receipts;
  - accepted messages;
  - paused turns awaiting continuation, with their pause fingerprints;
  - the duplicate-detection indexes: acknowledgement digests, call-input digests and per-turn mutation keys;
  - work claims.

  It MUST also document what is not recoverable: provider runtime objects, open stream connections, hidden reasoning, credentials and unsaved browser working state.
- **FR-002**: Hosts MUST be able to enable durable recovery through an opt-in EF Core persistence feature, following [ADR 0073](../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md). The feature MUST:
  - support SQLite, SQL Server, PostgreSQL and MySQL;
  - ship per-provider migrations;
  - prove its provider contracts on each engine.

  The in-memory default and its documented restart limit remain.
- **FR-003**: Elsa's recorded state MUST NOT contain credentials, provider runtime objects or hidden reasoning. Sensitive recorded content MUST receive the platform's existing payload protection. Provider-owned storage that a provider keeps under its own host configuration is documented as provider-owned; it is not Elsa's recorded state.
- **FR-004**: Durable recovery MUST keep server-owned autonomy supported. The session owner gains durable backing; it is not replaced. Policy readback and conditional autonomy changes therefore keep working, and no policy state exists outside that owner.
- **FR-005**: Work claims and their ordering against policy MUST survive a restart. Every restored claim MUST end in an explicit state:
  - **Foreground** claim of a turn that was running: the turn is interrupted.
  - **Prompt** claim of an accepted prompt that never started: the prompt is not started, and its owner can start or withdraw it.
  - **Pause** and **Proposal** claims: they follow their proposal's status and deadline.
  - **Client-scope Uncertain** claim: it is released when the originating continuation consumes the outcome, when a truthful receipt arrives, or when the owner resolves it (story 1.6). A receipt always supersedes an owner statement.

  Nothing becomes idle silently.
- **FR-006**: Recovery MUST NOT execute, dispatch or replay an accepted effect on its own initiative. An Approved, undispatched operation executes at most once, only on the owner's explicit execute action, and only while all the conditions of story 2.3 hold. Otherwise it is recorded as Expired or as a zero-effect refusal.
- **FR-007**: A dispatched operation without a recorded receipt MUST be recorded as Uncertain after a restart. When the owner returns, Studio may reread the owning module's authoritative state through a stable target identity ([ADR 0051](../../docs/adr/0051-interactive-design-commands-recover-from-authoritative-state.md)), under the owner's current authority. That reread is shown to the owner; it is never a receipt. A later truthful receipt MUST be reconciled to the original operation without re-execution, within #3440's existing receipt-validity rules. A receipt can be validated only while the operation's prepared lifetime is open. After that, the operation stays Uncertain and the owner can resolve it (story 1.6). A dispatched client operation past its deadline stays Uncertain, never Expired.
- **FR-008**: Deadlines MUST keep running across downtime. Settlement MUST evaluate them against the current time and record an overdue item once as Expired, with zero effect and its original correlation.
- **FR-009**: Repeated, duplicated or late deliveries and results MUST receive one correlated disposition across reloads and restarts, with no second effect or provider turn. Message fingerprints, pause fingerprints and the duplicate-detection indexes MUST stay stable across storage representation and restart. An identical retry then returns the original message, and a paused turn can still continue.
- **FR-010**: Studio MUST let the user find and reopen their sessions in the current workspace after a reload or dock close, showing authoritative server state. A turn runs only while its stream is open, as in #3440. Studio MUST NOT reattach to a turn or resubmit a prompt; it MUST show the recorded outcome and offer explicit start, execute, decide or resolve actions.
- **FR-011**: Availability of a client operation's unsaved target MUST be reported by the owning module's adapter. Studio renders it. The recovery store keeps module payloads opaque, and generic orchestration interprets no domain semantics. Vanished unsaved state is never reconstructed.
- **FR-012**: Disclosure and execution during recovery MUST happen only under an authenticated present user's current actor and tenant authority, acquired through the owning authorization boundary. Without it, recovery MUST fail closed. It MUST NOT synthesize a principal or reuse expired credentials. Zero-effect lifecycle settlement is attributed to the recovery process.
- **FR-013**: Listing, reopening and audit queries MUST be scoped to the session's owner (actor and tenant) before any limit is applied, through the owning store's query, not a global scan. They MUST use the shared session-authorization outcomes.
- **FR-014**: Disclosure snapshots MUST be recorded once per distinct snapshot identity and referenced afterwards. The complete ordered provenance ledger, write-ahead checkpoints and real accepted receipts MUST be preserved. No effect is ever replayed to rebuild evidence.
- **FR-015**: An authorization pass is one admission sequence. It ends at the next write, provider call or tool execution. Awaits for owner admission and tool discovery belong to the pass. Within a pass, each disclosure source MUST be asked to admit each distinct snapshot at most once. Admission MUST NOT be reused across passes, checkpoints or turns; the re-admission after each write stays.
- **FR-016**: The per-session limits MUST be documented product limits with a distinct outcome: 64 distinct snapshots and 8 MiB of recorded disclosure (the size limit is reached when less than 64 KiB is left). Studio MUST show the outcome and offer continuation in a new session that carries over only a link (story 3.4). A single disclosure that is larger than any session can record, or that does not fit the room left in a session that is not full, MUST be refused with its own distinct detail (`snapshot-too-large`, `snapshot-does-not-fit`) without marking the session limited.
- **FR-017**: When several host processes share durable state, at most one MUST own a session's active work at a time. Writes from a superseded owner MUST be refused.
- **FR-018**: Unreadable, corrupt or incompatible recorded state MUST yield an explicit unavailable outcome with a reason and no effect.
- **FR-019**: Workflow-runtime agent continuation and its recovery semantics MUST be reused from Program #2892 (leaf #2912) and Program #3519, not duplicated. This feature owns only the durability of Weaver foreground sessions, reviews and receipts. It adds no detached turn owner and keeps the foreground cancellation tokens from #3440.
- **FR-020**: Every replacement contract this feature adds MUST prevent or detect a second registration with a clear diagnostic (constitution-framework §2.6.2). The Agent-wide gap for existing contracts remains #3633. Extension and replacement rules, including the interaction with session-policy participation, MUST be documented in the Agent extension-point catalog.
- **FR-021**: For each supported provider, the behavior after an interrupted turn MUST be explicit: whether the provider's own stored conversation state for the session is resumed, and how output or tool calls it holds beyond Elsa's record are treated. Such provider-held content MUST never count as an accepted outcome. A provider resume that falls back to a new provider conversation MUST be an explicit outcome shown to the user. Session identity ([ADR 0022](../../docs/adr/0022-copilot-sessions-use-elsa-session-identity.md)) is preserved.

### Key Entities

- **Weaver session**: identity, owner (actor and tenant), status, expiry, workspace association, history, acknowledged policy and revision.
- **Disclosure snapshot**: a disclosure recorded once by identity, with the provenance ledger entries that reference it, in order.
- **Proposal**: the exact authorized tool request, its approval binding and stamped policy revision, its deadline, its shipped status (Draft, AwaitingApproval, Approved, Denied, Edited, Executing, AwaitingClient, Executed, Failed, Cancelled, Expired, Uncertain), and its correlated receipt.
- **Work claim**: the existing Prompt, Foreground, Preparation, Proposal and Pause claims, and the active-work ownership with a fence that supersedes stale owners.
- **Recovery outcome**: the explicit disposition given to in-flight work after a reload or restart:
  - interrupted: the message is Cancelled, with a reason distinct from a user stop;
  - not started: the message stays Pending;
  - Expired;
  - Uncertain;
  - refused: the proposal is Failed with zero effect;
  - owner-resolved.
- **Workspace**: the Studio backend the user is connected to. It only narrows session lists after actor and tenant scoping.

## Success Criteria

### Measurable Outcomes

- **SC-001**: In each of the six restart boundaries of story 2, before and after each deadline, each accepted effect executes at most once on both supported provider paths (Copilot's delegated loop and Anthropic's host-driven loop), verified against the owning module's actual state. The durable store's provider contracts pass on all four engines.
- **SC-002**: After a reload or dock reopen, the user reaches the same session in no more than two interactions. 100% of its pending reviews and recorded outcomes are shown.
- **SC-003**: Across a long session that repeats disclosures, recorded snapshot volume grows with the number of distinct snapshots, not with the number of checkpoints. There is at most one owner admission per distinct snapshot per authorization pass.
- **SC-004**: With no authorized user present, recovery records zero disclosure events and zero execution events.
- **SC-005**: No recovered session, list entry or audit record is visible to a different actor or tenant in any tested scenario.
- **SC-006**: Every in-flight item found after a reload or restart has exactly one explicit recovery outcome. None is shown as idle, or as executed without a receipt.

## Assumptions and Boundaries

- Prerequisite #3440 is verified Done. Its session-policy owner, claims, lifecycle outcomes, stream-coupled turns and owner-scoped audit are reused. Its contract's conservative busy state ("until the session expires or the process restarts") is settled by FR-005.
- Design defaults chosen under the owner's Program authorization:
  - Durable recovery is an opt-in EF feature; in-memory remains the default.
  - Turns stay coupled to their stream.
  - Disclosure and execution need the returning owner's explicit action.
  - Housekeeping records only zero-effect lifecycle outcomes.
  - Approved work runs only on an explicit execute, under the original revision and deadline.
  - Limit continuation creates a new linked session.
  - Session lifetime and the 5-minute prepared deadline stay as today. Because of that deadline, most restarts lasting longer than a few minutes settle pending and approved work as Expired.
- Weaver session lists are per workspace and per owner. A session list is new API surface, designed in planning.
- Recovery applies to declared Weaver journeys. There is no general autonomous-agent product, background agent execution or universal exactly-once guarantee for external effects.
- Broad workflow and current-module journeys remain #3442 and #3443.
- No live-provider purchase, credential extraction, release, deployment or production publisher activation is authorized by this feature.
- Evidence keeps three categories distinct:
  - deterministic component tests;
  - deterministic provider-protocol native evidence with packaged consumers;
  - opt-in live-provider evidence (none claimed).

## Issue Traceability

| #3441 acceptance | Requirements and stories |
| --- | --- |
| Supported session/turn/pending-approval recovery and stores through existing persistence boundaries | FR-001/002/004/005/017/018/020; stories 1/2 |
| Persist accepted decisions, exact requests and receipts; no SDK objects, credentials or hidden reasoning | FR-001/003/006/007/021; story 2 |
| Disconnect/reconnect, restart around approval and acknowledgement, retries, duplicates, late results after cancellation | FR-005–010; stories 1/2 |
| No silent repeat of accepted effects or reconstruction of vanished browser state; explicit uncertainty and unavailable targets | FR-006/007/011/018; stories 1/2 |
| Coordinate with Program #2892 instead of a second execution owner | FR-019 |

| #3514 Code Review follow-through | Requirements and stories |
| --- | --- |
| Bound long-session history cost | FR-009/014/015; story 3 |
| Deliberate snapshot limit with Studio outcome and continuation | FR-016; story 3 |
| Foreground versus recovery authority | FR-012/013; story 4 |

### Acceptance evidence

Evidence categories:
- **C**: deterministic component tests on the real default services and the EF store.
- **P**: deterministic provider-protocol native evidence. It uses the actual Copilot (delegated) and Anthropic (host-driven) adapters, with packaged consumers on the packaged EF store over SQLite, restarted by the driver.
- **Live**: live-provider evidence. None is claimed.

The final source is `23dc6bd4a`. Every run listed is recorded on #3441, including the failed attempts.

| #3441 item | Evidence |
| --- | --- |
| Supported recovery through existing persistence boundaries | C: `AgentRecoveryRestartTests` (both loops), `EfAgentSessionRecordStoreTests`, `AgentRecordStoreProviderTests` (PostgreSQL 16, SQL Server 2022, MySQL 8.4), `AgentCompositionTests`, `WorkbenchAgentDurableStoreTests`. P: 20 restart cases |
| Durable decisions, exact requests and receipts; no secrets, SDK objects or hidden reasoning | C: `EfAgentProtectionTests`, `AgentRecoveryDeduplicationTests`. P: `receipt-recorded-paused`, `approved-before-dispatch-*` |
| Reconnect, restarts, retries, duplicates and late results | C: `AgentRecoveryRestartTests`, `AgentRecoveryFailurePathTests`, `WeaverRecovery.test.tsx`, `weaver-recovery.spec.ts`. P: `idle`, `pending-review-*`, `dispatched-without-receipt`, `provider-turn-in-progress`, `second-client` |
| No silent repeat; explicit uncertainty and unavailable targets | C: `AgentRecoveryExecutionTests`, `clientInvocationRunner` tests. P: `dispatched-without-receipt`, `owner-resolution`, and the in-memory negative control |
| One execution owner (Program #2892) | C: turns stay coupled to their stream, and no detached owner exists. Boundary cross-linked on #2912 |
| Bounded history cost | C: `AgentDisclosureVolumeTests` (the T023 baseline failed first; T024 makes it pass) |
| Deliberate limits with a Studio outcome and continuation | C: limit tests at turn, store, API, Vitest and Playwright level |
| Foreground versus recovery authority | C: `AgentRecoveryAuthorityTests`, `AgentRecoveryAuthorizationTests` |

Final-head proofs:
- **Composed Studio:** 927 tests, 921 passed and 0 failed.
- **Complete package witness:** 89 cases, with `independentPackageWitness:true`.
- **Full affected suites:** listed in tasks.md T030.
