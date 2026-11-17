# Implementation Plan: Background eligibility

**Branch**: `claude/3052-s11-define-af1366` | **Date**: 2026-11-16 | **Spec**: [spec.md](spec.md)

**Status**: follows the spec, approved 2026-11-16. Both ADRs it applies are merged (spec *Approval*), so it can be
implemented on the initiative branch once this spec is merged there (D-18).

## Summary

Put ADR 0093's enforced criteria in one place and close the gaps it names (research R-1 to R-7):

- **One component.** `BackgroundEligibility` in `Elsa.Workflows.Runtime.Core` holds an ordered list of criteria
  C1–C6, C9, C10. Each criterion reads one subject record that publication and run time each build from what they know
  (R-3). Publication, the coordinator's admission and the worker call it; the two old lists go.
- **Refusals name the criterion.** `VF-BG-001 (C<n>): …`, first failing criterion wins (R-5, Q2).
- **Run time catches up.** C3 and the stream/transient part of C4 are checked at run time too.
- **C9 and C10 are new checks.** `IRuntimeScopedVariableReader` is refused; worker-bound inputs without a durable,
  materializable, protected representation are refused before admission.
- **No bypass.** Background nodes compiled by template providers (the agent's) are checked when placed (FR-012, Q9).
- **C7 and C8 on the attribute**, plus inheritance (Q6) and the two cases not to mark (Q8).
- **Built-in activities assessed and marked** (R-4), with issues for implementation gaps.
- **Causal-order evidence** on the in-memory and EF Core providers (R-6), using S-10's write history (Q7).

## Technical Context

**Language/Version**: C# / .NET 10 (`global.json`).

**Primary Dependencies**: none new. Projects touched: `Elsa.Workflows.Runtime.Core`, `Elsa.Workflows.Publishing`,
`Elsa.Activities.Runtime`, `Elsa.Activities.Runtime.Core` (attribute docs), and the modules whose built-in activities
get marked.

**Storage**: none changed. `SupportsBackgroundExecution` is already pinned in `ActivityContract`; marking a built-in
changes its contract fingerprint (spec 241: default false keeps historical bytes), so its activity version moves.

**Testing**: xUnit (no FluentAssertions). Unit tests for the component (one per criterion, publish and run time);
publishing compiler tests; worker and coordinator tests; an in-memory and an EF Core flow test (the existing
`NativeBackgroundActivityFlowchartEfTests` pattern); one end-to-end background run per marked built-in.

**Target Platform**: Elsa 4 server hosts with `BackgroundActivities` enabled.

**Project Type**: libraries.

**Performance Goals**: no measurable change; the checks run once per publish of a background node and once per
admission and worker activation, as today.

**Constraints**:
- Runtime takes no Design reference (§E2.2); the component reads only runtime contracts.
- Publishing gains no new reference (R-2).
- S-10's files: only one call line each in `ExecutableNodeCompiler.cs` and `BackgroundActivityCoordinator.cs` (R-7).

**Scale/Scope**: one new class and its tests; four call sites; one attribute's documentation; the built-in marks
(R-4); two provider tests; four documents.

## Constitution Check

*Loaded: `.specify/memory/constitution.md` and `.specify/memory/constitution-framework.md`. Re-checked after Phase 1:
unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §2.24.2 / §2.24.3 sanctioned patterns | PASS | A static rule set over runtime contracts; no new extension point. Not a replacement contract on purpose (R-2). |
| §E2.2 Runtime takes no Design reference | PASS | The component sits in Runtime Core and reads pinned runtime contracts only. |
| §2.17 duplication beats dependency | PASS | The point is to remove a duplicated rule list; Publishing already depends on Runtime Core. |
| §2.1 / §2.17 thin `.Core` implementation | DEVIATION | `BackgroundEligibility` is a rule set (domain-deciding branches) in `Elsa.Workflows.Runtime.Core`. Justified under Complexity Tracking. |
| §E2.6.1 executable-always-runs | ACTION | A pinned background node that now fails C3, C4, C9 or C10 at run time is refused with an incident (Q5). No shipped activity was marked before this spec, so no pinned built-in can be in the background and only custom activities can be affected; the release notes say so (T030). |
| §E5 / §4.2 versioning | ACTION | Marking a built-in changes its contract fingerprint; each marked activity's version moves per its module's rule (T020). `BackgroundEligibility` is new public API in `PublicAPI.Unshipped.txt`. |
| §2.22 docs | ACTION | Spec 241's contract, the attribute's docs, the background activities doc and `EXTENSION_POINTS.md` if it lists the attribute (T028–T030). |
| §2.23.1–§2.23.2 tests | PASS | One refusal test per criterion and point; the happy path per marked built-in; provider pair for decision 6. |
| ADR 0093 | PASS | Applied as merged (`875aa14c7`). |
| ADR 0092 | PASS | Only read: the conflict rule background results meet is S-10's (R-7). |

## Decisions

| # | Decision | Detail |
|---|---|---|
| P-1 | One static component in Runtime Core | R-2. Two entry points, one criterion list. |
| P-2 | First failing criterion wins | Criteria run in order C1 … C10; the report is stable whichever point checks. |
| P-3 | Placement precondition stays outside C1–C10 | Undefined `ExecutionPlacement` is refused before the criteria, as today. |
| P-4 | C10 reuses the snapshot checks | `EnsureSecretBindingsAdmissible` and the snapshot materializer stay; the component adds the input representation check and calls them where they apply. |
| P-5 | No queue change | R-6; evidence only. |

## Project Structure

### Documentation (this feature)

```text
specs/1230-background-eligibility/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── background-eligibility.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
Essentials/src/Workflows/Runtime/Core/
└── Background/BackgroundEligibility.cs           # new: criteria, subject, refusal
Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/
└── ExecutableNodeCompiler.cs                      # calls the component; ValidateBackgroundExecution goes
Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/
├── BackgroundActivityEligibility.cs               # removed (or reduced to the metadata constants)
├── BackgroundActivityCoordinator.cs               # calls the component
└── BackgroundActivityWorker.cs                    # calls the component; C6 reported through it
Essentials/src/Activities/Runtime/Core/Attributes/
└── BackgroundExecutionSafeAttribute.cs            # C7, C8, inheritance docs
Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/
└── ActivityTemplatePlacer.cs                      # template-compiled background nodes checked (FR-012)
Essentials/src/Activities/**, Extensions/**/src/** # [BackgroundExecutionSafe] on qualifying built-ins (R-4)
Essentials/tests/Workflows/Runtime/Tests/Background/BackgroundEligibilityTests.cs   # new
Essentials/tests/Workflows/Publishing/Api/Tests/WorkflowExecutableCompilerTests.cs # extended
Essentials/tests/Activities/Runtime/Tests/BackgroundActivityWorkerTests.cs         # extended
Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/          # causal-order pair
```

**Structure Decision**: no new project; the component goes where ADR 0093 decision 3 puts it.

## Complexity Tracking

| Deviation | Why it is needed | Simpler alternative rejected because |
|---|---|---|
| A domain-deciding rule set (`BackgroundEligibility`) lives in `Elsa.Workflows.Runtime.Core`, above the §2.1/§2.17 thin bar for a `.Core`. | ADR 0093 decision 3 (accepted) requires one component for publication, template placement, admission and the worker, in Runtime Core, because Publishing and `Elsa.Activities.Runtime` both reference it and it checks contracts that live there. It is static and dependency-free: no service, no persistence, no infrastructure. | An implementation project both callers reference does not exist; creating one, or adding a Publishing → `Elsa.Workflows.Runtime` reference, adds a dependency direction ADR 0093 avoided. Two copies in the callers are what the ADR removes. |
