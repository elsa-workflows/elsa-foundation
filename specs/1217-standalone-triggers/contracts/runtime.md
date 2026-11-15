# Runtime ingress admission and delivery contract

**Status:** Proposed, implementation-ready engineering contract for review. No implementation, compatibility or runtime qualification is claimed.
**Feature:** [#3582](https://github.com/elsa-workflows/elsa-foundation/issues/3582), spec 1217.
**Source base:** `e0ba87ecfc3b1e774c509ed274b19cd3f34a1124`. Reviewed 6 November 2026.
**Dependencies:** [architecture and target/input contract](../architecture.md), [current-source research](../research.md). The CLR names below are proposed names, not already published APIs.

## Product rules and physical boundary

Subscriptions follow their selected serving publication slots at **fresh admission**. Acceptance captures exact material, publication, declared inputs and authority. Later pause, unpublication, association edits or invocation-grant revocation do not rematch or revoke accepted work. Explicit cancel-pending is a separate operation. Current Runtime start-policy refusal remains an observable failure under the architecture contract.

Async HTTP admission policy is stored per trigger revision:

| Policy | Acceptance rule |
|---|---|
| `RequireAll` (default) | Evaluate the entire selected subscription set before effects. Any refused recipient prevents acceptance of every recipient; create no delivery source and start no workflow. |
| `AcceptAvailable` (explicit) | Evaluate the entire set, accept eligible recipients and retain each refused subscription and reason in the same admission receipt. At least one recipient must be accepted. |

Zero accepted recipients fails under either policy. Empty subscriptions, all disabled/incompatible/unavailable targets and all denied invocation grants cannot produce a successful acceptance. An unreadable/corrupt source, tenant scope, publication store or required material boundary is a failure of admission itself, not evidence that a recipient was safely evaluated and refused. `AcceptAvailable` cannot conceal such a failure by treating unknown recipients as absent. Partial admission means a complete evaluated set with explicit accepted/refused entries; it does not mean a truncated query.

One Runtime-owned source is the durable responsibility for accepted fan-out. Use the **existing Runtime outbox family, ordinary exact-kind dispatcher, scoped resumption sweep, actor command transport and execution/model queues**. There is no new API queue, fake workflow publisher or additional delivery pump. A `DispatchCommitted` recipient seal in that source resolves cancellation before enqueue; it is not a claim that execution work has already been queued or started.

## 1. Compatible CLR and wire evolution

### Keep the workflow intent unchanged; add a real source arm

Do not add an optional workflow ID to a generic intent or manufacture a workflow ID for external ingress. `RuntimePostCommitIntent` and its existing constructors, property order, JSON names, fingerprint format and validation remain workflow-specific and unchanged.

Evolve `RuntimePostCommitOutboxItem` with **three optional trailing properties**, omitted when absent:

```csharp
public RuntimePostCommitIntent? Intent { get; } // same CLR type; nullable annotation becomes accurate

[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public RuntimeStandaloneIngressSource? StandaloneIngress { get; }

[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public int? SubscriptionDeliveryProtocolVersion { get; }

[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public RuntimeSubscriptionDelivery? SubscriptionDelivery { get; }

[JsonIgnore]
public RuntimeDeliverySourceKind SourceKind { get; }
```

Add `WhenWritingNull` to `Intent`: it is always nonnull for existing workflow rows, so their bytes do not change. Keep every current constructor signature; current constructors still require a nonnull workflow intent and delegate to an expanded common constructor. The new `[JsonConstructor]` accepts the optional arm/protocol/progress at the end, while the former JSON constructor remains a callable CLR overload. A dedicated `CreateStandaloneIngress(...)` factory constructs the new branch without an intent. JSON accepts **exactly one** of `Intent` and `StandaloneIngress`. Null/both/unknown source arms refuse. All other existing item properties keep their order and serialization rules; new properties follow them.

`SubscriptionDelivery` is shared by workflow-origin Event sources and standalone ingress. Its immutable selection contains the accepted/refused standalone subscription entries; its mutable progress holds dispatch seals and receipts. It does not replace the existing `RecipientSnapshot` for embedded starts and exact waits.

| Item branch | Required shape |
|---|---|
| Legacy workflow | `Intent != null`; no `StandaloneIngress`; optional existing stimulus protocol/snapshot only; new subscription members absent. Existing JSON/fingerprint is unchanged. |
| Workflow with subscription recipients | Actual `Intent` and existing workflow/stimulus source scope; `SubscriptionDeliveryProtocolVersion = 1`; subscription selection initially absent until original complete cohort selection commits. Actual workflow/checkpoint provenance remains authoritative. |
| Standalone ingress v1 | `Intent == null`; `StandaloneIngress.Version = 1`; subscription protocol v1 and complete `SubscriptionDelivery` present at source creation; legacy stimulus protocol/snapshot absent. No workflow publisher or checkpoint is implied. |

Computed source-neutral accessors (`DeliveryKind`, `LogicalIntentId`, `SourceId`, `SourceFingerprint`, `RecordedAt`, `SourceScope`) are `[JsonIgnore]` views over these arms. Generic outbox code uses them. Existing workflow handlers receive their actual nonnull workflow intent through a checked workflow-only accessor. Merely suppressing nullable warnings at old `item.Intent` call sites is not a source-dispatch implementation.

Do not upgrade old workflow JSON when it is read, renewed, retried or completed. Checked rewrites copy optional new properties from the **current physical row**, so progress cannot be lost by a claim/diagnostic rewrite. Legacy workflow test vectors must compare serialized UTF-8 bytes and existing intent/checkpoint fingerprints before and after this change. New workflow Event sources explicitly opting into subscription delivery may carry the new optional members; old sources never acquire today's subscribers on replay.

### Concrete v1 data shape

These are immutable values unless explicitly listed as progress:

```text
RuntimeStandaloneIngressSource
  Version = 1
  AdmissionId, DeliveryKind = "Elsa.StandaloneTrigger.Dispatch"
  LogicalIntentId, RecordedAtUtc
  PersistenceScope, TenantId, Partition
  TriggerId, TriggerRevision, AssociationGeneration
  SourceFamily, SourceKey, RequestFingerprint
  PayloadSchema, CanonicalPayload
  AdmissionPolicy, SourceActorAttribution

RuntimeSubscriptionDelivery
  Version = 1
  SelectionFingerprint
  Selection[]                 // complete evaluated set, sorted by SubscriptionId ordinal
    AcceptedRecipient         // or RefusedRecipient, with safe typed reason
  Progress[]                  // accepted recipients only, same identity set/order

AcceptedRecipient
  RecipientId, SubscriptionId, SubscriptionRevision, GrantRevision
  LogicalDefinitionId, ModelKey, SlotId, SlotRevision, ActivationId
  CompleteMaterialPin, SourceProvenance
  ModelProtocol/Profile/Incarnation when model-owned
  InputContractFingerprint, MappingRevision/Version, NormalizedDeclaredInputs
  CapturedExecutionAuthority, Scope/Tenant/Partition
  DeterministicStartIdentity  // execution/command/envelope/model-input identities

RecipientProgress
  RecipientId
  Phase = Pending | DispatchCommitted | TargetAccepted | CancelledBeforeDispatch
  DispatchSeal?              // immutable once installed
  TargetReceipt?             // immutable accepted identity, monotonically enriched observations
  AttemptCount, RetryAt?, LastSafeFailure?, RecoveryState
```

`RecoveryState` is `None`, `RetryWaiting`, `BlockedPolicy`, `BlockedCapability`, `FailedFinal` or `TargetPoisoned`; it does not replace the monotonic recipient phase. A failure record has a stable code, safe parameters, observed time and retryability. Clearing an operational failure cannot clear a seal or change its command. Backoff belongs to the ordinary outbox retry policy; the handler skips TargetAccepted/CancelledBeforeDispatch entries and does not use subscriber order as orchestration.

`RefusedRecipient` includes subscription identity/revision, target descriptor visible under the caller's permissions, rejection stage and stable safe reason. It cannot contain a falsely accepted target pin. Scope, authority, pins and normalized inputs are produced/validated server-side; request JSON cannot assert them. All complete IDs are compared, not only their digests. Canonical payload/input JSON is cloned and round-tripped before acceptance; malformed/unrepresentable identity or wire refuses before a source exists.

`RequestFingerprint` hashes only immutable **submitted intent** after provider normalization: trusted persistence scope and exact tenant discriminator, source family/key, trigger, canonical payload/schema, source actor attribution and explicitly requested options. The tenant discriminator explicitly frames absence separately from a named string; an absent tenant and a tenant named `null` cannot share a key or fingerprint. It excludes current time, resolved slot publication and mutable delivery state. This allows identical retries to return the original acceptance after slot/configuration changes. An authorized request with the same scoped source key but different submitted intent conflicts.

`SourceFingerprint` is the canonical versioned fingerprint of the actual immutable source arm. Workflow sources retain their existing `RuntimeCheckpointCommitFingerprint.ComputeIntent` result. Standalone v1 uses a different domain/version and canonical source encoding. `SelectionFingerprint` separately hashes the complete immutable accepted/refused set, mapping/inputs/pins and authority. Progress, claim owner, attempt times and diagnostic text never alter either fingerprint. New authority carries both source and selection fingerprints; it never changes the legacy workflow fingerprint algorithm.

### Physical projections and schema readiness

Extend `elsa_runtime_post_commit_outbox`; do not create another delivery table:

- Preserve row identity, scope, delivery timing, kind, status, fencing, revision and content columns.
- Make workflow owner/hash/order projections nullable for external rows. Existing workflow projection values and existing row IDs remain identical.
- Add nullable `SourceKind`/source-ID and resource/source-key lookup projections. Null `SourceKind` means the legacy workflow branch. External rows require explicit standalone kind and complete source projections. Validate projections against canonical content on every checked read.
- Derive a bounded standalone `OutboxItemId` from a versioned digest of the complete `(persistence scope, exact tenant discriminator, trigger ID, source family, source key)` tuple, with domain distinct from checkpoint outbox IDs. Canonical framing distinguishes null tenant from every named tenant string. This existing unique row key arbitrates source replay; hash collisions compare the full tuple/content and refuse. No extra source-key ledger is needed.
- Keep old workflow identity/fingerprint formats frozen. New standalone rows and subscription-protocol rows require a schema-family write version/readiness that admits these arms. Unknown versions, old selected processor/store/collector/writer support or a split selected family refuse admission and activation. A nullable database migration alone is not old-reader compatibility.

Update the owning schema chain, gated writer, checked reader, row rewriter and four relational migrations. Legacy absent optional fields remain absent on ordinary writes. Do not fabricate subscription selections when upcasting old rows. Final schema version allocation follows the actual chain at implementation time; current inspected outbox family is `2.0.0`.

## 2. Normal composition and default refusal

### Public producer-bound module submission

The external module submission seam is **public** and distinct from the internal admission/capture service and provider replacement writer:

```csharp
public sealed record StandaloneTriggerSignal(
    string TriggerId,
    string SourceKey,
    ValueTypeDescriptor PayloadSchema,
    JsonElement Payload);

public interface IStandaloneTriggerIngress<TSource> where TSource : class
{
    ValueTask<StandaloneTriggerIngressResult> AdmitAsync(
        StandaloneTriggerSignal signal,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<StandaloneTriggerIngressResult>(
            new NotSupportedException("Standalone trigger ingress is not supported by this composition."));
}
```

Use the existing `ValueTypeDescriptor` for the detached schema identity/version. The detached signal has exactly resource identity, stable producer source key, payload schema and cloned payload. It has **no caller-supplied tenant/scope, producer identity, principal/grant, target/material, mapping, recipient capture, source/selection fingerprint, claim, fence or seal**. The public result exposes sanitized Accepted/PartiallyAccepted/Refused/Unavailable outcomes, admission ID and replay flag where an accepted receipt exists, and permitted per-subscription acceptance/refusal diagnostics. It does not expose an internal authority/capture object. Unsupported composition throws before any source/target effect; provider unavailability cannot become an empty or accepted result.

Normal trusted module registration binds `TSource` to one stable producer ID, trigger type ID and registered source authorization policy, for example the proposed `AddStandaloneTriggerSource<TSource>(producerId, triggerTypeId, sourcePolicy)` registration. The separately registered trigger-type descriptor supplies configuration editor metadata and configuration/payload schema validation. A descriptor alone neither registers a producer nor grants submission permission. Runtime rejects conflicting duplicate producer IDs, source CLR types, descriptor ownership, type/schema bindings or authorization policies at startup; identical repeated registration may be idempotent. Persisted identity uses the stable producer/type IDs, not CLR type names.

The implemented registration is `AddStandaloneTriggerIngress<TSource>(descriptor)`: `SourceFamily` is the unique stable producer ID, and the descriptor is keyed by `typeof(TSource)`. `IStandaloneTriggerIngressFactory.Create<TSource>()` takes no caller-selected descriptor or context. The scoped context explicitly implements `SupportsResourceAuthorization` and `AuthorizeAsync(StandaloneTrigger,StandaloneTriggerSignal,ct)`; both default to unsupported/refusal. Installed capability metadata reads policy support without invoking an HTTP/occurrence callback. Canonical workflow PublishEvent contributes its own capability requiring the actual combined-cohort provider, rather than pretending to be an external source. The producer result is a detached `StandaloneTriggerIngressResult`, while physical source/material/seal data remains on the Runtime provider path.

The registered source module obtains `IStandaloneTriggerIngress<TSource>` through normal scoped DI and calls it from its ordinary authorized transport/event/task callback. The implementation binds the registered producer and obtains persistence/tenant scope and source actor attribution from the trusted **normal source execution context** for that callback. It never resolves authority from signal JSON or a user-selected producer string. Missing/expired/ambiguous source context refuses; retaining a port object cannot retain a prior caller's authority across scopes. Scheduled/system sources use their registered server-owned context and source policy. This is ordinary trusted application code and normal feature/permission composition, with no new secret token or possession-based capability product.

For every submission Runtime validates the current source context, registered source authorization and resource-level source policy, then checks that the resource belongs to that scope and names the registered producer/type and permitted schema. These checks precede exposing an existing receipt or preparing new captures. A module cannot submit through one producer's port to another producer's resource merely because it knows its ID. After these checks, the **internal** admission service reads the resource/subscriptions, evaluates grants and slot targets, normalizes/maps/validates declared inputs and asks the selected physical writer to admit its checked capture. The module does not call that capture writer.

Do not expose a generic browser management “invoke any trigger” endpoint as this seam. First-party HTTP/external Event/schedule adapters and a separate compiled consumer module use the same public producer-bound port; workflow-origin PublishEvent keeps its actual canonical checkpoint source and cohort path. Browser resource management remains under its normal CRUD/control permissions. The independent T047 consumer must register its descriptor and source owner, obtain this public port, submit a real signal and observe persisted admission/delivery/input behavior without internal constructors, reflection, IVT or direct store writes. Negative consumer cases cover wrong resource/type/schema, missing/denied source context, conflicting registration and unsupported backend.

### Physical provider replacement and ordinary delivery

Add a small Runtime Core provider contract, registered as part of `RuntimePostCommitOutboxStoreBackend` and implemented by the **same selected physical outbox provider**:

```csharp
public interface IRuntimeIngressDeliveryStore
{
    bool SupportsStandaloneIngress => false;
    ValueTask<RuntimeIngressAdmissionReceipt> AdmitAsync(
        RuntimeIngressAdmission request, CancellationToken cancellationToken = default);
    ValueTask<RuntimeIngressAdmissionReceipt?> FindAdmissionAsync(
        RuntimeIngressAdmissionKey key, CancellationToken cancellationToken = default);
    ValueTask<RuntimeDispatchSealResult> CommitDispatchAsync(
        RuntimeDeliveryClaim source, string recipientId, CancellationToken cancellationToken = default);
    ValueTask<RuntimeRecipientReceiptResult> RecordTargetReceiptAsync(
        RuntimeDeliveryClaim source, RuntimeTargetAcceptanceReceipt receipt,
        CancellationToken cancellationToken = default);
    ValueTask<RuntimeCancelPendingResult> CancelPendingAsync(
        RuntimeCancelPending request, CancellationToken cancellationToken = default);
    ValueTask<RuntimeCommittedRecipient> ReadCommittedRecipientAsync(
        RuntimeCommittedRecipientAuthority authority, CancellationToken cancellationToken = default);
}
```

Default implementations of additive operations throw `NotSupportedException` before effects; source compatibility must never supply an empty result or process-local fallback. `ReadCommittedRecipientAsync` also serves sealed subscription recipients on an actual workflow Event source. Providers expose exact physical lookup, not a working-set overlay. APIs call an internal admission service, which supplies trusted evaluation/capture collaborators; they cannot construct an arbitrary admitted object and ask a store to persist it unchecked.

This provider replacement contract is not the public module submission port. A source module consumes only the producer-bound port above; backend implementations alone participate in the checked capture/retention/control writer family.

Extend the existing processor and dispatcher with `SupportsSourceKind(kind)` (legacy default: workflow-only) and default-false `SupportsSubscriptionDelivery(sourceKind, deliveryKind, protocolVersion)`. Fresh activation/admission requires both selected instances to support the exact subscription-delivery contribution and current protocol; workflow-kind support alone cannot qualify Event subscription cohorts. The built-in processor checks its actual claim, lookup and scope support as well as its selected dispatcher and contributed kind. Saved acceptance/reconciliation keeps its original responsibility when current pipeline support is removed. The selected collector, material/reference/root lease and resource/slot persistence boundaries must also expose actual compatible capabilities. Validate actual service instances/decorators and context/scope ownership, not just registration names. Existing in-memory/EF registration replaces or owns the whole new family together; it cannot leave an old outbox collector or processor behind.

Use the existing `RuntimePostCommitIntentHandlerContribution` collection/dispatcher for source-neutral delivery. Add a source-kind-aware contribution overload and `IRuntimeDeliverySourceExecutor.HandleAsync(RuntimeDeliverySourceContext, ...)`. The standalone exact kind uses this interface; existing workflow handlers/constructors and `IRuntimePostCommitDurableSourceIntentHandler` remain unchanged. One ordinal kind still has one owner; contradictory source-family, handler or retry contributions fail startup. There is no second handler registry. A source-neutral context is created from the actual physical row/claim/scope and validates its source arm; it never calls the checkpoint-parsing `PostCommitDeliveryContext` constructor for an ingress row.

The existing resumption sweep already processes all due outbox kinds. Its source-aware built-in processor delivers standalone rows through the same claim/renew-before-dispatch/retry/completion machinery. Workflow-specific live-drain filters, cleanup, dispatcher projection and diagnostic formatting explicitly use only the workflow arm. External rows are not discovered as fabricated executions.

## 3. Admission, replay and authority capture

Admission has one linearization point: the physical commit that inserts the accepted source and immutable complete selection under the checked resource/association/publication state.

1. Authenticate/authorize the source request and resource; obtain the server persistence/tenant scope. Read an existing physical source by scoped key first. An existing authorized identical request returns its original receipt without reevaluating today's targets; changed intent conflicts. An unauthorized request cannot use a key as read authority.
2. Read the complete selected subscription set and trigger revision; evaluate current invocation grants, model/ordinary-entry capability, mapping and declared inputs against each slot's current publication. Record every evaluated acceptance/refusal in deterministic order. Disable/pause/removal and incompatible inputs are visible conditions. HTTP applies its selected RequireAll/AcceptAvailable policy. Event/schedule use a fixed independent admission rule: accept eligible recipients and record refusals without holding healthy siblings. Zero accepted refuses; a source-wide unknown evaluation cannot be treated as partial success.
3. Prepare a canonical complete recipient capture and deterministic start identities. Protect the accepted material/dependency root set with existing fenced root-write leases. Revalidate the resource revision, association generation, target slot/reference selection and grant/authorization witness under the provider's authoritative admission compare/fence boundary. A failed compare retries boundedly from evaluation; a failed admission has no dispatch effects.
4. In one provider-owned physical commit, insert the standalone source, selection, Pending recipient progress and normal Pending outbox state. An identical-key winner is read back and returned; a competing source with different request content conflicts. Return only the **persisted winner**. A lost/ambiguous write acknowledgment requires physical exact lookup, never a fabricated success.

Publication replacement and admission must share the authoritative slot/reference fence or transactional comparison. An ordinary read followed by source insertion in an unrelated transaction does not meet this contract. For an in-flight schedule, resource-owned occurrence claim/revision is also part of the compare. Normal permission evaluation may require an external identity store; capture its trusted result/witness and perform the architecture contract's current-grant check, without pretending there is a transaction across identity providers. Revocation after durable acceptance affects fresh admissions.

Existing workflow PublishEvent sources keep their actual outbox ID and intent fingerprint. New producers explicitly declare subscription protocol v1 at their real checkpoint. On first delivery, atomically select/install their existing wait/embedded-start snapshot **and** standalone subscription selection under one root/reference/source acceptance operation. Present empty sets are final. A competing install returns the original complete winner. Old sources with no declaration retain old routing; do not add a cohort based on subscriptions created after their original acceptance.

For that workflow-origin path, committing the producer's PublishEvent intent records an owed event send; accepting the subscription cohort is the later physical recipient-selection/retention commit. The two times are distinct operator facts. Slot-following and the guarantee to retain an exact accepted target version apply at that cohort commit, before any recipient effect. A producer checkpoint without a selected cohort must not be described as already having accepted a particular subscriber/version.

`RequireAll` and `AcceptAvailable` are HTTP **admission** policies. They do not create transactional business execution or couple recovery after acceptance. A recipient that becomes temporarily unavailable after acceptance remains an independent owed delivery; neither policy rolls back already accepted siblings.

### Schedule occurrence with no admitted recipients

The owner confirmed that a schedule with zero eligible subscribers retains **one overdue occurrence with backoff until it can be admitted**. The cursor does not advance on evaluation, known refusal, store unavailability or a source key alone. There is no accepted empty delivery source and no refusal tombstone that would prevent a later reevaluation of the same occurrence.

| Pre-admission observation | Schedule action and diagnostic |
|---|---|
| Complete evaluation proves zero eligible recipients | Keep the same occurrence/cursor, record KnownZeroEligible with its safe complete refusal/disabled/unpublished reasons, release/renew the existing schedule claim with backoff and retry later. No source/target start is accepted. |
| Eligibility/source/publication/input/grant store could not be read or validated | Keep the same occurrence/cursor and backoff; report Unavailable/UnknownEligibility with the dependency cause. Do not represent unknown results as known zero or successful evaluation. |
| Policy admits at least one recipient and the physical source commit succeeds | Freeze the complete accepted/refused set and its exact pins/inputs. The persisted admission receipt now permits settlement of that occurrence under the existing schedule fence. |

Each retry **before actual admission** reads the then-current subscriptions, serving slots, mappings and grants; a later enabled/repaired subscriber can make the held occurrence eligible. Source identity remains the stable resource-owned occurrence, not an artifact or a new attempt ID. Schedule/resource revision changes fence old claimants; the owning schedule update contract decides the surviving occurrence without fabricating acceptance or silently advancing an unadmitted cursor.

Once an occurrence has an accepted source, retries/restarts look up that source before rematching. An identical accepted receipt after a lost acknowledgment permits schedule settlement; the source's ordinary delivery owns the accepted recipients even if the current resource is paused or targets changed. Event/schedule's fixed independent admission records refused entries in that original full evaluation; they do **not** join later when repaired, and new subscriptions are not added. Accepted recipients that already reached their target are not rerun. There is no configurable RequireAll schedule mode.

Prefer source insertion plus occurrence settlement in one qualified provider transaction. Where the existing provider has separate commits, use the same deterministic occurrence key and exact winning receipt to converge: crash after source admission but before cursor settlement rediscovers that source and settles without another cohort/start. Crash before source admission leaves the same overdue cursor and no successful receipt. Do not advance merely because a submission was attempted or its outcome is unknown. Use the existing recurring schedule pump/claim release/backoff and the ordinary outbox delivery processor, without another delivery loop.

## 4. Recipient identity and DispatchCommitted seal

Define a new identity domain/version for standalone subscription starts. Hash a length-framed complete `(source scope, source ID, subscription ID)` tuple. Include neither artifact ID nor current configuration revision in the execution identity: the source already freezes its exact selection. Different sources, subscriptions or slots cannot collapse merely because they share material. Persist execution, command, envelope and model input IDs in the accepted capture. Frozen `KeyedWorkflowStartIdentity` v1/v2 formats remain unchanged; new dispatcher support explicitly recognizes the new derivation.

Before queue enqueue, the owning handler invokes `CommitDispatchAsync` for the recipient under its current source claim. The provider rereads the physical source, validates its scope/fingerprints, current owner/fence/expiry and exact immutable selected recipient, then atomically applies one of:

| Current recipient phase | Result |
|---|---|
| `Pending` | Install immutable `DispatchSeal`, change to `DispatchCommitted`, increment source revision and commit. Return persisted seal. |
| `DispatchCommitted` | Return the existing identical seal. No new command identity. |
| `TargetAccepted` | Return existing receipt/observation; no new start. |
| `CancelledBeforeDispatch` | Return cancelled; enqueue is forbidden. |
| Missing/different selection, scope, corrupt state or stale source claim | Refuse before enqueue; preserve actual source and cause. |

The seal records source/selection fingerprints, recipient and deterministic start identities, full canonical root command body/fingerprint, `CommittedAt`, and command protocol version. It contains captured authority, inputs, pin and publication attribution, but **no expiring outbox claim token in the command's immutable authority**. Root command time is fixed at seal time; retries cannot regenerate a different envelope time, payload or idempotency identity. Prepare/round-trip/check the canonical command against the accepted recipient before the seal commit. Diagnostic attempts remain outside the command fingerprint.

The existing start dispatcher gains an additive accepted-recipient operation with default unsupported behavior. It reads the committed recipient/seal, constructs/sends the **ordinary native Start or model ExecuteModel command through its normal actor**, and preserves the sealed command's complete identity/timestamps. Callers cannot substitute inputs, a trigger node, parent scope, model protocol, target pin or root attribution. Native starts use a null trigger node; genuine models use their model-owned ordinary entry adapter. Preserve normal start policy and input validation. Do not write directly to a private queue from the trigger API.

Validate target deterministic-acceptance/readiness and prepare the current-policy/input-checked command before sealing where possible. If a capability or policy changes after sealing, the seal is still irrevocable responsibility: record BlockedCapability/BlockedPolicy (or the configured final delivery failure), retain it and require recovery; do not claim target acceptance, discard the source or make another target eligible. This is an observable delivery failure, not lost work. The module capability must default-refuse before initial admission when it cannot support deterministic target acceptance and physical receipt/recovery at all.

The seal is durable Runtime responsibility for delivering that exact command. Queue transport is still at least once; deterministic queue/target admission converges repeats. After the seal commits, any delivery attempt can be interrupted before enqueue, during enqueue, after enqueue or before receipt recording without losing responsibility: the same source remains recoverable through its existing outbox retry/claim machinery. A sealed recipient is never reset to Pending, remapped or assigned another execution ID.

## 5. Cancellation linearization and its limits

`CancelPendingAsync` is an authorized control operation, independent of an outbox handler claim. In one physical source row CAS/transaction it rereads current selection/progress and changes only `Pending → CancelledBeforeDispatch`. It leaves DispatchCommitted/TargetAccepted entries intact and returns a per-recipient outcome distinguishing cancelled, already cancelled, dispatch committed and target accepted. It records control request identity, actor and time for idempotent replay. A changed control request under the same identity conflicts.

Cancellation and `CommitDispatchAsync` serialize on the **same physical source recipient state**:

- Cancel wins: the current phase is CancelledBeforeDispatch; no later seal or command enqueue is authorized. A worker holding an earlier Pending snapshot must reread and refuse.
- Seal wins: cancellation reports DispatchCommitted even if no queue work exists yet. The source still owes enqueue/reconciliation, and the command may subsequently start. Cancel-pending does not revoke the seal or cancel that workflow.

No claim is made that sealing is queueing. Operator text must say “dispatch committed; awaiting target acceptance” until a target witness exists. This boundary is deliberately earlier and stronger than an unreliable “cancel if enqueue has not returned” check. It avoids a cross-store transaction while retaining a provable no-start guarantee for recipients cancelled before the seal. Any future operation to cancel committed dispatches/running workflows is a separate control contract.

If cancellation removes every remaining unsealed obligation and there are no sealed/transferred obligations, set the source to Cancelled, fence out its current worker and preserve the cancellation tombstone. If sealed obligations remain, retain the existing deliverable/claimed/retryable responsibility and do not clear its claim, progress or seal. Partial cancellation does not make the whole source terminal. A generic claim completion/renewal always rereads current progress and preserves it; an old stale completion cannot overwrite cancellation or seal state.

## 6. Target receipts, outcomes and recovery

The handler first reconciles the exact deterministic target, then dispatches if acceptance is not proven. An actor's in-memory Duplicate result alone is insufficient. `Deferred` can mean forwarding or shedding and is not universally acceptance. `AcceptedButFaulted` means the turn may have been admitted while execution failed; it is not a successful workflow outcome. Existing enum values keep their meaning.

The native/model target adapter must provide a physical `RuntimeTargetAcceptanceReceipt` matching source/selection/seal, recipient, command/input identity, complete pin/incarnation, scope/authority and normalized input fingerprint. Valid evidence is either:

1. The exact ordinary root command, or its deterministic initial-checkpoint successor, is physically persisted in the actual target's scheduler responsibility boundary, with a qualified recoverer; or
2. Its initial execution/model input receipt is physically committed, retaining an optional typed accepted-ingress origin in the first state/checkpoint.

Add a default-unsupported exact physical lookup to the owning queue/target reader where existing finite list APIs do not prove this efficiently. It bypasses coalescing overlays. Queue-before-state observation order must cover atomic queue-consumption-to-initial-checkpoint transitions; absent evidence is unknown/retryable, never evidence that delivery can be discarded. Existing process memory, a generated execution ID, an uncommitted working set or successful HTTP writing is not a receipt.

Distributed transport persistence/forwarding is an intermediate observation. It does not by itself complete source delivery: retain the sealed source until the actual target scheduler/initial-state receipt is physically verified. A consumer supporting a different durable acceptance boundary must prove that it owns the exact root responsibility, its recovery, retention and terminal failure observations; it cannot infer this from a generic Deferred response. The initial implementation may refuse that unqualified composition.

Two source-specific native changes are required by the inspected implementation:

- `InProcessWorkflowExecutionActorProvider` currently returns Duplicate from its process-local idempotency memo before a physical lookup. For the new ingress origin, return an accepted duplicate only with an exact physical queue/successor/state receipt. If the memo has no durable witness, reconcile or remove that memo entry and retry the same command **inside the ordinary mailbox**. A prior pre-checkpoint failure cannot leave a sealed command permanently suppressed by memory-only deduplication. Legacy command behavior remains unchanged.
- `WorkflowStartSchedulerWorkHandler` currently creates a random root activity ID and current start time for a non-child root, then queues a deterministic WorkflowStarted checkpoint successor. For the new ingress origin, derive root activity/attempt identity from the seal and use its fixed time. Preserve the typed ingress origin through Start, the initial-checkpoint successor, the root post-commit intent and initial execution state. The physical receipt reader recognizes that exact lineage, so disappearance of the original Start item while its initial checkpoint is queued is not mistaken for absence of responsibility. First-checkpoint and successor dedup must compare their canonical origin/payload and refuse conflicting same-ID work.

Target processing validates the immutable **physical DispatchSeal** and canonical command origin before first effects, independent of the source handler's current claim. This matters when the outbox claim expires, a peer repeats enqueue, or the source has already transferred responsibility. Current trigger pause/publication state is not reevaluated as fresh admission. A forged key/authority or a cancelled-before-seal recipient has no seal and refuses. Initial root state/model receipt commits the exact ingress origin through normal checkpoint participants, forcing a physical boundary when needed. Later target duplicate/retirement evidence must match that origin; an unrelated execution sharing an ID is an authority conflict.

`RecordTargetReceiptAsync` validates physical target evidence and changes only DispatchCommitted → TargetAccepted under the current source owner's fence, preserving the seal. If acknowledgment is lost, reconcile the same target and record again. If the source owner lost its lease, it cannot record progress; its successor reconciles the same deterministic command. A stale handler might have sent an identical sealed command during the claim race; target idempotency prevents a second execution.

| Observation | Source responsibility |
|---|---|
| No target witness after seal; crash/timeout/forwarding uncertainty | DispatchCommitted remains owed; retry same command. |
| Capacity shed before target command persistence | DispatchCommitted remains owed; backoff/retry; no false queued/started result. |
| Exact target queue/state receipt | Record TargetAccepted; source may complete delivery once every accepted recipient is transferred/cancelled. |
| Start policy refuses after acceptance, unsupported changed composition, corrupt source/material | Retain sealed responsibility and show a typed failure/recovery state. No fresh target selection or silent completion. Permanent faults may dead-letter under existing policy but remain retained for diagnosis/redrive. |
| Workflow later completes/faults/cancels | Separate execution observation; it does not alter the already factual acceptance/dispatch receipt or prove HTTP response delivery. |

Every accepted recipient must end delivery as TargetAccepted or CancelledBeforeDispatch; pre-admission refused entries remain Refused in the selection. The aggregate may be Delivered with explicit refused/cancelled entries; its meaning is “all accepted responsibility transferred or explicitly cancelled”, not “every workflow succeeded”. FailedFinal remains owed/recoverable evidence, not a retention release. Redrive preserves selection, seal and command identity.

### Loss and duplicate argument at each handoff

The protocol relies on three checked invariants: **(A)** a Pending recipient can be cancelled or sealed exactly once under its physical source revision; **(B)** a sealed canonical command and its pins remain recoverable until a physical matching target receipt; **(C)** the target admits only that deterministic command/lineage under its ordinary single-writer and durable dedup/receipt rules. These are implementation obligations, not assumptions about every custom provider.

| Interruption/failure boundary | Why responsibility survives and duplicates converge |
|---|---|
| Before source commit | No accepted source or seal exists; HTTP cannot acknowledge acceptance. An unknown commit outcome is resolved by exact scoped key lookup. |
| After source commit, before seal | Source is Pending and ordinary outbox recovery owns it. Cancel and seal compare the same progress; there is no command authorized before the seal. |
| After seal, before enqueue, including policy/capability refusal | B retains the exact command and roots. Cancellation reports committed. Failed/retryable/dead-letter source retains responsibility until the dependency/policy is repaired; no receipt or Delivered result is fabricated. |
| Enqueue succeeded, acknowledgment lost | Source remains DispatchCommitted. The next owner finds queue/successor/state evidence or repeats the same immutable command. C absorbs its identity; there is no new target version, input bag or root identity. |
| Original Start consumed, initial checkpoint queued | Deterministic successor carries the sealed origin and is physical responsibility. Its checked lineage receipt covers this intermediate state; replay cannot create a distinct initial checkpoint/root. |
| First checkpoint commits, source receipt update lost | Initial state/model receipt carries the exact ingress origin. Retry reconciles that physical receipt and records TargetAccepted without creating another execution. |
| Source completes after target receipt | The durable target queue/initial checkpoint and its normal recovery/poison owner now carry responsibility. Source retains audit/dedup/material obligations until guarded retirement. A later target execution failure remains visible as a target failure, not a source acceptance reversal. |
| Target loses/poisons work before initial state without a qualifying receipt | Source cannot complete on absence or a memory Duplicate. Record TargetPoisoned/FailedFinal with retained seal and evidence; repair/redrive resumes the same identity. |

A target receipt that is merely an acknowledgment of an unretained command cannot satisfy B/C and must not be advertised by its adapter. Tests must deliberately stop at each row; successful happy-path queue calls are insufficient proof.

## 7. Root retention and cleanup

Admission protects each accepted complete material/dependency closure before source commit using the existing root-write lease manager and provider fences. The authoritative source row/selection is the retention root; a caller's receipt object is not. Trigger deletion, slot unpublication, association removal, outbox retries and schema rewriting cannot remove it.

Extend the existing outbox-owned retention enumeration and ordinary-collection checks for standalone sources and subscription recipients on workflow sources. Include deterministic target execution/input/dedup dependencies before they exist. Retention must inspect the complete physical source set sharing material, not just one delivery page or tenant view of a physically shared native store. Unknown source/protocol/cleanup capability retains material and refuses unsafe collection.

Pending/DispatchCommitted recipients retain accepted material. CancelledBeforeDispatch may release its material obligation only after the cancellation and seal-exclusion tombstone is physically committed; its source-key tombstone must remain to prevent a later request retry becoming a new admission. TargetAccepted follows the existing conservative saved-cohort policy: retain source/selected pins and deterministic target dedup obligations until the target's sticky retirement/cleanup proof permits release. A target temporarily absent is not proof of retirement. Dead letters and uncertain target outcomes retain all owed roots.

The new protocol defines no automatic source-key expiry. Initial delivery keeps admission/control tombstones and follows existing guarded cleanup for referenced executions/material. Any future bounded receipt retention must specify what happens to late source retries and establish old-source fencing before deletion; it cannot be introduced as incidental cleanup in this feature.

## 8. Provider mutation mapping

| Operation | EF shared Runtime provider | Native in-memory provider |
|---|---|---|
| Admit | Owned clean-context transaction; compare/fence resource/association/route-or-schedule/slot/reference facts, exact material/root grants and source unique key; insert canonical outbox row once. No effects on conflict/failure. | Existing checkpoint/outbox write gate plus resource/reference/material gates in one documented lock order; validate before mutating dictionaries, install source and roots together. Process-local only. |
| CommitDispatch | Reload exact source, validate current scope/owner/fence/provider time and selection; row-revision CAS installs immutable seal and progress together. | Same existing source write gate, current physical dictionary entry and trusted clock; install one immutable replacement. |
| CancelPending | Source revision/control request CAS; modify current progress, fence fully cancelled worker, preserve source-key tombstone and all committed dispatches. | Same write gate; cancellation and seal have one serialization point. |
| Record receipt | Validate selected physical target boundary, source fence/seal and receipt; monotonic source revision update. | Same gate and qualified same-store target evidence. Memory observations cannot be described as durable restart evidence. |
| Open source/read committed recipient | Checked physical row/projections/version/scope; standalone does not read publisher workflow state; saved workflow variant still verifies actual workflow source. Current source claims only for handler mutations; immutable seal validation for target command admission. | Same actual stored source, no synthetic context; physical reference/material checks and explicit scope remain required. |
| Retention/cleanup | Extend existing source-root and execution-dependency queries, ownership fences, row rewrite/version gates and provider migrations. | Extend the existing full material-sharing source scan and gated root writes; do not add an independent singleton ingress store. |

Provider-owned participation is required when resource/route/schedule storage and Runtime persistence differ. The implementation cannot promise an atomic acceptance by joining unrelated public `SaveAsync` calls. Unsupported mixed configurations report capability unavailable before trigger activation/admission. Admission, seal/control, source success and initial target-origin mutations force coherent physical boundaries through normal checkpoint/fold integration; Balanced remains the general default.

## 9. Causal qualification matrix

These are required behavior scenarios, not claims of tests already run. Each failure/race test controls the named boundary and observes stored state/recovery through the real selected composition.

| Scenario | Required observation |
|---|---|
| Legacy workflow intent/item/checkpoint golden vectors | Byte-identical serialized workflow output and unchanged existing fingerprints/IDs; new members absent; old workflow recovery still works. |
| New external source round-trip and corrupt/unknown arm | One true standalone arm with null/absent workflow intent; invalid XOR/projections/version refuses before effects. |
| Unsupported old processor/store/collector or split contexts | Activation/admission refuses; no source/target queue write and no successful acknowledgment. |
| Independent public source consumer | Registered module submits a detached signal through its producer-bound port; Runtime supplies scope/actor and prepares captures. Wrong resource/type/schema, denied/missing context, conflicting registration and unsupported backend refuse before effects. |
| RequireAll with one invalid subscriber | Complete evaluation; zero accepted source/target effects; safe per-subscription reason. |
| AcceptAvailable with one valid/one invalid subscriber | One source with full accepted/refused selection; one accepted target; explicit partial receipt. Zero accepted fails. |
| Same source key concurrent requests | One persisted winner and one identity for each recipient; identical replay returns it; changed payload conflicts. |
| Admission versus pause, grant change, association edit or slot replacement | Physical compare determines winner; fresh acceptance captures the winning publication/revision; old accepted source remains exact through later changes. |
| Crash after source insert, before first seal | Ordinary recovery finds Pending source; cancellation can still win; accepted pins survive collection. |
| Cancel wins versus seal | Recipient tombstone persists; stale handler cannot seal/enqueue; no target execution appears after restart/retry. |
| Seal wins versus cancel; crash immediately before enqueue | Operator sees DispatchCommitted, cancellation reports committed; source recovery enqueues the exact sealed command once effectively; no false queued/started claim in the gap. |
| Crash during enqueue or after target queue persistence before source receipt | Source stays owed; physical reconciliation or identical re-enqueue reaches the same target; one effective execution. |
| Claim expires during handler and peer retries | Same immutable seal/command; stale completion cannot erase newer progress; target accepts at most one root. |
| Target queue consumed as first state/model receipt commits | Physical queue/state reconciliation never discards responsibility in the gap; typed ingress origin matches the sealed recipient. |
| Capacity shed or forwarded Deferred | No false target receipt; source remains DispatchCommitted/retryable. |
| AcceptedButFaulted or later workflow failure | Target acceptance and execution fault shown separately; no successful-completion inference. |
| Partial cohort delivered then process/target publication changes | Successful recipients never execute again; undelivered recipients retain original pins/inputs and recover independently. |
| Multiple subscriptions/slots sharing the same material | Distinct deterministic recipient roots and source attribution, no artifact-key collapse. |
| Coalescing/physical source check | HTTP acknowledgment and source/target receipt occur only after coherent physical persistence; no overlay-only acceptance. |
| Unpublication/deletion/GC while sealed recipient has no target yet | Source retains full material/closure and dedup dependency; after restart the exact target still starts. |
| Cancelled-before-seal source retry and cleanup | Cancellation tombstone returns original outcome and prevents readmission; material release does not remove idempotency/control proof. |
| New Event source plus embedded starts/exact waits | Actual original source retained; one complete original selection; retry never adds today's subscribers or changes Event broadcast/local semantics. |
| Schedule known zero eligible, then a target/subscription becomes eligible | Same overdue occurrence/backoff and no cursor advance until admission; later evaluation uses current targets, commits one source, then settles and freezes the full accepted/refused set. |
| Schedule eligibility store fails | Same overdue cursor with Unavailable/UnknownEligibility diagnostics; no false known-zero evaluation, empty admission or source success. |
| Schedule source commit succeeds but settlement acknowledgment is lost | Same occurrence key returns the original physical receipt; cursor settlement converges with no rematch, rerun or late subscriber addition. |
| Real HTTP/schedule/Event composed-host sources | Same ordinary outbox processor/recovery path; no private pump/queue or fake workflow rows; both native and supported genuine model targets prove declared input consumption. |

Independent review must check legacy wire preservation, source authority and physical composition, cancellation/seal linearization, receipt truthfulness and root retention. Public module/consumer qualification exercises actual adapter registration and default refusal. Multi-node/restart guarantees require real persistent provider/process witnesses; in-memory contract tests alone establish only process-local behavior.
