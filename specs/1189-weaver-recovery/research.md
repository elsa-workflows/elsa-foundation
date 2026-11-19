# Research: Weaver session, approval and outcome recovery

Spec: [spec.md](spec.md). Two independent read-only research passes ran on 2026-11-02 against main
`8da103a38`. Paths are relative to `Extensions/Agent/src/` unless shown otherwise. The decisive code
claims were spot-checked before acceptance:
- `MarkProposalDispatched` returns `true` for an unloaded row.
- Provider history includes Cancelled and Failed messages.
- The Copilot provider's resume fallback is silent.

## R1. Durable backing under the default session owner

**Decision:** Keep `InMemoryAgentSessionService` as the single owner type. Session-policy participation compares the concrete owner by reference: `AgentSessionPolicyOwnership.cs:50,55-57`, `DefaultAgentServices.cs:28`, `AgentProposalServices.cs:33`, `AgentToolInvoker.cs:17`, `AgentTurnServices.cs:85`. The owner gains an optional replacement contract, `IAgentSessionRecordStore`, and writes through it.

The store covers one session aggregate:
- the session itself: policy, revision and fence;
- messages;
- disclosure snapshots;
- the paused turn;
- proposals, with their execution, result and acknowledgement digest;
- the duplicate-detection unique keys: call, invocation, turn mutation and call input.

Writes are write-through, in this order:
1. Take an async per-session write gate.
2. Await the store write.
3. Apply the change in memory under the existing short row monitor.

Lock order is call key → proposal gate → session write gate → row monitor. An async gate already spans I/O at `AgentProposalServices.cs:219`; row monitors stay synchronous (`DefaultAgentServices.cs:107-139`).

Writes that MUST be durable before the next observable step:
- **Executing:** before the executor call (`AgentProposalServices.cs:214-219`).
- **AwaitingClient:** before the client envelope is returned.
- **Outcome:** receipt, result and status in one aggregate write (`StoreOutcome`, `:338`).
- **Paused turn:** before foreground is released (`AgentTurnServices.cs:599-604`).
- **Policy revision:** written by an async `ChangeAutonomy` (`DefaultAgentServices.cs:316-351`).

Two changes in the existing code:
- The three steps `TurnMutations.GetOrAdd` → `AddAsync` → `CallInputs.TryAdd` (`AgentToolInvoker.cs:175-178`) become one insert.
- Every internal owner method hydrates its row first. Today `MarkProposalDispatched` returns `true` when no row is loaded (`DefaultAgentServices.cs:255`).

**Ordering rules (planning review):**
- **Claims and policy changes serialize on the session write gate.**
  - Acquiring a Prompt, Foreground, Preparation, Proposal or Pause claim takes the gate asynchronously, the same way `ChangeAutonomy` does.
  - This keeps today's atomic order of busy check, revision write and claim admission (`DefaultAgentServices.cs:323-350`) across the awaited write.
  - A claim acquired while a policy write is in flight waits, then reads the new revision.
- **Hydration is single-flight under the session write gate.** It never takes proposal gates.
  - Settlement during hydration changes proposal status by compare-and-set on status and version.
  - The declared order (proposal gate → session gate) therefore cannot invert.
  - Concurrent first hydrations do not record Expired twice.
- **The record store resolves a proposal to its owning session.** The approve, deny and execute routes address proposals by id (`Api/AgentApi.cs:53-58`), and `FindAsync`/`AccessAsync` read by id before any session lookup (`AgentProposalServices.cs:97,329`). After a restart or eviction, a proposal lookup hydrates its session first.
- **Authorization uses the plain owner columns before any payload is decoded or ownership is taken.**
  - A foreign actor receives the shared 404 or 403 and triggers no hydrate, takeover or settlement.
  - `agent.session.unavailable_state` and ownership outcomes are reported only to the owner.
- **Message status changes become compare-and-set on the expected status.**
  - Start moves Pending to Streaming.
  - Withdraw moves Pending to Cancelled and is refused while a turn is registered, so a withdraw cannot lose to a concurrent Start (`DefaultAgentServices.cs:164-182`, `AgentTurnServices.cs:199`).

**Rationale:**
- Replacing the owner would make policy Unsupported, which violates FR-004.
- One store keeps fence checks coherent and follows the "replace together" rule.
- Claims are reference-identity tokens (`AgentSessionPolicyOwnership.cs:17-41`) and are rebuilt from durable facts (R4), so no cross-store transaction is needed. Each aggregate write is atomic, and every write checks the fence (R5).

**Alternatives considered:**
- *Write-behind:* rejected; it violates FR-006 and FR-007.
- *Three separate ports:* rejected; they split fence coherence.
- *Persisting claim tokens:* rejected; claim tokens have no stable identity.
- *Replacing the owner:* rejected; it violates FR-004.

## R2. `IAgentTurnStateStore` becomes asynchronous

**Decision:** The contract becomes `SaveAsync`, `FindAsync` and `RemoveAsync`. This is a Major change, accepted before GA under ADR 0080 D4, on the same basis as the prior leaf (`specs/1186-weaver-effects/contracts/session-policy.md` §4.2).
- The default implementation keeps the paused turn on the owner's session row. Saving the pause therefore also records the Pause claim, which closes the gap between `SavePause` (`AgentTurnServices.cs:1116`) and `EnsurePause` (`:599`).
- A replacement turn-state store keeps autonomy support through explicit `EnsurePause`, but cannot recover paused turns: with the in-memory record store the composition reports `unsupported`, and with a durable store selected it fails shell activation (`AgentDurableRecoveryValidator`, self-review round 2).

Affected call sites:
- `AgentTurnServices.cs:109`, `:172`, `:578`, `:872`, `:1116-1117`; `TrackPause` (`:151`) and its callers `:349` and `:521`.
- `Api/AgentApi.cs:305`.
- Existing tests: `AgentCumulativeDisclosureTests.cs:1068`, `AgentTurnOrchestratorTests.cs:70,110,153,557`, `AgentCompositionTests.cs:42,337`, `AgentSessionPolicyTests.cs:448`, `AgentDisclosureBoundaryTests.cs:230`, `AgentStreamBoundaryTests.cs:394`.

**Alternatives considered:**
- *A sync-over-async wrapper:* rejected; it would block inside the turn loop.
- *Absorbing the store without a public contract:* rejected; it would drop a cataloged extension point (`EXTENSION_POINTS.md:34`).

## R3. Snapshot identity, fingerprint stability and per-pass admission

**How it works today:**
- A snapshot's identity is `Digest(Freeze(snapshot))`, deduplicated only within one history instance (`AgentDisclosureHistory.cs:26-34`).
- Each checkpoint stores the full cumulative list on its message (`AgentTurnServices.cs:740-745`).
- `MessageFingerprint` and `PauseFingerprint` digest the whole list or turn (`AgentDisclosureHistory.cs:69-74`).
- `Digest` is SHA-256 over canonical JSON with sorted keys and raw scalars (`AgentInvocationIntegrity.cs:24-30,52-76`).

**Decision:**
- A snapshot table, keyed by `(SessionId, SnapshotDigest)`, holds the protected canonical JSON text.
- Messages and the paused turn store an ordered digest list, plus their existing fingerprints verbatim.
- Rehydration rebuilds the identical list, then re-verifies every snapshot digest and runs `AssistantHistoryMatches` and `PauseHistoryMatches` (`AgentTurnServices.cs:1002-1010`). Any mismatch yields the FR-018 unavailable outcome.
- In-memory copies are interned by digest.
- The 64-snapshot and 8 MiB limits become count and size checks over the session's distinct snapshots.

**Per-pass admission (FR-015):**
- Today, each pass admits every message's cumulative history (`:951-955`) and then `disclosures.Snapshots` again. Pass A is at `:209-213`, the write at `:226`, and pass B, after the write, at `:230-237`.
- A pass-local `AdmissionPass` set, keyed by `(snapshotDigest, continuation, historical)`, is passed through `AuthorizeHistoryAsync`, `AuthorizeDisclosuresAsync` and `AdmitAsync`. The flags in the key are the ones `AdmitAsync` takes (`:1014-1016`).
- The set is created per callback block, never stored on a field, and discarded at each checkpoint or other write. Pass B is kept.
- A pass ends at the next **non-admission** await: store, provider or tool. `AdmitAsync` itself awaits owners, and that await belongs to the pass. The contract states this precisely.

**Rationale:** The ledger keeps every disclosure boundary in order. Storage grows with the number of distinct snapshots. Fingerprints stay byte-identical because the canonical JSON is stored as text.

**Alternatives considered:**
- *Storing deltas between checkpoints:* rejected; this needs reconstruction logic and still requires re-verification.
- *Storing reconstructed objects:* rejected; numbers can drift, which breaks the fingerprints.

## R4. Settlement and claim rebuild after restart

**Decision:** Settlement is lazy and runs when a row is hydrated. An optional bounded startup sweep records only zero-effect outcomes.

Existing code already settles without an actor:
- `SettlePendingClientAsync(proposal, null, …)`, called from `FindResultAsync` (`AgentProposalServices.cs:302-318`).
- `Transition`, `Settled` and `PastPreDispatchDeadline` (`:372,383,391`).

Two changes:
- `ExpireBeforeDispatchAsync` takes `string?` for its actor.
- Recovery outcomes carry `settledBy=recovery` metadata. Audit `ActorId` is already nullable (`DefaultAgentServices.cs:145`).

Claims are rebuilt under the row monitor:

| Durable fact | Rebuilt claim or outcome |
| --- | --- |
| A Pending user message | Prompt claim at `PromptKey` (mirrors `:136-137`) |
| A paused-turn record | Pause claim at `PauseKey` (`:278-288`) |
| An unsettled prepared proposal | Proposal claim with `Deadline = ExpiresAt`. It is marked dispatched for Executing, AwaitingClient and client-scope Uncertain. Executing becomes Uncertain through `StoreOutcome` |
| A Streaming source message with no pause (`AgentTurnServices.cs:199`) | Interrupted: Cancelled with a reason distinct from a user stop. No Foreground claim is rebuilt |
| Preparation claims | Not rebuilt: preparation never survives a process |

The existing keyed releases (`:177`, `:269-275`) work unchanged.

Owner resolution of an uncertain client operation releases only that proposal's claim. A Pause claim always counts as busy (`DefaultAgentServices.cs:403-404`) and is removed only when the continuation runs (`AgentTurnServices.cs:578-582`). So a resolved operation inside a paused turn keeps the session busy until the owner starts the continuation. The continuation then reports the owner's statement to the provider as an uncertain outcome, not as a receipt. `ExecuteAsync` gains an explicit check that the proposal's `PolicyRevision` equals the current revision (story 2.3).

**Alternatives considered:**
- *Sweep only:* rejected; reads would still have to handle the gap until the sweep runs.
- *Settling on every read without hydration:* rejected; it duplicates the owner's logic.

## R5. Work ownership: owner token, fence and lease

**Precedents:**
- Rules: `WorkOwner`, a monotonic `WorkFence`, database time and a lease that is checked again after the write (`Extensions/Rules/src/Persistence/EntityFrameworkCore/EfRuleRunStore.Leases.cs:67-72,110-124,139-141`).
- Specialists: a conditional update on version, status and fence (`Specialists/Persistence/EntityFrameworkCore/Stores/EfSpecialistTurnStore.cs:276-282`).

**Decision:** The session record carries `OwnerToken` (one per process), `Fence` (incremented on every takeover), `LeaseExpiresAt` (in database time) and a concurrency token.
- Ownership is acquired at hydrate, when the owner is empty or its lease has expired, or when this process already owns it.
- Every write is guarded on the current owner and fence only. The same conditional update extends the lease, or re-acquires an expired lease that no other process took over. An idle session that one host revisits after the lease has lapsed therefore never fails. The Rules precedent, by contrast, requires an unexpired lease (`EfRuleRunStore.Leases.cs:136`).
- While a Foreground claim is held or a dispatched execution's executor is still running, the lease is also renewed in the background. An operation that only waits on a client or on its owner keeps the session busy without renewing the lease. The default is 30 seconds, configurable.
- A process that finds another process's live lease returns a retryable 409 `agent.session.owned_elsewhere` with a retry hint. It does not take over.
- Acknowledgements and owner resolutions are idempotent on retry. A retry after a takeover returns the retained outcome.

When ownership is lost:
- The row is evicted. Existing tokens become no-ops; `Release` already tolerates a missing row (`DefaultAgentServices.cs:300`).
- The running turn is cancelled through `IAgentTurnRegistry`.
- The caller receives an explicit `agent.session.ownership_lost`.
- An effect that was in flight is seen as Uncertain by the new owner.

**Alternatives considered:**
- *Distributed locks outside the store:* rejected; they add infrastructure the product doesn't have.
- *Optimistic concurrency only:* rejected; it cannot stop a stale process from continuing a turn.

## R6. Duplicate registration of new contracts

**Decision:** Extend `AgentRegistrationValidator.Validate` (`Elsa.Agent/Services/AgentRegistrationValidator.cs:12-17`) so that at most one `IAgentSessionRecordStore` may be registered. The validator runs at shell init and in `Create<T>` (`:26-30`). The pattern follows `Essentials/src/Workflows/Design/Persistence/Core/Services/ValidateDesignPersistenceReplacementContractsStartupTask.cs:14-25`.

The EF feature registers its store through an explicit selection that throws when a second selection is made, modelled on `Specialists/Core/DependencyInjection/SpecialistTurnStoreRegistration.cs:10-42`. The Agent-wide gap for existing contracts stays with #3633.

## R10. Durable owner-scoped audit

**Decision:** The EF feature replaces the audit sink, reader and ownership together. They are three `TryAdd` contracts implemented by one class (`Elsa.Agent/Extensions/AgentServiceCollectionExtensions.cs:44-47`). The registration validator detects a duplicate of the trio.
- Ownership association is persisted, so `ListOwnedAsync` still scopes recovered sessions after a restart. Today the in-memory `_owners` map is empty after a restart, and `AssociateAsync` runs only in `CreateAsync`.
- `settledBy` is an audit metadata key (`recovery` or `user`), so `AgentAuditEvent` keeps its shape.

**Alternatives considered:** Keeping audit in memory beside a durable session store was rejected. FR-013 audit queries would return nothing for recovered sessions.

## R7. EF Core persistence feature (ADR 0073)

**Decision:** One project, `Extensions/Agent/src/Persistence/EntityFrameworkCore/Elsa.Agent.Persistence.EntityFrameworkCore.csproj`, modelled on Specialists (`SpecialistsEntityFrameworkCoreFeature.cs:12-26`, `SpecialistsEntityFrameworkCoreRegistration.cs:15-36`).

**Feature and registration:**
- Shell feature `FoundationAgentEntityFrameworkCore`, opt-in, depending on the Agent feature and `Secrets`.
- `[EfModule("Agent")]`, with the stable history table name `ElsaAgent`.
- Settings: Provider, ConnectionString (Secret), ConnectionName, Schema and Pooling.
- A consumer-owned context remains a secondary path.

**Schema and migrations:**
- Contexts: `AgentDbContext`, plus `Agent{Sqlite,SqlServer,PostgreSql,MySql}DbContext`.
- Migrations live under `Migrations/Agent/<Provider>/` and are generated with `tools/ef/generate-module-migrations.sh`.
- Each context is registered in `tools/ef/Elsa.EntityFrameworkCore.Tooling/ModuleDesignTimeFactories.cs`.
- Tables are `elsa_agent_*`, with per-provider payload column types (`SpecialistsModelBuilderExtensions.cs:17-25`).

**Project files:** following the Specialists project, the project also carries `PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt`, `nuplane.json` (`ef-provider` capability), a README, an `EXTENSION_POINTS.md` and an `EfModule` `AssemblyInfo`.

**Payload protection:** `ISecretValueProtector` protects payloads in a codec owned by the feature, as Specialists does (`SpecialistTurnJournal.cs:13,30`).

**Tests:**
- `Extensions/Agent/tests/Persistence/Tests`: SQLite, PR tier.
- `Extensions/Agent/tests/Persistence/ProviderTests`: Testcontainers for PostgreSQL 16, SQL Server 2022 and MySQL 8.4 (`ProviderDatabase.cs:17-71`).
- Registered in `tools/ci/test-tiers.json` as an integration and container suite on an EF shard (`:614-619`, `:896-910`, `:1073-1076`); `EfSuiteSelector` requires the entry.

**Other touchpoints to update:**
- `Elsa.Server.slnx`.
- `ModuleContextCatalog.cs` and `EfModuleDescriptorTests.cs`.
- The reviewed EF closure in `EfCoreDependencyGuardTests.cs`.
- The all-modules migrations provider test.
- The root `EXTENSION_POINTS.md`.
- The solution-filter profiles. Workbench profile membership must be verified there.

**Alternatives considered:**
- *Per-provider projects:* rejected; no repository precedent.
- *A store inside `Elsa.Agent`:* rejected; ADR 0073 D2 keeps contracts provider-neutral.

## R8. Session list, explicit actions and Studio reopen

**Current routes** (`Api/AgentApi.cs:36-61`): bootstrap; create, get and autonomy for sessions; messages; cancel turn; stream; feedback; approve, deny and execute proposals; client-invocation acknowledgements; audit. Authorization uses `AgentSessionAuthorization` (shared 404 and 403) and `AgentEndpointActor` (fail closed).

**Decision:**

| Route | Purpose |
| --- | --- |
| `GET /_elsa/agent/sessions?workspace=&take=&before=` | Owner-scoped summaries with a cursor. Backed by `IAgentSessionService.ListOwnedAsync(AgentSessionOwnerQuery)`, which defaults to Unsupported (409 `agent.session.query_unsupported`) and filters by actor and tenant before the limit. The workspace is stamped at create time and only narrows the result |
| `GET /_elsa/agent/sessions/{id}/proposals` | The session's proposals and their statuses. The details response has no proposals today (`Api/Models/AgentApiModels.cs:99-109`) |
| `POST /_elsa/agent/sessions/{id}/messages/{messageId}/withdraw` | Withdraw a not-started prompt: Cancelled, with a withdrawn reason |
| `POST /_elsa/agent/sessions/{id}/client-invocations/{invocationId}/resolution` | The owner's statement about an uncertain client operation. It is not a receipt and does not go through `AcknowledgeAsync` |

Two existing routes cover the remaining actions:
- **Start a not-started prompt:** `GET …/stream` already runs the latest Pending prompt or the paused continuation (`AgentTurnServices.cs:108,914-915`).
- **Execute:** `POST proposals/{id}/execute` (`AgentApi.cs:535-558`) gains the story 2.3 rechecks.

**Studio:**
- The client remembers `lastSessionId` in localStorage under the existing user-and-backend scope key (`app/notificationStorage.ts:26-31`), using the try/catch storage pattern (`sdk/index.ts:290-311`). It is only a hint; the server list is authoritative.
- Reopen calls the list or the session details, then the proposals, and shows the recovery outcomes. It never opens the stream automatically; Start does that explicitly.
- The SDK `clientId` is regenerated on every registry (`sdk/index.ts:1939`), so recovery never keys on it.

**Alternatives considered:** A server-side Studio preference namespace was rejected for this leaf. It adds a dependency, and localStorage is enough because it is only a hint.

## R9. Provider behaviour after an interrupted turn (FR-021)

**Copilot, the delegated loop:** The SDK owns the conversation and receives only the latest prompt (`GitHubCopilot/Services/GitHubCopilotAgentProvider.cs:138,430-439`). When resume fails, the provider creates a new SDK session with the same id and logs only at Debug (`:74-86`, `:150-169`).

**Decision for Copilot:**
- Keep ADR 0022's session identity.
- Make the fallback an explicit stream event and turn outcome: `agent.provider.github_copilot.conversation_reset`.
- Document that content the SDK already holds from a turn Elsa recorded as interrupted may still be in Copilot's own history. It is never counted as an accepted outcome.
- A provider-session generation counter would need an ADR 0022 amendment; it is not part of this leaf.

**Anthropic, the host-driven loop:** It is stateless and rebuilds the model input from Elsa's history every step (`Anthropic…/AnthropicAgentProvider.cs:159-200`). That history includes every user and assistant message whatever its status (`AgentTurnServices.cs:925-929`).

**Decision for Anthropic:**
- Exclude Cancelled, interrupted and withdrawn user prompts from the provider history, together with the Failed partial assistant text that belongs to them.
- Keep them in the Elsa transcript, marked as not accepted.
- Tool receipt messages that an interrupted turn already recorded stay in provider history. They are real accepted outcomes.

**Rationale:** FR-021 requires every reset to be explicit and forbids counting interrupted content as accepted.

**Alternatives considered:** A Copilot generation suffix was rejected for now; it needs an ADR amendment.
