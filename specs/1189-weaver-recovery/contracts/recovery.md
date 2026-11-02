# Contract: Weaver recovery (#3441, candidate)

**Status**: A candidate. It is not frozen. It freezes only after the normal composed Studio proof and
the complete package proof pass with durable recovery configured, as the prior leaf did.

## 1. Extension surface (Agent Core)

| Member | Kind | Notes |
| --- | --- | --- |
| `IAgentSessionRecordStore` | Replacement contract, at most one, Preview | Aggregate reads and writes for one session: hydrate, write a transition with a fence check, take ownership, renew and release the lease, insert unique keys, list owned sessions. The default in-memory implementation matches today's behaviour. A second registration fails `AgentRegistrationValidator` |
| `IAgentTurnStateStore` | Existing contract, **changed** | Becomes `SaveAsync`, `FindAsync` and `RemoveAsync`. A Major change, accepted pre-GA under ADR 0080 D4. A replacement keeps autonomy support, but durable recovery is Unsupported for that composition |
| `IAgentSessionService.ListOwnedAsync(AgentSessionOwnerQuery)` | Additive, Preview | Defaults to Unsupported. Filters by actor and tenant, then workspace, then orders and limits |
| `AgentSessionOwnerQuery`, `AgentSessionSummary` | Additive models | Actor, tenant, workspace, take (at most 100) and a `before` cursor. The summary holds id, title, status, updatedAt, expiresAt, pendingReviewCount and recoveryState |
| `AgentMessage.StatusReason` | Additive | `interrupted`, `withdrawn` or `user-stop` for Cancelled |

**Participation.** Session policy remains supported only when session, streaming, invoker and proposal services are the default participants of one owner, as in #3440. Configuring durable recovery gives that owner a record store and does not replace it. A replaced `IAgentTurnStateStore` or a replaced `IAgentSessionRecordStore` that cannot fence makes durable recovery report Unsupported.

## 2. Wire

| Route | Success | Refusals |
| --- | --- | --- |
| `GET /_elsa/agent/sessions?workspace=&take=&before=` | 200 `{ items: AgentSessionSummary[], next }` | 403 `agent.actor.unresolved`; 409 `agent.session.query_unsupported` |
| `GET /_elsa/agent/sessions/{id}/proposals` | 200 proposals with status, preview and deadline | Shared session-authorization 404 and 403 |
| `POST /_elsa/agent/sessions/{id}/messages/{messageId}/withdraw` | 200 message, Cancelled with reason `withdrawn` | 404 and 403; 409 `agent.message.not_pending` |
| `POST /_elsa/agent/sessions/{id}/client-invocations/{invocationId}/resolution` `{ claim: applied \| notApplied \| unknown }` | 200 proposal with `ownerResolution`; the claim is released | 404 and 403; 409 `agent.invocation.not_uncertain` |
| `POST /_elsa/agent/proposals/{id}/execute` (existing) | As today | Adds 409 `agent.proposal.policy_stale` when the stamped revision differs; Expired once the deadline has passed |
| Any route on a session this process no longer owns | n/a | 409 `agent.session.ownership_lost` |
| Any route on unreadable recorded state | n/a | 409 `agent.session.unavailable_state` with a reason |

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

**Authorization pass.** A pass is one admission sequence that ends at the next store, provider or tool await. Awaits inside admission do not end it. Within a pass, each disclosure source admits each distinct snapshot at most once.

## 4. SemVer (ADR 0080, pre-GA)

| Change | Class |
| --- | --- |
| `IAgentTurnStateStore` members become async | Major, accepted pre-GA |
| Record-store port, list query, summaries, `StatusReason`, new routes and codes | Minor (additive) |
| Anthropic provider history excludes non-accepted turns | Behaviour change, recorded |
| The Copilot resume fallback becomes an explicit event | Behaviour change, recorded |
