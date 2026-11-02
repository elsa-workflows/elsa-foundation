# Contract: Weaver recovery (#3441, candidate)

**Status**: A candidate. It is not frozen. It freezes only after the normal composed Studio proof and
the complete package proof pass with durable recovery configured, as the prior leaf did.

## 1. Extension surface (Agent Core)

| Member | Kind | Notes |
| --- | --- | --- |
| `IAgentSessionRecordStore` | Replacement contract, at most one, Preview | Aggregate reads and writes for one session: hydrate, write a transition with an owner and fence check that also extends the lease, take ownership, release the lease, insert unique keys, resolve a proposal to its session, list owned sessions. The default in-memory implementation matches today's behaviour. A second registration fails `AgentRegistrationValidator` |
| `IAgentTurnStateStore` | Existing contract, **changed** | Becomes `SaveAsync`, `FindAsync` and `RemoveAsync`. A Major change, accepted pre-GA under ADR 0080 D4. A replacement keeps autonomy support, but durable recovery is Unsupported for that composition |
| `IAgentSessionService.ListOwnedAsync(AgentSessionOwnerQuery)` | Additive, Preview | Defaults to Unsupported (`AgentSessionListResult`). Filters by actor and tenant, then workspace, then orders and limits |
| `IAgentSessionService.FindOwnerAsync`, `IAgentProposalService.FindSessionIdAsync`, `IAgentSessionRecordStore.FindOwnerAsync` (`AgentSessionOwner`) | Additive, Preview, with defaults | The plain owner columns and the proposal-to-session lookup that authorization reads before anything hydrates, takes ownership or settles |
| `IAgentSessionService.WithdrawMessageAsync`, `IAgentProposalService.ListAsync`, `IAgentProposalService.ResolveClientInvocationAsync` | Additive, Preview | Default to a refusal (`agent.message.withdraw_unsupported`, `agent.proposal.query_unsupported`, `agent.invocation.resolution_unsupported`) |
| `AgentActionProposal.OwnerResolution` (`AgentOwnerResolution`, `AgentOwnerResolutionClaim`) | Additive, Preview | The owner's statement; omitted from JSON when absent |
| `AgentProposalRecord.Version`, `AgentSessionRecordOutcome.VersionConflict` | Additive, Preview | Every proposal record write names the version it read; the store refuses a mismatch |
| `AgentSessionOwnerQuery`, `AgentSessionSummary` | Additive models | Actor, tenant, workspace, take (at most 100) and a `before` cursor. The summary holds id, title, status, updatedAt, expiresAt, pendingReviewCount and recoveryState |
| `AgentMessage.StatusReason` | Additive | `interrupted`, `withdrawn` or `user-stop` for Cancelled |
| `AgentDisclosureSnapshotRecord`, `AgentMessageRecord`, `AgentPausedTurnRecord`; `AgentSessionRecord.Snapshots`, `AgentSessionRecordWrite.Snapshots` | Preview models of the record-store port | Each distinct snapshot is recorded once per session as its digest and byte-identical canonical JSON text. Messages and the paused turn hold ordered digests; `DeliveryFingerprint`, the message history fingerprint and the pause fingerprint are stored verbatim and re-verified at hydration |
| `AgentSessionUnavailableException` (`agent.session.unavailable_state`) | Additive, Preview | Thrown at hydration when recorded state fails verification, with a `Reason`; nothing is settled and the lease taken is released |
| `AgentAuditEventKind.ProposalResolved` | Additive, Preview | The owner's resolution of an uncertain client operation, correlated like every proposal audit record, with its `claim` |
| Audit sink, reader and ownership | Existing contracts, replaced together by the EF feature | A duplicate of the trio fails the registration validator. `settledBy` is a metadata key |

**Participation.** Session policy remains supported only when session, streaming, invoker and proposal services are the default participants of one owner, as in #3440. Configuring durable recovery gives that owner a record store and does not replace it. A replaced `IAgentTurnStateStore` or a replaced `IAgentSessionRecordStore` that cannot fence makes durable recovery report Unsupported.

## 2. Wire

| Route | Success | Refusals |
| --- | --- | --- |
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
| A late truthful receipt after Uncertain or an owner statement | Reconciled; the receipt wins and the statement is kept | No re-execution |
| The disclosure limit is reached (64 distinct snapshots or 8 MiB) | `agent.context.disclosure_budget_exceeded`, with `limit: snapshots \| bytes` | Zero |
| The Copilot resume falls back to a new SDK session | Stream event `agent.provider.github_copilot.conversation_reset`, recorded on the turn | n/a |

**Authorization pass.** A pass is one admission sequence. It ends at the next write, provider call or tool execution. Awaits for owner admission and tool discovery belong to the pass, as in spec FR-015. Within a pass, each disclosure source admits each distinct snapshot at most once.

## 4. SemVer (ADR 0080, pre-GA)

| Change | Class |
| --- | --- |
| `IAgentTurnStateStore` members become async | Major, accepted pre-GA |
| Record-store port, list query, summaries, `StatusReason`, new routes and codes | Minor (additive) |
| Anthropic provider history excludes non-accepted turns | Behaviour change, recorded |
| The Copilot resume fallback becomes an explicit event | Behaviour change, recorded |
| Studio SDK `StudioAgentToolContractContribution.clientTargetAvailability` (optional; the owning module reports before dispatch whether a client invocation's unsaved target still exists, and anything else refuses with zero effect) | Minor (additive), public SDK |
| Studio client runner reports a missing module adapter as "The owning module's client adapter for this change is not present. Nothing was applied." | Behaviour change (refusal text only), recorded |
| Studio SDK `StudioHttpError.retryAfterSeconds` (the response's `Retry-After` hint; a trailing optional constructor parameter) | Minor (additive), public SDK |
