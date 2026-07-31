# Implementation Plan: Publishing engine / API split

**Branch**: `145-publishing-engine-split` | **Date**: 2026-07-30 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/145-publishing-engine-split/spec.md`

## Summary

Extract the **auth-free workflow-publish + compile engine** out of `Elsa.Workflows.Publishing.Api` into a new endpoint-free feature `Elsa.Workflows.Publishing` (ShellFeature `WorkflowsPublishing`). The API feature keeps its `FastEndpointsFeatureBase` base and obtains the engine by **composition** — `DependsOn WorkflowsPublishing` (framework §2.11), not feature inheritance. The workflow-publish orchestration handler moves to the engine; the `PublishWorkflow` mediator command and its `PublishedWorkflowView` response move to `Elsa.Workflows.Publishing.Core`. Behaviour-preserving (framework §2.21.1). Prerequisite to PR2 (`CatalogActivation`), which needs the publish engine composable **without** endpoints.

The blast radius is small and well-bounded: the publish command has 2 production references; the workflow-publish handler has **zero HTTP/authorization dependencies**; the durable-store override seam is `RemoveAll`+`AddScoped` (move-safe); two downstream features DependsOn the Api feature and repoint to the engine. **Authorization stays at the transport boundary**: `IActivityPublishingAuthorizationContext`, its HttpContext impl, and the two activity-draft services that consume it (`ActivityDefinitionPublisher`, `ActivityDraftTestRunService`) remain in the API feature — no workflow-publish engine service depends on them, so the engine is authorization-free and no neutral default is introduced.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: CShells (feature composition / `[ShellFeature]`), Elsa.Mediator (`IRequestSender`, `AddRequestHandlersFrom`), FastEndpoints (transport — stays in Api), Elsa.Events, Elsa.Api.Capabilities (stays in Api), Groundwork persistence (override seam)

**Storage**: Unaffected. In-memory publication/executable stores remain the composable default; durable Groundwork providers override via `RemoveAll`+`AddScoped` regardless of which feature owns the in-memory defaults.

**Testing**: xUnit. Existing publishing tests (registration + implementation) must pass unchanged per framework §2.21.1; a new §2.23.1 registration test is added for the engine feature; architecture tests with hard-coded file-path/assembly literals get legitimate literal updates.

**Target Platform**: `Elsa.Server` host (modular monolith); features composed per shell.

**Project Type**: Modular framework feature packages (three-layer per feature, framework §2.1).

**Performance Goals**: None changed (behaviour-preserving refactor).

**Constraints**: framework §2.21.1 (golden rule — existing tests preserved); §E2.2 (no `Runtime.* → Design.*` dependency; Publishing is the sanctioned bridge); §E2.2.3 (preserve Design-only / Runtime-only / combined deployment shapes — this refactor improves the Runtime-only shape); framework §4.2 (command/view relocation = MAJOR for the affected package).

**Scale/Scope**: 1 new engine feature package (`Elsa.Workflows.Publishing`) + additions to `Elsa.Workflows.Publishing.Core` + slimmed `Elsa.Workflows.Publishing.Api`; 2 downstream `DependsOn` repoints; ~4–6 architecture-test literal updates; **no new services** (moves only; authorization stays in Api).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.* Loads both `constitution-framework.md` and `constitution.md`.

| Gate | Requirement | Status |
|---|---|---|
| framework §2.1 three-layer | Engine = Layer-3 feature; contract additions in `Publishing.Core` (Layer 1); Api = Layer-3 transport. No `.Core` gains a heavy dep. | ✅ PASS |
| framework §2.2 / §E6 naming | `Elsa.Workflows.Publishing` (bare engine form), ShellFeature `WorkflowsPublishing`; relocated `PublishWorkflow`/`PublishedWorkflowView` keep type names; persisted/wire identifiers unchanged (§E6 scope note). Keep the "Publishing" bridge identity (not "PublishRuntime"). | ✅ PASS |
| framework §2.11 DependsOn composition | `WorkflowsPublishingApiFeature : FastEndpointsFeatureBase` (unchanged base) declares `DependsOn WorkflowsPublishing`; the engine is a plain `IShellFeature`. The shell activates both; each runs its own `ConfigureServices`. No feature inheritance between them (avoids single-inheritance conflict; endpoints keep their natural base). | ✅ PASS |
| framework §2.6.2 replacement contract | `IActivityPublishingAuthorizationContext` keeps its single HttpContext implementation, registered by the **Api** feature only. The engine does not register or depend on it — authorization stays at the transport boundary. No neutral default; no conflict. | ✅ PASS |
| framework §2.22 / §2.22.1 docs + catalog | New engine feature ships `README.md` + `EXTENSION_POINTS.md` (it owns the compilation/metadata contribution events + registries); Api catalog updated (endpoints only). | ✅ PASS (obligation) |
| framework §2.23 tests | §2.23.1 registration test for the engine feature; §2.23.2 implementation tests preserved (moved, not rewritten). Existing `WorkflowsPublishingApiFeatureTests` keep passing through inheritance. | ✅ PASS (obligation) |
| framework §2.21.1 golden rule | Subject + objective of existing tests preserved. Architecture tests pinning the feature's file path / assembly get literal-only updates (wiring/location may change; assertions preserved). | ✅ PASS |
| framework §4.2 SemVer | Relocating `PublishWorkflow` + `PublishedWorkflowView` to `Publishing.Core` = MAJOR for the affected package(s); recorded. | ✅ PASS (recorded) |
| §E2.1 domain tree | Publishing's surface-package cell (currently only `…Publishing.Api`) gains `…Publishing` + `…Publishing.Core`; generated domain map re-enumerates. | ✅ PASS (doc follow-through) |
| §E2.2 / §E2.2.3 bounded-context | Publishing is the bridge (neither Design nor Runtime); engine may reference `Design.Persistence.Core` + `Runtime.Core`. No `Runtime.* → Design.*` introduced. `BridgeDependencyDirectionTests` forbidden-list extended to the new engine assembly. Deployment shapes preserved/improved. | ✅ PASS |
| framework §2.24 closed catalog | Uses only catalogued patterns/mechanisms: three-layer (P1); feature inheritance (P2 — Api → `FastEndpointsFeatureBase`); `DependsOn` composition (§2.11 mechanism) for the engine↔Api relationship; replacement contract (P5). No new pattern. | ✅ PASS |
| `Elsa.Server.slnx` / ArchitectureGuard | New project added to the solution + `.slnx` (guard test). | ✅ PASS (obligation) |

**Result: PASS — no violations requiring Complexity Tracking, and no new code beyond moves.** Authorization stays at the transport boundary; the engine is authorization-free (no neutral default). The only residual is the activity-draft publish/test-run logic remaining in the Api feature (a separate, transport-auth-coupled concern) — deliberately deferred to a follow-up rather than dragged into the engine.

## Project Structure

### Documentation (this feature)

```text
specs/145-publishing-engine-split/
├── plan.md              # This file
├── research.md          # Phase 0 — blast-radius map + decisions
├── data-model.md        # Phase 1 — package/feature/contract structure
├── quickstart.md        # Phase 1 — how to validate the split
├── contracts/           # Phase 1 — engine registration surface + relocated command
└── tasks.md             # Phase 2 — /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
src/Elsa/Workflows/Publishing/
├── Core/                         # Elsa.Workflows.Publishing.Core (Layer 1) — GAINS:
│   ├── Requests/PublishWorkflow.cs            # MOVED from Api/Requests (IRequest<PublishedWorkflowView>)
│   └── Models/PublishedWorkflowView.cs        # MOVED from Api/Models (Core-clean)
├── Publishing/                   # Elsa.Workflows.Publishing  (Layer 3 — NEW engine feature)
│   ├── Elsa.Workflows.Publishing.csproj
│   ├── WorkflowsPublishingFeature.cs          # [ShellFeature "WorkflowsPublishing"] : IShellFeature, virtual ConfigureServices,
│   │                                          #   DependsOn { WorkflowsRuntimeTriggers, Events }; registers the auth-free engine
│   ├── Handlers/                              # MOVED: workflow-publish handler + Collect* event handlers (NOT activity-draft handlers)
│   ├── Services/                              # MOVED: compiler + collaborators, activator/reconciler/preflight,
│   │                                          #   in-memory stores, layout/structure, deletion guard (NO authorization services)
│   ├── Exceptions/                            # MOVED: PublicationPreflight/Activation/ExpressionPublication/Compilation
│   ├── README.md + EXTENSION_POINTS.md        # NEW (owns Collect* events + template registries)
└── Api/                          # Elsa.Workflows.Publishing.Api (Layer 3 — SLIMMED, transport + activity-draft)
    ├── WorkflowsPublishingApiFeature.cs       # : FastEndpointsFeatureBase (unchanged base); DependsOn { WorkflowsPublishing,
    │                                          #   ApiCapabilities }; registers endpoints + API capabilities + AddHttpContextAccessor +
    │                                          #   IActivityPublishingAuthorizationContext (HttpContext) + activity-draft services
    ├── Endpoints/ Requests/ Models/           # STAY (transport DTOs, ProblemDetails mapping)
    ├── Services/                              # STAY: ActivityDefinitionPublisher, ActivityDraftTestRunService, HttpContext auth
    ├── Handlers/                              # STAY: activity-draft publish/preflight/test-run handlers
    └── EXTENSION_POINTS.md                    # UPDATED (endpoints + activity-draft)

# Cross-cutting edits
src/Elsa/Activities/Graph/Design/GraphActivitiesDesignFeature.cs          # DependsOn WorkflowsPublishingApi → WorkflowsPublishing
src/Elsa/Activities/DispatchWorkflow/Design/DispatchWorkflowDesignFeature.cs  # same repoint
Elsa.Server.slnx                                                          # add the new project
tests/Elsa/Architecture/GroundworkPersistenceLifetimeTests.cs             # file-path literals → engine feature file
tests/Elsa/Architecture/RuntimeExecutionSliceDependencyTests.cs           # cover engine assembly
tests/Elsa/Workflows/Publishing/Api/Tests/BridgeDependencyDirectionTests.cs   # forbidden-list covers engine assembly
tests/Elsa/Workflows/Publishing/Tests/…                                   # NEW engine feature registration test (§2.23.1)
```

**Structure Decision**: Three-layer-per-feature (framework §2.1) within the existing `Elsa.Workflows.Publishing` domain: contracts land in the existing `.Core`; the engine becomes the bare-form Layer-3 feature `Elsa.Workflows.Publishing`; `.Api` remains a Layer-3 transport feature that **depends on** the engine via `DependsOn` (keeping its `FastEndpointsFeatureBase` base). This mirrors the `Design`/`Design.Api` and `Runtime`/`Runtime.Api` shape already in the tree.

## Complexity Tracking

> No Constitution Check violations. Table intentionally empty.
