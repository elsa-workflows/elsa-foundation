# Specialist model contributions

Owner contract package: `Elsa.Agent.Specialists.Core`. Runtime owner: `Elsa.Agent.Specialists`.

## Model providers

[`ISpecialistModelCapabilitiesSource`](Contracts/ISpecialistModelCapabilitiesSource.cs) owns authoring
metadata; [`ISpecialistModelClientFactory`](Contracts/ISpecialistModelClientFactory.cs) owns runtime
client creation. Register scoped implementations through DI, keyed by their exact `ProviderKey`. Each
operation requires exactly one matching owner of its own port; duplicates and missing owners are refusals.
A provider can implement both ports, while authoring-only and runtime-only hosts register only what they consume.
This registry is independent of Weaver's selected assistant provider.
Known intra-domain contributions are `AnthropicSpecialistModelProvider` and
`AzureOpenAISpecialistModelProvider`, registered scoped by their respective optional specialist features.

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

Native turns supply `SpecialistModelOpenRequest.DispatchAuthority`, a per-invocation callback rather
than a DI contribution or serialized descriptor field. A qualified client calls `BeforeSendAsync`
below its SDK before every physical model request, and sets the lease's `EnforcesDispatchAuthority`
only when that mechanism is actually installed. Native execution refuses an unknown accounting
capability before requesting a response. Protocol fixtures may open standalone leases without this
authority; that establishes no native budget qualification. No callback grants access to a connection.

The [connection-backed contributors](../Connections/README.md) add separate Anthropic and Azure OpenAI
profiles. Their configured support metadata and physical-request controls remain distinct from durable
specialist acceptance and the Program's later live-provider qualification.

## Turn journal replacement

[`ISpecialistTurnStore`](Contracts/ISpecialistTurnStore.cs) is a replacement contract: select one backend
per shell. The explicitly enabled in-memory feature is for development; the
[EF contribution](../Persistence/EntityFrameworkCore/EXTENSION_POINTS.md) owns relational writes.
Neither feature changes Connections or Secrets ownership.
Custom packages select their scoped backend through
`SpecialistTurnStoreRegistration.ReplaceSpecialistTurnStore(factory)`; it replaces only the tracked
development default and rejects a second explicit or unowned selection. Runtime startup also rejects
missing or duplicate raw registrations. `TryAddSpecialistTurnStore<TStore>` registers a default with
an explicit lifetime only when no journal is selected. Contribution enumeration is not supported.

Providers isolate the ordinary tenant/partition scope before accessing a record, derive the native
operation index with `SpecialistTurnDocument.CalculateOperationKey`, and refuse conflicting request
fingerprints. An identical stage replays the original handle, ciphertext and absolute deadline.
Claim, dispatch and acceptance compare the exact native identity, lifecycle state and fence in storage;
claim and acceptance require an unexpired deadline. Acceptance stores the protected result/session pair
atomically and is immutable. Uncertain dispatch is terminal `OutcomeUnknown`, never an implicit retry.
The runtime authenticates the opaque protected material before using it.

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

`SpecialistJournalCapabilities` describes the configured backend, separately from reachability,
credential durability or end-to-end recovery qualification. Delivery rebinding, hard input/total-token accounting and retention controls remain separate M3 work.

Storage, persisted-record and scope failures cross the journal boundary as `SpecialistJournalException`,
with a safe failure code, canonical opaque handle when available and the original `InnerException`.
Cancellation remains cancellation. An exception means the transition is unconfirmed; it never permits
another inference request. Callers do not infer remote absence from a failed journal write.

Optional `SpecialistTurnDocument.Conversation` admits a base revision during claim. The initial owner
is derived from verified native provenance: tenant, partition, workflow execution and exact specialist
version/template. `ReadConversationAsync` requires that same owner; a conversation ID is not a grant.
Revision zero creates a fresh conversation. A positive revision must match the last accepted revision,
with no active operation or unresolved uncertainty. Claim and conversation admission form one atomic
write; acceptance commits the protected result/session and advances the conversation once in that
same transaction. Known failure releases the base revision without adding history. OutcomeUnknown
retains the active operation and blocks continuation. Old accepted operation replay never rewinds the
conversation. A custom backend must preserve all of these paired conditional transitions.
