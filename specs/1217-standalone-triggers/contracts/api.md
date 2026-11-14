# Standalone trigger API contract

**Status:** Proposed contract for spec 1217; no endpoint, DTO, capability or permission below is implemented or qualified by this document.
**Feature:** [#3582](https://github.com/elsa-workflows/elsa-foundation/issues/3582).
**Source baseline:** `e0ba87ecfc3b1e774c509ed274b19cd3f34a1124`.
**Related:** [Specification](../spec.md), [authority and input design](../architecture.md), [UX acceptance](../ux-acceptance.md).

The confirmed product choices are serving-slot following, retained authority/material for already accepted work, configurable asynchronous HTTP admission policy, and resource-owned invocation authority. The latter survives the creator's departure or permission loss. Authorized managers grant and revoke it; creator and granting-manager identities are audit facts. These choices govern this proposal even where an earlier design paragraph has not yet been reconciled. All route names and wire shapes below remain subject to normal architecture/public-contract review.

## Existing patterns and placement

Use the existing NativeEndpoints typed parsing, endpoint ownership, permission catalog and shell-relative capability conventions. [User Task endpoints](../../../Essentials/src/Workflows/Runtime/Api/UserTasks/UserTaskEndpoints.cs) demonstrate internal endpoint/request types, revisioned commands and server-issued actor projections. [Connections](../../../Essentials/src/Connections/Api/ConnectionsApi.cs) demonstrates resource-owned configuration and a bounded endpoint group. [Folder deletion](../../../Essentials/src/Workflows/Design/Api/Endpoints/Library/FolderDeletionEndpoints.cs) is the reference for impact review before a destructive change. These are implementation patterns, not dependencies on those products.

Proposed minimal placement uses existing packages:

| Concern | Existing package/path |
|---|---|
| Public module contracts and persisted resource/delivery models | `Essentials/src/Workflows/Runtime/Core/`; add only the contracts an independent source/model consumer needs. API view/command DTOs stay internal to the API package. |
| Resource operations, admission, mapping, retention and recovery | `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/`; share the normal Runtime authority and persistence boundary. |
| Physical resource/delivery storage | `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/` and the existing in-memory Runtime composition; add the actual transaction/readiness participation each profile requires. |
| Management API, actor projection and capability source | `Essentials/src/Workflows/Runtime/Api/StandaloneTriggers/` within `Elsa.Workflows.Runtime.Api`; a bounded feature/group owns these routes. |
| HTTP ingress and response lifetime | `Essentials/src/Activities/Http/`; use its existing endpoint namespace and middleware integration. |
| Event and schedule source adapters | `Essentials/src/Activities/Primitives/` and `Essentials/src/Activities/Scheduling/`, using the existing Runtime scheduling composition. |
| Publication-impact integration | Existing `Essentials/src/Workflows/Publishing/` commands and Runtime activation ledger; no second slot authority. |
| Studio collection/details, association panel and mapping component | `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/`; use existing feature areas, backend transport, designer panels and public UI. |

The current `WorkflowsRuntimeApiFeature.ConfigureServices` calls `AddWorkflowRuntime`. Do not enable that feature implicitly to serve authoring-only trigger configuration. The bounded standalone management feature must register/map its own required subset without starting the runtime, an HTTP ingress listener or a scheduler on a Design-only host. Operational links require their actual Runtime services and physical boundary. Runtime-only admission/recovery never resolves workflow Design drafts. If existing package gates prevent this composition, resolve the placement in the plan instead of weakening deployment separation.

## Discovery, scope and permissions

Advertise capability `elsa.api.standalone-triggers`, contract major `1`, through the existing [`IApiCapabilitySource`](../../../Essentials/src/Api/Capabilities/Elsa.Api.Capabilities/Contracts/IApiCapabilitySource.cs). Its links are relative to the active shell route base: no leading slash, foreign origin, assumed `/elsa` prefix or host-specific port. The examples below show route suffixes only. Studio resolves the advertised relations through its selected backend session.

Capabilities describe installed operations independently of caller permission. Omit operations that are not composed; use the existing dormant status/reason only when the owning feature is dormant. Resource/type readiness explains unsupported stores, absent adapters and missing response capability. Do not advertise durable intake merely because a configured store says it is durable: the selected physical participants must support the protected acceptance/retention transition.

Proposed permission catalog keys:

| Key | Scope |
|---|---|
| `triggers:read` | Read permitted trigger configuration, types, targets and associations. |
| `triggers:manage` | Create/edit/delete permitted configuration and associations. It implies their required read access, and does not grant invocation authority or cancellation. |
| `triggers:operate` | Enable/pause permitted triggers and subscriptions. It implies the read access necessary to review that operation. |
| `triggers:authorize` | Grant/revoke a shared resource's invocation authority under target resource policy. |
| `triggers/deliveries:read` | Read permitted delivery/recipient evidence. |
| `triggers/deliveries:retry` | Request recovery of eligible owed recipients. |
| `triggers/deliveries:cancel` | Explicitly cancel eligible pending recipient responsibility. It does not cancel workflow executions. |

Endpoint checks and resource/target authorization both apply. Granting additionally requires the manager's current permission to invoke/delegate the selected target under its normal policy; revocation requires authorized resource management, not continued permission to invoke that target. Editing a subscription from a workflow editor grants no extra rights. Use the same gates from both surfaces; normal workflow read/manage permission alone does not grant control over every shared trigger.

Tenant, persistence/access scope and management subject come from trusted request context, not request JSON. Foreign or inaccessible resource identities receive the owner's normal masked-not-found behavior. Type/schema catalogs contain no tenant resources, configuration secrets or readiness details about another scope. Producer permission to submit HTTP/Event data is separate from management permissions and target invocation grants.

## Routes and capability relations

| Relation | Method and route suffix | Result |
|---|---|---|
| `identity` | `GET runtime/standalone-triggers/identity` | Current actor's coarse permission projection; resource actions remain authoritative. |
| `types` | `GET runtime/standalone-trigger-types` | Installed type/schema descriptors and authoring/runtime readiness. |
| `triggers` | `GET runtime/standalone-triggers` | Authorized resource page. |
| `triggers` | `POST runtime/standalone-triggers` | Create a paused resource; `201` with its view and location. |
| `trigger` | `GET runtime/standalone-triggers/{triggerId}` | Resource view, current operational state and action availability. |
| `subscriptions` | `GET runtime/standalone-trigger-subscriptions` | One canonical association page, filtered by trigger or logical workflow. |
| `subscription` | `GET runtime/standalone-trigger-subscriptions/{subscriptionId}` | Association, target/contract/authority state and actions. |
| `targets` | `GET runtime/standalone-trigger-targets` | Authorized selectable logical workflow/slot page. |
| `target` | `GET runtime/standalone-trigger-targets/{definitionId}/{slotName}` | Actual serving target/input contract or explicit unavailable state. |
| `change-preview` | `POST runtime/standalone-triggers/{triggerId}/changes/preview` | Read-only validation and revisioned impact review. |
| `change-apply` | `POST runtime/standalone-triggers/{triggerId}/changes/apply` | Apply the exact reviewed typed command; return committed result/receipt. |
| `mapping-context` | `POST runtime/standalone-triggers/{triggerId}/mapping-context` | Authorized source/target/parameter schemas and portable expression availability for a proposed mapping. |
| `mapping-preview` | `POST runtime/standalone-triggers/{triggerId}/mapping-preview` | Safe sample evaluation; never admission or workflow execution. |
| `deliveries` | `GET runtime/standalone-trigger-deliveries` | Authorized source-attempt/admission page. |
| `delivery` | `GET runtime/standalone-trigger-deliveries/{deliveryId}` | Delivery, recipient and transport evidence plus allowed recovery actions. |

Every templated relation sets `templated: true`. One `triggers` relation serves GET/POST; relations are not duplicated by method. Authoring-only composition advertises read/create/preview/apply operations it can really support, while command availability refuses enable and runtime controls. Delivery relations appear only with inspection support. There is no generic management `invoke` endpoint that bypasses the source adapter, source policy or publication checks. Real HTTP requests enter the reserved source route; Events and scheduling use their ordinary source mechanisms.

Collection queries use `search`, `limit` (default 25, maximum 100) and opaque `after`; responses are `{ items, next }`. Additional filters are type/state for triggers; trigger/definition/slot for associations; trigger/outcome/time range for deliveries. Association queries require exactly one of `triggerId` or `definitionId`; `slotName` is optional with the latter. A workflow editor uses the logical definition filter across versions, never a draft/version identifier. Trigger details uses the trigger filter. Both send the same commands below. Denied or filtered resources cannot leak through totals; omit totals when they cannot be computed truthfully.

## Resource, association and target projections

All names below are JSON property names; timestamps are UTC ISO 8601. Revisions are server-issued values copied back unchanged. No client increments or infers a revision. Actions have `{ action, allowed, reasonCode?, message? }`; a positive projection is advisory until the command's server checks run.

`ActorView` has the normalized current `subjectId` and booleans `canRead`, `canManage`, `canOperate`, `canAuthorize`, `canReadDeliveries`, `canRetryDeliveries` and `canCancelPending`. These describe catalog-level permission, not authority over every row or target. Resource views supply scoped `actions`; a direct command always repeats the owning authorization checks. No copied role/claim snapshot from this projection is accepted as background execution authority.

`TriggerView` contains:

- `id`, `name`, `description`, `typeKey`, `schemaVersion`, `revision`, `createdAt`, `updatedAt` and permitted audit facts.
- `configuration` with readable authored values and resource/secret references; protected values are redacted with explicit redaction metadata, never substituted with editable dummy values.
- `origin: { kind: "Studio" | "Provider", sourceId?, configurationReadOnly }`; provider identity is server-owned. Provider-owned associations identify their own origin separately.
- `desiredState: "Paused" | "Enabled" | "Deleted"` and `effectiveState: "Paused" | "Enabled" | "EnabledUnavailable" | "Unavailable" | "Deleted"`, with `findings` and separate `subscriptionCount`/`eligibleSubscriptionCount` for authorized scope.
- `http`, only for HTTP: `{ mode: "WorkflowResponse" | "AsyncFanOut", admissionPolicy?, proposedEndpoint, reservationState, reservedEndpoint? }`. Async policy is `RequireAll` by default or explicit `AcceptAvailable`; other modes have no applicable policy. Reservation state is `Unreserved`, `Reserved` or `Conflict`. Proposed and actual reserved endpoints are distinguishable.
- `schedule`, where applicable: current generation, timezone, next due UTC instant and displayed offset, and the reviewed missed/pause policy identifiers. A failed preview has a finding rather than an invented next due time.
- `actions`, links and safe `lastDelivery` summary if delivery read is allowed. Configuration ownership, desired state and runtime readiness are separate facts.

`SubscriptionView` contains `id`, `triggerId`, `revision`, `enabled`, `origin`, `target: { definitionId, slotName }`, `mappings`, invocation grant state, target/compatibility findings, links and actions. Its safe `triggerSummary` includes effective state and the HTTP mode/admission policy where applicable, so either association surface can display the same policy without inventing a per-subscription override. Its `serving` projection has the observed slot revision, publication/version, model/profile and input-contract revision when available. Those are observations, never accepted from a caller as authority. The selected slot defaults visibly to `default`; model identity is resolved by its trusted target adapter.

Compatibility is `Valid`, `ValidationPending` or `Blocked`, with field findings and a checked-contract revision. A never-published/unavailable target can be saved only as a disabled association with `ValidationPending` and a publish/select-slot recovery action. A previous serving contract may be shown as historical evidence but cannot authorize enabling or validate new admission.

`TargetView` contains the authorized logical identity/slot, current serving observations, declared input contract, adapter profile/ordinary-entry/HTTP-response capabilities and readiness findings. Reuse the existing [`WorkflowExecutableInputContract`](../../../Essentials/src/Workflows/Runtime/Core/Models/WorkflowExecutableInputContract.cs): versioned case-sensitive names, shared type aliases/collection shapes, required flags and detached JSON defaults. Project absent contract and explicitly empty contract distinctly. Serving material has no second editable contract in this API.

Target selection reuses authorized workflow/slot discovery. A Runtime-only host lists its known Runtime targets without querying Design. An optional normal Design discovery adapter can identify authorized never-published logical workflows for authoring; its absence must not break Runtime intake/recovery. An arbitrary unknown identifier cannot be saved or granted by treating it as an authorized never-published workflow.

## Create and revisioned changes

Creation accepts `{ requestId, name, description?, typeKey, schemaVersion, configuration, http? }`. Type/schema must be installed for authoring and configuration must validate. HTTP mode is required, and `admissionPolicy` is accepted only in async mode. Creation is always paused with no subscriptions, no invocation authority and an unreserved proposed endpoint. `typeKey` is immutable; changing family creates another resource rather than migrating live source identity implicitly.

Every subsequent configuration, association, authority, enable/pause, delete or recipient-control mutation uses preview/apply. This is a bounded trigger command contract, not a general job/planning API. The tagged command union is:

| `kind` | Command-specific fields and checks |
|---|---|
| `SaveConfiguration` | Complete editable name/description/configuration and HTTP mode/policy; provider-owned fields are refused. A live route/schedule change replaces its effective revision atomically or preserves the previous one. |
| `AddSubscription` | `target`, `mappings`, desired `enabled` (default false), `invocationRequest: "None" | "Grant"` (default None); reject duplicate trigger/definition/slot. Enabling requires a permitted grant request and all readiness checks. An unavailable/unpublished target can only be saved disabled. The apply result provides the server-owned association ID. |
| `SaveSubscription` | `subscriptionId`, `expectedSubscriptionRevision`, complete target/mappings, desired `enabled` and `invocationRequest: "Keep" | "Grant"` (default Keep). Changing enabled state additionally requires operate permission. A proposed enabled association must validate against its real serving contract and grant; an unavailable/unpublished target is saved disabled. |
| `RemoveSubscription` | Association ID/revision; retain accepted recipients/history. Removing the last row atomically pauses the trigger. |
| `EnableTrigger`, `PauseTrigger` | Desired operational transition. Enable requires at least one eligible enabled association and actual source/readiness support. |
| `EnableSubscription`, `PauseSubscription` | Association ID/revision; enabling requires its real serving contract, valid mappings, source/model capability and active invocation grant. |
| `GrantInvocation`, `RevokeInvocation` | Association ID/revision and `expectedGrantRevision` (or explicit absent grant); manager intent only. Server constructs/revises/revokes the resource-owned grant after authorization. |
| `DeleteTrigger` | Stop new admission, tombstone configuration, release its reserved route and retain accepted responsibility/history. Cancellation is not implied. |
| `RetryRecipients`, `CancelPendingRecipients` | `deliveryId`, `expectedDeliveryRevision`, explicit recipient IDs and their observed revisions; permitted only for the projected eligible recipients. See delivery control below. |

The subscription save's optional `invocationRequest: "Grant"` is an explicit manager instruction to create/replace resource authority in that same protected change; it requires both subscription-management and grant authorization. Preview identifies that effect, and the UI explains it. `None`/`Keep` never manufacture authority. This avoids an arbitrary command batch or a partly saved association/grant operation. A caller cannot submit a grant record, `granted: true`, a system principal or saved-delivery authority.

Preview request example:

```json
{
  "expectedRevision": 14,
  "command": {
    "kind": "SaveSubscription",
    "subscriptionId": "subscription-42",
    "expectedSubscriptionRevision": 3,
    "enabled": false,
    "target": { "definitionId": "reconcile-accounts", "slotName": "default" },
    "mappings": [
      { "inputName": "AccountId", "binding": { "kind": "SourceField", "path": ["payload", "accountId"] } }
    ]
  }
}
```

Preview returns `{ previewId, expiresAt, observedRevision, commandFingerprint, canApply, impact, findings }`. It captures the exact typed command, subject/scope, resource/subscription/grant revisions, observed target contracts/slots and relevant route/schedule state. `impact` identifies affected workflows/slots and current/proposed state, endpoint reservations, next firing, invalid/disabled associations, grant invalidation, accepted pending/running counts and retention/cancellation effects. Insufficient permission to review the affected change makes it unappliable; do not hide an impacted target and still claim a complete review.

Apply accepts `{ requestId, previewId, expectedRevision, command }` with the exact previewed command. Recheck permissions, expiry, command fingerprint and relevant observations at the protected transition. The preview is neither authorization nor a lease on a publication. Return `409` with a safe stale-preview/current-revision finding when re-review is required. Return validation findings when it cannot apply. A refusal leaves prior effective configuration, associations, reservation and schedule intact; no partly applied live configuration is a successful result.

Apply returns `200 { requestId, disposition: "Applied" | "AlreadyApplied", revision, trigger?, subscription?, effects }`. Creation returns `201`; all other state-changing commands use this committed receipt. `effects` states what changed and what remains owed; it does not announce workflow start merely because a management command succeeded. Deleted resources disappear from normal lists; their retained tombstone can still authorize delivery inspection/control until the retention lifecycle permits collection. A later unknown/deleted-resource request never becomes permission to erase delivery retention roots.

`requestId` is a required bounded non-control string (maximum 128 characters, matching the existing redrive convention). Persist mutation identity/result in the same owned transition. A retry in the same authenticated tenant/subject/operation scope returns the original committed result before stale-revision evaluation, subject to current disclosure authorization. The same identity with different command content is `409`. Losing the response cannot cause creation of a second resource/subscription or a second retry generation. Preview itself has no idempotent business effect.

## Resource-owned invocation authority

A grant belongs to the trigger/subscription and scopes that resource to the selected logical workflow/slot and allowed source use. The API exposes `{ state: "Absent" | "Granted" | "Revoked" | "Unavailable", revision?, scopeFingerprint?, grantedAt?, grantedBy?, revokedAt?, revokedBy?, findings }` within authorized views. Creation/grant actors are audit facts. Departure, deletion or later role/permission loss of either person does not disable the resource or revoke its grant automatically.

At grant/update time, validate the acting manager's current authority and resource/target scope. The grant scopes the exact trigger/subscription, immutable source type, logical target/slot, model and mappings; it does not scope a configuration revision. Configuration edits within that source type, including HTTP mode/policy/endpoint and recurrence settings, preserve the existing grant without grant authorization. Target, mapping or source-type scope changes cannot silently expand an existing grant: preview identifies required scope revision, and apply either includes an explicit authorized replacement grant or leaves the association blocked for fresh admission. Preview shows safe changed settings and actual retained, replaced, revoked or cleared grants, redacts descriptor-sensitive values and never fabricates a proposed firing time when no calculator is composed. Provider-owned grants/configuration expose origin and allowed operations; ordinary API updates cannot overwrite them. Provider updates preserve a local operational pause.

Every fresh occurrence still checks the current resource-owned grant state/revision, current target publication and ordinary host start policy. Explicit revoke prevents fresh acceptance through that grant. It does not cancel accepted work or impersonate the granting manager during recovery. Capture grant revision and server-owned execution attribution in accepted responsibility; retain no bearer token, cookie, copied role list or serialized principal. A source caller cannot substitute a target or grant. An unsupported resource-authority provider reports `Unavailable` and refuses fresh acceptance instead of falling back to a human account.

## Mapping and safe preview

Mappings are a list keyed by exact declared `inputName`; unknown/duplicate destinations refuse. The proposed wrapper has three binding forms: `Literal` with a JSON `value`, `SourceField` with typed descriptor-selected path segments, or `Expression` with an accepted portable expression template and named parameter selectors. Each parameter selector is only literal JSON or a source-field path. The server realizes those selectors into ordinary literal parameters for the existing evaluator under `binding-pure-v1`, as specified in [the target mapping contract](targets.md). This wrapper does not add an expression binding discriminator or change expression serialization, conversion, null/default or binding semantics. Unsupported pure evaluation is a visible limitation; do not expose ambient workflow/activity state, a clock, network access or a service locator to make an expression run.

Omitted mapping/value and explicit JSON null remain distinct. Source-field absence is represented as absence under the shared binding rules, not coerced to null. Apply the target's ordinary validator/defaults and preserve exact JSON numeric values; JavaScript parsing must not round an Int64/UInt64/Decimal literal before submission. Resource/secret references use existing binding/authorization rules; preview and persisted revisions never disclose raw secret material.

Type descriptors include their configuration schema/version, source envelope field/type schema, generic editor hints, supported mapping expressions and source/model/HTTP-mode readiness. The bounded source envelope contains source payload and authorized source-specific fields plus server-owned read-only trigger/occurrence facts. HTTP metadata, Event data and schedule occurrence data have family descriptors; headers/credentials and protected facets follow normal host capture policy. No payload field can override tenant, principal, grant, trigger identity or selected target.

`mapping-context` accepts `{ expectedRevision, target, mappings }` for an existing trigger, including an unsaved association. It returns authorized source schema, actual target input contract/revision, declared template-parameter schema, available portable languages/profiles and safe availability findings. This is the trigger-owned authoring context; it never submits a fabricated workflow draft/activity ID to the workflow-specific expression tooling API. Editor widgets can consume these facts and the preview diagnostics. Rich language tooling is advertised only where its installed provider actually supports this context; a syntax editor alone is not advertised as semantic code intelligence.

`mapping-preview` accepts `{ expectedRevision, subscriptionId?, expectedSubscriptionRevision?, target, mappings, sample }`. It supports an existing association or an unsaved proposed association. The server constructs the sample context from the selected descriptor/configuration and explicitly synthetic sample payload/fields; callers cannot inject authoritative provenance. Supply the observed target-contract revision when available; changed contract/scope returns a stale finding rather than validating another target silently.

Return `{ advisory: true, sourceSchema, target, contractRevision, fields, findings }`. Each field has `inputName`, shared type, `state: "Mapped" | "Defaulted" | "Omitted" | "Invalid" | "Redacted"`, and only policy-permitted preview values. Findings carry `code`, `message`, `inputName?` and a command/source field pointer without echoing rejected secrets or raw payload. Safe preview has bounded sample size, expression work and output. Unsupported/missing modules return a visible unavailable result, never an empty successful validation. Preview reserves nothing, starts nothing and stores no authoritative delivery. Actual admission evaluates once against current protected facts and retains the normalized inputs; retry reads that capture.

## Delivery evidence and control

`DeliveryView` has its own `id`/`revision`, trigger identity, the current resource `triggerRevision` (including retained deleted-resource tombstones), and the separate accepted configuration revision, source occurrence identity, timestamps, admission policy/result, recipient outcome rows, safe findings and links. It distinguishes a refused source attempt from a committed admitted delivery. Failure to persist an attempt cannot produce an invented durable receipt; return the normal request correlation ID and mark evidence persistence unavailable.

This is an API projection over the ordinary Runtime outbox source, captured subscription selection and progress/target receipts defined in [the Runtime contract](runtime.md). It is not a second mutable delivery ledger or another delivery table. Source/recipient evidence keeps its owning persistence version and authority; a view DTO cannot be posted back to create trusted state.

Use distinct evidence dimensions:

| Dimension | Meaning |
|---|---|
| Admission | `Refused`, `Accepted` or `PartiallyAccepted`, with accepted/refused counts. Accepted time exists only after durable responsibility commits for the selected profile. |
| Recipient admission | `Accepted` or `Refused`, exact captured subscription/grant/slot/publication/version/model/inputs identity for accepted rows, and safe reason for refused rows. A refused row is not owed work. |
| Recipient dispatch | `NotDispatched`, `DispatchCommitted`, `AdmissionUnknown`, `Queued`, `Started`, `Failed` or `Cancelled`. `DispatchCommitted` means the immutable source dispatch seal won; source recovery still owes its exact target command, which may not be queued yet. `Queued` requires the target runtime's committed enqueue/admission receipt; `Started` requires observed target-runtime execution evidence. A seal or planned deterministic execution ID proves neither. |
| Execution | Independently observed runtime/model status, its observation time and an authorized exact-run link. Missing or expired execution evidence remains unknown/unavailable rather than inferred complete. Native and model identities retain their real kind. |
| HTTP transport | `NotApplicable`, `AwaitingResponse`, `WriteCompleted`, `ClientDisconnected`, `TimedOut`, `WriteFailed` or `ReceiptUnknown`, plus observed status/timestamps where known. Server write completion does not assert that a client consumed the response. |
| Recovery/control | Attempt count, last safe failure, next retry, current action availability and explicit retry/cancel outcome. Accepted responsibility remains visible when blocked by a current host policy. |

Separate `plannedExecutionIdentity` from `executionReference`; populate the latter only with the adapter's authoritative evidence and safe inspection link. Never label an API's intended dispatch as queued/started, or workflow completion as HTTP delivery. Captured target/input/grant revisions remain inspectable after current association changes, publication replacement/unpublication or trigger deletion, subject to current diagnostic access.

`RetryRecipients` requests progress only for eligible owed recipients with the captured target/inputs and stable identities. Return `RetryRequested`, `AlreadyRequested`, `NotEligible` or `AlreadyAdmitted` per selected recipient; a successful retry request does not mean execution started. An admission-unknown row is reconciled through retained target receipts before deciding whether another target command is required.

`CancelPendingRecipients` uses a distinct permission and the source-row dispatch-seal fence in [the Runtime contract](runtime.md). It changes only source `Pending` to `CancelledBeforeDispatch`; return `Cancelled`, `AlreadyCancelled`, `DispatchCommitted`, `TargetAccepted` or `NotEligible` per recipient. Once the immutable dispatch seal wins, cancel-pending reports `DispatchCommitted` even when no target queue work exists yet; it must not claim queued/started, revoke the seal or erase the obligation. After target admission, cancelling execution uses the ordinary separate execution-cancel operation. An uncertain read is unavailable/retryable, never a fabricated cancellation. The impact review identifies exactly the selected owed recipients and distinguishes unsealed, sealed and running work. Changed observed revisions require re-review; confirmed per-recipient outcomes remain available through replay of the same request ID.

## HTTP source contract

HTTP source routes are resolved from the configured normal endpoint mount, not this management prefix. A newly created paused resource is visibly unreserved. First successful enable atomically acquires its normalized method/route claim; paused previously enabled resources retain that claim and return `503` unavailability for new source work. A reviewed route change atomically exchanges claims or preserves the old one. Embedded and standalone owners compete through the same namespace in both directions. Deletion releases ingress ownership while retaining already accepted work.

Workflow-owned mode requires exactly one enabled compatible response-owning subscription; unsupported target response capability refuses activation. It uses that workflow's actual response and the bounded request/transport contract. A timeout, disconnect or missing response instruction has its own outcome and does not cancel accepted workflow work automatically.

Async mode has one explicit policy per trigger, visible in resource/association context and impact review:

- `RequireAll` is default. If any enabled subscriber cannot be admitted, commit no recipient acceptance and return refusal. Intentionally paused subscriptions are not promised recipients; disabled counts remain visible in management.
- `AcceptAvailable` commits the healthy subset and returns explicit partial acceptance with accepted/refused rows. Zero accepted recipients is a failed request under either policy.
- After acceptance, dispatch/retry/recovery remain independent under both policies. A dispatch failure after acceptance never rolls back a successful sibling or changes admission into a claim of business completion.

Both policies evaluate a complete consistent selected set. An unavailable/corrupt source, subscription query, publication read or required persistence boundary is admission failure, not a safely refused recipient. `AcceptAvailable` cannot accept a truncated or unknown cohort and call it partial success.

Successful async ingress returns `202` only after the protected admission/retention commit:

```json
{
  "deliveryId": "delivery-91",
  "admission": "PartiallyAccepted",
  "admissionPolicy": "AcceptAvailable",
  "acceptedCount": 1,
  "refusedCount": 1,
  "recipients": [
    { "recipientRef": "recipient-a", "admission": "Accepted", "dispatch": "NotDispatched" },
    { "recipientRef": "recipient-b", "admission": "Refused", "reasonCode": "TargetUnavailable" }
  ],
  "statusLocation": "runtime/standalone-trigger-deliveries/delivery-91"
}
```

The same shape uses `Accepted` when all promised recipients were accepted. The receipt's per-occurrence opaque recipient references and safe reason codes disclose the promised outcomes without exposing protected workflow names, IDs, payloads or management identifiers. Full target/evidence details require their ordinary authorization. The status endpoint enforces current authorized read policy; source intake, including a deliberately public HTTP route, does not grant management delivery browsing. Where the host supplies scoped source-receipt access, it must be limited to the submitting identity/occurrence through normal authorization, not an unrestricted bearer URL.

For `RequireAll` refusal, report no accepted rows; identify failing recipients and mark otherwise-valid recipients `NotAcceptedDueToPolicy`, not queued. For either policy with zero accepted, return a failure with `acceptedCount: 0` and explicit outcome rows, never `202`. Invalid request/mapped input is `400`; temporary target/readiness/storage unavailability or paused/zero-eligible state is `503`. Changed idempotency content is `409`; source authentication/access and request size use ordinary `401`/`403`/`413`. A transient failure to accept cannot be represented by a successful management receipt or hidden behind an acknowledgement.

## Errors and qualification obligations

Use the owning API's safe problem/fault pipeline with stable `code`, `message`, optional field findings, request correlation and safe current revision when authorized. Distinguish malformed/invalid input (`400`), unauthenticated (`401`), denied (`403`), masked missing resource (`404`), stale review/duplicate association/route or idempotency conflict (`409`), body limit (`413`), unsupported/unavailable physical readiness (`503`) and unexpected failure (`500`). A response lost after commit is an uncertain client observation; replay the same mutation/intake identity to reconcile instead of generating another operation.

Before acceptance, demonstrate both association surfaces using these same commands; durable replay/conflict behavior; atomic failed updates and first-enable/pause/route/deletion behavior; grant survival after creator departure plus explicit manager revoke; authorization/tenant isolation; safe mapping/null/numeric handling; RequireAll/AcceptAvailable and zero-accepted failures; honest recipient/transport projections under partial dispatch, uncertainty and restart; actual Design-only/Runtime-only composition; and an independent module consumer. See the linked UX and architecture qualification records. This document supplies no passing evidence for those checks.
