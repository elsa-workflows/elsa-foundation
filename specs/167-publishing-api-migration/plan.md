# Implementation Plan: Publishing API Minimal API Migration

**Branch**: `codex/1374-wave8-publishing` | **Date**: 2026-08-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/167-publishing-api-migration/spec.md`

## Summary

Migrate all 23 `Elsa.Workflows.Publishing.Api` FastEndpoints registrations to one explicit owner-local ASP.NET Core Minimal API mapper without changing the public REST contract or the existing endpoint-free Publishing engine. Freeze a reproducible FastEndpoints-era HTTP/OpenAPI oracle before deleting production endpoints, move API-visible contracts to an unload-safe `Elsa.Workflows.Publishing.Api.Core` lifetime boundary with compatibility forwarders, preserve Foundation Identity and inner activity-publication resource authorization, and prove the owner through real host, publication/preflight/policy/slot/test-run, OpenAPI, serialization, reload/unload, architecture, E2E, and post-merge gates.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (`net10.0`)

**Primary Dependencies**: ASP.NET Core Minimal APIs and endpoint metadata, CShells `IWebShellFeature`, Foundation Identity policies/evaluator, existing endpoint-free `Elsa.Workflows.Publishing` engine, Publishing/Design/Runtime contracts and stores, System.Text.Json source generation, native API Explorer/OpenAPI, retained FastEndpoints test oracle/canary, xUnit, TestServer, and `Elsa.Api.Compatibility.Testing`.

**Storage**: Existing publication, policy, slot, executable, projection-intent, activity publication, workflow/activity test-run, and Groundwork persistence contracts; live Workbench E2E uses a freshly deployed SQLite Groundwork database.

**Testing**: Historical before-host capture and replay, HTTP/OpenAPI differential comparison with mutation bites, route/security/metadata manifest tests, owner semantic suites, shared authorization matrix, three-cycle collectible host generation, full Architecture suite, full build, maps, format/diff, and live Publishing E2E.

**Target Platform**: ASP.NET Core hosts on Linux, Windows, and macOS, including CShells/Nuplane dynamic module replacement.

**Project Type**: Modular web API owner plus stable API contract package, immutable test oracles, architecture gates, backend E2E scripts, and migration documentation.

**Performance Goals**: No extra request-path framework layer; one standard authorization evaluation and owner handler invocation per request; no additional OpenAPI generation pass; no transient route duplication; no new publication/test-run background loop.

**Constraints**: Exactly 23 registrations removed; no production FastEndpoints dependency; no API DSL that recreates FastEndpoints; endpoint metadata owns only one catalog action; wildcard remains evaluator-only; no invented `manage -> read` implication; no reflection serialization fallback for owner contracts; no cache-clearing/sleep/forced-GC unload workaround; no blanket, unused, one-sided, or post-hoc approvals; existing public namespaces and feature identity remain compatible.

**Scale/Scope**: One first-party owner spanning 23 routes in catalog/construction, incident and conversion discovery, workflow snapshot/version preflight, workflow publication, slots and policy, workflow test runs, activity draft preflight/publication/receipt, activity test runs, and runtime-requirement preflight.

## Constitution Check

*GATE: Passed before research and re-checked after design. Framework §2.24 and Elsa §E2.8/§E2.9 contain draft/provisional material; this work uses only already-sanctioned contract separation and Adapter/Bridge and does not revise provisional workflow/activity domain policy.*

- **Framework §2.1 / §2.3 — three-layer contract boundary: PASS.** API-visible request/response contracts move to a stable contracts-only API Core package. ASP.NET Core mapping, authorization adapters, binders, handlers, stores, test-run resources, and source-generation implementation remain in the feature package.
- **Framework §2.5 / §2.23 — feature and implementation testability: PASS.** The migrated feature stays public, non-sealed, and virtual; new logic-bearing binding/result code receives direct branch coverage without reflection or `InternalsVisibleTo`.
- **Framework §2.7 — Adapter/Bridge: PASS.** The owner mapper adapts existing mediator/domain contracts to standard ASP.NET endpoints. It does not introduce a second endpoint framework or Elsa-owned routing DSL.
- **Framework §2.16 / §2.16.1 — project identity: PASS.** `Elsa.Workflows.Publishing.Api.Core` is an exempt contracts-only seam required by ADR 0069. Existing namespaces remain stable and the former implementation assembly forwards moved public types.
- **Framework §2.21.1 — golden rule: PASS.** Existing Publishing test subjects/objectives remain; setup is rewired but behavior is not deleted. Semantic suites for compilation, publication, activation/compensation, slots, policy, preflight, projection replay, deletion veto, activity publication, upgrades, and test runs are retained.
- **Framework §2.22 — documentation: PASS.** The report, route manifest, README/extension-point correction, and ADR cross-reference document the API boundary without duplicating domain rules.
- **Framework §2.24 — sanctioned patterns: PASS WITH DRAFT WARNING.** The section is unratified. The design uses only three-layer separation and Adapter/Bridge; no new pattern is proposed.
- **Framework §3 / Elsa §E5 — Strategy B lifetime: PASS.** Stable public API contracts share host/domain contract lifetime; mapper, handlers, authorizers, and test-run implementation remain replaceable and must unload.
- **Elsa §E2.2 / §E2.6 — Publishing bridge: PASS.** The endpoint-free engine remains the sanctioned Design-to-Runtime bridge. The API layer does not move orchestration into Runtime or authored state.
- **Elsa §E2.8 / §E2.9 — source and projection policy: PASS WITH PROVISIONAL CONTEXT.** Catalog/preflight routes continue to use persisted Design sources and derived views; the migration changes transport only.
- **Security: PASS.** Foundation Identity remains the sole policy/evaluator authority. Route metadata names one read/manage action; normalization, implication, wildcard, tenant, and resource decisions remain outside endpoint authoring. Inner activity-publication authorization stays in the API transport service.
- **Unloadability and subtraction: PASS.** The stable contract boundary is paired with real native OpenAPI and three-cycle collection evidence; exactly 23 production adapters and only the unused owner dependency are retired.

## Research and Design Decisions

1. Freeze baseline-first evidence in a commit containing no production migration. The clean-content-guarded capture runner is branch-reachable, reproducible from the pinned pre-migration source, run twice, and records exact fixture hashes.
2. Split API-visible wire contracts into `Elsa.Workflows.Publishing.Api.Core`, reuse existing `Elsa.Workflows.Publishing.Core` contracts only where they already genuinely belong, and type-forward moved public types from the former implementation assembly.
3. Convert `WorkflowsPublishingApiFeature` from `FastEndpointsFeatureBase` to a public, non-sealed `IWebShellFeature`; preserve its transport services/contributors and map one standard `RouteGroupBuilder` through `WorkflowsPublishingApi.MapWorkflowsPublishingApi`.
4. Preserve the endpoint-free `WorkflowsPublishing` engine and the API-owned `ActivityDefinitionPublisher`, `ActivityDraftTestRunService`, `IActivityPublishingAuthorizationContext`, persistence seams, and domain handlers. This wave is transport-only.
5. Use `RequirePermission(catalogAction)` only. The contributor continues to own `workflow-publishing.read` and `.manage`; no implication is invented. A configured test implication and evaluator wildcard prove framework behavior without changing the catalog.
6. Preserve the `{versionId:regex(^(?!drafts$).+$)}` constraint so draft workflow test runs never overlap versioned preflight, publish, or test-run routes.
7. Preserve exact route-over-body authority, FastEndpoints-effective JSON behavior, dynamic `201/200` workflow publication, activity `201 + Location`, test-run `202`, and the distinct generic, activity, workflow-expression/conversion, slot, and runtime-preflight ProblemDetails families.
8. Compare exact HTTP and consumed OpenAPI projections. Differences require typed two-sided approvals whose exact key/value occurs in both real documents; duplicate, unused, one-sided, no-op, stale, unknown, and false-valued entries fail mutation tests.
9. Combine real route invocation, authorization, stores/compilers/publishers, test-run resources, owner source-generated JSON, native OpenAPI, DI disposal, endpoint removal, and weak-reference collection in each unload cycle.
10. Extend live E2E beyond the existing eight GET and ten write cases to cover snapshot review/publish, policy CAS, slot unpublish/restore, runtime preflight, publication receipts/replay, activity test-run lookup/cancel, and route/body precedence.
11. Stop Wave 8 after the owner reaches zero. Shared FastEndpoints package/bases/discovery and historical oracle retirement remains issue #1376.

## Project Structure

### Documentation (this feature)

```text
specs/167-publishing-api-migration/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── publishing-route-manifest.md
│   ├── compatibility-evidence.md
│   └── authorization-collectibility.md
├── checklists/requirements.md
└── tasks.md
```

### Source and Tests (repository root)

```text
src/Elsa/Workflows/Publishing/Api/Core/
├── Elsa.Workflows.Publishing.Api.Core.csproj
└── stable API-visible Requests and Models in their existing namespaces

src/Elsa/Workflows/Publishing/Api/
├── WorkflowsPublishingApiFeature.cs
├── WorkflowsPublishingApi.cs
├── WorkflowsPublishingJsonContext.cs
├── ApiContractTypeForwards.cs
└── existing Handlers, Services, Authorization, README, and EXTENSION_POINTS.md

tests/Elsa/Workflows/Publishing/Api/Tests/
├── Baselines/
├── Support/
├── PublishingBeforeBaselineTests.cs
├── PublishingCompatibilityTests.cs
├── PublishingAuthorizationIntegrationTests.cs
└── existing owner semantic suites

tests/Elsa/Architecture/
├── FastEndpointsTransitionTests.cs
├── DomainManagementApiCompositionTests.cs
└── Wave8PublishingCollectibilityTests.cs

e2e-tests/get-endpoints/Test-PublishingGets.ps1
e2e-tests/write-endpoints/Test-PublishingWrites.ps1
e2e-tests/reusable-activities/Test-PublishingLifecycle.ps1
e2e-tests/reusable-activities/Test-ActivityDraftTestRun.ps1
e2e-tests/reusable-activities/Test-DraftTestRun.ps1

docs/reports/publishing-api-migration-2026-08.md
docs/adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md
docs/adr/0069-openapi-contract-types-use-stable-api-core.md
docs/maps/*
```

**Structure Decision**: Add one owner-specific contracts-only API Core package because native OpenAPI must never retain the replaceable Publishing implementation. Keep mapping, authorization adapters, handlers, stores, test-run resources, source generation, and provider orchestration in the existing API feature package. Reuse shared compatibility-test support and ADR 0069 rather than introduce new endpoint or documentation frameworks.

## Phase Sequence

1. **Baseline-first**: exact 23-route manifest, immutable FastEndpoints HTTP/OpenAPI capture, receipt, replay, comparer, and mutation bites.
2. **Stable contract boundary**: API Core project, public namespace/binary compatibility, serialization and OpenAPI metadata completeness gates.
3. **Owner migration**: explicit mapper, feature composition, binding/results, standard authorization metadata, removal of exactly 23 production FastEndpoints registrations and the unused owner reference.
4. **Behavior/security parity**: replay, retained semantic tests, shared evaluator matrix, activity-publication tenant/resource denial, and retained FastEndpoints canary.
5. **Lifecycle/integration**: real three-cycle unload proof including test-run resources, combined-host manifest, existing/new backend E2E, ratchet/maps/report/ADR reconciliation.
6. **Green gate and publication**: owner tests, full Architecture, full solution build, maps, format/diff, independent review, PR, merge, and exact-main post-merge checks.

## Rollback and Risks

The baseline evidence commit precedes production changes. The owner migration ships as one revertible wave PR; reverting it restores all 23 FastEndpoints endpoints and the owner project reference without changing domain data. Primary risks are incomplete before evidence, public type movement, reserved `drafts` route overlap, route-over-body binding, mixed `/publishing` and `/design/activities` ownership, dynamic `201/200` and `202` statuses, exact `Location`, four historical ProblemDetails paths, case-insensitive/camel-enum JSON behavior, review-token and receipt idempotency, slot/policy CAS and compensation, test-run expiry/cancellation resources, and native OpenAPI retention. Every risk receives executable evidence; none is waived through normalization, reflection fallback, test deletion, omitted OpenAPI generation, or process-memory observation.

## Complexity Tracking

No constitutional violation is required. The contracts-only API Core package is an explicit §2.16.1 exemption; all remaining work stays inside the existing owner, shared ASP.NET boundary, test projects, program docs, and existing E2E harness.

## Post-Design Constitution Re-check

Phase 1 preserves the pre-research gate: no new architectural pattern or cross-domain implementation dependency, no publication-policy change, no heavy dependency in Core, no test-objective deletion, and no collectible implementation type crossing the stable OpenAPI metadata boundary. Any implementation discovery contradicting this result stops the wave and is recorded for review rather than silently broadening scope.
