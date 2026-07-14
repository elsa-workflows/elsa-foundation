# Implementation Plan: Groundwork Design Persistence

**Branch**: `093-groundwork-design-persistence` | **Date**: 2026-07-14 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/093-groundwork-design-persistence/spec.md`

## Summary

Finish the already-proven Groundwork workflow- and activity-design persistence lane by replacing its transitional by-collection/load-all query path with declared physical tables and bound server-side query routes, running one black-box contract suite against SQLite, SQL Server, PostgreSQL, and MongoDB, and proving atomicity, isolation, restart behavior, schema readiness, and the accepted performance gates. Once those gates pass, switch all design composition to Groundwork and remove the workflow/activity design EF projects, migrations, registrations, packages, and EF-only test infrastructure while keeping the design core modules Groundwork-free.

The implementation is an in-place productionization of existing adapters, manifests, command implementations, serializers, and unified-host composition. It does not create a second design persistence model or rewrite working domain orchestration.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (`net10.0`)

**Primary Dependencies**: Elsa design persistence core contracts; Groundwork Core, Documents, SQLite, SQL Server, PostgreSQL, MongoDB, and Schema Tool from one binary-compatible version; Microsoft.Extensions.DependencyInjection; the existing Elsa serialization, event, locking, and validation contracts

**Storage**: Groundwork shared/linked, dedicated-document, and physical-entity forms with canonical JSON authoritative; SQLite, SQL Server, PostgreSQL, and MongoDB are mandatory

**Testing**: xUnit, Microsoft.NET.Test.Sdk, provider containers through Testcontainers, existing design behavior tests, shared black-box adapter conformance, provider-native plan inspection, restart/failure injection, and the repository architecture guard

**Target Platform**: Cross-platform .NET application hosts and CI/CD runners; container-capable CI for server providers; MongoDB replica-set or sharded topology where cross-document transactions are required

**Project Type**: Modular .NET libraries, provider-specific host adapters, reference server, test suites, and deployment-schema manifest source

**Performance Goals**: At the required 100K acceptance scale, each operation in the contract's Benchmark Acceptance Catalog uses the median of three independent measured processes (one untimed warm-up, at least 100 operations and 30 seconds steady state per process) and must achieve p95 no worse than 1.25x the same-provider EF oracle, throughput at least 80% of the oracle, and p99 no worse than 2x. Every selected physical-entity form must improve median p95 or throughput by at least 10% over shared/linked and dedicated-document forms at both 100K and 1M, in the same direction in all three runs, with a 95% bootstrap confidence interval excluding zero.

**Constraints**: No Groundwork dependency in design core modules; no unbounded client evaluation; exact scope isolation; atomic multi-aggregate writes; canonical JSON retained; no production EF-data migration; one host provider choice; provider capabilities and MongoDB topology requirements must be truthful; no direct or transitive design EF dependency after exit

**Scale/Scope**: Six primary design aggregate/document types plus activity availability settings; all public design read/write contracts; required fixed 1K correctness, 100K acceptance, and 1M scale-bearing benchmark datasets (unless an explicit architect-approved workload exclusion is recorded before timing); four providers; the actual `Elsa.Server` in design-only, runtime-only, and combined host shapes

## Constitution Check

*GATE: Passed before Phase 0 research. Re-checked after Phase 1 design.*

The Elsa constitution is still draft and its current §E2.5 and adjacent inventory text describe the temporary EF implementation. That draft status matters: [ADR 0042](../../docs/adr/0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md) is accepted, and the accepted [critical review](../../docs/reports/zero-ef-constitution-review.md) establishes that the target is compatible with framework §§2.9 and 2.20, but the actual constitution amendment must land only with full zero-EF compliance or a narrowly ratified transition exception. This design slice therefore follows the accepted ADR and does not silently edit constitutional meaning.

| Gate | Result | Evidence / Plan Consequence |
|---|---|---|
| Framework §2.9: persistence contracts and invariants remain provider-neutral | PASS | `*.Design.Persistence.Core` remains unchanged by provider concerns; Groundwork translation, storage declarations, and sessions stay in provider-suffixed implementation projects. |
| Framework §2.10: persistence CQS boundary remains intact | PASS | Existing named read stores and mutation commands remain the public seams; no queryable provider surface is introduced. |
| Framework §2.20: provider module decomposition | PASS | Concrete storage and provider composition live only in `*.Persistence.Groundwork*` projects; no empty provider-neutral umbrella is added. |
| Framework §§2.21.1 and 2.23: test continuity and implementation/registration coverage | PASS | Existing test objectives are migrated to provider-neutral fixtures rather than deleted; each new translator, manifest source, provider feature, and failure branch receives direct coverage. |
| Framework §2.22: feature and extension-point documentation | PASS | Groundwork design READMEs, extension-point catalogs, provider deployment guidance, and generated maps are updated in the same work unit. |
| Elsa §E2.2: Design/Runtime separation and deployment shapes | PASS | Design persists authored state only; no Runtime-to-Design dependency is introduced; unified host composition does not merge domain contracts. |
| Elsa §§E2.8–E2.9: catalog identity, SemVer, draft state/layout, and immutable-version rules | PASS | Physical projections accelerate these invariants but canonical JSON and existing entities remain authoritative. |
| Elsa §E6 naming rules | PASS | Existing `Groundwork…Store` and provider prefixes are retained; new types use one codified role suffix. |
| Accepted zero-EF provider boundary | PASS | The repository ships one Groundwork design implementation after the exit gates; EF is temporary oracle evidence only. |
| No historical EF data migration | PASS | Greenfield scope is explicit; schema evolution applies only to Groundwork declarations and future Groundwork data. |

**Post-design re-check**: PASS. The data model keeps provider-only physical fields out of core contracts; the contract document binds every scale-bearing read to a declared route; the quickstart requires all four providers, plan evidence, atomic failure injection, and dependency audits before deletion.

## Project Structure

### Documentation (this feature)

```text
specs/093-groundwork-design-persistence/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── design-persistence-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/
├── Activities/Design/Persistence/
│   ├── Core/                         # Provider-neutral contracts and entities; no Groundwork
│   ├── Groundwork/                   # Activity adapters, physical definitions, serializers, commands
│   └── EFCore{,/Sqlite}/             # Temporary oracle, then deleted
├── Workflows/Design/Persistence/
│   ├── Core/                         # Provider-neutral contracts and entities; no Groundwork
│   ├── Groundwork/                   # Workflow adapters, physical definitions, serializers, commands
│   └── EFCore{,/Sqlite}/             # Temporary oracle, then deleted
├── Persistence/Groundwork/
│   ├── Querying/                     # Elsa Query<T> -> bound Groundwork DocumentQuery translation
│   ├── Unified/                      # Composite manifest/schema source
│   ├── Sqlite{,/Unified}/
│   ├── SqlServer{,/Unified}/         # Added provider composition
│   ├── PostgreSql{,/Unified}/
│   └── MongoDb{,/Unified}/           # Added provider composition
└── Apps/Elsa.Server/                 # Design composition switches to Groundwork only

tests/Elsa/
├── Activities/Design/Persistence/Groundwork/Tests/
├── Workflows/Design/Persistence/Groundwork/Tests/
├── Persistence/Groundwork/
│   ├── DesignConformance/Tests/      # One black-box suite, four provider fixtures
│   ├── UnifiedHost/Tests/
│   ├── Sqlite/Tests/
│   ├── SqlServer/Tests/
│   ├── PostgreSql/Tests/
│   └── MongoDb/Tests/
└── Architecture/                     # Core/provider and design-EF removal guards
```

**Structure Decision**: Preserve the existing provider-suffixed adapters and unified-host packages. Add only the missing SQL Server/MongoDB host adapters and one shared conformance project. Remove the two design EF families and their provider-specific SQLite leaves after evidence passes. Do not move design orchestration into generic persistence infrastructure.

## Delivery Sequencing

1. **Freeze and prove the baseline**: inventory every public store/command and existing test objective; add shared red tests for bounded query execution, scope isolation, restart, lost acknowledgement, and four-provider behavior before changing the adapters.
2. **Compile physical storage and queries**: replace legacy manifests and `GroundworkReadStore` load-all evaluation with versioned `PhysicalTableDefinition` declarations, bounded query identities, a single `Query<TEntity>` translator, and immutable route/session binding.
3. **Complete provider composition**: materialize the same unified design/runtime schema for SQLite, SQL Server, PostgreSQL, and MongoDB; add schema-source/CLI validation and truthful topology checks.
4. **Run the black-box matrix**: execute all reads and mutations through public Elsa contracts with isolation, OCC, transaction, restart, cancellation, schema drift, and provider-native plan evidence.
5. **Capture parity and performance evidence**: run identical datasets and result hashes against the temporary EF oracle and Groundwork physical forms; ratify or remediate against the accepted thresholds.
6. **Remove the design EF lane**: migrate still-useful EF-specific tests to contract fixtures, delete design EF source/projects/migrations/registrations/packages, switch the reference host, update docs/maps, and tighten the architecture ratchet for this completed lane.
7. **Independent final review**: audit FR-001–FR-022 and SC-001–SC-008 against exact branch HEAD, remediate every blocker, then land the reviewed PR and verify `main` plus issue #641.

## Complexity Tracking

No constitutional violations require an exception. The additional provider leaves are real independently composable host choices, and the shared conformance project prevents provider-specific test duplication.
