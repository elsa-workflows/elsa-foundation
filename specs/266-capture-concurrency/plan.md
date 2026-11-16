# Implementation Plan: Warn about writes to one variable whose order is not defined

**Branch**: `claude/3052-s5-impl-9b216e` (base `initiative/3052-elsa3-to-elsa4-conversion`) | **Date**: 2026-11-16 |
**Spec**: [spec.md](spec.md) | **Board row**: S-5 on [#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052) | **Issue**: [#3198](https://github.com/elsa-workflows/elsa-foundation/issues/3198)

## Summary

Every structure declares how its children relate in time (before, unordered, mutually exclusive, or not said) and
whether it completes only after its children have ended. One design-time analysis reads only those declarations and
reports, per variable, every node whose write to it (output capture, Set, Merge, Reduce) has no defined order. The
existing `ValueFlow/ConcurrentWrite` finding becomes that warning in workflow draft validation; graph activity draft
validation and compilation report the same warning as an activity diagnostic. Nothing blocks on it. Research:
[research.md](research.md).

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: `Elsa.Workflows.Design.Core` (structure contract), `Elsa.Workflows.Design.Validations.Core`
(analyzer contract and findings), `Elsa.Workflows.Design.Validations` (analyzer implementation, workflow validator), `Elsa.Activities.Graph.Design` (graph provider), the
structure handlers in `Elsa.Activities.{Sequence,Flowchart,Parallel,If,Switch,Loops,Compensation,Bpmn}`

**Storage**: N/A (design time only)

**Testing**: xunit only (no FluentAssertions); tests next to the existing ones in
`Essentials/tests/Workflows/Design/Tests`, `Essentials/tests/Activities/Graph/Tests`,
`Essentials/tests/Activities/ControlFlow/Tests`, and the Flowchart and BPMN test projects

**Target Platform**: Elsa server (design API, publishing)

**Project Type**: library

**Performance Goals**: the analysis is quadratic in the writers of one variable and linear in tree depth; graph
reachability is computed once per structure node and cached for the pass

**Constraints**: no run-time change (S-10 owns the commit path); no multi-outcome flag (S-3); no edit to
`ActivityExecutionState.cs`

**Scale/Scope**: authored definitions of up to a few thousand nodes

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md` and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §2.24.2 / §2.24.3 sanctioned patterns | PASS | No new pattern: two default-implemented members on the existing activity-owned structure handler contract (the same shape as `ChildrenCompletedBefore` and `AssessChildResult`). |
| §E2.2 Runtime takes no Design reference | PASS | Design-time only; no Runtime project changes. |
| §2.1 / §2.17 thin `.Core`, §2.23.3 visibility | PASS | Review round 1 moved the analysis out of `Elsa.Workflows.Design.Core`: the contract and its finding models (crossing the package boundary to Graph Design) are public in `Elsa.Workflows.Design.Validations.Core`; the implementation is internal in `Elsa.Workflows.Design.Validations`. Graph reachability helpers stay inside each structure module. |
| Extension-point catalogs | ACTION | FR-012: `Elsa.Workflows.Design.Core` and every built-in structure module record the declaration (T014). |
| §2.22 docs | ACTION | FR-004, FR-014: the authoring reference states the rule, last-commit-wins, the limits and the inspection gap (T021). |
| §2.23.1–§2.23.2 tests | PASS | One test per acceptance scenario, plus the declaration of every built-in structure and the SC-005 evidence test. |
| ADR 0048 §4/§5 | PASS | Already amended by #3286 (FR-013 done). |
| ADR 0092 dec. 5–6 | PASS | Publish-time only; the message points to the `Fault` opt-in. |
| ADR 0045 §6 (2026-11-16) | PASS | No multi-outcome flag; every node may select several outcomes, so port-distinct paths are unordered (research R-2, finding F-1). |

## Decisions

| # | Decision | Detail |
|---|---|---|
| P-1 | Declaration | `ActivityChildTimeRelation` and `IActivityStructureHandler.RelateChildren` / `CompletesAfterItsChildren`, forwarded by `IActivityStructureService` ([data-model.md](data-model.md)). |
| P-2 | Analysis | `IUnorderedVariableWriteAnalyzer` (contract and finding models in `Elsa.Workflows.Design.Validations.Core`), implemented by the internal `UnorderedVariableWriteAnalyzer` in `Elsa.Workflows.Design.Validations` and registered by its feature (the `IActivityDataSelectionAnalyzer` pattern), applying the rules in the data model. `.Core` stays thin (framework §2.1, §2.17). |
| P-3 | Workflow validator | `ValueFlowValidator` replaces its concurrency code with P-2; writes are captures plus Set/Merge/Reduce (and legacy `elsa.intrinsic.set@1`); severity `Warning`. The hard-coded kinds stay only for Parallel branch identities, which are not ordering (FR-009). |
| P-4 | Graph provider | `ValidateAsync` and `CompileAsync` run the analyzer (P-2, required by `GraphActivitiesDesignFeature`) over the body with captures as writes and add warnings, alongside any errors ([contract](contracts/unordered-write-warning.md)). |
| P-5 | Built-in declarations | Sequence: index order. If, Switch: exclusive. Loops: one child, never overlapping. Parallel: unordered; completes after children unless threshold < branches. Flowchart: reachability with waiting joins, `merge` exceptions, trigger-entry exclusivity, port-blind (R-2, R-4); completes after children unless a `firstWins` node exists. BPMN: element-graph analysis (R-6), multi-instance self-overlap, completes after children. CompensationScope: step before its handlers; handlers per its replay order, otherwise `Unordered`; completes after children. |
| P-6 | Message | One finding per variable, all writers, last-commit-wins, the three fixes, the ADR 0092 `Fault` pointer, and the undeclared-structure sentence when D-2b applied. |
| P-7 | Docs | `docs/reference/variable-write-order.md` (new) holds FR-004/FR-014 text; `activity-definition-authoring-composition.md` links it instead of saying the check is not built. |

## Project Structure

### Documentation (this feature)

```text
specs/266-capture-concurrency/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/unordered-write-warning.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code

```text
Essentials/src/Workflows/Design/Core/
├── Contracts/IActivityStructureHandler.cs        # + RelateChildren, CompletesAfterItsChildren
├── Contracts/IActivityStructureService.cs        # + forwarding members
├── Models/ActivityChildTimeRelation.cs           # new
└── Services/DefaultActivityStructureService.cs   # forwarding
Essentials/src/Workflows/Design/Validations/Core/
├── Contracts/IUnorderedVariableWriteAnalyzer.cs  # new
└── Models/UnorderedVariableWriteAnalysis.cs      # new
Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/
├── Services/UnorderedVariableWriteAnalyzer.cs    # new, internal
└── Validators/ValueFlowValidator.cs
Essentials/src/Activities/Graph/Design/Services/GraphActivityProvider.cs
Essentials/src/Activities/{Sequence,Flowchart,ControlFlow/Parallel,ControlFlow/If,ControlFlow/Switch,ControlFlow/Loops,Compensation,Bpmn/Design}/  # declarations + EXTENSION_POINTS.md
Essentials/tests/Workflows/Design/Tests/        # analysis + validator tests
Essentials/tests/Activities/Graph/Tests/        # graph draft + compilation tests
Essentials/tests/Activities/ControlFlow/Tests/  # Parallel/If/Switch/Loop declarations
```

**Structure Decision**: existing projects only; no new project.

## Complexity Tracking

None.
