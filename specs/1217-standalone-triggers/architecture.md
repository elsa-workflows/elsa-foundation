# Standalone trigger authority, targets and inputs

**Status**: Proposed engineering contract; no production implementation or qualification is claimed.  
**Feature**: [#3582](https://github.com/elsa-workflows/elsa-foundation/issues/3582).  
**Source inspected**: `e0ba87ecfc3b1e774c509ed274b19cd3f34a1124` on 5 November 2026.  
**Scope**: The authority, target, input and module/model seams required by this feature. Delivery, scheduling, HTTP and UX details must agree with these boundaries before implementation.

## Decisions and authority

The owner has confirmed that a subscription follows the currently serving publication slot. Pause and unpublication prevent new admissions; work already durably accepted finishes using its captured version. Cancelling pending work is a separate explicit operation. Start authority belongs to the shared resource and continues until paused or its resource invocation grant is revoked; its creator remains audit attribution. Async HTTP uses a per-trigger admission policy: `RequireAll` is the default, and explicit `AcceptAvailable` can admit an eligible subset with visible partial outcomes; accepting no subscriber fails. These are product decisions. The mechanisms below are proposals that implement them.

On 6 November the owner selected one configurable async HTTP admission policy per trigger: Require all subscribers by default, or Accept available subscribers with explicit accepted/refused partial outcomes. Zero accepted recipients is a failed request. Capture the chosen policy and full evaluated set in the occurrence evidence; partial policy never silently removes refused recipients, and require-all never starts healthy siblings before learning that another recipient cannot be admitted. Once accepted, both policies deliver/recover recipients independently.

[ADR 0043](../../docs/adr/0043-publication-slots-define-start-authority.md) is accepted for publication-slot start authority and exclusive HTTP claims. Its 4 September Runtime-ledger ownership amendment says Runtime owns `IWorkflowActivationAuthority`; Publishing owns lifecycle commands and publication records. Its 1 October convergence amendment remains explicitly **proposed, awaiting owner acceptance**. The existing coordinator/switch implementation is source evidence for its mechanism, not evidence that this proposed amendment was accepted. This feature must not silently ratify that amendment or weaken the accepted authority invariants.

There are two distinct checks:

1. The trigger's current enabled revision grants permission to admit a new occurrence through this entry point.
2. Each subscription's selected authoritative publication grants permission to start its target.

The trigger cannot publish a workflow or make an unpublished artifact eligible. Its configuration, route table, subscription row and delivery receipt are not alternative publication authorities. Conversely, a workflow remaining published does not reactivate a paused trigger.

An HTTP trigger has one endpoint claim owned by its active trigger revision. Its response mode and downstream subscriber count do not multiply that claim. Embedded and standalone endpoint claims use the same normalized route/method namespace and admission protection. Publication replacement must see standalone claims, and standalone activation must see embedded claims; validation in only one direction is insufficient. Async fan-out has one endpoint owner and several independently admitted workflow starts.

This extends ADR 0043's current publication-only description of serving projections and endpoint claimants. A bounded proposed amendment must explicitly cover independently owned ingress claims and model publication selection before the affected production work proceeds. It must preserve publication slots as the sole target start authority. No ADR is changed by this document.

## Verified implementation boundaries

| Current source | Consequence for this work |
|---|---|
| [`WorkflowActivationSlot`](../../Essentials/src/Workflows/Runtime/Core/Models/WorkflowActivation.cs) selects an activation by definition/slot with a revision. [`EfWorkflowActivationAuthority`](../../Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfWorkflowActivationAuthority.cs) additionally scopes physical identities through the persistence access context. | Reuse the ledger and scope; do not create a trigger-specific serving-version table. |
| [`WorkflowActivationCommand`](../../Essentials/src/Workflows/Runtime/Core/Models/WorkflowActivationLifecycle.cs) currently requires a native `WorkflowExecutable`. The [coordinator](../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executables/WorkflowActivationCoordinator.cs) derives the slot owner key from that material identity and prepares native trigger/schedule projections. | Genuine model publication needs a bounded material-neutral coordinator command; a fake native root is not a valid adapter. |
| [`WorkflowExecutionStartService`](../../Essentials/src/Workflows/Runtime/Api/Handlers/WorkflowExecutionStartService.cs) already supplies inputs, live Published selection and a null trigger node to the ordinary native dispatcher. | Native business workflows can start without a dummy trigger activity. |
| [`WorkflowStartDispatcher`](../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executions/WorkflowStartDispatcher.cs) validates executable inputs before enqueue. Its saved-stimulus authority currently forbids supplied inputs and variables and requires a captured embedded binding/node. | Standalone mapped starts require a new versioned captured-recipient shape and checked admission path, not extra fields passed to the existing saved-binding path. |
| [`WorkflowExecutableStartSourceResolver`](../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Executions/WorkflowExecutableStartSourceResolver.cs) captures exact publication provenance and validates it at retention acceptance. | Extend this physical authority/retention boundary; do not infer accepted authority from a caller-provided key. |
| [`RuntimeModelMaterial`](../../Essentials/src/Workflows/Runtime/Core/Models/RuntimeExecutionMaterial.cs) already carries a protocol, profile, declared input contract and exact dependency pins. Its behavioral hash excludes producer identity. [Fenced source publication](../../Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfWorkflowExecutableSourceReferenceStore.RootWrites.cs) explicitly permits source attribution to differ from deduplicated material's original producer. | Preserve the distinction between logical authored identity, publication provenance and immutable material identity. |
| [`BpmnModelRuntime`](../../Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnModelRuntime.cs) currently creates hash-derived definition identities, an empty input contract and live Published references without activation slots; it sends `ExecuteModel` commands. [`BpmnAuthoredDirectProjection`](../../Essentials/src/Activities/Bpmn/Interchange/Services/BpmnAuthoredDirectProjection.cs) rejects declared workflow inputs. The [direct driver](../../Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnDirectExecutionDriver.cs) also rejects nonempty inputs. | Genuine BPMN targets need explicit authored identity/slot/input integration. Native `BpmnProcess` tests alone do not establish this support. |
| [`DispatchPinSource`](../../Essentials/src/Activities/DispatchWorkflow/Design/Services/DispatchPinSource.cs) resolves a small trigger workflow's reusable target during compilation and retains an exact dependency pin. | Standalone subscriptions add independently editable operations and slot-following targets. They are not an alternative spelling of this pinned child call. |

## Persisted target and captured start

An authored subscription contains a stable subscription ID, trigger ID, revision, enabled state, execution-model key, logical workflow definition ID, slot name and input mappings. Tenant and persistence scope come from the server-owned resource context. `default` is the explicit default slot shown to the user. Selecting a Design draft, artifact hash or latest numeric version is not a production subscription target.

The trigger and workflow editor address the same subscription ID and revision. The workflow-side lookup uses the logical definition ID, so it includes associations across its versions without copying them into workflow version JSON. The subscription survives replacement of the slot's serving version. A new subscription does not take ownership of a slot or imply a publication.

Admission resolves the actual slot, its selected publication/reference and native/model material. The resulting immutable recipient capture contains:

- Occurrence/source ID, content fingerprint, source scope, trigger ID and accepted trigger revision.
- Subscription ID and revision, logical definition ID, slot ID/revision and selected activation/publication ID.
- Full material pin and, for model material, protocol/profile and physical incarnation.
- Exact source provenance, normalized declared inputs, mapping/schema version and declared-input-contract fingerprint.
- Runtime-owned execution authority, tenant/partition and deterministic recipient/start identity.

Source payload stays on its own channel. It cannot overwrite declared inputs, trigger identity, model protocol, source provenance, tenant or caller authority. No embedded executable node is fabricated for a standalone start.

The logical definition and source provenance are separate from the complete material pin. Two authored definitions may share identical material. Capturing one cannot replace the other's logical identity with the material's original producer fields. Existing pinned-snapshot comparisons remain strict for material; the new capture separately validates the logical definition/source/slot relationship.

## Tenant, invocation grant and execution identity

Management transport obtains the normalized subject and tenant before resource lookup, following the existing [native start service](../../Essentials/src/Workflows/Runtime/Api/Handlers/WorkflowExecutionStartService.cs) and [resource permission gate](../../Essentials/src/Workflows/Runtime/Api/Services/HttpContextWorkflowRuntimeOperatorAccess.cs) patterns. The selected persistence scope is a separate server fact. Request JSON, event payload, route values and mapping expressions cannot select a foreign tenant, impersonate another subject or turn a missing/invalid principal into the global tenant. Use the identity provider's existing canonical representation; do not equate null, empty and a named tenant.

Saving an association requires both the relevant trigger/subscription management permission and permission to invoke the exact target definition/slot. Workflow-side association editing does not imply permission to edit a shared trigger's source configuration. Each lookup and impact preview applies resource-aware authorization before disclosing target names or payloads. Denied cross-tenant reads have the same absence behavior as other Runtime resources.

The minimal persistent authorization is a resource-owned invocation grant attached to the subscription revision: server-captured tenant, logical target/slot, allowed trigger/source, granted operation, grant revision and active/revoked state. The authorized granting subject and time are audit facts. It contains no password, bearer token, cookie, copied role list or serialized `ClaimsPrincipal`. A caller cannot submit a granted flag or construct this record. Creating or widening the grant requires current management and target invocation permission through the ordinary resource-aware permission service. Updating target/mapping/source scope requires a new authorized revision. Revoking the grant or pausing/removing the association blocks new occurrence admission through the same conditional transition.

At every new occurrence admission, check the resource grant's current state/revision and the host's current target start policy. Do not recheck the creator's continuing membership, employment or personal permissions as an eligibility condition. The owner explicitly selected shared-resource authority: removing the creator does not stop a centrally managed schedule. The existing [`ICurrentSubjectPermissionAuthorizer`](../../Essentials/src/Foundation/Identity/Core/Authorization/AuthorizationContracts.cs) can authorize deferred user-owned operations elsewhere, but it is not the authority for this autonomous resource. No background step reconstructs or impersonates the creator's principal. An authorized manager can pause the resource or revoke its grant deliberately, and the operator view identifies that action and the affected subscriptions.

The HTTP/event producer's authorization to submit to an entry point is separate from this grant's authorization to start its targets. An anonymous/public HTTP route is possible only when its source access policy explicitly allows that; it does not grant management rights or let the caller choose another target. Schedules have no interactive caller and use their resource grant. Under `RequireAll`, an invalid grant refuses acceptance of the whole request. Explicit `AcceptAvailable` can exclude the denied/unavailable target only with a visible per-subscription outcome and at least one durably accepted recipient. Accepting no work cannot receive a successful work-acceptance response.

At durable acceptance, capture the evaluated grant revision and immutable execution attribution. The execution system identity is a registered server-owned standalone-trigger identity. The root initiator identifies the authenticated source actor where one exists, or the server-owned scheduled/source identity otherwise; the granting subject and trigger/subscription/occurrence are separate audit facts. Construct [`WorkflowExecutionAuthoritySnapshot`](../../Essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutionAuthoritySnapshot.cs) inside the trusted adapter and preserve it across retries. The grant's author is not impersonated as the background request's principal. Modules cannot supply arbitrary system identities or root attribution through their payloads.

Resource grant revocation affects future occurrence acceptance. It does not retroactively invalidate a durable accepted capture, reselect a target or cancel an execution. Later changes to the granting user's personal permissions do not alter either existing captures or the resource grant. Explicit cancel-pending operates on accepted responsibility under its own management permission. Each fresh target dispatch still applies the host's current [`IWorkflowExecutableStartPolicy`](../../Essentials/src/Workflows/Runtime/Core/Contracts/IWorkflowExecutableStartPolicy.cs) with the captured authority and target. That existing policy is a single replacement contract, not a new trigger policy chain. A current policy refusal retains a visible blocked owed recipient; it is not retried as another user, acknowledged as successful dispatch or silently discarded. Model adapters must apply the equivalent ordinary current start policy before fresh model admission, while already accepted target command replay converges on its recorded result.

Source-provisioned trigger configuration follows the established [Connections management-origin pattern](../../Essentials/src/Connections/Core/Models/ConnectionModels.cs): the owning provider supplies its stable resource identity, configuration revision and management origin. API/Studio cannot overwrite provider-owned fields or turn them into Studio-owned configuration. Independently permitted operational pause and locally owned associations remain explicit fields outside that immutable provider configuration. A provider update preserves the local pause and cannot reactivate it. If the provider also owns associations, those rows expose their origin and are read-only from both editing surfaces. Trusted host provisioning must supply an explicit host-authorized invocation grant/capability for its tenant and target; it must not fall back to an owner account, arbitrary string identity or absent authorization. This is bounded resource provisioning, not a new service-account or credential-management product.

## Admission and lifecycle races

Durable acceptance linearizes the current trigger/subscription revisions, selected publications, captured normalized inputs, per-recipient deterministic identities and material/reference retention roots. The supported physical participant must protect the trigger-state and selected-publication facts at that boundary. A preflight read followed by an unrelated receipt write is insufficient.

For a supported durable profile, acceptance and retirement use the same Runtime backend's actual concurrency/transaction boundary, extending the existing checked source/retention composition. Provider readiness must establish that the selected instances participate in that boundary. If they cannot, activation/admission refuses with an actionable capability finding. A capability flag without physical participation is not qualification. In-memory composition is explicitly process-local and must not advertise restart/multi-node durability.

The race outcomes are deterministic:

| Competing change | Required result |
|---|---|
| Pause wins before acceptance | No new occurrence is accepted. |
| Acceptance wins before pause | Its captured recipients remain owed; pause affects later occurrences. |
| Subscription removal/change wins before acceptance | Admission uses the new association set or refuses the stale revision. |
| Acceptance wins before association change | It retains the old captured set and inputs. A retry does not discover new subscribers. |
| Slot replacement wins before acceptance | Admission validates/maps against the replacement's actual contract. |
| Acceptance wins before replacement/unpublication | Its captured publication/material remains retained and dispatchable through saved authority. No fresh live-source lookup substitutes a replacement or rejects already accepted work merely because its reference retired. |
| Two equivalent retry submissions overlap | One source/recipient set is authoritative; the other returns its retained admission result. |
| Reused identity with changed content/scope | Refuse a conflict; never silently adopt the changed payload. |

Once accepted, each subscription has its own retained outcome. Retry resumes only owed recipients. An accepted sibling is not started again because another target was unavailable or failed. Identity includes the accepted occurrence and subscription, not merely the artifact: two subscriptions to identical material remain distinct intended starts. Existing frozen keyed-start formats are preserved; add a versioned derivation if their identity is insufficient.

Cancellation of pending delivery competes with a physical source-side `DispatchCommitted` seal. The seal irrevocably retains responsibility for the exact captured command; it does not itself prove Queued, Started or Completed. If cancellation wins, no seal or target command is produced. If sealing wins, cancel-pending reports dispatch committed, even before a target queue row exists. The source stays owed/recoverable across a crash between seal and enqueue until ordinary deterministic queue/state/input receipt lookup proves actual target admission. Retained seal validation is independent of an expired source delivery claim, while new seal/progress writes require current ownership. Cancelling an admitted execution is a separate command. A timeout or missing response never proves non-admission or cancellation.

## Input mapping

Reuse [`WorkflowExecutableInputContract`](../../Essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutableInputContract.cs) and [`IWorkflowExecutableInputValidator`](../../Essentials/src/Workflows/Runtime/Core/Contracts/IWorkflowExecutableInputValidator.cs) for both native and model targets. Do not introduce a second definition of required fields, defaults, aliases, collection types or null handling.

Each subscription owns an immutable mapping revision. Its editor can offer literals, typed source-field selection and the existing supported expression languages. Expression parsing/evaluation, value conversion and name semantics use their existing accepted contracts. The trigger supplies a bounded evaluation context containing the captured source payload/metadata and authorized trigger configuration; there is no preexisting workflow activity, workflow variable state or target activity-output context. Server-owned source facets and caller authority are read-only. Persistent mappings may reference authorized resource/secret identities according to existing rules; they do not copy secret values into trigger revisions or receipts.

Validation has three levels:

1. Save/preflight validates mapping names, syntax, source-field availability and statically known literals/types using the target's actual declared contract. Every required input must have a mapping or supported default. Unknown, duplicate and undeclared destinations refuse.
2. Preview evaluates a supplied sample through the same mapper/validator and returns safe per-field findings. It reserves neither publication nor readiness.
3. Admission evaluates once against the captured payload and selected contract, validates the complete realized bag and captures normalized JSON inputs with defaults applied. Retry uses that captured bag; it does not reevaluate time, changed configuration or source data.

Publication replacement recomputes subscription compatibility. A newly required unmapped field, removed/renamed mapped input or incompatible type makes the affected association visibly blocked for fresh admission. Other compatible associations and already accepted occurrences retain their own state. The publication preview shows the impacted associations; activation and trigger admission still recheck current facts. A stale compatibility badge never grants admission.

This context extension must not amend accepted binding/serializer/null/undefined/expression semantics. If implementation discovers such a change is necessary, isolate its proposal under the applicable architecture process. In particular, changes to a contract on the [#3052 initiative board](https://github.com/elsa-workflows/elsa-foundation/issues/3052) follow that initiative's human agreement rules. The standalone feature has no parent or Part-of relationship to that initiative at the inspected checkpoint; topical dependency alone does not move the whole feature into it.

## Minimal genuine BPMN integration

This work delivers standalone starts for genuine authored BPMN using its ordinary model execution protocol. It does not complete the separate BPMN program, replace its interpreter, expand general BPMN semantics or implement durable message cardinality.

### Stable authored identity and existing slot ledger

The authored publisher already receives `WorkflowDefinitionVersion`, including its stable definition ID and exact version ID. Extend the trusted BPMN publication request/provenance with those authored identities and the same slot/revision intent used for native publication. The server validates them from the stored version; public input cannot assert arbitrary source authority. Raw/legacy model publication retains its documented identities and does not become a stable slot-following target by inference.

Add a material-neutral activation request to the existing coordinator, carrying the logical definition/slot key, exact material pin, source reference and prepared projection contribution. Native activation adapts to the same internal operation without changing its wire/material format. Model activation supplies no invented native trigger nodes. The existing Runtime slot/switch remains the ledger; Publishing remains the lifecycle/publication-record owner. Model source-reference creation/retirement and slot visibility use the same protected transition and retained-root rules. Do not call `TryActivateAsync` on the authority alone after a separate model source write.

Keep the model behavioral hash and canonical material pin unchanged when only its source/slot provenance changes. Read back deduplicated material before using its full pin. The new model start path validates the logical definition through the selected source/slot relation; it must not require logical definition/version fields to equal the deduplicated material's original producer fields. Qualify two logical authored definitions and two versions sharing material to prevent false conflicts or identity collapse.

### Declared inputs and ordinary model entry

Project the authored workflow's declared inputs into the existing `RuntimeModelMaterial.InputContract`. Introduce an explicit supported input-capable BPMN profile/version rather than relaxing every old profile. Validate the normalized bag with the shared validator, persist it with the model's initial state and retained input receipt, and expose it through authorized model inspection. Subsequent decisions/recovery read the committed bag. Older closed profiles continue requiring empty inputs and retain their existing serialized bytes/readers.

The model adapter sends the ordinary `ExecuteModel` start command with exact material incarnation, captured source provenance and deterministic recipient input ID. It uses model receipts/queue lookup for duplicate admission and result reconciliation. It does not invoke the native workflow-start scheduler for model material or represent the BPMN run as a dummy native activity execution.

Direct calls to a newly authored slot-bound model use a bounded target-owned admission operation: under the protocol router's existing exact material/root leases, the selected Runtime physical participant protects the current logical slot/revision/activation and source identity while committing the actual deterministic ordinary `ExecuteModel` queue command with copied direct provenance. The existing router already protects material retention; it does not currently fence slot/reference authority, so a live read followed by enqueue is insufficient. If queue admission wins, its accepted start survives later unpublication through existing queued-model retention. If unpublication/replacement wins, stale admission refuses without a command or raw-legacy fallback. Exact queue/state/input-receipt replay is reconciled before fresh publication eligibility, including lost acknowledgment. This uses no trigger resource/grant or fabricated ingress source; unsupported/mixed providers default-refuse. The [target contract](contracts/targets.md#provider-protected-authored-direct-admission) records the proposed command version, provider boundary and real race witnesses.

The first input-capable model profile needs a real consumption witness: an authored supported BPMN process receives a declared input and a supported native service binding consumes it through an explicit, typed model-to-worker input mapping. The initial worker binding selects a literal or a case-sensitive declared model input name and reuses the normal declared-input validator. Portable expression evaluation belongs to the subscription mapping described in the [target contract](contracts/targets.md); no second expression language or general model expression context is needed for this worker witness. Keep model inputs read-only; do not invent mutable BPMN variables, general expression/condition support, new activity output semantics or a new BPMN interpreter. Current service bridge refusal of nonempty worker inputs is replaced only for this explicit versioned profile. Human-work/inspection also displays authorized declared inputs without exposing source transport credentials.

Ordinary BPMN start semantics choose the process's normal none-start entry. The adapter must not forge signal/message/timer start metadata or silently choose among unsupported event-only entries. Capability/preflight refuses a model whose normal start or declared-input/profile requirements are unsupported and identifies the reason. Existing signal/message semantics and mid-flow catches remain separate.

### Bounded public extension points

Keep orchestration/admission implementations internal. Public module contracts are limited to capabilities modules must supply or consume:

- Trigger type descriptor and configuration/source schema, validation and normalized stimulus identity/cardinality.
- Source ingress adapter into the common Runtime-owned admission/delivery boundary.
- Target authoring discovery contract (`IStandaloneTriggerTargetDiscovery`): picker/schema/tooling descriptions and declared entry/capability metadata, independently dispatched without loading the runtime target implementation.
- Target runtime contract (`IStandaloneTriggerTargetRuntime`): actual protocol/profile/ordinary-entry readiness, captured-input validation, command preparation and admission reconciliation, independently dispatched without Design stores or authoring discovery. The contracts may share immutable Core models; neither inherits or requires the other ([framework §2.6.4](../../.specify/memory/constitution-framework.md)).
- Optional HTTP response capability descriptor, with synchronous/async constraints explicit.

Names are provisional until the public API review. New implementations default-refuse operations they cannot physically guarantee. A module cannot return arbitrary saved authority, write another target's publication slot, skip input validation or acknowledge work that its configured durable backend did not accept. A second consumer module must exercise the public contracts outside the first-party implementation projects.

Genuine BPMN supports Event, schedule and asynchronous HTTP starts through its model adapter. Workflow-owned HTTP response requires a qualified model/host response capability; lack of that capability is a visible unsupported combination, never a second response writer or a hidden conversion to async mode. Native workflow-owned response remains subject to the same explicit capability check. Any expansion of model HTTP response behavior requires a concrete request-lifetime/response-bridge design and qualification before it is advertised.

## Shared-work boundaries

[Durable messaging #3583](https://github.com/elsa-workflows/elsa-foundation/issues/3583) is drafting spec 1216. Its durable receipt, per-definition consumption and ready-wait-before-start arbitration are a different product contract. Event subscribers retain broadcast and standalone occurrences retain per-subscription outcomes. Share physical source admission, retention, mapping validation and dispatch reconciliation where their contracts fit; do not add message buffering or per-definition message locks to Event.

[Workflow releases #3590](https://github.com/elsa-workflows/elsa-foundation/issues/3590) may later package supported trigger revisions or bind to independently managed trigger resources through an explicit release dependency adapter. It must apply target publications through the same ledger and preserve running pins. This feature neither requires the whole release product nor transfers trigger lifecycle to it.

[Shared-runtime mapping review on #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6670940060) proposes a Runtime-owned standalone ingress family in normal persistence/delivery composition. That direction excludes fake publisher workflows, fabricated delivery contexts, private API queues and a second pump. It is proposed coordination context, not shipped readiness or blanket agreement to new persisted/public contracts.

## Required qualification before accepting this design as delivered

- Native Sequence/Flowchart and genuine authored BPMN enter through their ordinary runtimes with unchanged business graphs and validated declared inputs.
- A genuine BPMN input reaches the model's committed state and its mapped service worker, including restart; unsupported model entries/profiles refuse visibly.
- Slot replacement, unpublication, trigger pause and subscription update are raced against acceptance using the selected real physical backend. Readiness refusal, winning transition and retained accepted work are observed independently.
- Authored direct model calls separately prove both queue-admission/unpublication race orders and lost-ack replay after unpublication, using the existing material-root leases/queued retention and the new protected slot/source comparison. No trigger grant is fabricated for those calls.
- Shared material across logical definitions/versions preserves distinct slot/provenance/recipient identities. Input-incompatible replacement blocks new admission while accepted prior work completes.
- Tenant/resource authorization is exercised through both association surfaces and ingress: creator removal leaves the resource grant effective, explicit resource-grant revocation blocks fresh admission, accepted captures preserve attribution across background retry, current start-policy denial remains visible, and provider-owned configuration cannot be overwritten or silently unpaused.
- Partial fan-out/retry, uncertainty lookup and explicit cancel-pending preserve already admitted siblings and use the captured inputs/publication after restart.
- Embedded/standalone HTTP conflicts are checked in both directions, including concurrent attempts; async fan-out owns one route and no downstream response writer.
- A separate module consumer validates public contract usability, default refusal and real composed-host capability discovery. Authoring target discovery is qualified without a runtime target registration; target runtime preparation/reconciliation is qualified without Design or discovery services. Durable and multi-node claims require their actual provider/process witnesses.
- Current-commit Studio journeys and independent UX acceptance cover both association surfaces, compatible/blocked states, source-owned settings, operational outcomes and permissions. Build/unit summaries alone do not close these criteria.

Implementation must retain failed, skipped, unavailable and not-run rows separately from passed evidence. This document does not claim any of these observations has been executed.
