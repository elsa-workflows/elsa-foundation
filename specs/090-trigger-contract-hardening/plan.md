# Implementation Plan: Trigger Publication Contract Hardening

**Branch**: `597-trigger-contract-hardening` | **Date**: 2026-07-11 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/090-trigger-contract-hardening/spec.md`

## Summary

Harden the existing first-party trigger publication path without redesigning routing or persistence. The runtime will evaluate every configured stimulus provider for each executable trigger node, require exactly one claim, record a stable provider identity in a non-persisted preflight outcome, and preserve recognized-empty non-start behavior. The recurring scheduling decorator will fully materialize Timer/Cron schedules before invoking the trigger indexer, so invalid or exhausted schedules fail before either binding or schedule replacement. Existing index/store contracts, durable document shapes, executable shapes, catalog identities, and publication response models remain unchanged.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (`net10.0`)

**Primary Dependencies**: Existing `Elsa.Workflows.Runtime.Core`, `Elsa.Workflows.Runtime`, `Elsa.Workflows.Runtime.Scheduling`, first-party activity packages, Microsoft.Extensions.DependencyInjection, existing Cronos-backed recurring calculator

**Storage**: Existing `IWorkflowTriggerBindingStore` and `IRecurringTriggerScheduleStore` implementations (in-memory and Groundwork); no new document kind or persisted field

**Testing**: xUnit 2.9, focused runtime/scheduling/activity/publishing tests, Groundwork compatibility fixtures when persistence shape is touched (expected not to be needed)

**Target Platform**: Cross-platform .NET server/runtime hosts

**Project Type**: Modular library and server-side workflow runtime

**Performance Goals**: One bounded provider scan per executable trigger node during publication; no additional work on stimulus dispatch or workflow execution hot paths

**Constraints**: Runtime remains Design-free; no raw provider metadata exposure; `Recognized([])` remains valid; provider-specific uniqueness stays provider-owned; semantic failures precede trigger/schedule mutation; no claim of cross-store transactionality

**Scale/Scope**: Four first-party trigger families (Event, Timer, Cron, HttpEndpoint), the shared trigger extractor/indexer, and the recurring schedule decorator

## Constitution Check

*GATE: Passed before research and re-checked after design. Both constitutions are draft/provisional; this plan treats them as quality gates and proposes no amendment.*

| Gate | Result | Evidence / design consequence |
|---|---|---|
| Framework §2.1 three-layer separation | PASS | Runtime contracts/models remain in `Elsa.Workflows.Runtime.Core`; logic remains in existing runtime and scheduling implementations. No new project. |
| Framework §2.6 / §2.24 sanctioned composition | PASS | `IActivityTriggerStimulusProvider` is explicitly classified as a Strategy set: multiple algorithms are registered, the executable node supplies selection context, and exact-one ownership is required. It is not a data-contribution fan-in and does not claim the rare §2.6.5 sync-contributor exception. The scheduling decorator remains local; no generic projection framework or ad-hoc event is introduced. |
| Framework §2.21 / §2.23 test discipline | PASS | Existing extractor/indexer/scheduling/provider tests are preserved and expanded branch-by-branch; feature registration remains unchanged. |
| Framework §4.2 Core compatibility | PASS | Existing extractor/indexer signatures and durable models remain. Stable provider identity is additive with a default compatibility path; any Core API addition is minor-compatible. |
| Elsa §E2.2 Design/Runtime split | PASS | Preflight consumes `WorkflowExecutable` and runtime providers only. No Runtime → Design reference. |
| Elsa §E2.6 executable-always-runs / artifact-only runtime | PASS | Existing executable shapes remain readable; corrected classification is produced on republish, and runtime execution reads only the artifact. |
| Elsa §E2.8 catalog hash immutability | PASS | Same-version catalog `ExecutionType` remains untouched; CLR trigger capability continues to be projected at compilation. This section is provisional, so PR #621 and compatibility tests remain supporting evidence. |
| Elsa §E6 naming | PASS | Proposed names stay within the component budget and use established `Provider`, `Outcome`, `Validator`, and `Exception` roles. |
| Groundwork schema evolution | PASS | `WorkflowTriggerBinding` and `RecurringTriggerSchedule` are unchanged. If implementation discovers durable shape drift, stop and obtain an amended spec/plan/tasks approval before touching schema versions, upcasters, or fixtures. |

### Post-design re-check

Phase 1 preserves every pre-research gate. The design deliberately rejects a generic publication-projection candidate hierarchy because only recurring schedules require additional durable materialization in this unit; keeping that behavior in `Elsa.Workflows.Runtime.Scheduling` avoids a speculative Core abstraction. The non-persisted preflight models do not alter executable or Groundwork wire formats.

## Project Structure

### Documentation (this feature)

```text
specs/090-trigger-contract-hardening/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── trigger-publication-contract.md
│   └── trigger-contract-matrix.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/Workflows/Runtime/Core/
├── Contracts/IActivityTriggerStimulusProvider.cs
├── Contracts/IWorkflowTriggerBindingExtractor.cs
├── Contracts/IWorkflowTriggerIndexer.cs
├── Exceptions/WorkflowTriggerPreflightException.cs
└── Models/
    ├── ActivityTriggerStimulusResult.cs
    └── WorkflowTriggerPreflightOutcome.cs

src/Elsa/Workflows/Runtime/
├── EXTENSION_POINTS.md (canonical Runtime-domain catalog; relocated here if needed)
└── Services/
    ├── WorkflowTriggerBindingExtractor.cs
    └── WorkflowTriggerIndexer.cs

src/Elsa/Workflows/Runtime/Scheduling/
└── RecurringTriggerScheduleIndexer.cs

src/Elsa/Activities/{Primitives,Scheduling,Http}/
└── first-party trigger stimulus providers

tests/Elsa/Workflows/Runtime/Tests/
├── WorkflowTriggerBindingExtractorTests.cs
└── WorkflowTriggerIndexerTests.cs

tests/Elsa/Workflows/Runtime/Scheduling/Tests/
└── RecurringTriggerScheduleIndexerTests.cs

tests/Elsa/Activities/{Runtime,Scheduling,Http}/Tests/
└── first-party provider contract tests

tests/Elsa/Workflows/Publishing/Api/Tests/
├── PublishWorkflowTriggerIndexingTests.cs
└── WorkflowExecutableCompilerTests.cs
```

**Structure Decision**: Modify the existing Runtime Core contract/model surfaces and their current implementations. Keep recurring schedule materialization inside the existing scheduling implementation. Add no projects, endpoints, stores, or host composition changes.

## Complexity Tracking

No architectural exception is required. One existing test objective changes under explicit architect/user approval:

| Approved behavior correction | Approval and rationale | Superseded expectation |
|---|---|---|
| Exhausted Cron start triggers fail publication before mutation | Approved as part of the Unit A boundary on 2026-07-11 and explicitly re-approved after `speckit-analyze`. A start trigger with no future occurrence is unroutable and violates the unit's no-silent-success outcome. | The existing warning-and-skip test is intentionally replaced; preserving it would preserve the defect this unit was approved to correct. |

The Runtime extension-point catalog currently lives under Runtime Core despite the framework §2.22.1 composition-root rule. Because this unit changes those extension points, implementation must relocate the canonical catalog to `src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md`, update the repo-wide index, and leave redirects only where needed for discoverability.

## Phase 0: Research

Research decisions are recorded in [research.md](research.md). All technical unknowns are resolved; no `NEEDS CLARIFICATION` markers remain.

## Phase 1: Design & Contracts

- [data-model.md](data-model.md) defines the non-persisted preflight outcome and existing durable candidates.
- [trigger-publication-contract.md](contracts/trigger-publication-contract.md) defines provider claiming, validation order, failure semantics, and compatibility.
- [trigger-contract-matrix.md](contracts/trigger-contract-matrix.md) pins Event, Timer, Cron, and HttpEndpoint behavior.
- [quickstart.md](quickstart.md) describes focused validation and compatibility proof.
