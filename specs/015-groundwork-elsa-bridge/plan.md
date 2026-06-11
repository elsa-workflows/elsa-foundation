# Implementation Plan: Groundwork Elsa Bridge

**Branch**: `codex/groundwork-elsa-bridge` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/015-groundwork-elsa-bridge/spec.md`

## Summary

Implement the G3 Elsa bridge MVP. Add `Elsa.Persistence.Groundwork` as an opt-in integration package that registers Groundwork manifests, provider adapters, startup materialization, and diagnostics. Validate the bridge with a SQLite adapter in tests and a Secrets-like manifest without introducing Elsa references into generic Groundwork packages.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: `CShells.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging.Abstractions`, `Elsa.Tasks.Core`, `Groundwork.Core`, `Groundwork.Documents`

**Storage**: Bridge is storage-agnostic; tests use `Groundwork.Sqlite` with in-memory SQLite

**Testing**: xUnit integration tests for bridge registration, startup task behavior, diagnostics, SQLite materialization, and dependency boundaries

**Target Platform**: Elsa Foundation server/library composition

**Project Type**: Elsa integration library plus tests

**Performance Goals**: Startup materialization correctness only; no runtime benchmark goals in G3

**Constraints**: Bridge is opt-in; bridge must not reference provider-specific packages; Groundwork projects must not reference Elsa projects; no existing EF persistence path may be replaced

**Scale/Scope**: One bridge project, one test project, solution entries, Spec Kit artifacts, program-goal pointer updates

## Constitution Check

| Gate | Status | Note |
|---|---|---|
| Framework §2.1 three-layer separation | PASS | Generic Groundwork remains separate; Elsa-specific integration lives under `Elsa.Persistence.Groundwork`. |
| Framework §2.9 persistence invariants provider-neutral | PASS | Bridge registers manifests/providers but does not redefine storage invariants. |
| Framework §2.20 provider module decomposition | PASS | Provider-specific SQLite adapter is test-local for G3; bridge depends only on provider abstraction. |
| Elsa §E2.2 / §E2.6 | PASS | G3 is opt-in and does not migrate workflow runtime hot paths. |
| Framework §2.23 tests | PASS | Bridge registration, startup, diagnostics, and boundaries are covered with focused tests. |

No justified violations.

## Project Structure

### Documentation (this feature)

```text
specs/015-groundwork-elsa-bridge/
├── spec.md
├── plan.md
├── tasks.md
├── quickstart.md
└── checklists/
    └── requirements.md
```

### Source Code

```text
src/Elsa/Persistence/Groundwork/
├── Elsa.Persistence.Groundwork.csproj
├── Diagnostics/
│   ├── GroundworkPersistenceDiagnostics.cs
│   └── GroundworkPersistenceDiagnosticSnapshot.cs
├── Extensions/
│   └── ServiceCollectionExtensions.cs
├── Options/
│   └── GroundworkPersistenceOptions.cs
├── Providers/
│   └── IGroundworkPersistenceProvider.cs
├── Secrets/
│   └── SecretsGroundworkManifestFactory.cs
├── Tasks/
│   └── MaterializeGroundworkStartupTask.cs
└── GroundworkPersistenceFeature.cs

tests/Elsa/Persistence/Groundwork/Tests/
├── Elsa.Persistence.Groundwork.Tests.csproj
├── GroundworkPersistenceFeatureTests.cs
├── MaterializeGroundworkStartupTaskTests.cs
└── GroundworkBridgeBoundaryTests.cs
```

**Structure Decision**: G3 adds only the Elsa bridge project under the existing persistence tree. SQLite remains a Groundwork provider dependency used by tests, not by the bridge package.

## Complexity Tracking

No constitution violations or complexity exceptions are introduced in G3.
