# Implementation Plan: Variable lifecycle inspection

**Branch**: `claude/3052-s6-define-b4bd05` | **Date**: 2026-11-16 | **Spec**: [spec.md](spec.md)

**Status**: approved with the spec. Implemented on the initiative branch (D-18) after spec 1221's implementation of
the write history, including the write-time data this plan reads, has merged there.

## Summary

Read the write history spec 1221 records and present it through the runtime inspection API: per-variable history with
the winning write, lost updates and concurrent overwrites, a commit-ordered journal, and a comparison of two instances
of one version. Research R-1 to R-8 hold the reasons.

- **Write-time data from spec 1221 (Q1, Q7, Q9, R-1).** The instance commit sequence on write records and lifecycle
  stamps (including the scheduling commit), frame and digest on write records, each row's commit-sequence range, the
  alteration write record, and the written values as activity value evidence. This plan only reads them.
- **Store columns and per-activity count (Q2, Q7, R-2, R-6).** Columns and an index for each row's commit-sequence
  range, and a per-activity lost-update count summed for the details; a migration in every runtime store provider.
- **Scheduling causes (Q3, R-3).** The one run-time change of this plan: provenance records the completions that
  caused a scheduling (a callback's or next iteration's completed child, a join's arrivals).
- **Derived read model (Q2, R-2).** Rows paged by their commit-sequence range, stamps and records flattened, alteration
  records merged, ordered by (commit sequence, position). No new store.
- **Concurrent overwrite marking (Q3, Q4, R-3).** A bounded happens-before walk over provenance, when read.
- **Values by state; written values through the existing payload read, record envelopes through its extension (Q5, R-4).**
- **Comparison by run-time path and digest (Q6, R-5).**
- **Lost-update count on instance details, no incident (Q7, R-6).** **Existing permission (Q8).**

## Technical Context

**Language/Version**: C# / .NET 10 (`global.json` SDK).

**Primary Dependencies**: the existing runtime API endpoints in `Elsa.Workflows.Runtime.Api`,
System.Text.Json source-generated context `WorkflowsRuntimeJsonContext`; no new external dependency.

**Storage**: no new store. Optional members of its own: `CausedBy` on `ActivitySchedulingProvenance` and
`LostUpdateWarningCount` on `ActivityExecutionState`; store columns and an index for the row's commit-sequence range
and the count, with a migration in every runtime store provider. Everything else it reads is recorded by spec 1221.
Older records read back with members absent.

**Testing**: xUnit (no FluentAssertions). Read-model unit tests over built record sets; runtime drives (parallel
branches, blind `Set`, joins, `Parallel`, loops, suspension, `Fault`, accepted completion, coalesced segments,
`ModifyVariable`); endpoint tests for paging, filters, access and refusal; read-time measurement for SC-004 and the
causes overhead for SC-006.

**Target Platform**: Elsa 4 server hosts (Foundation host, Workbench).

**Project Type**: libraries — `Elsa.Workflows.Runtime.Core` (provenance member), `Elsa.Activities.Runtime` and the
control-flow activities (scheduling causes where callbacks, iterations and joins schedule), the runtime store
providers (columns, index, migration), `Elsa.Workflows.Runtime.Api`
(read model, payload read, endpoints, views).

**Performance Goals**: run time: the cause ids on a callback, iteration or join scheduling (SC-006). Read time: first
page of history/journal for 10,000 activity executions within the existing activity-list read time on the same store,
and an unchanged instance-details read (SC-004).

**Constraints**:
- Runtime takes no Design reference (§E2.2); comparison matches by executable node id, which the executable carries.
- No value leaves the API except through the authorized payload read (SC-005).
- The write-history physical storage stays as spec 1221 chose it.
- New members are optional; positional signatures of spec 1221's records stay as they are.

**Scale/Scope**: the scheduling sites that act on a completion (parent callbacks, loop iterations, join and `Parallel`
completion), one read-model service, one payload-read extension and five endpoints.

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
| Sensitive values | PASS | Value states and the authorized payload read, extended to write records under the existing payload permission (FR-006, FR-012, FR-013). |
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
└── ActivitySchedulingProvenance.cs     # + CausedBy (optional)
Essentials/src/Activities/…                       # scheduling causes at callbacks, loop iterations, joins, Parallel
Essentials/src/Workflows/Runtime/Api/
├── Services/VariableLifecycleInspection.cs       # derived read model (history, trace, journal, comparison)
├── Handlers/VariableLifecycleInspectionService.cs
├── Models/VariableLifecycleViews.cs
├── Services/VariableWritePayloadReader.cs        # payload read for write-record values
└── Endpoints/Instances/{Variables,VariableWrites,VariableWritePayload,Journal,Compare}/Endpoint.cs
Essentials/test/…/Runtime.Api.Tests and Runtime.Tests   # VariableLifecycleInspection/*
docs/runtime-variable-lifecycle.md       # inspection section
```

**Structure Decision**: extend the existing runtime API project and its inspection services; no new project.

## Complexity Tracking

None.
