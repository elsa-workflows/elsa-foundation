# Agent model contributions

Owner contract package: `Elsa.Agent.Core`. Runtime owner: `Elsa.Agent`.

## Model providers

[`IAgentModelCapabilitiesSource`](Contracts/IAgentModelCapabilitiesSource.cs) owns authoring
metadata; [`IAgentModelClientFactory`](Contracts/IAgentModelClientFactory.cs) owns runtime
client creation. Register scoped implementations through DI, keyed by their exact `ProviderKey`. Each
operation requires exactly one matching owner of its own port; duplicates and missing owners are refusals.
A provider can implement both ports, while authoring-only and runtime-only hosts register only what they consume.
This registry is independent of Weaver's selected assistant provider.
Known intra-domain contributions are `AnthropicAgentModelProvider`, `OpenAIAgentModelProvider` and
`AzureOpenAIAgentModelProvider`, registered scoped by their respective optional agent features.

`GetCapabilitiesAsync` reads authorized current model metadata without inference. The capabilities
fingerprint identifies the effective settings/schema/token support. The supplied settings schema must
use the [bounded JSON profile](../JsonSchema/README.md); unsupported or invalid settings refuse
publication. Treat unsupported or unknown structured-result support as unavailable.
`SupportsResultSchemaAsync` admits the actual expanded result schema before publication. Its compatibility
default preserves the original declaration of support for the bounded profile; contributors with narrower
SDK support override it. A false result refuses publication even when general JSON support is true.

`OpenAsync` resolves the logical operational connection at point of use, checks current access and
revocation against the supplied native provenance, and returns an owned transient client lease. The
identity is provenance for verification, not a caller-provided authorization grant. Credentials and
SDK objects never enter the descriptor or journal material. The lease's capability fingerprint must
match publication and its connection version must be positive. The runtime owns model selection,
response schema, maximum output tokens and tool admission; it clones the supplied options.

Native turns supply `AgentModelOpenRequest.DispatchAuthority`, a per-invocation callback rather
than a DI contribution or serialized descriptor field. A qualified client calls `BeforeSendAsync`
below its SDK before every physical model request, and sets the lease's `EnforcesDispatchAuthority`
only when that mechanism is actually installed. Native execution refuses an unknown accounting
capability before requesting a response. Protocol fixtures may open standalone leases without this
authority; that establishes no native budget qualification. No callback grants access to a connection.

A lease's `SupportsStreaming` defaults to false. Set it only after qualifying the client's actual
streaming terminal/refusal signals; a host selecting streaming refuses an unqualified lease before
model dispatch. The default atomic response path does not require that flag. Streaming still requires
physical-request authority and current authorization, and grants no tools, provider-managed session or
resume behavior. The runtime owns [bounded filtering and final acceptance](../Elsa.Agent/EXTENSION_POINTS.md#optional-streaming-responses):
partial visible text is never an accepted business result or reusable history. Include changed effective
support in the model profile fingerprint; never claim qualification from the presence of an SDK method.

The [connection-backed contributors](../Connections/README.md) add separate Anthropic, OpenAI and Azure OpenAI
profiles. Their configured support metadata and physical-request controls remain distinct from durable
agent acceptance and the Program's later live-provider qualification.

## Invocation evidence

[`IAgentInvocationInspector`](Contracts/IAgentInvocationInspector.cs) is a replacement contract: one
inspector per shell. `AgentRuntime` registers its scoped default with `TryAdd`, so a host package replaces it by
registering first; a registration after the runtime feature is a second one, which the runtime's startup check refuses.
Callers authorize the read first. The [inspection API](../Inspection/Api/EXTENSION_POINTS.md) does this
through the generic draft Test Run. The default resolves the exact invocation through the same native binding the turn
was staged under, in the caller's ordinary persistence scope, and returns `null` for anything else.
`AgentInvocationEvidence` carries:

- the candidate or published version, the model reference, the limits and the native status;
- the fault code, plus the message only for a `agent.*` refusal;
- the result as the host's `IRuntimePayloadCapturePolicy` admits it;
- the journal turn's status, model requests, usage, conversation and application status.

The result is returned only for a full payload capture, and never when the envelope, the contract result or a projected
member is sensitive or encryption-required. A diagnostic snapshot stays with the runtime's own inspection.

The application status counts only this invocation's own delivery. It separates a turn with no accepted result
(`NotAccepted`), a recorded settlement (`Applied`), a native completion whose settlement is still pending
(`SettlementPending`), a native fault, cancellation or supersession (`NotApplied`) and a boundary that has not settled
(`Pending`).

Protected request, result and session material, instructions, fences, leases, connection incarnations and capability
fingerprints never enter it. The fingerprint and incarnation stay in the immutable artifact and turn record, which
identify the exact admission. An implementation must not decrypt, infer or mutate.

## Turn journal replacement

[`IAgentTurnStore`](Contracts/IAgentTurnStore.cs) is a replacement contract: select one backend
per shell. The explicitly enabled in-memory feature is for development; the
[EF contribution](../Persistence/EntityFrameworkCore/EXTENSION_POINTS.md) owns relational writes.
Neither feature changes Connections or Secrets ownership.
Custom packages select their scoped backend through
`AgentTurnStoreRegistration.ReplaceAgentTurnStore(factory)`; it replaces only the tracked
development default and rejects a second explicit or unowned selection. Runtime startup also rejects
missing or duplicate raw registrations. `TryAddAgentTurnStore<TStore>` registers a default with
an explicit lifetime only when no journal is selected. Contribution enumeration is not supported.

Providers isolate the ordinary tenant/partition scope before accessing a record, derive the native
operation index with `AgentTurnDocument.CalculateOperationKey`, and refuse conflicting request
fingerprints. An identical stage replays the original handle, ciphertext and absolute deadline.
Claim, dispatch and acceptance compare the exact native identity, lifecycle state and fence in storage;
owned mutations require the current admission generation and an unexpired finite lease/deadline. Acceptance stores the protected result/session pair
atomically and is immutable. Uncertain dispatch is terminal `OutcomeUnknown`, never an implicit retry.
The runtime authenticates the opaque protected material before using it.

The initial lease profile is 30 seconds, capped by the original absolute deadline. Claim returns an
incremented `AdmissionGeneration` and finite `LeaseExpiresAt`; owned mutations use the complete
`AgentTurnLeaseToken` (nonce and generation). Renew only a still-live Claimed/Dispatching lease;
renewal cannot reset the operation deadline. After pre-dispatch expiry, the same native child or an explicitly registered delivery
of that logical operation can reclaim the turn, incrementing its generation even if a nonce is reused. Expired dispatch
becomes OutcomeUnknown and retains its reservations and conversation block. It cannot admit another
model request. Failure settlement may record uncertainty after expiry but cannot change another
generation or an accepted/unknown terminal decision. Legacy accepted documents with generation zero
and no lease remain readable; they do not acquire inference authority from that legacy shape.

`ReserveModelRequestAsync` conditionally appends one unique physical-send reservation while the turn
is dispatching under its live fence/deadline and finite `MaximumModelCalls`. Reusing a reservation ID
does not grant another send. Output ceilings and configured resource observations remain in the
reservation even after unknown usage/outcome. The runtime authenticates the request maximum against
the protected staged descriptor. Returned collection values cannot mutate retained in-memory state.
`CompleteModelResponseAsync` records available response counts and the provider-reported model ID
for the exact reserved request set under the live fence/deadline. It freezes further request admission;
acceptance with reservations requires that observation. A response with unavailable or partial usage
keeps missing counts unknown. The accepted protected receipt distinguishes known single-request output
usage from conservative reserved output when attribution is unavailable. Input/total-token hard limits
are not established by this mechanism. Configured model identity and reported identity remain distinct.

`AgentJournalCapabilities` describes the configured backend, separately from reachability,
credential durability or end-to-end recovery qualification. `ApplyAsync` records one exact accepted
native delivery's application as copied metadata, using `AgentTurnApplication` source facts.
Equivalent sources are idempotent, conflicts refuse, and protected result/session and conversation
state are preserved. The runtime consumer verifies physical native source authority before calling
this neutral port. `CancelAsync` records an exact native-parent cancellation and returns a retained decision:
CancelledBeforeAdmission, Cancelled, AlreadyAccepted, AlreadyTerminal or OtherDeliveryUnchanged.
The backend must serialize staging and pre-dispatch takeover against that parent's tombstone, including
when no protected turn exists yet. The cancellation consumer supplies verified source facts; they grant
no authority by themselves. The first equivalent source is retained and changed canonical content
refuses. Cancelling the current unaccepted delivery invalidates later writes, releases its exact
conversation owner without advancing history, and retains request reservations/observations. Accepted
pairs, other deliveries and later conversation owners remain unchanged. Hard input/total-token
accounting and complete recovery/compatibility qualification remain separate M3 work.

Storage, persisted-record and scope failures cross the journal boundary as `AgentJournalException`,
with a safe failure code, canonical opaque handle when available and the original `InnerException`.
Cancellation remains cancellation. An exception means the transition is unconfirmed; it never permits
another inference request. Callers do not infer remote absence from a failed journal write.

Optional `AgentTurnDocument.Conversation` admits a base revision during claim. The initial owner
is derived from verified native provenance: tenant, partition, workflow execution and exact agent
version/template. `ReadConversationAsync` requires that same owner; a conversation ID is not a grant.
Revision zero creates a fresh conversation. A positive revision must match the last accepted revision,
with no active operation or unresolved uncertainty. Claim and conversation admission form one atomic
write; acceptance commits the protected result/session and advances the conversation once in that
same transaction. Known failure releases the base revision without adding history. OutcomeUnknown
retains the active operation and blocks continuation. Old accepted operation replay never rewinds the
conversation. A custom backend must preserve all of these paired conditional transitions.

`AgentRuntimeIds` retains manifest/descriptor1 and adds explicit continuity schema2 identities.
The descriptor's optional `InvocationControlsProfile` is omitted while null, preserving the old
serialized descriptor shape. The runtime validates the exact profile/schema pair and retains both
readers. Native public controls are defined by the provider's reviewed contract, not by model settings
or a connection credential. See [the authoring contract](../Design/EXTENSION_POINTS.md#explicit-continuation-contracts).

Invocation controls are interpreted by the native runtime under their exact descriptor profile. The
current invocation/2 profile may only lower the published call/time/output bounds; the legacy
continuation/1 reader refuses the new limits member. Protected request material retains effective
limits separately from the immutable provider descriptor. A custom physical accounting contribution
must respect the bound output options and current journal reservation maximum; a control value grants
no connection, owner or tool authority.

## Logical operation and native deliveries

New runtime records use delivery protocol1, with at most64 verified parent associations. The turn's
original identity/request and accepted physical-dispatch provenance remain immutable. A new native
parent can register during staging only when the complete logical fingerprint matches: every native
scope/artifact/version/template/operation field, business input, contract/binding policy, settings,
continuation and effective bounds remains bound. Only the parent invocation ID is excluded from the
new request/2 fingerprint. An unregistered parent cannot read the handle or replay acceptance.

`ReplayAcceptedAsync` binds a registered parent to one exact child. It updates turn delivery metadata
without reapplying conversation acceptance, even if history has advanced or another operation is
unknown. Repeated matching binding is safe; a conflicting child refuses. Copies of returned delivery
collections cannot mutate retained memory state. A new physical claim records its active delivery,
nonce and generation; expired dispatch and rejected/unknown terminal decisions cannot issue a fresh
request. Exact-original request/1 and result/1 readers remain; old records do not acquire new native
alias authority from missing protocol fields. Unsupported legacy cross-attempt staging conflicts.


## Conditional content retention

The creation-policy `StageAsync` overload atomically refuses new operations when disabled, after
matching retained-operation checks. Implementations retain existing delivery registration/replay and
settlement. A host setting never changes the persisted fingerprint or implicitly cancels work.

`QueryRetentionAsync` takes an explicit tenant/partition and finite keyset page (default32, maximum128).
It returns copied content-free candidate metadata and an opaque store snapshot; `NextHandle` advances
across scanned records even when every row is pinned or already purged. A null legacy tenant remains
distinct from the partition's named tenant. Both stores require the selected ordinary scope.

`PurgeContentAsync` consumes an unchanged candidate only after native runtime eligibility proof.
Implementations reject changed turn/delivery/application metadata and active/unknown conversation
ownership atomically. Memory uses its existing lock; EF uses conversation-version and turn-version
CAS inside one fresh owned transaction, without altering conversation history. Unknown commit outcome
remains unconfirmed. No retry creates a new request or erases a retained operation row/gate.

A cancelled fresh conversation that never claimed has no conversation row: only generation zero,
base revision zero and the original private turn handle as conversation ID admit that absence.
Missing referenced/admitted conversation state remains pinned.

A successful mutation clears only protected request/result content and records `ContentPurgedAt`.
Status, operation fingerprint, native associations, cancellation authority, reservations, usage and
conversation references remain. Normal records omit the marker, preserving their existing serialized
shape. Prepared/claimed/dispatching/unknown records cannot become purged markers. Runtime content
readers report `agent.journal.content-unavailable`; matching Stage cannot replace the marker with
new ciphertext or inference authority. Native business-output retention is owned separately.

The runtime's default thirty-day policy requires old terminal native ownership and resolved physical
application sources; active work and unsettled conversation history remain pinned. Key/codec/module
upgrade and full independent-process durable runtime/outbox qualification remain separate receipts.
