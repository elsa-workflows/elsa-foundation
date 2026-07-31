# Phase 1 Data Model — Publishing engine / API split

This is a structural refactor, so the "entities" are **packages, features, and the relocated contract**, not persisted data. No database schema, entity, or migration changes.

## Feature units

| Unit | Package | Kind | Registration surface |
|---|---|---|---|
| `WorkflowsPublishing` (NEW) | `Elsa.Workflows.Publishing` | Layer-3 engine feature | `public`, non-sealed, `virtual ConfigureServices`. Registers the whole engine: compiler + collaborators, activator/reconciler/preflight, in-memory publication/executable stores + readers, layout/structure, deletion guard, template registries, activity publishers, test-run services, `Collect*` event handlers, `AddRequestHandlersFrom(engineAssembly)`, and the **neutral** `IActivityPublishingAuthorizationContext` default. `DependsOn { WorkflowsRuntimeTriggers, Events }`. |
| `WorkflowsPublishingApi` (SLIMMED) | `Elsa.Workflows.Publishing.Api` | Layer-3 transport feature | `public`, non-sealed. `: WorkflowsPublishingFeature`. `base.ConfigureServices(services)` then: `AddHttpContextAccessor`, `IActivityPublishingAuthorizationContext → HttpContextActivityPublishingAuthorizationContext` (override), FastEndpoints endpoints (via base `FastEndpointsFeatureBase`), `AddApiCapability` + `AddApiCapabilitySource`. `DependsOn { WorkflowsPublishing, ApiCapabilities }`. |
| `Elsa.Workflows.Publishing.Core` (GAINS) | `Elsa.Workflows.Publishing.Core` | Layer-1 contracts | Gains `PublishWorkflow` request + `PublishedWorkflowView` response (relocated). No new external deps. |

## Relocated contract (the only "moved data shape")

- **`PublishWorkflow`** — `IRequest<PublishedWorkflowView>`. From `Elsa.Workflows.Publishing.Api.Requests` → `Elsa.Workflows.Publishing.Core.Requests`.
- **`PublishedWorkflowView`** — response record. From `Elsa.Workflows.Publishing.Api.Models` → `Elsa.Workflows.Publishing.Core.Models`. Depends only on `Core.Models` + `Runtime.Core.Models` (Core-clean).
- **Wire preservation**: the FastEndpoints HTTP request/response DTOs (`PublishWorkflowRequest`, endpoint route, `PublishedWorkflowView` JSON shape) are unchanged on the wire; only the CLR namespace of the mediator command + view moves. Per §E6 scope note, persisted/serialized identifiers are preserved.

## New service

- **`NeutralActivityPublishingAuthorizationContext`** (name provisional) — engine-owned default impl of `IActivityPublishingAuthorizationContext` (contract already HTTP-neutral: `TenantId` + `CanAccessTenant`). Provides a no-HTTP tenant context so the activity-draft engine services resolve in an engine-only shell. Overridden by `HttpContextActivityPublishingAuthorizationContext` when the Api feature is composed. Replacement contract (framework §2.6.2): exactly one active; override via `RemoveAll`+`AddScoped`.

## Dependency direction (unchanged invariants)

- Engine `Elsa.Workflows.Publishing` may reference `Design.Persistence.Core`, `Design.Validations.Core`, `Runtime.Core`, `Locking.Core`, `Events.Core`, `Mediator.Core`, `Publishing.Core` — Publishing is the §E2.2 bridge (neither Design nor Runtime), so this does not violate the `Runtime.* → Design.*` hard rule.
- No new `Runtime.* → Design.*` edge is introduced. `BridgeDependencyDirectionTests` forbidden-list is asserted against the engine assembly too.
- Deployment shapes (§E2.2.3) preserved: **Design-only** unaffected; **Runtime-only** improved (engine composable without endpoints); **combined** unchanged (Api still brings the engine via inheritance).
