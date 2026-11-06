# Durable messaging delivery design

**Status: DRAFT engineering proposal — not an adopted contract or implementation.**

Canonical feature: [#3583](https://github.com/elsa-workflows/elsa-foundation/issues/3583).
The owner selected the product requirements recorded there. The proposals below make those
requirements concrete for review; imperative wording describes the proposed implementation,
not behavior currently supplied by Elsa. Approval of an individual product choice does not
approve every persistence shape or provider capability in this document.

Source inspection began at `b7fc0ec330ad55a2062b5badbf7697705852201b` on 5 November 2026.
Subsequent fetched main changes included outbox claim-delivery observations and coalescing
refresh. They do not implement the inbox or the standalone ingress proposed below. Source,
review and runtime qualification must be refreshed against the eventual implementation head.

## Accepted product decisions

The owner accepted Q1–Q3 on 5 November 2026. Their behavioral direction is selected; the
concrete model, schema, numeric defaults and provider mechanisms remain engineering proposals.

| Question | Recorded direction | Status and consequence |
|---|---|---|
| Acceptance and acknowledgement | The owner accepted a durable receipt with separately observable delivery, explicit unmatched immediate-only outcomes and retry of uncertain submission using the same producer ID/content. | Accepted product decision Q1. Success acknowledges durable acceptance, not recipient processing or workflow completion. Exact HTTP statuses/wire names and provider mapping remain engineering review. |
| Historical eligibility | The owner accepted existing eligible definitions only: freeze the logical definition cohort at acceptance. Later waits and replacement versions inside that cohort and message interest may receive the message. Brand-new definitions do not receive historical messages. | Accepted product decision Q2. A first delivery to Fulfillment never closes Accounting's independent pending opportunity; a newly deployed definition cannot join yesterday's cohort. Runtime interest declarations and the exact consistent snapshot mechanism remain engineering work. |
| Starts versus existing waits | The owner accepted a ready wait first; consider an eligible published start only when no wait is ready and the message-started key is free. This proposal selects waits by name/key/order without payload conversion filtering. | Accepted product decision Q3. A payment for an existing order goes to its wait rather than opening another run. Exact ordering, reservation and payload-failure mapping remain engineering review. |

The frozen cohort is a set of logical definitions with declared Message interest, not a frozen
set of exact recipients. Buffered messages can wait for a later subscription inside that cohort.
Do not silently replace it with the existing whole fixed recipient snapshot or open-ended
definition discovery. Q1–Q3 do not adopt every mechanism below. Routine engineering choices
remain subject to independent review and their actual physical-provider qualification.

## Existing owners and reusable seams

| Observed source | What it supplies | Work still needed |
|---|---|---|
| [StimulusRouter](../../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Triggers/StimulusRouter.cs) | Starts every matching binding and resumes the pre-start snapshot of matching waits. Ordinary Events remain broadcast. | A Message selection policy with one start-or-wait winner per logical definition and continued discovery while buffering. Do not change Event routing to obtain this policy. |
| [IStimulusDeliveryStore](../../../Essentials/src/Workflows/Runtime/Core/Contracts/IStimulusDeliveryStore.cs), [StimulusDeliveryServices](../../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Triggers/StimulusDeliveryServices.cs) | Source-owned immutable cohort installation, current persisted claims/fences, captured publications, exact waits and executable retention. | Current sources are workflow outbox rows and require a real publisher execution. They do not provide application acceptance or late-recipient buffering. Extend the real Runtime source authority rather than fabricate a publisher execution or delivery context. |
| [WorkflowTriggerBinding](../../../Essentials/src/Workflows/Runtime/Core/Models/WorkflowTriggerBinding.cs), [activation authority](../../../Essentials/src/Workflows/Runtime/Core/Contracts/IWorkflowActivationAuthority.cs), [start source resolver](../../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executions/WorkflowExecutableStartSourceResolver.cs) | Logical definition and exact trigger/publication identities; ordinary publication selection, source capture and validation. | Choose one eligible start across all versions/slots in a definition, then retain its exact provenance. Publication ownership and the start dispatcher's normal policy remain authoritative. |
| Runtime publication and retained active material | Immutable published/active executable pins and current activation ownership. | Runtime-owned Message interest declarations, including receive-only definitions with neither a ready wait nor a start trigger. No existing trigger lookup supplies this complete population. |
| [StimulusResumeCapture](../../../Essentials/src/Workflows/Runtime/Core/Models/StimulusResumeCapture.cs), [checkpoint capability](../../../Essentials/src/Workflows/Runtime/Core/Contracts/IRuntimeCheckpointCommitStore.cs) | Exact physical bookmark generation, execution/activity/node identity, scope/pin capture and atomic bookmark-resume checks. | Atomic Message selection/acceptance conditions and durable completion acknowledgement on this actual provider boundary. A captured DTO alone grants no authority. |
| [KeyedWorkflowStartIdentity](../../../Essentials/src/Workflows/Runtime/Core/Models/KeyedWorkflowStartIdentity.cs) | Frozen idempotent individual-start identities, including artifact-scoped and recurring-occurrence forms. | Same-key active-run admission and a Message delivery identity across versions. Preserve all existing derivations and historical fingerprints. |
| [RuntimeCheckpointCommit](../../../Essentials/src/Workflows/Runtime/Core/Models/RuntimeCheckpointCommit.cs), [model state](../../../Essentials/src/Workflows/Runtime/Core/Models/RuntimeModelExecutionState.cs) | Existing fenced checkpoint/outbox authority, native state and separate model decisions/input receipts. | Message participants, capabilities and terminal release evidence, qualified through real physical and coalescing compositions. |
| [Direct BPMN contracts](../../../Essentials/src/Activities/Bpmn/Core/README.md), [BpmnModelRuntime](../../../Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnModelRuntime.cs) | Genuine model execution, explicit protocol/material incarnation, model-owned progression and native worker integration. | Model-owned Message subscription/start adapters and compatible definition formats. Do not substitute a native enclosing run for model execution. |

The [StateMachine C2 shared-owner advisory](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6670940060)
proposes Runtime-owned standalone ingress in the normal persistence/delivery composition. It
does not adopt a persisted ABI, enable a producer or qualify that mechanism. The
[current M0 checkpoint](https://github.com/elsa-workflows/elsa-foundation/pull/3840#issuecomment-6671015378)
contains internal accounting/tests only, under the retained
[#3820 claim](https://github.com/elsa-workflows/elsa-foundation/issues/3820#issuecomment-6642889421).
Coordinate the source-family extension and exact acceptance/completion boundaries with those
owners before editing shared contracts. This feature adds domain semantics to that shared
delivery path; it does not add a scheduler, competing pump, broker, execution store or independent
delivery authority. Conceptual records below belong to the owning Runtime persistence family;
their physical representation and public extension points remain unapproved.

## Identity and authorization addresses

Use ordinal, case-sensitive matching for the message name, business key and producer ID. Do
not trim, case-fold or parse a key as a number. Reject absent, empty and whitespace-only
business keys in the first release. This makes the selected active-run rule understandable;
an unkeyed mode would need a separate owner decision. Leading/trailing nonblank characters
remain significant rather than being silently normalized.

| Identity | Proposed scope and purpose |
|---|---|
| Accepted message | An opaque server-generated identity within the actual owning persistence scope and tenant. It survives delivery and has no executable-version meaning. |
| Producer deduplication address | Persistence scope + tenant + authorized stable producer namespace + producer message ID. Message name is content, not a way to evade a reused ID. Different producers can use the same ID independently. |
| Logical recipient definition | Persistence scope + tenant + stable definition namespace + logical definition ID. Ordinary authored native and BPMN workflows share namespace `WorkflowDefinition` and their canonical authored `DefinitionId`. Exclude execution model/protocol family or version, definition version, artifact hash, activation slot and instance. A model change cannot create another recipient for the same authored definition. |
| Per-definition delivery | Accepted message identity + logical recipient definition. A unique persisted constraint establishes at most one selected winner. |
| Native wait recipient | Exact execution, activity, node, bookmark/resume target and physical registration generation, with captured executable/source and scope. |
| Model recipient | Exact model execution, semantic protocol version, material incarnation and model-owned subscription/activation generation, with its owning input identity and authority. A native worker additionally retains its model-work origin. |
| Start recipient | Exact authorized publication/source reference, activation/slot and trigger node or model-owned start subscription, immutable material pin and deterministic target execution/start identity. |
| Message-started admission | Persistence scope + tenant + stable definition namespace + logical definition ID + business key. Ordinary authored native and BPMN versions share `WorkflowDefinition` identity. Deliberately excludes model/protocol family/version, message name, artifact/version, slot and individual message ID. |

The ordinary publication slot and authored definition identity are model-neutral. Native and
BPMN versions of the same authored definition therefore share one delivery and admission lane;
adding a model/protocol discriminator there would violate cardinality and allow concurrent
same-key runs after a model change. Model protocol/version and material incarnation remain
essential exact target facts, but do not define another logical business recipient.

Current direct [BpmnModelRuntime](../../../Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnModelRuntime.cs)
creates `pin.DefinitionId` as `bpmn-model:` plus the material hash; it is content identity, not
stable authored definition identity. Its source reference currently uses that synthetic value
too. [BpmnModelPublicationProvenance](../../../Essentials/src/Activities/Bpmn/Core/BpmnModelPublicationProvenance.cs)
currently carries authored version/presentation facts without the canonical logical DefinitionId.
Extend that existing trusted source-provenance publication path to carry the authored
`WorkflowDefinition` namespace and stable `DefinitionId`, retain it in execution/subscription
ownership, and recover it without querying Design. Do not invent logical identity from a hash,
version ID or a native worker's executable pin.

A standalone nonauthored model may use a different owner-defined stable definition namespace
and ID only where a real publication/model authority owns that identity and its lifecycle.
Absent such an authority, refuse Message participation rather than assign material hash identity.
Native activities participating as workers for one genuine model report its canonical logical
recipient identity through that owner; treating every worker artifact as another business
definition would violate cardinality. An embedded model's owner must declare whether its
subscriptions belong to the enclosing authored definition or an independently published
definition with its own real authority. No arbitrary parent-ID inference is permitted. These
mappings require concrete source-owner review before implementation.

Resolve persistence scope, tenant and producer namespace from authenticated host/runtime
context. A request body cannot select another tenant or assert system/root authority. Rotated
credentials for one application can map to the same configured producer namespace; choosing
that namespace requires permission. A namespace derived from a transient access token would
break retries after credential rotation.

Global/null tenant attribution is not cross-scope access: the explicit persistence scope still
binds the operation. Shared publication material does not make a tenant's message public.
Across-scope maintenance uses existing privileged purposes and cannot act as ordinary submission.

## Message interests and the acceptance-time cohort

Propose a Runtime-owned interest projection contributed by publication/active-material owners.
An interest declares the logical definition, model owner, immutable material identity and the
finite Message names that executable can receive. Its authority comes from an actual authorized
publication or retained active execution, not from mutable Design records. Drafts and a definition
merely existing in the Design database do not establish a production Message interest.

A published receive-only definition remains eligible even when no Message wait is open and it
has no Message start trigger. This is required for early arrival: a workflow started through
another mechanism can reach its receive step later. Looking only at current bookmarks or start
bindings would silently exclude that definition. Interest projection and lifecycle therefore
need a separate explicit implementation within normal Runtime publication/material ownership.

Use a conservative static declaration contract for the initial implementation proposal: each
receive/start declares its fixed Message name at publication, or its model owner supplies an
explicit finite declared-name set. An expression choosing a name at runtime must be bounded by
that declared set and checked before subscription registration. Unbounded dynamic names cannot
silently imply interest in every historical message. Exact authoring representation and whether
the first implementation admits bounded alternatives remain engineering review; ordinary Event
name behavior is unchanged. Business keys may still be computed when a concrete wait opens;
an interest does not claim a particular key or reserve a recipient.

Current authoritative publications contribute their interests. An old pinned executable with
an active execution also continues contributing its declared interests after its publication is
replaced or retired; otherwise a still-running old version reaching its receive later would be
lost. Material retention and lifecycle owners maintain those roots. A superseded publication
with no active execution does not grant new-start authority. Test-run/private material must not
enroll a production definition in the cohort.

At acceptance, capture the complete authorized interest population for the submitted Message
name as an immutable logical definition cohort, consistently with acceptance/deduplication and
the actual scope. Traverse all finite pages before installing a complete result; an interrupted
or unavailable interest read cannot be interpreted as an empty or partial cohort. The provider
must prove a coherent publication/active-material revision boundary, using an atomic read/write
or equivalent validated snapshot. The exact mechanism is open engineering work, not an assumed
cross-store transaction. If composition cannot provide it, refuse before acceptance.

The accepted cohort never expands to a definition first published later, or to an existing
definition that had no eligible interest in this Message name at acceptance. A replacement
version inside the same frozen logical definition can supply a later matching subscription or
current authorized start for that same Message interest. Removing an interest does not erase
the accepted pending obligation: actual candidate eligibility still follows current publication
or active-execution authority, and an unselected obligation can remain pending until expiry.
An old version's future wait and a new version's wait still compete for the same one delivery.

Capture trusted scope and the sender's admission authority with the source. Cohort membership
does not bypass the recipient owner's normal authorization or current delivery policy, and
permission to inspect a definition is not permission to deliver to it. Shared/global material
may contribute under its normal ownership policy, but cannot import a different tenant's waits
or let later publication widen the frozen source authority.

## Acceptance, identity comparison and clocks

The proposed acceptance transaction validates input, authorization, supported physical
capabilities and capacity, then records the source, payload reference, immutable effective
policy, complete definition cohort and deduplication identity atomically. It also records durable
discovery/delivery work through the normal Runtime path. Immediate-only additionally completes
the acceptance-time selection/reservation described below within that boundary. Do not
acknowledge success between those writes. The durable receipt and separate delivery meaning
are selected Q1 behavior; this transaction mapping remains a proposal requiring provider proof.

For a supplied producer ID, compare a versioned semantic-content fingerprint under a unique
deduplication address. The transaction has three outcomes:

1. No live deduplication identity: accept one new message and its receipt.
2. Same live identity and same content: return the original accepted identity and original
   effective policy/receipt; expose its current delivery summary separately. Do not reset
   recipients, extend either clock or create new work under a fresh message identity.
3. Same live identity and different content: return an explicit conflict referencing only the
   caller's permitted original receipt. Preserve the original message and its pending work.

Fingerprint message name, exact key, durable payload and declared payload contract, sender
lifetime intent and other delivery-affecting request fields. Exclude tracing/request IDs and
access tokens. Preserve absent versus explicit null/empty values where their contract differs.
Use a reviewed canonical representation with a format version rather than depending on current
host JSON options. A retry that omitted its lifetime must be compared with the original omitted
intent before resolving today's default; configuration changes must not turn an identical retry
into changed content. Transport authorization is rechecked even for duplicate lookup.

`AcceptedAt` is the trusted persistence acceptance instant. Record `BufferUntil` and
`DeduplicateUntil` once from that instant and the accepted policy snapshot. A duplicate never
slides either deadline. Sender timestamps do not control retention. Propose finite durations,
checked for overflow and bounded by deployment policy; the default buffer duration is positive.
Default/maximum durations, payload limits, quotas and receipt retention need an explicit
configuration review and workload evidence; no numeric value here is approved.

Use the same authoritative clock domain for admission/expiry comparisons at a shared provider.
For positive-lifetime buffered messages define expiry as `now >= BufferUntil`; a boundary-time
acceptance race is serialized in storage. Immediate-only is an explicit acceptance-time mode,
not a pending buffered message immediately disqualified by `BufferUntil == AcceptedAt`.
Wall-clock adjustments and skew must not permit an expired deduplication row to be deleted
while another worker is using it. The concrete provider clock/transaction mechanism needs
qualification on every claimed topology.

A producer ID can outlive a deduplication window: after that window, the same ID may be accepted
as a new message even while the original has pending deliveries if buffer retention is longer.
That follows the independent windows and must be documented. Pending obligations from the
original retain their accepted-message identity and are never overwritten by ID reuse.

On cancellation or connection loss before the caller observes an answer, the caller cannot
infer rejection. Retry the same producer ID and content, or query the authorized receipt.
An ambiguous storage acknowledgement remains unresolved on that identity until reconciliation
reads committed evidence. It is never converted into a fresh accept or an assumed non-commit.
Without a producer ID, an uncertain retry may accept another message; document that limit.

## Discovery and per-definition selection

Discovery is resumable bounded Runtime work. Publication changes, bookmark registration,
authoritative admission release and recovery sweeps can wake it through existing scheduling
and outbox mechanisms. Durable pending state remains discoverable if a wake-up is lost.
Notifications improve latency; they are not the authority proving that work exists.

For each definition in the frozen acceptance-time cohort:

1. Read actual eligible subscriptions and ordinary publication/start authority in scope. A
   temporarily unavailable lookup means unresolved work, not an empty match set.
2. Apply the owner-selected arbitration rule. Q3 selects ready waits first, proposed ordering by
   registration time then stable exact subscription identity. Match name/key/authority and
   order without payload conversion filtering. Only when no wait is ready consider an eligible
   published start, if the business-key admission is free.
3. Capture the exact candidate and immutable payload/contract identity. Atomically install one
   per-definition winner by absent-to-selected CAS, revalidating deadline, subscription or
   publication authority and any start-admission condition at the owning physical boundary.
   For a one-shot wait, acquire an exclusive reservation of that exact subscription generation
   in this same boundary. That reservation is unique across all distinct accepted messages,
   not merely within one message/definition record.
4. Dispatch only the persisted winner. A loser of the per-message CAS rereads that message's
   existing winner; it never executes its discarded proposal. A different message losing the
   exclusive subscription reservation installs no winner and remains unselected/pending, so
   a later wait can receive it. It is not a permanently failed delivery to the lost wait.
   Record enough trusted source authority and material roots for replay.
5. Settle progress from authoritative recipient acceptance/consumption evidence. A queue
   enqueue or outbox transport acknowledgement alone is not proof that a receive was satisfied.

Concurrent workers must serialize the per-message winner and exclusive one-shot subscription
reservation with the exact bookmark generation and competing resume condition, rather than rely
on a previous lookup. The physical provider must prove that selection cannot commit for a wait
already cancelled/consumed and that two accepted messages cannot reserve or satisfy one one-shot
wait. The winner owns the reservation until its exact acceptance/consumption or closed failure
is authoritatively settled; a lapsed transport lease does not free it. If a provider cannot establish the actual
joint boundary, composition refuses before enabling this guarantee. Cross-database transactions
or process-local locks are not assumed.

Carry trusted accepted-message/per-definition delivery identity and the exclusive subscription
reservation owner plus monotonic fencing generation into target admission and semantic
consumption. The target owner's authoritative checkpoints must atomically compare those facts
with the still-valid reservation and source delivery capability, in addition to the physical
bookmark/subscription generation and its ordinary execution checks. A request, queued envelope
or captured bookmark DTO cannot supply its own authority. A source-side check performed before
enqueue alone is insufficient: a delayed old resume may already be queued when its source
delivery closes. The concrete typed condition, serialization and selected physical-store
capability remain a shared Runtime/model engineering proposal requiring owner coordination
and provider proof, not an existing supported field.

Record exact target acceptance and semantic consumption proof under the same durable delivery
identity. Once consumption is proved, retries resolve that proof rather than satisfy the wait
again. If acceptance or consumption acknowledgement is ambiguous, reconcile authoritative
source/target records before either redriving or attempting closure; neither a missing response
nor an expired transport lease proves unconsumed work. Cancellation/registration replacement
and delivery-reservation transitions must serialize with these target checkpoint conditions.

Only one winner exists for a message/definition, across versions, instances, named publication
slots and workers. Publication replacement before selection uses ordinary current authority.
After selection, retry keeps the same captured target/publication and follows existing retained
start authority; it must not switch to the newest version. A selected wait that disappears
cannot be replaced with another wait or start for that accepted message. Report its precise
terminal failure/unavailable outcome and retain proof; uncertain recipient acceptance is
reconciled before reporting failure. A new explicit business submission is a different message.

For multiple eligible starts in the same definition, propose refusing ambiguous configuration
with a visible diagnostic rather than choosing an arbitrary slot/version. Normal publication
preflight should prevent ambiguity where possible. Named slots remain valid for other purposes;
inbox delivery does not acquire authority to retire them.

One definition's outcome never consumes the source for another. Persist per-definition progress
so a crash after Fulfillment accepts and before Accounting accepts retries Accounting while
recognizing Fulfillment's existing winner. The overall record reports discovery-open plus
per-definition outcomes, including mixed outcomes. It cannot be called finally delivered while
another definition in the frozen cohort still has an unselected or selected obligation. A
definition outside that cohort never becomes a recipient of this accepted message.

For fairness, propose FIFO among eligible pending messages within one definition/business-key
lane by acceptance order with an immutable tie-breaker. A blocked key does not block other
keys/tenants/definitions. Ordering is selection order, not completion or external-effect order;
there is no global ordering guarantee. Bound discovery pages and use durable continuations so
a large fan-out or repeatedly unavailable definition cannot monopolize a worker. Starvation
controls must preserve the selected winner and cannot silently skip older eligible work based
on payload conversion. A reserved candidate is no longer available to another message; the
remaining eligible candidates are ordered using the same declared rule.

## Same-key message-started admission

The admission invariant covers only runs started by this Message feature. Ordinary API,
manual, timer, HTTP, Event or child starts remain governed by their existing owners and do not
acquire this admission unless the owner later explicitly expands scope. Any eligible existing
wait may receive a message according to the selected arbitration; receipt at a wait does not
turn that run into a message-started run.

Selection of a start atomically reserves the definition/business-key admission and the exact
target identity before a start can escape. Reservation must survive process death, including
the interval before an execution header exists. Record a monotonic fencing generation and
accepted-message/delivery owner. A second worker may redrive that reservation but cannot
grant the key to another message because a host lease expired or an execution lookup is absent.
Individual target start dispatch stays with the native/model owner and its normal authorization,
publication provenance, input validation and checkpoint protocol.

After authoritative start admission, keep the key occupied through Pending/Running/Suspended
and incidents that remain resumable. New messages may satisfy eligible waits without acquiring
another start reservation. Other unmatched messages remain buffered until expiry or release.

Release uses committed authoritative lifecycle evidence and conditional comparison of the
exact admission generation/target:

- Native execution: terminal Completed, Faulted or Cancelled committed by the existing Runtime
  checkpoint owner. Merely having an empty work queue or a missing execution is insufficient.
- Genuine model execution: model-owned terminal outcome plus completed settlement of its
  owed native work/result/cancellation responsibilities. Propose releasing at
  `TerminalSettled`, not `TerminalPendingSettlement`; the model owner must confirm this mapping.
- Pre-admission permanent refusal: release only after durable proof that this selected target
  was not admitted, its saved start capability is closed, and no delayed retry can create it.
  An ambiguous refusal/commit keeps the reservation until reconciled.

Record terminal/release intent with the authoritative native/model transition at its existing
atomic checkpoint boundary. Apply the release idempotently through the same Runtime source
composition, then wake pending discovery. Until a delayed release lands the key remains
occupied, sacrificing availability rather than permitting overlap. Recovery reconciles missed
terminal intents from retained state and sticky tombstones. Deleting a terminal execution must
retain sufficient release/retirement proof; absence is never such proof. No timeout-based
administrative unlock can permit a still-authorized old start to run later.

Faulted native runs are terminal under today's Runtime contract. Existing
[RetryFaultedActivity](../../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Alterations/Handlers/RetryFaultedActivityAlterationHandler.cs)
refuses a terminal workflow; it is not a mechanism for reviving that run after key release.
Recoverable incidents that leave a run nonterminal retain admission. Cancellation requested is
not cancellation completed.
The model mapping likewise distinguishes a requested stop from settled termination. Replaying
old terminal work cannot clear a newer admission generation. A buffered, unselected message
may start the next run after release; a message already selected/delivered in that definition
can never do so again.

## Payloads, failures and authorized recovery

The same durable payload representation and declared compatibility checks serve API submission,
workflow send, published starts and waits. Payload values use the dedicated stimulus/message
channel, not a merged workflow-input bag where data could spoof reserved routing or authority.
Do not persist live resources or resolve Secret references into broadly readable message data.
Protection metadata must follow the actual value, disclosure and retention owners.

Validate request shape, representability and policy limits before acceptance. A recipient's
deterministically incompatible contract is a per-definition failure; it does not reject already
accepted work for other definitions. Select the recipient solely by matching name/key/authority
and declared arbitration/order, then persist that exact winner before evaluating its payload
conversion. Record the compatibility result against the frozen target contract. A deterministic
conversion or target-contract error produces a visible permanent failure for that selected
recipient; it does not skip to a newer compatible wait or a published start. If every matching
wait is incompatible, the first selected wait still receives the recorded failed outcome for
this definition, with no business receive satisfaction and no fallback start. Other definitions
continue independently. This makes payload errors observable rather than letting payload shape
change who wins correlation. Quotas and basic durable-payload validation remain preacceptance
checks; they are not recipient selection filters.

Classify transient unavailability, permanent refusal, expiry and ambiguous acknowledgement
separately. Selected retains an open capability while its attempt is retry-scheduled, unavailable
or intervention-required. Authorized Retry redrives only that still-open winner after current
policy checks. Failed is a closed terminal outcome, including immutable payload incompatibility;
it cannot reopen or offer Retry. Correcting availability can recover an open attempt; changing
immutable payload/target requires a new explicit business submission/identity. Never reset a
delivered definition to pending. An operator may abandon
pending work through a durable audited transition, but cannot delete a deduplication identity
to bypass its window or unlock a potentially live start.

Final failure or authorized abandonment of a selected wait delivery closes that per-definition
source delivery authority and its exact target admission/consumption capability durably. Other
definitions' deliveries remain unaffected. Release an exclusive reservation on a still-live
wait only with committed proof that the old delivery is unconsumed and closed and that its
previously queued/retried target commands can no longer satisfy that wait. If target consumption
is uncertain, retain the reservation and report unresolved recovery; do not infer unconsumed
state from absence or failure of one lookup. Proved consumption remains delivered even when
its acknowledgement was lost and cannot be converted into abandonment.

Retain closed-delivery tombstones and reservation-generation/target proof through the actual
delayed-command, retry, execution/material deletion and recovery lifetimes. A later message may
reserve the same still-live bookmark registration with a newer delivery reservation generation.
An old command must then fail its original source capability and reservation owner/fence checks
at target admission and semantic consumption, even though the bookmark's physical registration
generation still matches. A stale release likewise cannot clear the newer reservation. The
per-definition failed/abandoned winner never becomes eligible to select another recipient.
Replay to a different recipient is an explicit new accepted business message, not recovery
disguised as retry. The concrete atomic close/release/target-fence mapping must qualify the
actual provider boundary; no cross-store fallback or timeout-based unlock is implied.

The proposed Delivered outcome means the owning recipient durably satisfied the selected
start/receive obligation. It does not mean the entire recipient workflow completed successfully.
Expose target acceptance, semantic consumption, later workflow state and failure as distinct
facts whenever the owner has separate boundaries. Never claim exactly-once external effects.

## Expiry, retention and backpressure

Buffer expiry closes eligibility for selecting new recipients. A winner committed before its
deadline remains owed and recoverable after it; otherwise a crash at expiry could erase an
already admitted operation. In-progress delivery deadlines/retry policy are separate bounded
operational settings and cannot quietly reinterpret an accepted obligation as unmatched.
Cleanup cannot remove its payload/material roots while the winner still needs them. Pending
unselected per-definition work expires visibly, while delivered/failed siblings retain theirs.

Immediate-only uses a special bounded acceptance path. Complete the entire acceptance-time
cohort read and candidate selection/reservation for each definition in the same authoritative
acceptance provider boundary as the source, receipt and deduplication identity. Persist the exact
selected recipients and explicit unmatched per-definition outcomes before returning the durable
receipt. A key held by an active run and no ready wait is Unmatched for immediate-only; it cannot
wait for the run to end. A candidate reservation lost to a concurrent message is reconsidered
within this bounded acceptance boundary or recorded Unmatched when no candidate remains.
There is no later unselected obligation and no delivery-discovery work that can choose a newer
listener after queue delay or restart. Normal asynchronous work only redrives exact recipients
already selected at acceptance, including their eventual compatibility/acceptance outcomes.

Discovery/interest/authority storage unavailable before commit causes an explicit retryable
rejection, with no accepted source or consumed producer ID; it is never Unmatched. A provider
unable to support the complete bounded boundary refuses this mode. An uncertain commit remains
unresolved and reconciles the same producer ID/content to the persisted original receipt,
including its original selected/unmatched outcomes. It must not perform a fresh immediate-only
selection at retry time. This implements selected Q1 acknowledgement without treating zero
lifetime as permission to lose accepted work or to rescan future listeners.

Buffer and deduplication cleanup are independent:

- Buffer expiry never deletes a still-live deduplication receipt or converts a duplicate into a
  new message. Delivery/run completion likewise does not end deduplication early.
- Deduplication expiry only releases the producer-ID address for future acceptance. It never
  deletes outstanding per-definition work or replaces the original source.
- Operator-history retention can exceed either window. Retain bounded safe terminal metadata
  independently from payload, with the actual retention limitation visible in inspection.
- Delete payload only after no pending/selected obligation or declared evidence/disclosure
  retention requires it. Material collection honors actual source and recipient roots across
  scopes; missing/unreadable authority fails closed, following existing collection contracts.

Admission quotas cover accepted sources, outstanding recipient obligations, payload bytes and
reserved deduplication/history capacity within each scope/producer policy. Enforce admission
and capacity reservation atomically with acceptance; a preflight count alone races. At capacity,
return an explicit retryable refusal with safe diagnostics and no new consumed producer ID or
receipt. A duplicate of already accepted content remains resolvable without reserving another
payload or delivery. Reserve the bounded frozen cohort's fan-out obligations at acceptance;
do not accept unlimited future obligations and later evict old ones to make room. Cleanup,
throttling and backoff use normal Runtime work and provider policies.

## Inspection and permission boundaries

Separate submit, list/inspect metadata, reveal payload and recover/manage permissions. Scope
every lookup before disclosing whether an identity exists. A caller cannot probe another
producer's IDs through duplicate/conflict responses. Authorized receipt access may expose the
caller-permitted per-definition summary; it does not automatically expose every target's private
definition name, execution details, payload or current authority. Normal resource authorization
still applies when following target links.

Inspection should show effective acceptance/buffer/deduplication times, discovery-open/closed,
per-definition pending/selected/delivered/expired/failed outcomes, retry availability and safe
reason codes. Distinguish Not run/Unavailable evidence from a confirmed absence or expiry.
Payload reveal is an explicit authorized action; ordinary logs, metrics, URLs and exception
messages exclude business keys, message IDs, payloads, credentials and secret values unless
their owning disclosure policy deliberately permits them. Correlation hashes can still identify
sensitive data and must not be treated as public merely because they are hashed.

Workflow sends execute under the existing immutable runtime authority and current permission
policy. Stage the send intent with its owning workflow checkpoint; retry then uses that exact
stable invocation/send identity and original message content. Applications and authored activities
reach the same acceptance service/source authority. No activity-specific shortcut bypasses
capacity, deduplication, tenant checks or durable acknowledgement.

## Open engineering decisions before implementation

These are review items for the implementation plan and owning engineers, not additional product
questions for the owner unless evidence reveals a material change to selected behavior.

- Choose finite buffer/deduplication defaults and bounds, zero-window semantics, payload/name/key/ID
  limits, frozen-cohort/fan-out limits, quotas and history/payload retention. Justify values from
  expected workloads and failure recovery, validate overflow and expose effective configuration.
- Specify the Runtime interest declaration wire/version, static/bounded-name authoring rules,
  current-publication and retained-active-material projection lifecycle, consistent cohort snapshot
  and atomic immediate-only acceptance/reservation mapping for each supported provider.
- Fix model-neutral authored logical definition provenance and standalone owner-defined
  namespaces, including native, genuine BPMN, embedded model and native-worker subscriptions;
  map genuine model terminal settlement with its owner. Preserve logical identity through
  explicit model migrations rather than treating migration as historical fan-out.
- Define stable authorized producer namespaces, credential-rotation mapping, duplicate receipt
  disclosure, canonical request fingerprint version, clock domain and exact deduplication
  boundary races. Omitted lifetime retries retain the original policy snapshot.
- Define persisted winner/exclusive subscription reservation/start admission participants,
  source/target capability and generation checks at admission/consumption, exact completion and
  unconsumed closed-failure/release proofs, material roots and cleanup/deletion tombstones.
  Qualify these through physical and coalescing boundaries and old-writer/version gates.
- Finalize bounded discovery ordering/fairness, multi-start ambiguity diagnostics, transient versus
  permanent error codes, retry/abandon permissions, recipient compatibility evidence and safe
  operator summaries. Reserve enough capacity before promising the complete frozen cohort.
- Coordinate the standalone source-family representation with #2892/#3815, API/activity contracts,
  package boundaries and the actual provider/multi-node qualification matrix. No implementation
  may infer adoption or availability from this document.

## Adoption and proof boundary

Before implementing this proposal, review the concrete standalone
source-family/participant/capability mapping with the shared Runtime and model owners.
Do not amend frozen old source fingerprints, weaken exact bookmark admission or enable an
unsupported provider fallback. Unknown persisted versions and mixed physical compositions must
refuse safely. Qualify Balanced/coalescing behavior without changing the host default.

Required causal proofs include concurrent CAS losers, same-key starts across workers/versions,
wait-versus-start races, exact bookmark cancellation/replacement, pre-header reservation death,
target commit before source acknowledgement, multi-definition partial completion, publication
replacement, terminal release replay, deletion/tombstone retention, uncertain commit, changed
content IDs, independent window expiry and quota races. Include a selected delivery closed as
provably unconsumed, a new message reserving the same still-live bookmark generation, and the
old queued resume/release arriving after restart: target checks must refuse the old command
and preserve the new reservation. Use real composed hosts, process death,
actual durable providers and the model/native boundaries being claimed. Tests of a new in-memory
dictionary or mocked dispatcher cannot establish these guarantees. API, send/receive authoring
and operator inspection also require composed Studio acceptance and independent inspection.

This document records a candidate design and its unresolved contracts. It is not evidence that
any acceptance criterion, provider topology, product journey or protected delivery gate passed.

## Native sender outcome and scoped execution clarification

The source-to-message binding is the accepted branch of a create-only native SourceOutcome:
Accepted(original receipt and duplicate result), PermanentlyRejected(safe reason) or
Cancelled(authoritative sender cancellation proof). A definitive
post-checkpoint refusal records rejection and an exact private continuation even though no inbox
message exists. The callback guards against that source outcome, consumes the sender wait and
faults the same send invocation atomically. Temporary/unknown failures cannot become rejection.
A rejected source never later accepts after authority changes; a new invocation is a new source.
Continue owed callbacks until consumed or authoritative sender retirement; missing state is not
retirement. Source proof survives independent producer windows and source/terminal collection.

Owner Q4 requires cancellation and acceptance to serialize through one retained sender control
inside their actual transactions. The first send checkpoint provisions it and reserves outcome/
continuation capacity. Cancellation seals that control under the sender checkpoint fence, without
enumerating source history or taking transport claims. New finalization locks/rereads the control;
sealed derives Cancelled without acceptance/callback, while existing Accepted remains immutable.
Callbacks consume or retire using the same control and exact target proof. Retain the cancellation
proof beyond sender deletion. Before the original send checkpoint, cancellation discards staging.
A request, missing sender or lost acknowledgement cannot decide the race. B32 qualifies both
orders, pre-reserved rejection capacity and stale claims through real checkpoint boundaries.

Production business identity is persistence scope + tenant + canonical definition namespace/ID.
Finite test scopes are isolated; source run-kind/partition are retained and validated authority,
not ways to mint extra production recipients/admission lanes. Default partition derives from
PersistenceScope. Unsupported host partition/run-kind mappings refuse before acceptance.
ReceiveMessage start mode captures the admitted name/key; wait mode evaluates its expected key.
Both expose exact name/key, payload and receipt reference so later waits can bind the key without
requiring it to be duplicated in payload or inferred from execution CorrelationId.
