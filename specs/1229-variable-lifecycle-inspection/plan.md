# Implementation Plan: Variable lifecycle inspection

**Branch**: `claude/3052-s6-define-b4bd05` | **Date**: 2026-11-16 | **Spec**: [spec.md](spec.md)

**Status**: approved with the spec (2026-11-17). Written to the answers of Q1–Q9 (as recommended). Implemented on the initiative branch
(D-18) after spec 1221's implementation of the write history has merged there.

## Summary

Read the write history spec 1221 records and present it through the runtime inspection API: per-variable history with
the winning write, lost updates and concurrent overwrites, a commit-ordered journal, and a comparison of two instances
of one version. Research R-1 to R-8 hold the reasons.

- **One instance commit sequence (Q1, R-1).** A per-instance counter, assigned to each logical checkpoint in the
  committer under the instance fence, stamped on write records and activity lifecycle transitions. Coalesced commits
  keep each logical checkpoint's number.
- **Derived read model (Q2, R-2).** A runtime-API service reads one instance's activity execution states (paged),
  flattens their write records and transitions, merges alteration records, and orders by commit sequence. No new store.
- **Concurrent overwrite marking (Q3, Q4, R-3).** Computed when read: the replaced value's commit does not happen
  before the overwriting invocation by scheduling provenance. Provenance gains the arrivals that satisfied a join.
- **Values by state only (Q5, R-4).** Reuse the existing value-evidence states and the authorized payload read. Until
  spec 1221 FR-025 lands, non-operational values read `omitted`.
- **Comparison by node, origin path and digest (Q6, R-5).**
- **Counts on instance details, no incident (Q7, R-6).**
- **Existing permission (Q8).** **Alteration write records (Q9, R-7).**

## Technical Context

**Language/Version**: C# / .NET 10 (`global.json` SDK).

**Primary Dependencies**: the existing runtime API endpoints in `Elsa.Workflows.Runtime.Api`,
System.Text.Json source-generated context `WorkflowsRuntimeJsonContext`; no new external dependency.

**Storage**: no new store. Two additive optional members: the commit sequence on `VariableWriteRecord` and on the
activity lifecycle stamps of `ActivityExecutionState`; the instance's last commit sequence on the workflow execution
state; an alteration write record on the alteration job outcome. Older records read back with the members absent.

**Testing**: xUnit (no FluentAssertions). Read-model unit tests over built record sets; runtime drives (parallel
branches, script blind write, suspension, `Fault`, coalesced segments, `ModifyVariable`); endpoint tests for paging,
filters, access and refusal; one read-time measurement for SC-004.

**Target Platform**: Elsa 4 server hosts (Foundation host, Workbench).

**Project Type**: libraries — `Elsa.Workflows.Runtime.Core` (record members), `Elsa.Workflows.Runtime` (committer
stamp, alteration record), `Elsa.Workflows.Runtime.Api` (read model, endpoints, views).

**Performance Goals**: run time: one counter increment per logical checkpoint one number per write record and
transition, and the arrival ids on a join completion (SC-006). Read time: first page of history/journal for 10,000 activity executions within the existing
activity-list read time on the same store (SC-004).

**Constraints**:
- Runtime takes no Design reference (§E2.2); comparison matches by executable node id, which the executable carries.
- No value leaves the API except through the existing authorized payload read (SC-005).
- The write-history physical storage stays as spec 1221 measured it (Open question B there).
- Spec 1221's implementation is not edited by this plan; where its shapes are missing a member, this plan adds it as an
  optional member (keeping positional signatures, as that implementation's review asked).

**Scale/Scope**: one stamp site (`RuntimeCheckpointCommitter`, plus the coalescing drain for logical checkpoints), the
join-completion scheduling site (join arrivals), one alteration site (`ModifyVariableAlterationHandler` / alteration outcome), one read-model service and five endpoints.

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md` and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §2.24.2 / §2.24.3 sanctioned patterns | PASS | No new pattern: an endpoint + handler service, as the existing inspection reads. |
| §E2.2 Runtime ↛ Design | PASS | All code is runtime-side. |
| ADR 0080 internal by default | PASS | Read model and services internal; only views and route contracts are public. |
| ADR 0080 risk-based tests | PASS | Tests on ordering, overwrite marking, access and paging; no tests for plain view mapping. |
| Extension-point catalog | PASS | No new extension point; `EXTENSION_POINTS.md` unchanged unless the read model is made replaceable (not planned). |
| Sensitive values | PASS | Value states and the existing payload read only (FR-006, FR-012, FR-013). |
| No FluentAssertions | PASS | xUnit only. |

## Project Structure

### Documentation (this feature)

```text
specs/1229-variable-lifecycle-inspection/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── inspection-api.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
Essentials/src/Workflows/Runtime/Core/Models/
├── VariableLifecycleModels.cs          # + CommitSequence on VariableWriteRecord (optional)
├── ActivityExecutionState.cs           # + lifecycle transition stamps with commit sequence (optional)
├── ActivitySchedulingProvenance.cs     # + join arrivals (optional)
└── Alterations/WorkflowAlterationModels.cs  # + alteration write record on the outcome (optional)
Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/
├── Checkpoints/RuntimeCheckpointCommitter.cs     # assign the sequence under the fence
├── Coalescing/                                   # number logical checkpoints before folding
└── Alterations/Handlers/ModifyVariableAlterationHandler.cs  # alteration write record
Essentials/src/Workflows/Runtime/Api/
├── Services/VariableLifecycleInspection.cs       # derived read model (history, trace, journal, comparison)
├── Handlers/VariableLifecycleInspectionService.cs
├── Models/VariableLifecycleViews.cs
└── Endpoints/Instances/{Variables,VariableWrites,Journal,Compare}/Endpoint.cs
Essentials/test/…/Runtime.Api.Tests and Runtime.Tests   # VariableLifecycleInspection/*
docs/runtime-variable-lifecycle.md       # inspection section
```

**Structure Decision**: extend the existing runtime API project and its inspection services; no new project.

## Complexity Tracking

None.
