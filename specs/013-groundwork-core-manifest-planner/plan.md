# Implementation Plan: Groundwork Core Manifest And Planner Kernel

**Branch**: `codex/groundwork-core-manifest-planner` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/013-groundwork-core-manifest-planner/spec.md`

## Summary

Implement the first buildable Groundwork kernel slice. Add generic `Groundwork.Core`, `Groundwork.Relational`, and `Groundwork.Documents` projects plus focused tests. The slice defines provider-neutral storage manifests, workload classifications, index/query declarations, provider capability compatibility results, materialization plan/history concepts, and relational/document planners that can consume the same sample manifest. It deliberately stops before concrete database providers, Elsa integration, startup materialization, and live database tests.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: BCL only for Groundwork projects unless a narrow `Microsoft.Extensions.*` abstraction becomes necessary; xUnit test dependencies in `tests/Groundwork/Groundwork.Tests`

**Storage**: Provider-neutral contracts only; no concrete database access

**Testing**: xUnit unit/contract tests plus architecture boundary tests

**Target Platform**: .NET library packages consumed first by Elsa Foundation

**Project Type**: Generic persistence framework libraries

**Performance Goals**: N/A for G1; no runtime hot paths or provider execution

**Constraints**: Generic `Groundwork.*` projects must not reference `Elsa.*`; no SQLite/SQL Server/PostgreSQL/MongoDB providers; no Elsa bridge; provider-neutral models only

**Scale/Scope**: Three source projects, one test project, solution entries, focused manifest/planner/capability tests

## Constitution Check

| Gate | Status | Note |
|---|---|---|
| Framework §2.1 three-layer separation | PASS | `Groundwork.Core` owns generic contracts; `Groundwork.Relational` and `Groundwork.Documents` are focused helper/planning packages. |
| Framework §2.3 primitives admission | PASS | New generic persistence concepts stay in Groundwork, not `Elsa.Primitives`. |
| Framework §2.9 persistence invariants provider-neutral | PASS | Manifest and storage-unit invariants are independent of provider enforcement. |
| Framework §2.10 CQS at persistence boundary | PASS | G1 defines plans and contracts only; future stores must keep command/query surfaces separate. |
| Framework §2.20 provider module decomposition | PASS | Concrete providers are deferred to provider-suffixed packages starting in G2. |
| Elsa §E2.2 Design/Runtime split | PASS | G1 has no Elsa references and no workflow runtime/design coupling. |
| Elsa §E2.6 artifact-only runtime | PASS | Runtime migration is not included; workload classification preserves benchmark gating. |
| Framework §2.23 tests | PASS | G1 includes focused tests for validation, planner output, capability failures, and dependency boundaries. |

No justified violations.

## Project Structure

### Documentation (this feature)

```text
specs/013-groundwork-core-manifest-planner/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── manifest-contract.md
│   ├── planner-contracts.md
│   └── test-contract.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code

```text
src/Groundwork/Core/
├── Groundwork.Core.csproj
├── Manifests/
├── Workloads/
├── Indexing/
├── Queries/
├── Capabilities/
├── Materialization/
└── Validation/

src/Groundwork/Relational/
├── Groundwork.Relational.csproj
└── Planning/

src/Groundwork/Documents/
├── Groundwork.Documents.csproj
└── Planning/

tests/Groundwork/Groundwork.Tests/
├── Groundwork.Tests.csproj
├── ManifestValidationTests.cs
├── PlannerContractTests.cs
├── ProviderCapabilityTests.cs
└── GroundworkDependencyBoundaryTests.cs
```

**Structure Decision**: G1 creates generic Groundwork projects outside `src/Elsa`. `Groundwork.Relational` and `Groundwork.Documents` depend only on `Groundwork.Core`. `tests/Groundwork/Groundwork.Tests` references the Groundwork projects and may inspect project files to prove no generic Groundwork project references `Elsa.*`.

## Complexity Tracking

No constitution violations or complexity exceptions are introduced in G1.
