# Contract: Weaver recovery (#3441, preview)

> **Amended by spec 1203 — 4 November 2026.** Agent 1.1 readable protected records, performed terminal results and current privilege/principal checks on explicit recovered execution are amended by [spec 1203](../../1203-weaver-module-support/spec.md), approved in [PR #3757](https://github.com/elsa-workflows/elsa-foundation/pull/3757). The proposed [leaf 0 plan and contracts](../../1203-weaver-module-support/plan.md) require renewed old-row/restart and no-replay evidence; the historical qualification and owner-statement distinctions below remain intact.

**Status**: Frozen for #3441 as Preview API. It was frozen after these proofs passed on the final source
`23dc6bd4a`:
- the normal composed Studio proof;
- the complete independent package witness, with durable recovery configured on the packaged EF store.

Pre-GA it may still change under ADR 0080.

## 1. Extension surface (Agent Core)

| Member | Kind | Notes |
| --- | --- | --- |
| `IAgentSessionRecordStore` | Replacement contract, at most one, Preview | Aggregate reads and writes for one session: hydrate, write a transition with an owner and fence check that also extends the lease, take ownership, release the lease, insert unique keys, resolve a proposal to its session, list owned sessions. The default in-memory implementation matches today's behaviour. A second registration fails `AgentRegistrationValidator` |
| `IAgentTurnStateStore` | Existing contract, **changed** | Becomes `SaveAsync`, `FindAsync` and `RemoveAsync`. A Major change, accepted pre-GA under ADR 0080 D4. A replacement keeps autonomy support but cannot recover paused turns: with the in-memory default that composition reports `unsupported`, and with a durable store selected it fails shell activation (`AgentDurableRecoveryValidator`) |
| `IAgentSessionService.ListOwnedAsync(AgentSessionOwnerQuery)` | Additive, Preview | Defaults to Unsupported (`AgentSessionListResult`). Filters by actor and tenant, then workspace, then orders and limits |
| `IAgentSessionService.FindOwnerAsync`, `IAgentProposalService.FindSessionIdAsync`, `IAgentSessionRecordStore.FindOwnerAsync` (`AgentSessionOwner`) | Additive, Preview, with defaults | The plain owner columns and the proposal-to-session lookup that authorization reads before anything hydrates, takes ownership or settles |
| `IAgentSessionService.WithdrawMessageAsync`, `IAgentProposalService.ListAsync`, `IAgentProposalService.ResolveClientInvocationAsync` | Additive, Preview | Default to a refusal (`agent.message.withdraw_unsupported`, `agent.proposal.query_unsupported`, `agent.invocation.resolution_unsupported`) |
| `AgentActionProposal.OwnerResolution` (`AgentOwnerResolution`, `AgentOwnerResolutionClaim`) | Additive, Preview | The owner's statement; omitted from JSON when absent |
| `AgentSessionRecord`, `AgentSessionRecordWrite`, `AgentSessionRecordOutcome`, `AgentSessionRecordResult` (`Fence`, `LeaseExpiresAt`, `ConflictingKeys`), `AgentSessionFence(OwnerToken, Fence)`, `AgentSessionUniqueKey(Kind, Key, Value)`, `AgentSessionKeyKinds` (`call`, `invocation`, `turn-mutation`, `call-input`), `AgentSessionSummaryPage(Items, Next)`, `AgentSessionOwner(ActorId, TenantId)` | Additive models of the record-store port, Preview | The aggregate a store reads and writes, the outcome and ownership a store answers with, the duplicate-detection keys and their kinds, and the owner list's page with its opaque `before` cursor |
| `AgentSessionListQueryRequest` (`workspace`, `take`, `before`), `AgentSessionListResponse(Items, Next)` with `AgentSessionSummaryResponse`, `AgentClientInvocationResolutionRequest` (`claim`) | Additive API wire models, Preview | The `GET /sessions` query and page, and the resolution route's body (section 2) |
| `AgentProposalRecord.Version`, `AgentSessionRecordOutcome.VersionConflict` | Additive, Preview | Every proposal record write names the version it read; the store refuses a mismatch |
| `AgentSessionOwnerQuery`, `AgentSessionSummary` | Additive models | Actor, tenant, workspace, take (at most 100) and a `before` cursor. The summary holds id, title, status, updatedAt, expiresAt, pendingReviewCount and recoveryState. Read from plain columns at store time, before any hydration: `pendingReviewCount` counts only reviews not past their prepared deadline, and an active session past its expiry is listed `expired` |
| `AgentMessage.StatusReason` | Additive | `interrupted`, `withdrawn` or `user-stop` for Cancelled |
| `AgentMessageViewModelResponse.AcceptedPolicy` | Additive, Preview | The public session policy (mode, ceiling, allowed modes, revision) a recorded message was accepted under, on session detail; omitted when unattributed. Studio names a reopened message's mode from it, never from a revision number seen in another session |
| `AgentDisclosureSnapshotRecord`, `AgentMessageRecord`, `AgentPausedTurnRecord`; `AgentSessionRecord.Snapshots`, `AgentSessionRecordWrite.Snapshots` | Preview models of the record-store port; **breaking within Preview** | Each distinct snapshot is recorded once per session as its digest and byte-identical canonical JSON text. Messages and the paused turn hold ordered digests; `DeliveryFingerprint`, the message history fingerprint and the pause fingerprint are stored verbatim and re-verified at hydration. A recorded history fingerprint whose digests are missing is `history_missing`. T024 changed member types of the earlier Preview port: `AgentSessionRecord.Messages` and `AgentSessionRecordWrite.Messages` hold `AgentMessageRecord` instead of `AgentMessage`, and `AgentSessionRecord.PausedTurn` and `AgentSessionRecordWrite.PausedTurn` hold `AgentPausedTurnRecord` instead of `AgentPausedTurn` |
| `AgentSessionOwnershipException` (`agent.session.owned_elsewhere`, `agent.session.ownership_lost`; `SessionId`, `Code`, `RetryAt`, `Find`) | Additive, Preview | Thrown when another process holds a live lease on the session (retryable after `RetryAt`, which the API returns as `Retry-After`) or when this process lost the session's ownership during the call; nothing was applied. `Find` locates it in a wrapped or aggregated cause |
| `IAgentRecoverySupport` (`Describe()`), `AgentRecoveryModes` | Overridable contract, at most one, Preview; normally not replaced | What a restart does to the composition's sessions (`durable`, `processLifetime`, `unsupported`), as bootstrap reports it. The default evaluates the composition; a second registration fails the registration validator |
| `AgentProviderNotice(Code, Label)` | Additive, Preview | An explicit provider outcome a provider emits as the payload of a `Progress` stream event (`{ code, label }`), for example `agent.provider.github_copilot.conversation_reset`. The turn records it; it is never an accepted outcome, a receipt or assistant content |
| `AgentSessionUnavailableException` (`agent.session.unavailable_state`) | Additive, Preview | Thrown at hydration when recorded state fails verification or the store cannot decode it (`unreadable`), with a `Reason`; nothing is settled and the lease taken is released. A store that cannot answer right now (an infrastructure failure, such as a pool timeout, exhausted retries or a read that kept changing) is not unreadable state: the lease is released and the failure keeps its own, retryable outcome |
| `AgentError.Limit` | Additive, Preview | `snapshots` or `bytes` on `agent.context.disclosure_budget_exceeded` when the session reached a documented limit; `snapshot-too-large` when one disclosure is larger than any session can record (8 MiB on its own), and `snapshot-does-not-fit` when one disclosure does not fit in the room left in a session that is not full (the message names the bytes used). Neither is a session limit: nothing is counted or recorded on the session and no continuation is offered. `bytes` is a session limit only when less than 64 KiB is left (`MinimumRoom`), and the session is then treated as full; omitted from JSON otherwise. The turn passes the recorded refusal through whole, so the stream and the API carry it |
| `AgentSession.LimitReached`, `AgentSessionRecoveryStates`, `AgentSessionDetailsResponse.LimitReached` | Additive, Preview | The documented limit (`snapshots` or `bytes`) a turn was refused at, recorded on the session when the refusal is reported (best effort; a later refused prompt records it again). A session's distinct snapshots and recorded bytes only grow, so once reached the limit stays recorded, also after a later turn that adds no snapshot completes. A `snapshot-too-large` refusal is never recorded. Session detail carries it as `limitReached` (omitted when none), and the owner list's `recoveryState` is `limit-reached:<limit>`, so Studio shows the limit and its continuation on reopen |
| `AgentSessionRecord.PendingAudits`, `AgentSessionRecordWrite.PendingAudits` | Additive, Preview (record-store port) | The audit records a recovery settlement owes are written with the settlement and cleared once emitted; a hydration that finds owed records emits them first. Delivery is at least once: a process that stops after emitting and before clearing repeats those records (same event id) at the next hydration. A store that ignores the member keeps the earlier behaviour, where a stop between the settlement write and the emit loses the records |
| `AgentAuditEventKind.ProposalResolved` | Additive, Preview | The owner's resolution of an uncertain client operation, correlated like every proposal audit record, with its `claim` |
| Audit sink, reader and ownership | Existing contracts, replaced together by the EF feature | A duplicate of the trio fails the registration validator. `settledBy` is a metadata key |

**Reporting support.** `GET /_elsa/agent/bootstrap` carries `recovery` (`AgentBootstrapResponse.Recovery`, Additive, Preview), read from `IAgentRecoverySupport.Describe()` (an `AgentRecoveryModes` value): `durable` when a store other than the in-memory default backs a supported composition, `processLifetime` for the in-memory default (sessions survive reloads while the process lives; a restart ends them) and `unsupported` for a composition that cannot recover. Studio states the restart limit when it is not `durable`. At shell activation, a durable store configured into an Unsupported composition fails activation with a diagnostic (`AgentDurableRecoveryValidator`): such a composition would rehydrate sessions while losing paused turns and claims, so it is refused rather than run partly durable. `unsupported` is therefore reported only by a composition without a durable store, or one whose lifecycle services cannot be evaluated (bootstrap still answers).

**Participation.** Session policy remains supported only when session, streaming, invoker and proposal services are the default participants of one owner, as in #3440. Configuring durable recovery gives that owner a record store and does not replace it. A replaced session, streaming, invoker, proposal or `IAgentTurnStateStore` service cannot recover: with the in-memory default that composition reports `unsupported`, and with a durable store selected it fails shell activation (`AgentDurableRecoveryValidator`). A custom `IAgentSessionRecordStore` that cannot fence always fails activation, because any store other than the in-memory default is a durable selection.

## 2. Wire

| Route | Success | Refusals |
| --- | --- | --- |
| `POST /_elsa/agent/sessions` (existing) `{ workspace?, continuedFrom? }` | As today; the workspace and the continuation link are stamped on the session | Adds 400 `agent.session.invalid_request` for a workspace over 256 characters or a `continuedFrom` over 128 (`AgentSessionCreateRequest.MaxWorkspaceLength`, `MaxContinuedFromLength`), checked before the actor and provider checks as a malformed body is; nothing is recorded or associated |
| `GET /_elsa/agent/sessions?workspace=&take=&before=` | 200 `{ items: AgentSessionSummary[], next }`; `take` defaults to 20 and is clamped to 1..100 | 400 for a `take` or `before` this server did not issue; 403 `agent.actor.unresolved`; 409 `agent.session.query_unsupported` |
| `GET /_elsa/agent/sessions/{id}/proposals` | 200 proposals with status, preview and deadline | Shared session-authorization 404 and 403 |
| `POST /_elsa/agent/sessions/{id}/messages/{messageId}/withdraw` | 200 message, Cancelled with reason `withdrawn` | 404 and 403; 404 `agent.message.not_found`; 409 `agent.message.not_pending` |
| `POST /_elsa/agent/sessions/{id}/client-invocations/{invocationId}/resolution` `{ claim: applied \| notApplied \| unknown }` | 200 proposal with `ownerResolution`; the claim is released | 404 and 403; 400 `agent.invocation.resolution_invalid`; 404 `agent.invocation.not_found`; 409 `agent.invocation.not_uncertain`; 409 `agent.invocation.resolution_conflict` for a different statement than the recorded one |
| `POST /_elsa/agent/sessions/{id}/turns/{turnId}/cancel` (existing) | As today; the stopped prompt records `StatusReason=user-stop` (a dropped stream records none) | As today |
| `POST /_elsa/agent/proposals/{id}/execute` (existing) | As today | Adds 409 `agent.proposal.policy_stale` when the stamped revision differs; Expired once the deadline has passed |
| Any route on a session another process holds a live lease on | n/a | 409 `agent.session.owned_elsewhere`, retryable, with a retry hint |
| Any route on a session this process lost during the call | n/a | 409 `agent.session.ownership_lost` |
| Any route on unreadable recorded state | n/a | 409 `agent.session.unavailable_state` with a reason |
| Approve, deny, execute, acknowledgement and resolution, when the proposal changed after the decision read it (for example, another process settled it and this process hydrated the newer record) | n/a | 409 `agent.proposal.conflict`; nothing is recorded, and the caller reads the current state and decides again |

Authorization runs on the plain owner columns first. A foreign or missing caller receives only the
shared 404 or 403 and causes no hydration, takeover or settlement. Ownership and unavailable-state
outcomes are reported only to the owner. Withdraw is a compare-and-set from Pending, and is refused
while a turn is registered. Acknowledgement and resolution are idempotent on retry. Resolution
releases the operation's own claim. A paused turn that awaits it stays busy until the owner continues
it.

**Paging (Preview limitation).** The session list orders by last update, newest first, and the `before` cursor names
the last item's update time and id. A session updated while a client pages moves to the front, so a later page can skip
it, and an earlier page can show it again after the update. Reload the first page to see recent changes; a stable
cursor over an immutable key can replace this before GA.

The stream route is unchanged. Opening it starts the latest Pending prompt or the paused continuation; Studio opens it only on an explicit Start.

## 3. Lifecycle outcomes (in addition to #3440's)

| Situation | Outcome | Effect |
| --- | --- | --- |
| A turn was streaming at restart | Message Cancelled, reason `interrupted` | None caused by recovery |
| Approved, undispatched, deadline not passed, every story 2.3 check holds, explicit execute | Executes once | At most one |
| Approved, undispatched, any check fails | Expired, or a zero-effect Failed refusal | Zero |
| Executing at restart | Uncertain | Possible earlier effect; never replayed |
| The store cannot record an outcome after dispatch | That operation alone is brought in line with its record and, while the record says Executing, settled Uncertain (`settledBy=recovery`); a failed settlement is retried on the operation's next access. The session, its running turn and other operations in flight are untouched | Possible earlier effect; never replayed |
| A late truthful receipt after Uncertain or an owner statement | Reconciled; the receipt wins and the statement is kept | No re-execution |
| The disclosure limit is reached (64 distinct snapshots, or 8 MiB with less than 64 KiB left) | `agent.context.disclosure_budget_exceeded`, with `limit: snapshots \| bytes`; recorded on the session for good | Zero |
| One disclosure is larger than any session can record | `agent.context.disclosure_budget_exceeded`, with `limit: snapshot-too-large`; not recorded on the session, no continuation | Zero |
| One disclosure does not fit in the room left, while at least 64 KiB is left | `agent.context.disclosure_budget_exceeded`, with `limit: snapshot-does-not-fit` and the bytes used; not recorded on the session, no continuation; smaller context still fits | Zero |
| The Copilot resume falls back to a new SDK session | A `Progress` stream event whose payload is the `AgentProviderNotice` `{ code: agent.provider.github_copilot.conversation_reset, label }`, recorded on the turn; never an accepted outcome | n/a |

**Authorization pass.** A pass is one admission sequence. It ends at the next write, provider call or tool execution. Awaits for owner admission and tool discovery belong to the pass, as in spec FR-015. Within a pass, each disclosure source admits each distinct snapshot at most once.

## 4. Mandatory native restart inventory

The complete package witness (`tools/studio/Weaver.Conformance.Probe/qualify.py`) requires each case below
exactly once **on each actual adapter**: Copilot's delegated loop and Anthropic's host-driven loop, each
through its deterministic protocol factory, in addition to the unchanged original 38, the four
current-authority and the 26 autonomy and lifecycle cases. The packaged backend consumer enables
`FoundationAgentEntityFrameworkCore` (with `Secrets`) on SQLite, over a new database file under the run's
owned output root, and the driver restarts that same owned host process between phases. Consumers keep the
no-source-reference and no-frontend-invocation rules; every host generation keeps its own receipt and log,
new empty runtime caches and the same owned origin. Each record names the actual adapter and its observed
outcome; omissions, duplicates, a wrong adapter, a wrong-cause outcome, a missing restart between the
prepared and recovered observations, or absent or changed owner, correlation or restart evidence fail the
qualifier. Every case asserts three things against retained native bytes:

- the recovered outcome, read from session detail, the proposal list and the owner routes after the restart;
- the execution count at the fixture's independent native owner: the Preferences size owner, which must be
  unchanged or equal to exactly the one recorded receipt, or the workflow draft read through its owning API;
- the audit `settledBy` values for the case's proposal or session: `["recovery"]` for a recovery
  settlement with no actor, and `[]` otherwise.

Each graceful case gets a restart of its own, because every one of them applies one effect to the same native
owner (the seed user's status card): its owner reads bracket only its own effect, and its reviewed operation stays
bound to the revision it was prepared against. The abrupt cases share one restart, and so do the after-deadline cases;
neither changes the owner. A graceful stop releases the leases its sessions hold (the default owner's shell
terminator), so after a graceful or after-deadline restart a recovered request answered
`agent.session.owned_elsewhere` fails the case (`restart.lease_not_released`); only an abrupt stop's leases lapse.

| Case key | Restart | Native observation and owner |
| --- | --- | --- |
| `idle` | graceful | A completed automatic size effect, then a restart. The same session, history, acknowledged policy and revision and the stored receipt read back, and a new prompt completes. One execution, no settlement. |
| `pending-review-before-deadline` | graceful | A pending review stays decidable against the same prepared operation before its deadline. Explicit approve and execute apply it once, and the originating turn continues once with the receipt. |
| `pending-review-after-deadline` | after-deadline | The host is stopped before and started after the prepared deadline on the real clock. The review is recorded Expired once with `settledBy=recovery`, execute answers `agent.proposal.expired`, and the owner is unchanged. |
| `approved-before-dispatch-before-deadline` | graceful | Recovery does not execute an approved operation (the owner is unchanged at recovery). An explicit execute applies it once and continues the turn. |
| `approved-before-dispatch-after-deadline` | after-deadline | Expired once with `settledBy=recovery`, refused on execute, and zero effect. |
| `dispatched-without-receipt` | abrupt | The approved server write is dispatched and held at the module's native write rendezvous; the owned process group is killed. The execute request receives no response. After the restart the operation is Uncertain with `settledBy=recovery`, a repeated execute returns the retained uncertainty, and the owner is unchanged: it is never replayed. |
| `receipt-recorded-paused` | graceful | Approve and execute record the receipt while the turn stays paused. After the restart the owner still holds exactly that receipt, the stream continues the turn once and reports the recorded receipt to the provider, and a repeated delivery answers `agent.message.no_pending`. |
| `provider-turn-in-progress` | abrupt | A provider turn is held at the protocol fixture's pre-call gate when the process group is killed. The prompt is Cancelled with `statusReason=interrupted`, no proposal exists, the owner is unchanged and the session continues with a new prompt. |
| `owner-resolution` | after-deadline | A reviewed client working-state change (`workflow_set_designer_position`) is dispatched (202) to an HTTP client that disappears with the host and applies nothing. Past its deadline it is Uncertain with `settledBy=recovery`. The owner's `notApplied` statement is recorded, a repeat is idempotent, a different statement answers `agent.invocation.resolution_conflict`, one `proposalResolved` audit record carries the claim, execute does not dispatch it again, and the workflow draft is unchanged. |
| `second-client` | graceful | After the restart a foreign actor of the same tenant receives only the shared 404 or 403 on the session, its proposals and execute, with no owner change. Two separately signed-in clients of the owner see the same pending review; both approvals return the one recorded decision, concurrent executions return one identical receipt, and the turn continues once. |

**Negative and oracle controls.** Before the store is configured, `restart-without-durable-store` restarts
the same host on the in-memory default: the pending review must be lost (`proposal-lookup`,
`agent.proposal.not_found`) with an unchanged owner. That proves the durable store is what carries every
case above. The probe's unit tests hold each verifier to the driver-owned outcome and refuse a skipped
settlement write, a replayed effect, a second continuation or an admitted repeated delivery, a recovery
observed outside its deadline window, a missing restart between observations, a wrong stop kind, a
populated runtime cache, a dispatch that was answered instead of interrupted, and a replayed uncertain
operation.

`restart_journeys.py` owns the inventory, the phases and the gates. The real host restart is
`runtime.RestartableServer`, and `consumers.enable_durable_agent_store` configures the store in the
consumer's runtime JSON while no generation runs. The fixture's pre-call gate and write rendezvous only
hold real paths. They never replace the engine, the store or the owner. The restart inventory is
deterministic provider-protocol evidence, and live-provider recovery is not claimed.

## 5. SemVer (ADR 0080, pre-GA)

| Change | Class |
| --- | --- |
| `IAgentTurnStateStore` members become async | Major, accepted pre-GA |
| Record-store port (`AgentSessionRecord`, `AgentSessionRecordWrite`, `AgentSessionRecordOutcome`, `AgentSessionRecordResult`, `AgentSessionFence`, `AgentSessionUniqueKey`, `AgentSessionKeyKinds`, `AgentSessionSummaryPage`, `AgentSessionOwner`), list query and summaries, the wire models `AgentSessionListQueryRequest`, `AgentSessionListResponse`, `AgentSessionSummaryResponse` and `AgentClientInvocationResolutionRequest`, `StatusReason`, a recorded message's `acceptedPolicy`, new routes and codes | Minor (additive) |
| A transient store failure during hydration keeps its own outcome instead of `agent.session.unavailable_state` | Behaviour change, recorded |
| Record-store port member types (`Messages` and `PausedTurn` on `AgentSessionRecord` and `AgentSessionRecordWrite` become `AgentMessageRecord` and `AgentPausedTurnRecord`) | Breaking within Preview; the port has not shipped |
| Anthropic provider history excludes non-accepted turns | Behaviour change, recorded |
| The Copilot resume fallback becomes an explicit event | Behaviour change, recorded |
| Studio SDK `StudioAgentToolContractContribution.clientTargetAvailability` (optional; the owning module reports before dispatch whether a client invocation's unsaved target still exists, and anything else refuses with zero effect) | Minor (additive), public SDK |
| Studio client runner reports a missing module adapter as "The owning module's client adapter for this change is not present. Nothing was applied." | Behaviour change (refusal text only), recorded |
| Studio SDK `StudioHttpError.retryAfterSeconds` (the response's `Retry-After` hint; a trailing optional constructor parameter) | Minor (additive), public SDK |
| `Elsa.Agent.Extensions.AgentPersistenceSelection` (`SelectAgentSessionRecordStore`, `SelectAgentAuditStore<TStore>`) | Minor (additive), Preview |
| New package `Elsa.Agent.Persistence.EntityFrameworkCore`: feature `FoundationAgentEntityFrameworkCore`, `AgentEntityFrameworkCoreRegistration` (`AddAgentEntityFrameworkCore`, `AddAgentStores<TContext>`), `AgentEntityFrameworkCoreOptions`, `AgentEfModule`, `AgentDbContext` and its four provider contexts, `AgentModelBuilderExtensions.AddAgentModel`, and the per-provider `Initial` migrations | Minor (new package), Preview |
| `GitHubCopilotAgentProvider.ConversationResetCode` | Minor (additive) |
| `AgentProviderNotice(Code, Label)` | Minor (additive), Preview |
| `IAgentRecoverySupport` (`Describe()`) and `AgentRecoveryModes` (`Durable`, `ProcessLifetime`, `Unsupported`) | Minor (additive), Preview |
| `AgentError.Limit` values `snapshot-too-large` and `snapshot-does-not-fit`; `bytes` only when less than 64 KiB is left | Behaviour change, recorded (a disclosure that did not fit used to mark the session limited for good) |
| `AgentSessionOwnershipException` | Minor (additive), Preview |
| `AgentRegistrationValidator` refuses a second `IAgentAuditSink`, `IAgentAuditReader` or `IAgentAuditOwnership` registration (as it does a second `IAgentSessionRecordStore`) | Behaviour change, recorded; Major-class, accepted pre-GA under ADR 0080 D4 |
| `AgentSession.LimitReached`, `AgentSessionRecoveryStates`, `AgentSessionDetailsResponse.LimitReached` (`limitReached`), and `recoveryState` = `limit-reached:<limit>` | Minor (additive), Preview |
| `AgentBootstrapResponse.Recovery` (`recovery`: `durable`, `processLifetime` or `unsupported`) | Minor (additive), Preview |
| Shell activation fails when a durable record store is selected into a composition that cannot use it for recovery | Behaviour change, recorded (such a composition previously ran partly durable) |
| `AgentSessionRecord.PendingAudits`, `AgentSessionRecordWrite.PendingAudits` (record-store port) | Minor (additive), Preview; a store that ignores them keeps the earlier delivery |
| `AgentSessionCreateRequest.MaxWorkspaceLength` and `MaxContinuedFromLength`; create refuses longer values with 400 `agent.session.invalid_request` (the owner throws `ArgumentException`) | Minor (additive), Preview; refusing over-long values is a behaviour change, recorded |
| The owner list counts only reviews before their prepared deadline and lists an active session past its expiry as `expired` | Behaviour change, recorded |
| A lease is renewed only while a turn runs or an executor is still running (no longer for an operation that only waits on a client or its owner) | Behaviour change, recorded |
