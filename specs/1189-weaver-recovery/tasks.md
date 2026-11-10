# Tasks: Weaver session, approval and outcome recovery

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [contract](contracts/recovery.md) and [quickstart](quickstart.md). The spec has passed two independent review rounds and the plan one planning review.

**Status**: Approved for implementation after an independent task review on 2026-11-02. Its verified findings are incorporated:
- each task now compiles on its own;
- the gated owner callers are listed;
- the restart harness round-trips records and has a negative control;
- the coverage gaps are tasked: idle restart, decidable review, the SC-001 matrix, late and conflicting receipts, deduplication after restart, recoverable-state documentation, ciphertext proof, closed sessions, busy-while-restored, Unsupported compositions, workspace and `ContinuedFrom`, and the foreign-user list;
- the §2.23 registration and wrapping tests, causal controls for the two known defects, and the JSON context are added;
- the false overlap with US3 is corrected.

Write risk-based tests alongside each increment, with causal red/green controls for changed behaviour, as in spec 1186. The constitutions declare no global TDD cadence; under the owner's delegated technical authority, the causal-control discipline applies to every changed behaviour. Keep existing valid behaviour and tests. Change existing tests only where a contract or an internal owner method they call becomes async or gated, and list those edits in the commit. A task is checked only after its actual outcome is verified. Restart tests dispose a host and build a new one over the same record store. They use the existing fake clock and barriers, never sleeps.

## Phase 1: Setup

- [x] T001 Refresh main, #3441 and its parents, dependencies, claims, PRs and Project state. Record the claim, Project In progress and the PR receipt on #3441, and update `docs/program-goals/weaver-across-studio.md` without adding Ready work. Verified: claim comment 6570049738, draft PR #3640, Program doc commit `7506a1b40`.
- [x] T002 Complete the independent task review of this file and record the review outcomes for the spec, plan and tasks on #3441. Verified: two spec rounds, one planning review and one task review, all recorded on #3441.

## Phase 2: Foundational — owner seams with unchanged behaviour (plan step 1)

**Goal**: The owner writes through a record store and hydrates before acting. In memory, behaviour is unchanged. Every story depends on this phase.

- [x] T003 Add the Preview contracts to `Extensions/Agent/src/Core/Contracts/AgentContracts.cs` and `AgentTurnContracts.cs`, and their models to `Extensions/Agent/src/Core/Models/AgentModels.cs`:
  - `IAgentSessionRecordStore`, covering hydrate, guarded write, ownership, unique keys, proposal-to-session lookup and the owned list;
  - `AgentSessionOwnerQuery` and `AgentSessionSummary`;
  - `AgentMessage.StatusReason`;
  - `Workspace` and `ContinuedFrom` on `AgentSessionCreateRequest` and `AgentSession`.

  The async `IAgentTurnStateStore` change belongs to T006, so this task compiles on its own.

  Record the public surface in `Extensions/Agent/src/Elsa.Agent/PublicAPI.Unshipped.txt` where it applies.

  Verified: commit `b67c7926e`; Core holds the surface (no PublicAPI file there), the accepted-message wire projection carries `StatusReason`, and `Elsa.Agent`'s tracked public surface is unchanged.
- [x] T004 Implement the default in-memory record store and register it in `Extensions/Agent/src/Elsa.Agent/Extensions/AgentServiceCollectionExtensions.cs`. Extend `Extensions/Agent/src/Elsa.Agent/Services/AgentRegistrationValidator.cs` so that registering a second `IAgentSessionRecordStore`, or a second audit trio, fails with a clear diagnostic. Add a registration test for each in `Extensions/Agent/tests/Tests/AgentCompositionTests.cs`. Durable recovery reports Unsupported when `IAgentTurnStateStore` is replaced or the store cannot fence. Add a test for that too. (Self-review round 2: with a durable store selected such a composition now fails shell activation instead, and a store that cannot fence always does; with the in-memory default it reports `unsupported`.)

  The default store round-trips records by serializing on write and on hydrate, so a test can never pass because an old host and a new host share mutable objects.

  Verified: commits `b67c7926e` and `5f8bf5fa2`; `AgentSessionRecordStoreTests` (round-trip copies, fence, lease, keys, owned list), `AgentCompositionTests` (shell activation and lifecycle resolution refuse a second store or audit facet; one-of-each replacement stays supported), and `AgentSessionOwnerWriteThroughTests` (Unsupported for a replaced turn-state store, an unfenced store or a replaced owner).
- [x] T005 Route `InMemoryAgentSessionService` in `DefaultAgentServices.cs` through an async per-session write gate, then an awaited store write, then the monitor. Requirements:
  - Hydration is single-flight.
  - Claim acquisition (Prompt, Foreground, Preparation, Proposal, Pause) and `ChangeAutonomy` serialize on the gate.
  - `MarkProposalDispatched` and every other internal method hydrate first.
  - Message status changes are compare-and-set on the expected status.

  Keep every #3440 refusal and outcome order. Update the callers in:
  - `AgentTurnServices.cs`, `AgentToolInvoker.cs`, `AgentProposalServices.cs` and `AgentSessionPolicyOwnership.cs` (`:71,78`);
  - the tests `AgentSessionPolicyTests.cs` (`:200-228`) and `AgentSessionAutonomyApiTests.cs` (`:205`).

  Add causal red-then-green tests for the unloaded-row `MarkProposalDispatched` defect.

  Verified: commit `cf359df62`; `AgentSessionOwnerWriteThroughTests` (the unloaded-row causal control was red on the previous code, write-through hydration, refused writes apply nothing, message compare-and-set) plus all 737 existing tests; the listed test edits are in the commit message.
- [x] T006 Change `IAgentTurnStateStore` in `Core/Contracts/AgentTurnContracts.cs` to `SaveAsync`, `FindAsync` and `RemoveAsync`. In the same commit, update every caller (research R2): `AgentTurnServices.cs`, `Api/AgentApi.cs`, the default registration in `AgentServiceCollectionExtensions.cs`, and the 17 test references. The default store keeps the paused turn on the owner's row, so saving the pause also records the Pause claim. Adapt only the async call sites in the existing tests, and list them in the commit message.

  Verified: commit `5f8bf5fa2`; the pause records itself and its Pause claim in one transition and hydrates on a new host; the adapted test sites are listed in the commit message.
- [x] T007 Make the call-key, turn-mutation and call-input registration in `AgentToolInvoker.cs` a single store insert. Make the durable transitions in `AgentProposalServices.cs` awaited writes:
  - Executing is written before the executor runs.
  - AwaitingClient is written before the envelope is returned.
  - `StoreOutcome` is written as one aggregate.

  Add a causal red-then-green test for the non-atomic three-step key insert.

  Verified: commit `c232d6981`; `AgentProposalRecordTests` (the three-step key causal control was red on the previous code: an identical retry re-prepared and a conflicting retry threw; Executing before dispatch, AwaitingClient before the envelope, the outcome and acknowledgement as one aggregate).
- [x] T008 Run the complete Agent suite and require all 737 existing tests plus the new ones to pass. Add barrier tests that prove:
  - a claim acquired during an in-flight policy write reads the new revision;
  - concurrent first hydrations settle once;
  - the lock order holds (proposal gate before session gate, never the reverse).

  Verified: `AgentSessionGateOrderingTests` (4 barrier cases); the complete Agent suite passes 772/772 (737 existing plus 35 new), three repeated runs, at load average about 5.5. Concurrent first hydrations are proved single-flight here; recording Expired once at hydrate belongs to T010–T011.

## Phase 3: US2 — Survive a restart without repeating or losing work (P1, plan step 2)

**Independent test**: Restart at all six story 2 boundaries, before and after each deadline. Count executions at the owning fixture.

- [x] T009 [US2] Add a restart-simulation harness to `Extensions/Agent/tests/Tests/` that disposes and rebuilds the host over one shared in-memory record store. Add these controls:
  - A causal red control: on the current code, a pending review does not survive a restart.
  - A negative control: deliberately skip one awaited write, and a restart test must fail.

  Own the SC-001 matrix: the six boundaries, before and after each deadline, with execution counts. That includes story 2.1 (idle restart) and story 2.2 (a review stays decidable before its deadline).

  Verified: commit `1bb7ac936`; `AgentRestartHarness` plus `AgentRecoveryRestartTests`. The pending-review control was red on the pre-T010 code (`agent.proposal.not_found`). A skipped approval write makes the approved-before-dispatch scenario fail. The matrix covers 6 boundaries × before/after deadline with module dispatch counts on the host-driven loop; the delegated (Copilot) adapter is exercised by T015's tests and is qualified against actual adapters in T031. Self-review round 1 (S3) runs the six boundary theories on a deterministic delegated loop too (`HeldDelegatedProvider`, dispatching through the server's tool invoker as the Copilot adapter does), before and after each deadline, on both stores.
- [x] T010 [US2] Rebuild claims at hydrate (research R4) in `DefaultAgentServices.cs`:
  - Prompt, from a Pending message;
  - Pause, from the paused turn;
  - Proposal, with deadline and dispatched flag;
  - a Streaming message with no pause becomes Cancelled with reason `interrupted`.

  Also:
  - an expired session is closed at hydrate;
  - an autonomy change returns busy while restored claims are held.

  Add one test for each claim-rebuild row and for the interrupted row of the data-model transition table.

  Verified: commit `1bb7ac936`; one test per claim-rebuild row (Prompt, Pause, Proposal with deadline and dispatched flag), the interrupted row, the expired-session closure and busy-while-restored, in `AgentRecoveryRestartTests`.
- [x] T011 [US2] Implement lazy settlement in `AgentProposalServices.cs`:
  - compare-and-set on status and version, with no proposal gates, during hydrate;
  - `ExpireBeforeDispatchAsync` accepts a null actor;
  - audit metadata `settledBy=recovery`;
  - Executing becomes Uncertain;
  - AwaitingClient past its deadline becomes Uncertain, never Expired.

  Add a test for each settlement row of the transition table, covering:
  - zero effect, a single Expired record and correlation;
  - a late truthful receipt after restart-Uncertain reconciles once;
  - a conflicting receipt is refused (FR-007, story 2.4).

  Verified: commit `1bb7ac936`; Expired once with zero effect, one `settledBy=recovery` record with no actor and the original correlation, and no second settlement on a later restart; Executing and a client request past its deadline become Uncertain, never Expired; a late truthful receipt after a restart uncertainty reconciles once and a conflicting one is refused.
- [x] T012 [US2] Add the story 2.3 recheck to `ExecuteAsync`. The stamped `PolicyRevision` must equal the current revision; otherwise refuse with 409 `agent.proposal.policy_stale`. The approval binding, the module's exact target and revision, current authority, validation, risk and policy must all still permit it, and the deadline must not have passed. Otherwise record Expired or a zero-effect Failed. Map `policy_stale` in `Api/AgentApi.cs`. Add a refusal test for each condition and an execute-once test after a restart.

  Verified: commit `06cdf3860`; `AgentRecoveryExecutionTests` refuses each condition after a restart with zero effects (the policy-revision case executed without the new check), executes once across two restarts, and the execute route answers 409 `agent.proposal.policy_stale` with the recorded failed outcome.
- [x] T013 [US2] Prove that a paused turn with a recorded receipt continues once after a restart and reports the receipt to the provider without re-executing. Prove that an identical retry of an accepted message returns the original message after a restart. Both cases use the stored fingerprints verbatim. Prove that duplicate call, call-input and acknowledgement keys deduplicate after a restart (FR-009).

  Verified: commit `715952a07`; `AgentRecoveryDeduplicationTests` (verbatim pause fingerprint and one continuation, identical prompt retry, call and call-input keys without re-preparation, acknowledgement retry and conflict).
- [x] T014 [US2] Implement owner token, fence and lease (research R5):
  - every guarded write extends the lease;
  - background renewal runs while Foreground is held or an execution is dispatched;
  - another process's live lease returns `owned_elsewhere`;
  - losing ownership mid-call returns `ownership_lost`, evicts the row and cancels the turn;
  - acknowledgement and resolution are idempotent on retry.

  Add two-process tests over one store.

  Verified: commit `c2be3ff19`; `AgentSessionOwnershipTests` over two hosts and one store (retryable `owned_elsewhere` with its hint and the API 409, lease extension by writes, takeover with a new fence, refused superseded writes, eviction and turn cancellation, renewal only while a turn runs or an execution is dispatched). Acknowledgement retries after a takeover return the retained outcome (T013); owner resolution and its retry arrive with T018.
- [x] T015 [US2] Make provider behaviour after an interruption explicit (FR-021).
  - In `Extensions/Agent/src/GitHubCopilot/Services/GitHubCopilotAgentProvider.cs`, emit `agent.provider.github_copilot.conversation_reset` when the resume falls back to a new session, and record it on the turn.
  - In `AgentTurnServices.cs` provider-history assembly, exclude Cancelled, interrupted and withdrawn prompts and their Failed partial assistant text. Keep recorded Tool receipts.
  - Document both behaviours in the Copilot README and in a new `Extensions/Agent/src/Anthropic/README.md`.
  - Add causal tests for each provider.

  Verified: commit `3c0d46622`; `AgentProviderInterruptionTests`, each case red without its change. The complete Agent suite passes 818/818 in three runs (load average up to 12).

## Phase 4: US4 — Recover only under current authority (P1)

**Independent test**: Restart with pending, approved, dispatched and running work. Then attempt recovery with no user, with the original user, with that user after a permission was revoked, and with a different user.

- [x] T016 [US4] Authorize on the plain owner columns before decoding any payload, hydrating or taking ownership, in `Api/AgentApi.cs` and the owner. A foreign actor receives only the shared 404 or 403, and the test proves no store write occurs.

  Verified: commit `33ea3d7d1` (extended to the new routes in `efece695f`); `AgentRecoveryAuthorizationTests` with a recording record store: a foreign actor, a foreign tenant and a claim-less caller reach only the owner-column and proposal-to-session reads on every session and proposal route, across pending, approved, dispatched and running work after a restart, with no write, takeover, settlement or audit record; `owned_elsewhere` reaches only the owner. Red on the previous code. The Phase 3 review fixes landed alongside in `bae4bd903`.
- [x] T017 [US4] Prove that recovery with no authenticated user records zero disclosure events and zero execution events, and only lifecycle outcomes attributed with `settledBy=recovery`. Prove that a revoked permission refuses execution with zero effect. Add the tests to `Extensions/Agent/tests/Tests/`.

  Verified: commit `e28dc85fc`; `AgentRecoveryAuthorityTests` over hosts with the HTTP tool authorizer: with no request present, hydration records only actor-less `settledBy=recovery` lifecycle records (Expired twice, Uncertain once, the turn interrupted), asks no context source anything, calls no provider and dispatches nothing; an approved operation fails closed without a user; with the tool's permission revoked the execute route records a zero-effect Failed refusal that a restored permission does not reopen. No production change was needed.

## Phase 5: US1 — Return to the same session after a reload (P1, plan step 5)

**Independent test**: Reload, reopen the dock, open a second tab and drop the stream mid-turn. Compare what Studio shows with the server records and the owning module's state.

- [x] T018 [US1] Add `IAgentSessionService.ListOwnedAsync` (default Unsupported) and `GET /_elsa/agent/sessions`, `GET …/proposals`, `POST …/messages/{id}/withdraw` and `POST …/client-invocations/{id}/resolution` to `Api/AgentApi.cs` and `Api/Models/AgentApiModels.cs`, following the contract. Requirements:
  - Scope by owner before the limit.
  - Withdraw is compare-and-set from Pending, and is refused while a turn is registered.
  - Resolution records the owner's statement, and a later receipt supersedes it. Resolution is idempotent on retry. A user's stop records `StatusReason=user-stop`.

  Add `[JsonSerializable]` entries for the new request and response types in `Extensions/Agent/src/Api/AgentJsonContext.cs`. Stamp `Workspace` and `ContinuedFrom` from the create request. Add API tests covering each refusal code and owner scoping with `take=1`.

  Verified: commit `efece695f`; `AgentRecoveryApiTests` (owner scoping with `take=1` across actor, tenant and workspace, the `before` cursor, every refusal code, idempotent resolution and a later superseding receipt, withdraw versus start in both orders behind a store-write barrier, user stop versus dropped stream); the user-stop, registered-turn and resolution-release tests are each red without their change. The complete Agent suite passes 855/855 in three runs (818 before this phase).
- [x] T019 [US1] Add the list, proposals, withdraw and resolution calls, plus `workspace` and `continuedFrom` on create, to `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/agent/agentClient.ts` and `agentTypes.ts`, with Vitest coverage.

  Verified: commit `f8131f90c`; `agentRecoveryClient.test.ts` covers the routes, cursor and workspace, message reasons and the owner's statement, every new refusal code on HTTP and wrapped refusals, the shared 404/403 as one not-found, and the `owned_elsewhere` `Retry-After` hint through the public SDK's additive `StudioHttpError.retryAfterSeconds` (recorded in the contract's SemVer table).
- [x] T020 [US1] In `weaver/useWeaverSession.ts` and `weaver/WeaverSurface.tsx`:
  - Remember `lastSessionId` under the user-and-backend scope key, inside try/catch.
  - Reopen from the server list or the details, and load proposals.
  - Render the recovery outcomes: Cancelled or interrupted, Uncertain, not started, Expired, refused, owner-resolved.
  - Offer explicit Start, withdraw, execute, decide and resolve actions. Never open the stream automatically.

  Add Vitest coverage for reload, dock reopen, the second tab and a foreign session.

  Verified: commit `f40f97c54`; `WeaverRecovery.test.tsx` (reload, dock reopen, every recorded outcome with only its actions, explicit Start and Withdraw, the owner's resolution and a conflicting one, a second tab that neither refuses nor marks the first tab's delivery, a foreign session never listed or opened, retryable `owned_elsewhere`, scope and storage failures); no stream opens on reopen in any case. The hint's scope hashes the backend profile, not the per-connection key, whose nonce changes on every load.
- [x] T021 [US1] Make unsaved-target availability module-owned. The client tool contract in `Studio/src/essentials/Elsa.Studio.Hosting/Client/src/sdk/index.ts` (`executeClient`, around line 1295) and `app/agent/clientInvocationRunner.ts` report target availability. Record the public SDK change in the contract's SemVer table. Add Vitest coverage. An undispatched client operation whose target or adapter is gone is refused with zero effect, and a dispatched one stays Uncertain. Extend the probe fixture module `tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Studio/Client/src/module.tsx`.

  Verified: commit `429303fba`; the SDK's optional `clientTargetAvailability` is asked before dispatch, and the runner tests (red on the previous runner) refuse a gone, disabled, unanswered or cancelled target with zero effect, report a missing adapter, and keep a dispatched failure uncertain once; the recovery tests refuse an approved client change after a reload or a closed editor. The probe's Acme module adds a client-working-state contract with its own availability adapter, typechecked against the SDK source; its backend counterpart and restart journeys belong to T031.
- [x] T022 [US1] Add Playwright journeys under `Studio/tests/browser/`:
  - reload with a pending review;
  - dock reopen;
  - a dropped stream;
  - Start and withdraw of a not-started prompt;
  - owner resolution;
  - a second tab;
  - limit continuation;
  - a foreign user's session list.

  Verified: commit `11335b66e` (limit continuation in `043228b58`); `weaver-recovery.spec.ts` runs every journey over a durable fake backend shared by tabs, through the real Studio agent client, with the server's write and stream log asserted: 10/10, with `agent-review.spec.ts` 7/7.

## Phase 6: US3 — Bounded, continuable long sessions (P2, plan step 3)

**Independent test**: Run at least 40 checkpoints that repeat and vary disclosures, then reach each limit.

- [x] T023 [US3] Record a causal baseline in `Extensions/Agent/tests/Tests/`. Measure stored snapshot volume and owner admissions per pass on the current code, and show that they grow quadratically.

  Verified: commit `cb4642452`; `AgentDisclosureVolumeTests` runs 40 turns (80 checkpoints) alternating a repeated and a new snapshot. On the previous representation, from turn 20 to turn 40, stored copies went 318 to 1038 and the largest authorization pass 306 to 1016, with 1966 repeated admissions within the last turn's passes; the test asserted that quadratic growth before T024 flipped it.
- [x] T024 [US3] Store snapshots by digest. Messages and the paused turn hold ordered digest references, with their fingerprints stored verbatim. Hydration re-verifies the digests and the history-match checks, and returns `unavailable_state` on mismatch. Changes go in `AgentDisclosureHistory.cs` and `AgentTurnServices.cs`. Add the `AdmissionPass` set from research R3, passed along explicitly, never stored on a field, and discarded at each write, provider call or tool execution. The baseline from T023 turns green, and re-admission after each write is kept.

  Verified: commit `0818f09ba`. The same scenario now records 21 snapshots for 21 distinct ones, stored copies 71 to 141 and the largest pass 12 to 22 (turn 20 to 40), with no repeated admission in any pass. Withdrawing owner authority right after a checkpoint write stops the provider call (red, with the existing R9 and R10 cumulative tests, when the step's pass is reused after the write). Repeated snapshots keep message digests, the pause fingerprint, an identical retry and the paused continuation stable across a restart, and the T013 tests pass unchanged. Five kinds of recorded state that fail verification answer 409 `agent.session.unavailable_state` with their reason and no effect (red without the verification). The limits count distinct identities and canonical bytes; their distinct `limit` detail stays with T025. The complete Agent suite passes 870/870 in three runs (load average up to 9.7), including the review fixes in `97aa7da3e` and the resolution audit in `6f837f8ad`.
- [x] T025 [US3] Run after T022, because it shares Studio files with T020 and T022. Give the two limits (64 distinct snapshots, 8 MiB) distinct `limit` details. Add Studio's "session limit reached" outcome and continuation, which creates a new session with `ContinuedFrom` and carries nothing else over. Cover the limit reached mid-turn (story 3.3) and prove that provenance is never erased, the budget never reset and nothing replayed (story 3.5). Add tests at the API, Studio and Playwright levels.

  Verified: commits `9c776a5d1` (backend) and `043228b58` (Studio). `AgentError.Limit` names `snapshots` or `bytes`, and the turn passes refusals through whole so the stream and the API wire carry it; the tests were red before. A limit reached mid-turn after a read keeps the tool result's transcript record and the retained provenance, a later prompt is refused again before the provider, and the read ran once. A continuation links the original and carries nothing. Studio shows "Session limit reached" with the limit and creates the continuation with only `continuedFrom` and the workspace (Vitest and Playwright). The complete Agent suite passes 882/882 twice, including the review fixes in `8b6919c85`.

  Review follow-up (#3441 Studio review): Studio shows the server's limit message, which names the bound, instead of restating "64" and "8 MiB" itself, and marks the notice with `data-limit`. Self-review round 1 (S1) closed the reload gap: a refusal at the limit is recorded on the session (`AgentSession.LimitReached`), session detail carries `limitReached` and the owner list's `recoveryState` is `limit-reached:<limit>`, in both stores, so a reopened session shows the notice and its continuation before any prompt (store, API and Studio tests). Round 2: a reached limit stays recorded for good, because a session's distinct snapshots and recorded bytes only grow; a single disclosure larger than any session can record is refused as `snapshot-too-large` and never recorded.

## Phase 7: Durable EF store (plan step 4)

- [x] T026 Create `Extensions/Agent/src/Persistence/EntityFrameworkCore/` following the Specialists EF project:
  - feature class `FoundationAgentEntityFrameworkCore`, opt-in, depending on the Agent feature and Secrets;
  - `[EfModule("Agent")]`, `AgentDbContext` and the four provider contexts;
  - a codec using `ISecretValueProtector`;
  - the record store with owner-and-fence-guarded conditional updates;
  - the durable audit trio;
  - `PublicAPI` files, `nuplane.json`, a README and `EXTENSION_POINTS.md`.

  Add it to `Elsa.Server.slnx`.

  Verified: commits `06fe59310` and `e7639b577`. Eight `elsa_agent_*` tables (the context attachments get their own `elsa_agent_context` table rather than a session column, so an append never re-encrypts the session). Writes and acquisitions take the session row lock first, under owner and fence for a write; the provider matrix showed a read-then-compare write exhausting its retries under 18 competing writers. The store and the audit trio are chosen through the new `AgentPersistenceSelection` in `Elsa.Agent`, which replaces only the process-local defaults in either registration order and throws on a second selection; `AgentSessionUnavailableException` gained an inner-exception constructor so unreadable state keeps its cause (§2.23.5). The feature and the API project build in Release.
- [x] T027 Generate the per-provider `Initial` migrations with `tools/ef/generate-module-migrations.sh`. Register the contexts in `tools/ef/Elsa.EntityFrameworkCore.Tooling/ModuleDesignTimeFactories.cs`, and update `ModuleContextCatalog`, `EfModuleDescriptorTests`, `EfCoreDependencyGuardTests` and the all-modules migrations provider test.

  Verified: commit `06fe59310` (test-project closures in `e7639b577`). `Initial` per provider under `Migrations/Agent/<Provider>/`, history `__EFMigrationsHistory_ElsaAgent`, no provider identity column. Updated `ModuleContextCatalog`, `EfModuleDescriptorTests` (18 modules), `ExpandOnlyMigrationScanTests`, `OrdinalCollationMigrationTests`, `UsesEfModuleGuardTests` and the `Spec1189AgentEf` closure. EF migration tests 508/508; the all-modules install, ordinal-collation and schema tests 9/9 on actual PostgreSQL, SQL Server and MySQL containers; architecture 610/610.
- [x] T028 Add `Extensions/Agent/tests/Persistence/Tests` (SQLite, PR tier) and `Extensions/Agent/tests/Persistence/ProviderTests` (Testcontainers for PostgreSQL, SQL Server and MySQL). They cover:
  - aggregate round-trips with byte-identical fingerprints;
  - the unique keys;
  - fence and lease;
  - the proposal-to-session lookup;
  - the owner list and audit;
  - unreadable state;
  - the restart boundaries from T010–T014 run against SQLite;
  - protected columns hold ciphertext, and no credential or provider object is stored (FR-003);
  - codec, `JsonException` and `DbUpdateException` failures are wrapped into the FR-018 unavailable outcome (§2.23.5);
  - a registration test for `FoundationAgentEntityFrameworkCore`: provider selection, the audit-trio replacement, and the explicit selection that throws on a second selection.

  Register both projects in `tools/ci/test-tiers.json` (tier, container suite, EF shard).

  Verified: commit `e7639b577`. `Elsa.Agent.Persistence.Tests` 73/73, including the 38 T009–T014 restart, deduplication, execution and ownership cases compiled unchanged and run over the EF store; removing the write's owner and fence guard turns 8 of them red, removing it together with the proposal compare-and-set 10, and dropping the codec's row binding 1. `Elsa.Agent.Persistence.ProviderTests` 3/3 with `ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX=1` on actual PostgreSQL 16, SQL Server 2022 and MySQL 8.4 containers. Registered as `pr-3`, integration/testcontainers and container suite `weaver-records` on `ef-2`; `ef-suites-self-test` and `test-tiers-check` pass. The Agent suite still passes 882/882.
- [x] T029 Update documentation: what is and isn't recoverable (FR-001), in `Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md` (record store, async turn state, audit trio, participation with durable backing, recovery outcomes) and the root `EXTENSION_POINTS.md`. Regenerate the maps and solution filters, and verify Workbench profile membership in `tools/solution-filters/profiles.json`.

  Verified: the `Durable recovery` section of `Elsa.Agent/EXTENSION_POINTS.md`, the record-store and audit rows, and the root index. The maps check reports the committed solution filters current, so none changed. The Workbench profile is the Workbench project's closure, and the Workbench does not reference the EF store yet, so the store is not a member; T032 adds that reference when it configures the Workbench for the EF store.

## Phase 8: Qualification and freeze (plan step 7)

- [x] T030 Run all affected suites as whole projects:
  - Agent, Persistence, and ProviderTests with `ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX=1`;
  - the probe, Studio Vitest, typecheck and lint;
  - the architecture guard and maps.

  Record the counts and any existing skips.

  Verified on merged head `1b28472c3`, then on `23dc6bd4a` for the CS0436 fix:

  | Suite | Result |
  | --- | --- |
  | Agent | 936/936 |
  | Persistence (SQLite) | 95/95 |
  | Agent ProviderTests, matrix required, real containers | 3/3 |
  | EF Migrations | 508/508 |
  | Migrations ProviderTests | 32/32 |
  | Workbench | 64/64 |
  | Architecture | 610/610 |
  | Maps | current |
  | Probe unit tests | OK; `node --check` passes |
  | Studio Vitest | 1054/1054 |
  | Typecheck | exit 0 |
  | Lint | 0 errors |

  - The Workbench run excludes `WorkbenchCompensationSampleTests`, which needs the separately built compensation sample and is unrelated.
  - The Agent API Release build shows only main's existing warnings (IDE0019 ×2, SYSLIB1034 ×4). The CS0436 warnings introduced by an internals grant were removed in `23dc6bd4a`.
- [x] T031 Extend `tools/studio/Weaver.Conformance.Probe/` so its consumers configure the packaged EF store on SQLite and restart their own host processes between phases. Define the restart inventory in `contracts/recovery.md`: each story 2 boundary on each actual adapter, plus the owner-resolution and second-tab journeys. Add oracle and negative controls.

  Verified (wiring and unit tests): commit `20a598f7e`. The backend consumer references the packaged store and Secrets; `consumers.enable_durable_agent_store` selects `FoundationAgentEntityFrameworkCore` on SQLite over a per-run database while no host generation runs, and `runtime.RestartableServer` restarts the owned backend (graceful, abrupt or after the real deadline) on one origin with a receipt, log and empty caches per generation. Section 4 of the contract defines the ten restart cases on each actual adapter (second-tab as a second signed-in client of the owner beside a foreign actor) and the in-memory negative control; the complete witness is now 89 (38 + 4 + 26 + 20 + 1), and `validate_complete_witness` and the export refuse a report without the passed restart section. The probe suite passes 268/268 (21 new tests: every verifier's oracle on both adapters, and refusals of a skipped settlement write, a replayed effect, a second continuation, a recovery outside its deadline window, a missing restart, a wrong stop kind, a populated cache and a replayed uncertain operation), and `node --check browser.mjs` passes. The inventory has not run natively; its first native observation is the complete witness in T033.
- [x] T032 Run the normal composed Studio proof with the Workbench configured for the EF store, alone at low load: the complete browser suite plus the recovery journeys.

  Preparation: commit `d1fbe589d` references the store from the Workbench, opt-in (the stock shells do not list it). Listing `FoundationAgentEntityFrameworkCore` with `Provider` and `ConnectionString` in the shell enables it; `WorkbenchAgentDurableStoreTests` (2/2) shows the stock shell keeps it disabled and that SQLite composes, migrates and serves the owner list. The Workbench solution filter now contains the store.

  Verified on `23dc6bd4a`, run alone, from a clean, exact checkout on an owned port:
  - The complete Studio browser suite discovered and reported 927 tests: 921 passed, 0 failed, 0 flaky, and 6 opt-in screenshot cases skipped. That includes `weaver-recovery.spec.ts`.
  - The port was closed afterwards, and the source was still exact and clean.
  - `results.json` SHA-256 is `8c6d5e4651401a1a030364e431e93546efac1b49d341bdaf31cddecb3915d8fb`.

  The browser suite runs Studio against the repository's mocked browser backends. The durable composition itself is proved by `WorkbenchAgentDurableStoreTests` and by the package witness's driver-restarted host on the packaged EF store.
- [x] T033 Run the complete independent package witness from committed source: the original 38 cases, the 4 current-authority cases, the 26 autonomy and lifecycle cases, and the new restart inventory. Run it alone at low load.

  Verified: attempt 3 on `d014b2dc8` passed all 89 cases with `independentPackageWitness` true; report SHA-256 `f79661dda0a13c23edf8a5b2b12b7f4c5ea35a5ff223478332125041f21f6d3f`. Attempts 1 and 2 failed and are recorded on #3441; their fixes are `4899dcb9a`, `b1b3c5a61` and `d014b2dc8`. The witness was then run again on the final head `23dc6bd4a`:
  - Attempt 1 (`4.0.0-weaver3441.20261102.2`) failed in the original `browser-consumed-intent-remount` journey. A 30 s `waitForURL` timed out after a feature apply; the source and machine-load evidence are on #3441.
  - Attempt 2 (`4.0.0-weaver3441.20261102.3`, run alone, about 16 min) passed all 89 cases, with `independentPackageWitness` true:
    - 38 original cases: 16 passed, 22 recognized expected-negatives;
    - 4/4 current-authority cases;
    - 26/26 autonomy cases;
    - 20/20 restart cases, plus the in-memory control;
    - 152/152 stages.
  - Report SHA-256 `93d16706045acf7f75404529247629475c58dfeef6a5289eea0a8b6130066caa`.
- [x] T034 Freeze `contracts/recovery.md` as Preview after T032 and T033 pass. Write its SemVer section from the actual delivered surface, set the spec to Implemented, write the acceptance-to-evidence table, and update `quickstart.md`.

  Verified: the contract is frozen for #3441 as Preview at `23dc6bd4a`, and its §5 SemVer section matches the delivered surface (round 2–4 reviews). The spec is Implemented, with an acceptance-evidence table. `quickstart.md` describes the 89-case witness that ran.
- [ ] T035 Open or update the draft PR. Attach the gate and causal evidence, and keep the `pr:` receipt and the Program, M2 and Project checkpoints current without claiming Done.
- [ ] T036 Converge independent non-author review over the whole PR diff and the issue and PR threads. Lenses: correctness, standards, and acceptance against this spec. Keep the review rounds on the PR. Mark the PR ready and request Code Review.
- [ ] T037 Refresh the reviewed head, main, org rules, claims and required checks. Merge only through Code Review at the exact reviewed head, and keep #3441 In review until resulting main is verified.
- [ ] T038 Verify the actual resulting main using `quickstart.md`, required CI and fresh composed and native package proof. Keep deterministic, provider-protocol and live-provider evidence distinct. Requalify if main advances.
- [ ] T039 Reconcile the five #3441 criteria, the three follow-through items, the tasks and the evidence on the issue and PR. Only then mark #3441 completed and Project Done, release the claim, and update Program/M2/dependencies and `docs/program-goals/weaver-across-studio.md`. Continue with the next Program leaf.

## Dependencies and execution order

T001–T002 → Phase 2 (T003–T008) → US2 (T009–T015) → US4 (T016–T017) → US1 (T018–T022) → US3 (T023–T025) → EF (T026–T029) → T030–T039.

- US4 depends on the owner seams and the settlement from US2.
- US1's API (T018) depends on hydrate and settlement. Its Studio work (T019–T022) depends on T018.
- T023–T024 depend only on Phase 2 and can overlap US1's Studio work (T019–T022), because the files are separate. T025 runs after T022.
- The EF phase depends on the final aggregate shape from US2 and US3.
- Qualification depends on everything before it.

**Parallel opportunities:**
- [P] T019 and T021 (client and SDK) can run beside T023 and T024 (backend history).
- T027 and T028 can run in parallel once T026 settles the model.

Shared owner files (`DefaultAgentServices.cs`, `AgentTurnServices.cs`, `AgentProposalServices.cs`) are changed sequentially. At most two subagents are active alongside the root, with one implementation lane.

## Delivery strategy and live completion

The first useful internal increment is Phase 2 plus US2 on the in-memory store, proved by the restart harness. It is not a shipped MVP and not permission to stop: every story, the EF store and all Program milestones remain required. Commit logical groups to the draft PR and keep expected-failing baselines separate.

T035–T039 include GitHub, merge and resulting-main events that happen after the final source commit. Their completion is recorded on the owning issue, PR and Project. Do not pre-check them in source, and do not open a separate status-only PR. A spec marked Implemented describes the delivered source, not a verified Done for the issue or the Program.
