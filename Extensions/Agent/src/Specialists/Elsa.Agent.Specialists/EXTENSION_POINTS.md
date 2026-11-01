# Specialist runtime

`Elsa.Agent.Specialists` owns `SpecialistsRuntime`. Compose it with a model contribution and an
explicit journal profile. Its feature depends on ordinary Background Activities, Serialization,
and Secrets. The feature registers the shared helper's scoped specialist JSON validator. Configure the existing Secrets key ring; there is
no implicit ephemeral encryption key or memory-journal fallback.

## Cross-domain contributions

| Contract | Contribution |
|---|---|
| `IActivityActivationStrategy` | Scoped custom activation of `elsa.specialist/1`, without CLR hydration of dynamic business members. |
| `IRuntimeActivityConsumerCapability` | Declares the installed specialist descriptor/schema consumer. |
| `IStartupTask` | Registers the actual facade and background worker under their frozen runtime aliases. |

Both activity classes opt out of standalone CLR catalog discovery with `Activity(IsDiscoverable=false)`.
Published user specialists remain discoverable through their normal versioned provider definitions.
The opt-out changes reconciliation, not runtime alias registration or existing artifact execution.

The native entry checkpoint stages a protected request and opaque handle alongside child scheduling.
The worker checks the committed parent, child slot, scheduling handle, scoped workflow and immutable
artifact/contract/input fingerprints. Only its admitted nonce/generation under a finite live lease may dispatch and accept the turn. Its
native completion carries an opaque receipt; the parent verifies that receipt and contributes the
accepted `JsonElement` through the native typed completion checkpoint. Journal acceptance and process
application are distinct boundaries.

The worker renews the initial 30-second journal lease in fresh ordinary partition-bound persistence
scopes, independently of the model callback's store/DbContext. A denied or unconfirmed renewal cancels
the waiting model operation and prevents acceptance. Renewal I/O is itself bounded by the last
confirmed lease expiry, separately from the operation deadline. A dispatched turn records OutcomeUnknown.
Redelivery of the exact same native child waits for the existing live lease, bounded by native
cancellation and the original operation deadline. It may recover an expired pre-dispatch claim with
a new generation. Expired dispatched work is never reissued. New native attempt/child associations require the explicit delivery registration below. Protected accepted receipts also authenticate the admission generation while
retaining the reader for legacy generation-zero accepted receipts.

The native worker creates a journal-bound physical-request authority. Qualified provider transports
reserve before every SDK HTTP send; the reservation requires the current dispatch fence, deadline
and authenticated finite request maximum. An unavailable backend, exhausted budget or lost fence
permits no send. A missing provider accounting capability is refused before asking for a response.
Output ceilings are bound to the actual runtime options. Before finish/privacy/result validation,
the runtime records the response's available counts and reported model ID for its exact reserved
request set and freezes further sends. Refusal or invalid business output can therefore retain
observed usage without accepting history or a business result. Invalid metadata and reported output
overruns are explicit faults. Missing counts remain unknown. A known output count is attributed only
to a single physical request; otherwise output accounting retains every reserved ceiling. The accepted
protected receipt carries these observations separately from the public business JSON. Input/total-token
hard limits are not claimed. SDK retries and primary redirects remain independently disabled.

Legacy manifest1/descriptor1 keeps fresh-only public invocation. Manifest2 with explicit continuity
publishes optional AgentOptions and safe Conversation metadata; see the
[authoring contract](../Design/EXTENSION_POINTS.md#explicit-continuation-contracts). A new MAF session
still restores only the admitted accepted prefix. No tool or streaming admission is supplied by these
controls. The current invocation/2 profile can lower call/time/output limits separately from the
immutable published descriptor. Effective limits are protected with the staged request, bind the
persisted request maximum and original deadline, and clamp actual MAF/SDK output options before
physical reservations. Staged replay cannot reset these bounds; changed limits conflict. The old
continuation/1 profile retains conversation-only controls and rejects the new limit member. Known
entry refusals return their safe specialist code through a native structural fault without scheduling
a model child. A definition's continuity capability does not implicitly reuse history. The private
`elsa.specialist.text-history/1` codec now produces protected candidates for continuity-enabled turns;
the journal accepts that candidate and business result together. Restoration requires a trusted
admitted conversation/revision reference, selected ordinary owner scope and exact specialist version.
The journal now admits a workflow-owned revision before dispatch and restores only its previous
accepted receipt. Explicit continuation checks the workflow-owned conversation and expected base
revision before opening a client; unavailable admission becomes a native conversation conflict.
It whitelists alternating user/assistant text, binds conversation/revision/version inside protection,
and uses the existing Secrets key ring with retained configured decryption keys. Histories are bounded to64
messages/eight MiB; byte capacity is checked before dispatch and is not an input-token accounting claim.
Portable history excludes private reasoning, SDK raw state and system-role instruction fields. Validate business
inputs and outputs against both the exact schema and declared native conversion. Withheld,
encryption-required and secret-reference business inputs are refused before model dispatch.
Root-variable captures resolve their current committed destination policy at native entry and worker
binding checks; encryption-required or withheld destinations are refused before opening a client.

`SpecialistsInMemoryJournal` is an explicit development/test profile, retaining at most 10,000 turns
for one host lifetime through scoped adapters over shared in-memory state. Both backends enforce the
selected ordinary scope at their public store boundary. Requests/results are protected with purpose/scope/provenance checks inside
the authenticated envelope. Repeated operations retain their deadline; altered arguments conflict;
stale fences and late acceptance are refused. Missing state after restart is a fault, never permission
for a fresh model request. This profile establishes no durable recovery or distributed ownership.

The [EF journal](../Persistence/EntityFrameworkCore/README.md) now implements the same conditional
turn lifecycle over relational storage and supports consumer-owned contexts. A selected database
survives development-feature registration order; conflicting explicit selections and missing/duplicate
startup registrations are refused. Conversation claim/accept/failure transitions now commit with the associated turn;
native cancellation/applied settlement remains separate work.

The [model provider port](../Core/EXTENSION_POINTS.md) owns actual credential resolution/current
authorization and physical SDK/HTTP retry guarantees. Durable sessions, journal/cancellation/process
settlement, complete provider qualification, tools, teams and the final support matrix remain Program #3519 work.

## Native retry and accepted-result replay

Staging a new verified native parent uses the request/2 logical fingerprint and registers its delivery
against the existing operation. The origin request remains protected and immutable; after authentication
the transient runner request uses the current verified parent identity for point-of-use connection
authorization. New physical claims record that delivery; accepted result/2 receipts bind its original
dispatch identity, child, nonce and generation. Replay binds the new native child and returns the same
accepted business/session/usage without a provider call or conversation mutation. Parent completion
requires this recorded child and the ordinary native completion proof. Failed/unknown operations
return coded refusals; changed arguments/bounds/artifact policy conflict.

The real `RetryFaultedActivity/1` test injects a facade receipt-read failure after a completed model
child, requests native intervention, submits an alteration, and observes a new facade/child completing
with one total model call in Memory and SQLite. It preserves accepted history and the original physical
receipt. Native retry/reschedule staging now assigns retained input snapshots to the successor's own
invocation while preserving their values, policy fingerprints and materialization time; strict
specialist and kernel ownership checks remain unchanged. This is native delivery proof with a seeded
structural wrapper, not M4 authoring qualification.

A distinct acceptance-response-loss probe faults the model child and retains its blocking incident;
intervention on only the facade incident does not override it. That failed probe is retained as open
native settlement/reconciliation work under M3, separately from the qualified facade-read retry path.
Cancellation/applied dispositions, pre-admission tombstones and retention are still required by T021–T023.
