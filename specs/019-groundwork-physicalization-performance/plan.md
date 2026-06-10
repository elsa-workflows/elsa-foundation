# Implementation Plan: Groundwork Physicalization And Performance

**Branch**: `codex/groundwork-physicalization-performance` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/019-groundwork-physicalization-performance/spec.md`

## Summary

Implement G7 by extending Groundwork's existing manifest/planner/provider stack with opt-in optimized physicalization for declared single-field equality indexes. Portable units keep the generic document/index tables and MongoDB content-path indexes. Optimized units additionally project eligible index values into provider-native physical structures, and provider stores route eligible equality queries through those structures without changing `IDocumentStore`.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: Existing `Groundwork.Core`, `Groundwork.Documents`, `Groundwork.Relational`, `Groundwork.Sqlite`, `Groundwork.MongoDb`

**Storage**: Groundwork portable document storage with opt-in optimized projections

**Testing**: xUnit tests with SQLite in-memory provider and Testcontainers MongoDB

**Target Platform**: Groundwork provider packages inside Elsa Foundation

**Project Type**: Library/provider framework

**Performance Goals**: Correctness of optimized physical query path; benchmark suite deferred to G8 runtime hardening

**Constraints**: Portable default remains unchanged; no caller API changes; Elsa concepts cannot leak into generic Groundwork packages; optimized projections must honor optimistic concurrency

**Scale/Scope**: Physicalization plan metadata, relational optimized projections for SQLite validation, MongoDB optimized projections, provider tests, roadmap pointer updates

## Constitution Check

| Gate | Status | Note |
|---|---|---|
| Framework §2.1 three-layer separation | PASS | Manifest vocabulary stays generic; Elsa bridge is not involved. |
| Framework §2.9 persistence invariants provider-neutral | PASS | `IDocumentStore` remains the caller contract. |
| Framework §2.20 provider module decomposition | PASS | Provider-specific physicalization stays in provider packages. |
| Elsa §E2.2 / §E2.6 | PASS | Workflow runtime stores remain out of scope. |
| Framework §2.23 tests | PASS | SQLite and MongoDB provider-backed tests prove optimized behavior. |

No justified violations.

## Project Structure

### Documentation (this feature)

```text
specs/019-groundwork-physicalization-performance/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── optimized-physicalization.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code

```text
src/Groundwork/Core/
├── Manifests/StoragePolicies.cs
├── Materialization/MaterializationPlan.cs
└── Physicalization/
    ├── PhysicalizationProjection.cs
    └── PhysicalizedFieldPlan.cs

src/Groundwork/Documents/Planning/
└── DocumentManifestPlanner.cs

src/Groundwork/Relational/
├── Documents/RelationalDocumentStore.cs
├── Documents/RelationalDocumentStoreDialect.cs
├── Materialization/RelationalMaterializerBase.cs
└── Physicalization/RelationalPhysicalizationNames.cs

src/Groundwork/MongoDb/
├── Documents/MongoDbDocumentStore.cs
└── Materialization/MongoDbGroundworkMaterializer.cs

tests/Groundwork/Groundwork.Tests/
├── PlannerContractTests.cs
└── PhysicalizationProjectionTests.cs

tests/Groundwork/Groundwork.Sqlite.Tests/
└── SqliteOptimizedPhysicalizationTests.cs

tests/Groundwork/Groundwork.MongoDb.Tests/
└── MongoDbOptimizedPhysicalizationTests.cs
```

**Structure Decision**: G7 extends existing Groundwork core/provider packages. It does not introduce new provider packages or Elsa bridge code.

## Complexity Tracking

No constitution violations or complexity exceptions are introduced in G7.
