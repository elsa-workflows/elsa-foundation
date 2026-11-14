# Standalone trigger management API

`StandaloneTriggersApiFeature` owns management endpoints, permission contributions, actor projection and capability `elsa.api.standalone-triggers` v1. It is independent of `WorkflowsRuntimeApiFeature` and does not call `AddWorkflowRuntime()`, install source intake or start a scheduler. The [operator guide](../../../../../../docs/standalone-triggers.md) contains the canonical request examples and recovery instructions. This API is an implementation candidate; host, provider, permissions and Studio acceptance remains open.

## Composition and discovery

Select feature `StandaloneTriggersApi` with its `ApiCapabilities` and `Events` dependencies. Its authoring subset collects installed type sources/validators and initializes the metadata catalog. The selected host must also supply exactly one `IStandaloneTriggerStore`, `IStandaloneTriggerTargetDiscovery` and `ITriggerInputMapper`. Without them, the feature does not advertise its capability. The normal Runtime composition supplies these services; an authoring-only host must supply suitable replacements explicitly.

The default `AuthoringTriggerCommitter` accepts only never-activated paused resources with disabled associations. When a protected `ITriggerSourceResourceCommitStore` is present, post-configuration selects `ProtectedTriggerManagementCommitter`. Source-specific activation still requires an installed producer descriptor and compatible physical projection/storage. Duplicate selected resource, management-committer, type-catalog, target-discovery or mapper registrations fail configuration.

Capability links are relative to the shell API base. The capability is independent of caller permission; views include authoritative findings and advisory allowed actions. Delivery links appear only when the actual `IRuntimeIngressDeliveryStore` supports inspection. The endpoint repeats all permission, scope and revision checks.

## Routes and current request shapes

| Route suffix | Operations |
|---|---|
| `runtime/standalone-triggers/identity` | GET current actor's coarse permission projection |
| `runtime/standalone-trigger-types` | GET installed authoring type/schema/configuration metadata |
| `runtime/standalone-triggers` | GET resource page; POST create paused resource |
| `runtime/standalone-triggers/{triggerId}` | GET resource/configuration, source readiness and allowed actions |
| `runtime/standalone-trigger-subscriptions` | GET canonical association page; exactly one of `triggerId` or `definitionId`, optionally `slotName` with the latter |
| `runtime/standalone-trigger-subscriptions/{subscriptionId}` | GET association, grant, compatibility and serving projection |
| `runtime/standalone-trigger-targets` | GET target page |
| `runtime/standalone-trigger-targets/{definitionId}/{slotName}` | GET serving slot and declared input contract |
| `runtime/standalone-triggers/{triggerId}/changes/preview` | POST reviewed configuration, association, grant, operation or delivery-control command |
| `runtime/standalone-triggers/{triggerId}/changes/apply` | POST identical reviewed command with `requestId`, `expectedRevision` and `previewId` |
| `runtime/standalone-triggers/{triggerId}/mapping-context` | POST target selector; returns source fields and target contract |
| `runtime/standalone-triggers/{triggerId}/mapping-preview` | POST current revision, target, mappings and sample envelope; no admission or execution |
| `runtime/standalone-trigger-deliveries` | GET authorized physical delivery page |
| `runtime/standalone-trigger-deliveries/{deliveryId}` | GET recipient/transport evidence and linked execution when permitted |

Pages return `{ items, next }`; `limit` defaults to 25 and is bounded to 1–100. Resource queries support `search`, `typeKey`, `state` and opaque `after`; delivery queries currently support `triggerId` and `after`, with no outcome/time-range filters. There is no generic management invocation endpoint: real inputs enter the actual HTTP, Event or schedule source.

Create requests use string `schemaVersion` and `requestId`. HTTP requests put reserved `http` settings alongside `configuration`; the API persists them together and projects them separately on reads. Change previews last five minutes and bind the current actor/scope, resource, exact command and observed serving targets. Idempotent apply replay uses the retained mutation receipt before revision comparison; changed request content conflicts. `SaveSubscription` and single-association operations also require `expectedSubscriptionRevision`; grant/revoke requires `expectedGrantRevision` (null for an absent grant).

`SaveConfiguration` preserves invocation grants within the resource's immutable source type; HTTP mode/policy/endpoint and schedule settings do not replace target or mapping authority. Target/mapping edits still clear the grant unless explicitly authorized replacement is requested, and explicit revocation remains effective. Preview includes safe setting differences and actual grant consequences. Descriptor-sensitive values are redacted; without a composed next-firing calculation, schedule impact states that the source recomputes its firing on commit or enable and supplies no invented timestamp.

`mapping-context` advertises the actual installed `IPortableExpressionHandler` languages, each with evaluator availability and an unavailable reason when applicable. With no installed handlers the list is empty. The binding model supports Literal, SourceField and Expression; the latter still requires a composed portable evaluator with a pure capability profile and supported language/template. Preview is advisory and evaluates the detached `sample` envelope only. No evaluation executes a workflow.

Delivery controls share change preview/apply and require the latest delivery/recipient revisions. Retry preserves the accepted selection and command identity. Cancel-pending can cancel only selected unsealed new-root responsibility; committed dispatch and ordinary saved waits remain owed. Delivery inspection does not infer queued/running from a planned execution ID or dispatch seal. Execution details require normal workflow read permission, and source deletion retains tombstone authority for history reads.

## Access and source ownership

`TriggerRequestAccess` binds normalized current subject/tenant and one ordinary persistence scope from trusted request services. It evaluates resource permissions through Foundation Identity. Configuration JSON never chooses actor, tenant, target captures or grants. The seven `triggers:*` / `triggers/deliveries:*` permissions, grant requirements, provider-owned restrictions and accepted-work limits are documented in the [operator guide](../../../../../../docs/standalone-triggers.md#grants-and-publication-changes).

The API owns no durable delivery store or second queue. It reads/writes through the actual selected physical participant, and unavailable capabilities refuse explicitly. Replacing internal projection services or API DTOs is not a public extension path; module authors use the [Core source/model/store contracts](../../Elsa.Workflows.Runtime/EXTENSION_POINTS.md#standalone-trigger-authoring-and-source-boundaries).

Schedule change review uses the installed module's pure `IStandaloneSchedulePlanner` and the actual owned cursor. It shows current and proposed UTC instants, timezone, offset and preserve/replace/pause/remove action when available. Missing or incompatible readers/planners produce an explicit unavailable preview. Review does not reserve a firing or change a cursor; apply rechecks the resource through the normal physical writer.

## Cross-domain contributions

`TriggerPermissionContributor` contributes seven management/delivery grants to Foundation Identity's `IPermissionContributor`; `StandaloneTriggerCapabilitySource` contributes conditional links through API Capabilities' `IApiCapabilitySource`. `StandaloneTriggersApiFeature` owns both registrations. Neither contribution starts intake.

Resource views omit descriptor-sensitive configuration values and identify their exact paths in `configurationRedactions`. Saving configuration preserves omitted protected fields; an explicitly supplied value (including null where the owning validator permits it) replaces the retained value. Impact-changing configuration/enable edits require read access to affected targets at preview and apply. Resource-owned stop/revoke operations retain safe redacted impact.
