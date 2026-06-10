# Implementation Plan: Groundwork SQLite Document Store

**Branch**: `codex/groundwork-sqlite-document-store` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/014-groundwork-sqlite-document-store/spec.md`

## Summary

Implement the G2 SQLite portable document store MVP. Add document-store contracts to `Groundwork.Documents`, a `Groundwork.Sqlite` provider package using `Microsoft.Data.Sqlite`, SQLite materialization/schema-history support, transactional document/index save/load/delete/query behavior, optimistic concurrency, and provider integration tests.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: `Microsoft.Data.Sqlite` in `Groundwork.Sqlite`; xUnit test dependencies already used by Groundwork tests

**Storage**: SQLite tables for document envelopes, generic index rows, and schema history

**Testing**: xUnit provider integration tests with SQLite in-memory connection

**Target Platform**: Generic .NET library package consumed later by Elsa Foundation

**Project Type**: Provider-specific Groundwork library plus tests

**Performance Goals**: MVP correctness; no benchmark targets in G2

**Constraints**: No Elsa references; no EF Core; equality queries only; document and index mutations must be transactional

**Scale/Scope**: One provider package, document-store contracts, SQLite tests, solution entries

## Constitution Check

| Gate | Status | Note |
|---|---|---|
| Framework §2.1 three-layer separation | PASS | Generic document contracts remain in `Groundwork.Documents`; provider implementation lives in `Groundwork.Sqlite`. |
| Framework §2.9 persistence invariants provider-neutral | PASS | Store contracts remain provider-neutral; SQLite enforces invariants through provider logic. |
| Framework §2.10 CQS at persistence boundary | PASS | Store operations separate save/delete from load/query methods. |
| Framework §2.20 provider module decomposition | PASS | SQLite provider is provider-suffixed and references generic Groundwork contracts only. |
| Elsa §E2.2 / §E2.6 | PASS | G2 has no Elsa references and no workflow runtime migration. |
| Framework §2.23 tests | PASS | Provider behavior is covered with focused SQLite integration tests. |

No justified violations.

## Project Structure

### Documentation (this feature)

```text
specs/014-groundwork-sqlite-document-store/
├── spec.md
├── plan.md
├── tasks.md
├── quickstart.md
└── checklists/
    └── requirements.md
```

### Source Code

```text
src/Groundwork/Documents/
├── Store/
│   ├── DocumentEnvelope.cs
│   ├── DocumentStoreQuery.cs
│   ├── DocumentStoreResult.cs
│   └── IDocumentStore.cs

src/Groundwork/Sqlite/
├── Groundwork.Sqlite.csproj
├── Materialization/
│   └── SqliteGroundworkMaterializer.cs
└── Documents/
    └── SqliteDocumentStore.cs

tests/Groundwork/Groundwork.Sqlite.Tests/
├── Groundwork.Sqlite.Tests.csproj
├── SqliteDocumentStoreTests.cs
└── SqliteGroundworkMaterializerTests.cs
```

**Structure Decision**: G2 adds provider-neutral document-store contracts to `Groundwork.Documents` and provider implementation to `Groundwork.Sqlite`. Tests live in a separate provider test project to keep provider dependencies out of generic `Groundwork.Tests`.

## Complexity Tracking

No constitution violations or complexity exceptions are introduced in G2.
