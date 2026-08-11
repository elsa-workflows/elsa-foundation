# Phase 0 Research — Publishing engine / API split

Blast-radius map against current `main` (branch `145-publishing-engine-split`). Baseline: `src/Elsa/Workflows/Publishing/Api/WorkflowsPublishingApiFeature.cs:44` (`ConfigureServices` lines 46-120). No `WorkflowsPublishing` engine feature exists yet.

> **Mechanism correction (post-analysis):** the engine↔API relationship is **`DependsOn` composition (§2.11)**, not feature inheritance. The API feature keeps its `FastEndpointsFeatureBase` base and declares `DependsOn WorkflowsPublishing`; the engine is a plain `IShellFeature`. The shell activates both features when the API is enabled.
>
> **Scope correction (post-analysis):** authorization is a transport concern. The **workflow-publish + compile engine is authorization-free** and is what moves. The **activity-draft** publish/test-run services and the authorization context stay in the API feature.

## Decision 1 — The publish command + its response both move to `Publishing.Core`

- **Decision**: Move `PublishWorkflow` (`Api/Requests/PublishWorkflow.cs:7-13`, `IRequest<PublishedWorkflowView>`) **and** its response `PublishedWorkflowView` (`Api/Models/PublishedWorkflowView.cs:7`) to `Elsa.Workflows.Publishing.Core`.
- **Rationale**: A relocated `IRequest<T>` drags its `T`. `PublishedWorkflowView` is Core-clean (depends only on `Core.Models` + `Runtime.Core.Models`), so the move is legal under §2.1.
- **Blast radius**: exactly **2 production references** — the endpoint alias + `new PublishWorkflowCommand(...)` (`Api/Endpoints/PublishWorkflow.cs:13,32`) and the handler signature (`Api/Handlers/PublishWorkflowRequestHandler.cs:17`). Plus test constructions in `Publishing/Api/Tests/`. SemVer **MAJOR** per §4.2.
- **Do NOT move**: `PublishWorkflowRequest` (`Api/Requests/PublicationManagementRequests.cs:34`) — the FastEndpoints HTTP body DTO; transport-only, stays in Api.

## Decision 2 — Which handlers move (workflow-publish only)

- **Move to engine** (auth-free, workflow-side): `PublishWorkflowRequestHandler`, `StartWorkflowTestRunRequestHandler`, `PublicationSlotLifecycleRequestHandlers` (workflow publication slot Unpublish/Restore), `RunRuntimeRequirementPreflightRequestHandler`, and the `ExecutableCompilationCollecting` / `ExecutableNodeMetadataCollecting` event handlers.
- **Stay in Api** (activity-draft, authorization-coupled): `PublishActivityDraftRequestHandler`, `ActivityPublicationPreflightHandlers`, and the activity-draft endpoints — these consume `IActivityDefinitionPublisher` / `IActivityDraftTestRunService`, which inject `IActivityPublishingAuthorizationContext` (see Decision 3).
- **`PublishWorkflowRequestHandler` is HTTP/auth-free** — ctor (`Handlers/PublishWorkflowRequestHandler.cs:18-38`) has no `IHttpContextAccessor` and no `IActivityPublishingAuthorizationContext`; tenant arrives as plain data (`request.TenantId`), resolved at the endpoint by `PublicationRequestTenant.Resolve(User)`.
- **Assembly-scan boundary**: the engine feature calls `AddRequestHandlersFrom(engineAssembly)`; the Api feature keeps its own scan for the activity-draft handlers it retains. Guard: the §2.23.1 registration tests.

## Decision 3 — Authorization stays at the transport boundary (no neutral default)

- **Decision**: `IActivityPublishingAuthorizationContext` (contract `Api/Contracts/…`), its `HttpContextActivityPublishingAuthorizationContext` impl, and the two services that inject it — `ActivityDefinitionPublisher` (`Services/ActivityDefinitionPublisher.cs`) and `ActivityDraftTestRunService` (`Services/ActivityDraftTestRunService.cs`) — **all stay in the Api feature**. The engine registers and depends on **none** of them.
- **Evidence**: `grep` confirms `IActivityPublishingAuthorizationContext` is injected by exactly those two services; their consumers are exactly the **activity-draft** handlers/endpoints (`PublishActivityDraftRequestHandler`, `ActivityPublicationPreflightHandlers`, `ActivityDraftTestRunEndpoint`). Neither the workflow-publish handler nor the compiler nor any compiler collaborator references them. So the workflow-publish engine is authorization-free.
- **Why (correcting the earlier draft)**: authorization is a transport concern; the engine must not reach for it. The earlier "neutral default" idea was a band-aid over a pre-existing smell (those activity-draft services inject a transport auth context instead of receiving tenant as data, the way the workflow-publish path correctly does). Scoping the engine to the auth-free core removes the need for any default and keeps the layering clean.
- **Residual + follow-up**: the activity-draft publish/test-run logic remains in the Api feature for this PR (a known "logic in the transport feature" residual). Decoupling its authorization (take tenant as data → move it to an engine later) is a **separate follow-up unit**, not this behaviour-preserving split.

## Decision 4 — Registration classification (corrected)

- **ENGINE (move)** — the auth-free workflow-publish + compile core: `IActivityContractStorageDriverProvider`, executable + source-reference stores/readers, slot/record/policy/intent stores, policy resolver + preflight, projection preparer/activator, snapshot-review + receipt stores, layout + structure services, deletion guard, the compiler collaborator graph + template registries + `IWorkflowExecutableCompiler`, the workflow-publish/test-run/slot-lifecycle/preflight handlers, the two `Collect*` event handlers, `TimeProvider`.
- **STAY in Api** — transport + activity-draft: `base.ConfigureServices` (FastEndpoints), `AddHttpContextAccessor`, `IActivityPublishingAuthorizationContext → HttpContextActivityPublishingAuthorizationContext`, `ActivityDefinitionPublisher`, `ActivityDraftTestRunService` (+ `IActivityDraftTestRunStore`, cancellation policy), the activity-draft handlers/endpoints, `AddApiCapability` + `AddApiCapabilitySource`.
- **`IActivitySourceVersionPublisher → SourceOwnedActivityVersionPublisher`** is auth-free but is an *activity*-publishing service consumed by `ActivityVersionReconciler` (a separate Activities.Design.Reconciliation feature). Keep it registered in the Api feature (behaviour-preserving) with the other activity-publishing services; it is not part of the workflow-publish engine.
- **Durable override seam is move-safe**: publication/policy/slot/intent/snapshot/receipt durable stores override via `RemoveAll`+`AddScoped` (`Publishing/Persistence/Groundwork/DependencyInjection/GroundworkPublishingStoreRegistration.cs:34-67`); executable + source-reference durable stores override via `RemoveAll`+`AddScoped` in the Runtime groundwork lane (`Persistence/Groundwork/DependencyInjection/GroundworkRuntimeStoreRegistration.cs:57,113`). Because overrides are `RemoveAll` (not TryAdd-order dependent), moving the in-memory defaults into the engine does not break durability.

## Decision 5 — Downstream consumers repoint to the engine

- **Two features** DependsOn `WorkflowsPublishingApi` today and need only engine contracts: `GraphActivitiesDesignFeature` (`Activities/Graph/Design/…:17`, uses `IActivityTemplateProviderCompiler`/`…DependencyDiscoverer` registries) and `DispatchWorkflowDesignFeature` (`Activities/DispatchWorkflow/Design/…:18`, contributes `IExecutableCompilationSource`). **Repoint both to `DependsOn "WorkflowsPublishing"`** so design-only shells don't pull endpoints.
- **Shell configs** (`shells.json:163`, `shells.baseline.json:38`, `docker/compose/elsa-server.shells.json:87`) keep enabling `WorkflowsPublishingApi` — it keeps its name and (via `DependsOn`) still brings the engine, so these remain behaviour-preserving.

## Decision 6 — Architecture-test literals need updates (golden-rule compliant)

Subject + objective preserved; only literals change (framework §2.21.1 permits wiring/location changes):
- `tests/Elsa/Architecture/GroundworkPersistenceLifetimeTests.cs:138-140` — file-path literals for `IPublicationProjectionPreparer`/`IPublicationActivator`/`PublicationSnapshotReviewService` now point at the engine feature file.
- `tests/Elsa/Architecture/RuntimeExecutionSliceDependencyTests.cs:32` — the "does not reference Runtime.Api" assertion must also cover the new engine assembly.
- `tests/Elsa/Workflows/Publishing/Api/Tests/BridgeDependencyDirectionTests.cs:21` — the forbidden-reference list must also be asserted against the engine assembly.
- **Registration golden test** `WorkflowsPublishingApiFeatureTests.cs:29-53` calls `new WorkflowsPublishingApiFeature().ConfigureServices()` **directly** and asserts engine services resolve. Because engine services now arrive via `DependsOn` (not inheritance), that direct call no longer registers them. Per §2.21.1 the test's **wiring** updates (compose the engine feature alongside, or move the engine-service assertions to the new engine registration test); its subject/objective are preserved. The activity-draft/auth presence assertions (incl. line 37) stay green — those services remain in the Api feature.

## Open questions

None blocking. FR-010 / SC-002's behavioural half (in-process publish → live reference → startable) is proven at the unit level by the engine registration test (resolution + zero endpoints) and behaviourally by the `quickstart.md` validation; a full end-to-end publish is integration-flavoured and out of the unit-test scope per framework §2.23.6.
