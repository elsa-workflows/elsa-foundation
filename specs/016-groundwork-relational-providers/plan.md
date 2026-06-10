# Implementation Plan: Groundwork SQL Server And PostgreSQL Providers

**Branch**: `codex/groundwork-relational-providers` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/016-groundwork-relational-providers/spec.md`

## Summary

Implement G4 by adding SQL Server and PostgreSQL Groundwork providers that pass the same portable document/index contract as SQLite. Extract shared ADO.NET relational document-store behavior into `Groundwork.Relational`, keep provider-specific DDL/upsert/paging/parameter SQL in provider packages, and validate both providers with Testcontainers-backed integration tests.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: `Microsoft.Data.SqlClient` 7.0.1, `Npgsql` 10.0.3, `Testcontainers.MsSql` 4.12.0, `Testcontainers.PostgreSql` 4.12.0

**Storage**: SQL Server and PostgreSQL relational tables for document envelopes, generic index rows, and schema history

**Testing**: xUnit provider contract tests with SQL Server and PostgreSQL containers

**Target Platform**: Generic .NET Groundwork provider packages consumed later by Elsa bridge/application composition

**Project Type**: Provider-specific Groundwork libraries plus integration tests

**Performance Goals**: Contract correctness; no benchmark targets in G4

**Constraints**: No Elsa references; no EF Core; equality queries only; document/index mutations transactional; provider tests must hit real databases

**Scale/Scope**: Shared relational base, two provider packages, shared provider test harness, solution entries, Spec Kit artifacts, program-goal pointer updates

## Constitution Check

| Gate | Status | Note |
|---|---|---|
| Framework §2.1 three-layer separation | PASS | Generic store contracts remain in `Groundwork.Documents`; shared relational behavior remains in `Groundwork.Relational`; provider packages remain provider-specific. |
| Framework §2.9 persistence invariants provider-neutral | PASS | Providers implement the same document/index/concurrency invariants from the generic contract. |
| Framework §2.20 provider module decomposition | PASS | SQL Server and PostgreSQL are isolated in provider-suffixed projects. |
| Elsa §E2.2 / §E2.6 | PASS | G4 adds generic providers only; no Elsa runtime hot-path migration. |
| Framework §2.23 tests | PASS | Provider behavior is covered with real container-backed integration tests plus existing SQLite tests. |

No justified violations.

## Project Structure

### Documentation (this feature)

```text
specs/016-groundwork-relational-providers/
├── spec.md
├── plan.md
├── tasks.md
├── quickstart.md
└── checklists/
    └── requirements.md
```

### Source Code

```text
src/Groundwork/Relational/
├── Documents/
│   ├── RelationalDocumentStore.cs
│   └── RelationalDocumentStoreDialect.cs
└── Materialization/
    └── RelationalMaterializerBase.cs

src/Groundwork/SqlServer/
├── Groundwork.SqlServer.csproj
├── Documents/
│   └── SqlServerDocumentStore.cs
└── Materialization/
    └── SqlServerGroundworkMaterializer.cs

src/Groundwork/PostgreSql/
├── Groundwork.PostgreSql.csproj
├── Documents/
│   └── PostgreSqlDocumentStore.cs
└── Materialization/
    └── PostgreSqlGroundworkMaterializer.cs

tests/Groundwork/Groundwork.RelationalProviders.Tests/
├── Groundwork.RelationalProviders.Tests.csproj
├── RelationalProviderContractTests.cs
├── SqlServerProviderTests.cs
└── PostgreSqlProviderTests.cs
```

**Structure Decision**: G4 extracts shared ADO.NET document behavior into `Groundwork.Relational` and keeps provider-specific dialects/materializers in provider packages. Tests use real container databases for SQL Server and PostgreSQL.

## Complexity Tracking

No constitution violations or complexity exceptions are introduced in G4.
