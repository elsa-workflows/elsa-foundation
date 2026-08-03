# Phase 1 Data Model — Publishing engine / API split

This is a structural refactor, so the "entities" are **packages, features, and the relocated contract**, not persisted data. No database schema, entity, or migration changes.

## Feature units

| Unit | Package | Kind | Registration surface |
|---|---|---|---|
| `WorkflowsPublishing` (NEW) | `Elsa.Workflows.Publishing` | Layer-3 engine feature | `public`, non-sealed, `: IShellFeature`, `virtual ConfigureServices`. Registers the **auth-free workflow-publish + compile engine**: compiler + collaborators, activator/reconciler/preflight, in-memory publication/executable stores + readers, layout/structure, deletion guard, template registries, the `Collect*` event handlers, `AddRequestHandlersFrom(engineAssembly)` (workflow-publish/test-run/slot-lifecycle/preflight handlers), `TimeProvider`. `DependsOn { WorkflowsRuntimeTriggers, Events }`. Registers **no** authorization or activity-draft services. |
| `WorkflowsPublishingApi` (SLIMMED) | `Elsa.Workflows.Publishing.Api` | Layer-3 transport feature | `public`, `: FastEndpointsFeatureBase` (**unchanged base**). `DependsOn { WorkflowsPublishing, ApiCapabilities }`. `ConfigureServices`: `base.ConfigureServices` (FastEndpoints) + FastEndpoints endpoints + `AddApiCapability`/`AddApiCapabilitySource` + `AddHttpContextAccessor` + `IActivityPublishingAuthorizationContext → HttpContextActivityPublishingAuthorizationContext` + the activity-draft publish/test-run services. No engine (workflow-publish) registrations — those arrive via `DependsOn`. |
| `Elsa.Workflows.Publishing.Core` (GAINS) | `Elsa.Workflows.Publishing.Core` | Layer-1 contracts | Gains `PublishWorkflow` request + `PublishedWorkflowView` response (relocated). No new external deps. |

## Relocated contract (the only "moved data shape")

- **`PublishWorkflow`** — `IRequest<PublishedWorkflowView>`. From `Elsa.Workflows.Publishing.Api.Requests` → `Elsa.Workflows.Publishing.Core.Requests`.
- **`PublishedWorkflowView`** — response record. From `Elsa.Workflows.Publishing.Api.Models` → `Elsa.Workflows.Publishing.Core.Models`. Core-clean.
- **Wire preservation**: the FastEndpoints HTTP DTOs, route, and JSON shape are unchanged on the wire; only the CLR namespace of the mediator command + view moves (§E6 scope note preserves serialized identifiers).

## No new service

Authorization stays at the transport boundary. `IActivityPublishingAuthorizationContext` keeps its single `HttpContextActivityPublishingAuthorizationContext` implementation, registered by the **Api** feature only. The engine neither registers nor depends on it — so **no neutral default is introduced**. (Previously-drafted `NeutralActivityPublishingAuthorizationContext` is withdrawn.)

## Composition mechanism

- **`DependsOn` (§2.11), not inheritance.** The API feature keeps its `FastEndpointsFeatureBase` base and declares `DependsOn WorkflowsPublishing`; the shell activates both features; each runs its own `ConfigureServices` against the shared collection. Ordering: `DependsOn` configures the engine before the API, so the API's transport registrations layer on top cleanly.

## Dependency direction (unchanged invariants)

- Engine `Elsa.Workflows.Publishing` may reference `Design.Persistence.Core`, `Design.Validations.Core`, `Runtime.Core`, `Locking.Core`, `Events.Core`, `Mediator.Core`, `Publishing.Core` — Publishing is the §E2.2 bridge (neither Design nor Runtime), so this does not violate the `Runtime.* → Design.*` hard rule.
- No new `Runtime.* → Design.*` edge is introduced. `BridgeDependencyDirectionTests` forbidden-list is asserted against the engine assembly too.
- Deployment shapes (§E2.2.3) preserved: **Design-only** unaffected; **Runtime-only** improved (engine composable without endpoints); **combined** unchanged (Api still brings the engine via `DependsOn`).
