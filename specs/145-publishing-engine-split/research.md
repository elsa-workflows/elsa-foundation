# Phase 0 Research — Publishing engine / API split

Blast-radius map against current `main` (branch `145-publishing-engine-split`). Baseline: `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApiFeature.cs:44` (`ConfigureServices` lines 46-120). No `WorkflowsPublishing` engine feature exists yet.

## Decision 1 — The publish command + its response both move to `Publishing.Core`

- **Decision**: Move `PublishWorkflow` (`Api/Requests/PublishWorkflow.cs:7-13`, `IRequest<PublishedWorkflowView>`) **and** its response `PublishedWorkflowView` (`Api/Models/PublishedWorkflowView.cs:7`) to `Elsa.Workflows.Publishing.Core`.
- **Rationale**: A relocated `IRequest<T>` drags its `T`. `PublishedWorkflowView` is Core-clean (depends only on `Core.Models` + `Runtime.Core.Models`), so the move is legal under §2.1. Ctor param `PublicationAction` already lives in `Core/Models/PublicationLifecycle.cs`.
- **Blast radius**: exactly **2 production references** — the endpoint alias `using PublishWorkflowCommand = …Requests.PublishWorkflow` + `new PublishWorkflowCommand(...)` (`Api/Endpoints/PublishWorkflow.cs:13,32`) and the handler signature (`Api/Handlers/PublishWorkflowRequestHandler.cs:17`). Plus test constructions in `Publishing/Api/Tests/` (handler tests, trigger-indexing tests). SemVer: **MAJOR** for the affected package(s) per §4.2.
- **Do NOT move**: `PublishWorkflowRequest` (`Api/Requests/PublicationManagementRequests.cs:34`) — the FastEndpoints HTTP body DTO; transport-only, stays in Api.

## Decision 2 — The workflow-publish handler moves to the engine; it is HTTP-free

- **Decision**: Move `PublishWorkflowRequestHandler` (+ sibling orchestration handlers `PublishActivityDraftRequestHandler`, `StartWorkflowTestRunRequestHandler`, `PublicationSlotLifecycleRequestHandlers`, `RunRuntimeRequirementPreflightRequestHandler`, and the `OnExecutableCompilationCollecting` / `OnExecutableNodeMetadataCollecting` event handlers) into the engine feature.
- **Rationale**: `PublishWorkflowRequestHandler`'s ctor (`Handlers/PublishWorkflowRequestHandler.cs:18-38`) has **no `IHttpContextAccessor` and no `IActivityPublishingAuthorizationContext`**; tenant arrives as plain data (`request.TenantId`), resolved at the endpoint by `PublicationRequestTenant.Resolve(User)` using `ClaimsPrincipal`. So the workflow-publish path is transport-independent and moves cleanly.
- **Assembly-scan boundary**: the handlers are discovered by `AddRequestHandlersFrom(assembly)` (feature line 115) scanning the **Api** assembly today. After the move, the **engine** feature calls `AddRequestHandlersFrom(engineAssembly)`; the Api feature keeps its own scan for any endpoint-local handlers. Miss this → the publish handler is silently unregistered (guard: the registration golden test at §2.23.1 and `WorkflowsPublishingApiFeatureTests.cs:29-53`).
- **Exception types** (`PublicationPreflightConflictException`, `PublicationActivationException`, `ExpressionPublicationValidationException`, `WorkflowExecutableCompilationException`) move with the handler into the engine and remain visible to the endpoint's ProblemDetails catch blocks (`Api/Endpoints/PublishWorkflow.cs:42-89`) — Api references the engine, so this holds.

## Decision 3 — HTTP-coupled services stay in Api; the engine gets a neutral auth-context default

- **Decision**: `AddHttpContextAccessor()` (line 52) and `IActivityPublishingAuthorizationContext → HttpContextActivityPublishingAuthorizationContext` (line 53) **stay in the Api feature**. The engine registers a **new neutral (non-HTTP) default** for `IActivityPublishingAuthorizationContext`; the Api overrides it with the HttpContext impl.
- **Rationale**: The *activity-draft* engine services `ActivityDefinitionPublisher` (`Services/ActivityDefinitionPublisher.cs:91`) and `ActivityDraftTestRunService` (`Services/ActivityDraftTestRunService.cs:38`) inject `IActivityPublishingAuthorizationContext`. Once they live in an endpoint-free engine, DI must still satisfy that dependency without ASP.NET. The contract is already HTTP-neutral (`TenantId` + `CanAccessTenant`); the only impl today is HttpContext-based. Test fakes already model a neutral impl (`Fakes.cs:27`, `ActivityDraftTestRunTests.cs:1184` `MutableAuthorizationContext`) — the engine's default follows that shape (e.g. a permissive/no-tenant default). This is a framework §2.6.2 replacement default: engine `TryAdd`/registers the neutral default; Api uses `RemoveAll`+`AddScoped` (or registers-before-base) so the HttpContext impl wins. No silent conflict.
- **Alternative rejected**: leaving `IActivityPublishingAuthorizationContext` unregistered in the engine — rejected because the engine's own activity-draft services would fail to resolve in an engine-only shell.

## Decision 4 — Registration classification is clean; durable override seam is move-safe

- **ENGINE (move)**: lines 54–114 — `IActivityContractStorageDriverProvider`, executable + source-reference stores/readers, slot/record/policy/intent stores, policy resolver + preflight, projection preparer/activator, snapshot-review + receipt stores, layout + structure services, deletion guard, the entire compiler collaborator graph + template registries + activity publishers + test-run services, the two `Collect*` event handlers, `IWorkflowExecutableCompiler`, `TimeProvider`.
- **STAY in Api**: `base.ConfigureServices` (FastEndpoints, line 48), `AddHttpContextAccessor` (52), HttpContext auth override (53), `AddApiCapability` + `AddApiCapabilitySource` (116, 119).
- **Durable override seam is move-safe**: publication/policy/slot/intent/snapshot/receipt/draft-test-run durable stores override via `RemoveAll`+`AddScoped` (`Publishing/Persistence/Groundwork/DependencyInjection/GroundworkPublishingStoreRegistration.cs:34-67`); executable + source-reference durable stores override via `RemoveAll`+`AddScoped` in the **Runtime** groundwork lane (`Persistence/Groundwork/DependencyInjection/GroundworkRuntimeStoreRegistration.cs:57,113`), independent of this split. Because overrides are `RemoveAll` (not TryAdd-order dependent), moving the in-memory defaults into the engine does not break durability. Only requirement: the engine's `TryAdd` defaults run via `base.ConfigureServices` before any same-scope re-registration (standard inheritance order).

## Decision 5 — Downstream consumers repoint to the engine

- **Two features** DependsOn `WorkflowsPublishingApi` today and need only engine contracts: `GraphActivitiesDesignFeature` (`Activities/Graph/Design/…:17`, uses `IActivityTemplateProviderCompiler`/`…DependencyDiscoverer` registries) and `DispatchWorkflowDesignFeature` (`Activities/DispatchWorkflow/Design/…:18`, contributes `IExecutableCompilationSource`). **Repoint both to `DependsOn "WorkflowsPublishing"`** so design-only shells don't pull endpoints.
- **Shell configs** (`shells.json:163`, `shells.baseline.json:38`, `docker/compose/elsa-server.shells.json:87`) keep enabling `WorkflowsPublishingApi` — the transport feature keeps its name and (via inheritance) still brings the engine, so these remain behaviour-preserving.

## Decision 6 — Architecture-test literals need updates (golden-rule compliant)

The subject + objective of these tests are preserved; only literals change (framework §2.21.1 permits wiring/location changes):
- `tests/Elsa/Architecture/GroundworkPersistenceLifetimeTests.cs:138-140` — file-path literals for `IPublicationProjectionPreparer`/`IPublicationActivator`/`PublicationSnapshotReviewService` now point at the engine feature file.
- `tests/Elsa/Architecture/RuntimeExecutionSliceDependencyTests.cs:32` — the "does not reference Runtime.Api" assertion must also cover the new engine assembly.
- `tests/Elsa/Workflows/Publishing/Api/Tests/BridgeDependencyDirectionTests.cs:21` — the forbidden-reference list (Activities.Runtime, Activities.Design.Api, Workflows.Design.Api, Activities.Primitives) must also be asserted against the engine assembly.
- Registration golden test `WorkflowsPublishingApiFeatureTests.cs:29-53` must keep passing **unchanged** — it validates every engine service still resolves through the inherited Api feature (including the HTTP auth context at line 37). This is the end-to-end proof the inheritance wiring is correct.

## Open questions

None blocking. The neutral auth-context default's exact policy (permissive vs. deny-by-default when no HTTP tenant is present) is a design detail settled in Phase 1 / tasks — the safe choice is to mirror the existing test fake's permissive default so behaviour is preserved for the workflow-publish path (which never consults it) and the activity-draft path in an engine-only shell behaves as an unauthenticated/no-tenant context.
