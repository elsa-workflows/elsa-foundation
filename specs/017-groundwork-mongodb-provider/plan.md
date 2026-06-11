# Implementation Plan: Groundwork MongoDB Provider

**Branch**: `codex/groundwork-mongodb-provider` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/017-groundwork-mongodb-provider/spec.md`

## Summary

Implement G5 by adding `Groundwork.MongoDb`, a native MongoDB provider for Groundwork document workloads. The provider materializes one collection per storage unit, creates native indexes for declared indexes, records schema history, implements `IDocumentStore`, and validates the contract against a container-backed MongoDB database.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: `MongoDB.Driver` 3.9.0, `Testcontainers.MongoDb` 4.12.0

**Storage**: MongoDB native collections and indexes

**Testing**: xUnit provider integration tests with MongoDB Testcontainers

**Target Platform**: Generic .NET Groundwork provider package

**Project Type**: Provider-specific Groundwork library plus integration tests

**Performance Goals**: Contract correctness; no benchmark targets in G5

**Constraints**: No Elsa references; no EF Core; equality queries only; one-field indexes only; no relational index-row emulation

**Scale/Scope**: One provider package, one provider test project, solution entries, Spec Kit artifacts, program-goal pointer updates

## Constitution Check

| Gate | Status | Note |
|---|---|---|
| Framework §2.1 three-layer separation | PASS | Generic contracts remain in `Groundwork.Documents`; MongoDB implementation remains provider-specific. |
| Framework §2.9 persistence invariants provider-neutral | PASS | MongoDB implements the same document/query/concurrency invariants as other providers. |
| Framework §2.20 provider module decomposition | PASS | MongoDB lives in a provider-suffixed Groundwork package. |
| Elsa §E2.2 / §E2.6 | PASS | G5 adds generic provider support only; no Elsa runtime migration. |
| Framework §2.23 tests | PASS | Provider behavior is covered by real MongoDB integration tests. |

No justified violations.

## Project Structure

### Documentation (this feature)

```text
specs/017-groundwork-mongodb-provider/
├── spec.md
├── plan.md
├── tasks.md
├── quickstart.md
└── checklists/
    └── requirements.md
```

### Source Code

```text
src/Groundwork/MongoDb/
├── Groundwork.MongoDb.csproj
├── Documents/
│   └── MongoDbDocumentStore.cs
├── Materialization/
│   └── MongoDbGroundworkMaterializer.cs
└── README.md

tests/Groundwork/Groundwork.MongoDb.Tests/
├── Groundwork.MongoDb.Tests.csproj
├── MongoDbDocumentStoreTests.cs
├── MongoDbGroundworkMaterializerTests.cs
├── MongoDbDependencyBoundaryTests.cs
└── MongoDbTestManifests.cs
```

**Structure Decision**: G5 keeps MongoDB logic in `Groundwork.MongoDb` and validates it through a dedicated provider test project. No shared relational code is reused because MongoDB uses native collections and indexes.

## Complexity Tracking

No constitution violations or complexity exceptions are introduced in G5.
