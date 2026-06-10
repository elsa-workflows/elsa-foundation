# Implementation Plan: Groundwork Runtime Evaluation And Hardening

**Branch**: `codex/groundwork-runtime-evaluation-hardening` | **Date**: 2026-06-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/020-groundwork-runtime-evaluation-hardening/spec.md`

## Summary

Implement G8 by adding an Elsa Groundwork runtime evaluation surface that classifies workflow runtime persistence candidates using Groundwork workload categories, preserves benchmark/concurrency/retry/operational evidence gates, and publishes a report with explicit go/no-go decisions. No workflow runtime store is migrated in this slice.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: Existing `Elsa.Persistence.Groundwork`, `Groundwork.Core`

**Storage**: N/A; decision/evaluation surface only

**Testing**: xUnit tests in Elsa Persistence Groundwork tests

**Target Platform**: Elsa Groundwork bridge package

**Project Type**: Library/integration decision surface plus report

**Performance Goals**: No runtime performance target in code; benchmark gates are recorded as migration prerequisites

**Constraints**: Do not migrate runtime stores; do not classify runtime hot paths as Groundwork default; keep generic Groundwork packages Elsa-free

**Scale/Scope**: Runtime store evaluator, tests, report, G8 Speckit artifacts, roadmap pointer updates

## Constitution Check

| Gate | Status | Note |
|---|---|---|
| Framework §2.1 three-layer separation | PASS | Evaluation lives in Elsa bridge, not generic Groundwork. |
| Framework §2.9 persistence invariants provider-neutral | PASS | Runtime hot paths remain gated until evidence exists. |
| Framework §2.20 provider module decomposition | PASS | No provider package changes. |
| Elsa §E2.2 / §E2.6 | PASS | No runtime store implementation or Design/Runtime dependency change. |
| Framework §2.23 tests | PASS | Tests preserve conservative runtime candidate classification. |

No justified violations.

## Project Structure

### Documentation (this feature)

```text
specs/020-groundwork-runtime-evaluation-hardening/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── runtime-evaluation-matrix.md
├── checklists/
│   └── requirements.md
└── tasks.md

docs/reports/
└── groundwork-runtime-evaluation.md
```

### Source Code

```text
src/Elsa/Persistence/Groundwork/RuntimeEvaluation/
├── RuntimeStoreCandidate.cs
├── RuntimeStoreEvaluation.cs
└── GroundworkRuntimeStoreEvaluator.cs

tests/Elsa/Persistence/Groundwork/Tests/
└── GroundworkRuntimeStoreEvaluatorTests.cs
```

**Structure Decision**: G8 is Elsa-specific evaluation of Elsa runtime stores, so it belongs in `Elsa.Persistence.Groundwork`, not generic Groundwork packages.

## Complexity Tracking

No constitution violations or complexity exceptions are introduced in G8.
