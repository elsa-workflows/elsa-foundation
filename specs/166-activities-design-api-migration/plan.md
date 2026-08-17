# Implementation Plan: Activities Design API Minimal API Migration

**Branch**: `codex/1373-wave7-activities-design` | **Date**: 2026-08-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/166-activities-design-api-migration/spec.md`

## Summary

Migrate all 38 `Elsa.Activities.Design.Api` FastEndpoints registrations to one explicit owner-local
ASP.NET Core Minimal API mapper without changing the public REST contract. Freeze a reproducible
FastEndpoints-era HTTP/OpenAPI oracle before deleting production endpoints, move API-visible contracts
to a stable `Elsa.Activities.Design.Api.Core` lifetime boundary with compatibility forwarders, preserve
Foundation Identity permission/resource behavior, and prove the resulting owner through real host,
authoring/upgrade, OpenAPI, serialization, reload/unload, architecture, E2E, and post-merge gates.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (`net10.0`)

**Primary Dependencies**: ASP.NET Core Minimal APIs and endpoint metadata, CShells `IWebShellFeature`,
Foundation Identity policies/evaluator, existing Activities Design mediator handlers/stores/providers,
System.Text.Json source generation, native API Explorer/OpenAPI, retained FastEndpoints test oracle/canary,
xUnit and TestServer, `Elsa.Api.Compatibility.Testing`.

**Storage**: Existing Activities Design persistence contracts/providers; live Workbench E2E uses a freshly
deployed SQLite Groundwork database.

**Testing**: Historical before-host capture and replay, HTTP/OpenAPI differential comparison with mutation
bites, route/security/metadata manifest tests, owner behavioral tests, shared authorization matrix,
three-cycle collectible host generation, full Architecture suite, full build, maps, format/diff, and live
authoring/upgrade E2E.

**Target Platform**: ASP.NET Core hosts on Linux, Windows, and macOS, including CShells/Nuplane dynamic
module replacement.

**Project Type**: Modular web API owner plus stable API contract package, test oracles, architecture gates,
backend E2E scripts, and migration documentation.

**Performance Goals**: No extra request-path framework layer; one standard authorization evaluation and
owner handler invocation per request; no additional OpenAPI generation pass; no transient route duplication.

**Constraints**: Exactly 38 registrations removed; no production FastEndpoints dependency; no API DSL that
recreates FastEndpoints; endpoint metadata owns only catalog actions; wildcard remains evaluator-only;
no reflection serialization fallback for owner contracts; no cache-clearing/sleep/forced-GC unload workaround;
no blanket, unused, one-sided, or post-hoc approvals; existing public names/namespaces and feature identity
remain compatible.

**Scale/Scope**: One first-party owner spanning 38 routes in catalog, availability, reusable definition/draft,
fork, version/diff/dependency/lifecycle, contract proposal/provider payload, and upgrade-plan families.

## Constitution Check

*GATE: Passed before research and re-checked after design. Framework §2.24 and Elsa §E2.8/§E2.9 contain
draft/provisional material; this work relies only on already-sanctioned contract separation and Adapter/Bridge,
and preserves rather than revises the provisional activity/workflow domain policies.*

- **Framework §2.1 / §2.3 — three-layer contract boundary: PASS.** API-visible request/response contracts move
  to a stable contracts-only API Core package. ASP.NET Core, mapping, authorization adapters, handlers, and
  source-generation implementation remain in the feature package; cross-Core references remain dependency-light.
- **Framework §2.5 / §2.23 — feature and implementation testability: PASS.** The migrated feature stays public,
  non-sealed, and virtual; any new logic-bearing binder/mapper/validator is directly branch-tested without
  reflection or `InternalsVisibleTo`.
- **Framework §2.7 — Adapter/Bridge: PASS.** The owner mapper adapts existing mediator/domain contracts to
  standard ASP.NET endpoints. It does not introduce a second endpoint framework or Elsa-owned routing DSL.
- **Framework §2.16 / §2.16.1 — project identity: PASS.** `Elsa.Activities.Design.Api.Core` is an exempt
  contracts-only seam required by the API/OpenAPI lifetime boundary. Existing namespaces stay stable and the
  former implementation assembly forwards moved public types.
- **Framework §2.21.1 — golden rule: PASS.** Existing Activities Design test subjects/objectives remain; broken
  setup is rewired, behavior is not deleted. Before fixtures and semantic mutation bites make subtraction
  executable rather than census-based.
- **Framework §2.22 — documentation: PASS.** README/extension-point changes are made only if the registration
  inventory changes; the migration report, route manifest, and ADR cross-reference document the API boundary.
- **Framework §2.24 — sanctioned patterns: PASS WITH DRAFT WARNING.** The section is unratified. The design uses
  only three-layer separation and Adapter/Bridge; no new pattern is proposed.
- **Framework §3 / Elsa §E5 — Strategy B lifetime: PASS.** Stable public API contracts share the host/domain
  contract lifetime; mapper/handler/provider implementation remains replaceable and must unload.
- **Elsa §E2.8 — activity catalog source of truth: PASS WITH PROVISIONAL CONTEXT.** Catalog and picker routes
  continue to query persisted design stores; no live-provider picker fallback or reconciliation policy change.
- **Elsa §E2.9 — design/read/runtime separation: PASS WITH PROVISIONAL CONTEXT.** API views remain API-owned
  projections and are not folded into workflow authored state or runtime artifacts.
- **Security: PASS.** Foundation Identity remains the sole policy/evaluator authority. Route metadata names one
  catalog action, while normalization, implication, wildcard, tenant, resource, and provider-payload decisions
  remain outside endpoint authoring.
- **Unloadability and subtraction: PASS.** The stable contract boundary is paired with real native OpenAPI and
  three-cycle collection evidence; exactly 38 production adapters and only unused dependencies are retired.

## Research and Design Decisions

1. Freeze baseline-first evidence in a commit that contains no production migration. The capture runner is
   self-contained, clean-content guarded, and reproducible from a branch-reachable historical source tree.
2. Split API-visible contracts into `Elsa.Activities.Design.Api.Core` and use type forwarding from the former
   implementation assembly. This follows the proven unload-safe OpenAPI boundary in spec 165.
3. Map one standard `RouteGroupBuilder` surface from a public owner-local extension and expose it through
   `IWebShellFeature.MapEndpoints`; keep domain behavior in existing handlers/services.
4. Use `RequirePermission(catalogAction)` only. Preserve route read/manage ownership and the separate inner
   author/provider-payload/resource checks; Foundation Identity evaluates implication and wildcard grants.
5. Compare exact HTTP and consumed OpenAPI projections. Differences require a typed two-sided approval whose
   key/value occurs in both real documents; duplicate, unused, one-sided, stale, and unknown facets fail.
6. Combine real route invocation, authorization, owner source-generated JSON, native OpenAPI, provider/store
   execution, DI disposal, endpoint removal, and weak-reference collection in each unload cycle.
7. Add a live Activities Design upgrade-plan E2E beside the existing reusable-activity and GET suites so the
   highest-risk multi-step authoring path is covered against real persistence and Workbench composition.
8. Preserve existing semantic tests and add focused bites for route/body precedence, malformed/empty bodies,
   cursor errors, provider-payload redaction, lifecycle conflicts, cancellation identity, and upgrade outcomes.
9. Preserve the effective FastEndpoints JSON contract: case-insensitive input, camel-case output and dictionary
   keys, camel-case string enums, explicit nullability, optional activity type keys, and opaque provider payloads.
10. Preserve the seven `201 Created` responses with exact `Location`, the single discard `204`, and the two
    existing error-translation families rather than collapsing all routes into one generic result helper.

## Project Structure

### Documentation (this feature)

```text
specs/166-activities-design-api-migration/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── activities-design-route-manifest.md
│   ├── compatibility-evidence.md
│   └── authorization-collectibility.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source and Tests (repository root)

```text
src/Elsa/Activities/Design/Api/Core/
├── Elsa.Activities.Design.Api.Core.csproj
└── shared API-visible Commands, Requests, and Models (linked from existing namespaces)

src/Elsa/Activities/Design/Api/
├── ActivitiesDesignApiFeature.cs
├── ActivitiesDesignApi.cs
├── ActivitiesDesignJsonContext.cs
├── ApiContractTypeForwards.cs
└── existing Handlers, Services, Authorization, README, and EXTENSION_POINTS.md

tests/Elsa/Activities/Design/Tests/
├── Api/Baselines/
├── Api/Support/
├── Api/ActivitiesDesignBeforeBaselineTests.cs
├── Api/ActivitiesDesignCompatibilityTests.cs
├── Api/ActivitiesDesignAuthorizationIntegrationTests.cs
└── existing owner semantic/registration suites

tests/Elsa/Architecture/
├── FastEndpointsTransitionTests.cs
├── DomainManagementApiCompositionTests.cs
└── Wave7ActivitiesDesignCollectibilityTests.cs

e2e-tests/get-endpoints/Test-DesignActivityGets.ps1
e2e-tests/write-endpoints/Test-DesignActivityWrites.ps1
e2e-tests/reusable-activities/Test-ReusableActivity.ps1
e2e-tests/reusable-activities/Test-ReusableActivityPinning.ps1
e2e-tests/reusable-activities/Test-ActivityUpgradePlan.ps1

docs/reports/activities-design-api-migration-2026-08.md
docs/adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md
docs/maps/*
```

**Structure Decision**: Introduce one owner-specific contracts-only API Core package because real native
OpenAPI must never retain the replaceable implementation generation. Keep mapping, authorization adapters,
handlers, source generation, and provider/store orchestration in the existing API feature package. Reuse the
shared compatibility-test support and stable OpenAPI lifetime convention rather than introduce a new framework.

## Phase Sequence

1. **Baseline-first**: exact 38-route manifest, immutable FastEndpoints HTTP/OpenAPI capture, receipt, replay,
   comparer, and mutation bites.
2. **Stable contract boundary**: API Core project, public namespace/binary compatibility, serialization and
   OpenAPI metadata completeness gates.
3. **Owner migration**: explicit mapper, feature composition, binders/results, standard auth metadata, removal
   of exactly 38 production FastEndpoints registrations and the unused reference.
4. **Behavior/security parity**: replay, semantic tests, shared evaluator matrix, provider payload and tenant/
   resource denial, retained FastEndpoints canary.
5. **Lifecycle/integration**: real three-cycle unload proof, host manifest, existing and new backend E2E,
   ratchet/maps/report/ADR reconciliation.
6. **Green gate and publication**: owner tests, full Architecture, full solution build, maps, format/diff,
   independent review, PR, merge, and exact-main post-merge checks.

## Rollback and Risks

The baseline evidence commit precedes production changes. The owner migration ships as one revertible wave PR;
reverting it restores all 38 FastEndpoints endpoints and their project reference without changing domain data.
Primary risks are incomplete before evidence, public type movement, nineteen route-over-body binding shapes,
picker-versus-identifier route precedence, seven `201 + Location` contracts, two historical ProblemDetails paths,
case-insensitive/camel-enum JSON behavior, signed cursor binding, inner provider-payload authorization, large owner
metadata graphs, native OpenAPI retention, and multi-step upgrade semantics.
Every risk has an executable contract/mutation gate; none is waived through fixture normalization, reflection
fallback, test deletion, omitted OpenAPI generation, or process-memory observation alone.

## Complexity Tracking

No constitutional violation is required. The contracts-only API Core package is an explicit §2.16.1 exemption;
all remaining work stays inside the existing owner, shared ASP.NET boundary, test projects, and program docs.

## Post-Design Constitution Re-check

Phase 1 preserves the pre-research gate: no new architectural pattern or cross-domain implementation dependency,
no domain-policy change, no heavy API dependency in Core, no test-objective deletion, and no collectible owner type
crossing the stable OpenAPI metadata boundary. Any implementation discovery that contradicts this result stops the
wave and is recorded for review rather than silently broadening scope.
