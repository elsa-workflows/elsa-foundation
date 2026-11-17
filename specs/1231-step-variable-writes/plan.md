# Implementation Plan: Activities change variables, committed with the step

**Branch**: `claude/3052-s7-define-spec` | **Date**: 2026-11-17 | **Spec**: [spec.md](spec.md)

## Summary

Give step bodies a variable session (ADR 0090 as amended): reads of committed values recorded as session reads,
explicit writes collected as intents and applied in the step's checkpoint commit under S-10's commit rule, intents
before captures. `RunJavaScript` writes by name resolved and pinned at publish (S-4's resolver, S-8's change finder).
C# activities read and write through bound `VariableReference<T>` / `ReadOnlyVariableReference<T>` inputs, or free
names on a type marked `[AccessesUndeclaredVariables]`. Publish refuses undeclared, computed, opaque, transient and
read-only targets, feeds the may-write set to the concurrent-write rule, and warns on overlap with a capture. In the
same change, C9 in `BackgroundEligibility` refuses each new signal. The Elsa 3 converter turns `setVariable`,
`set<Name>()` and nested mutation inside `RunJavaScript` into `variables` writes.

## Technical Context

**Language/Version**: C# / .NET (repo default); JavaScript via Jint 4.9.1 (`IsolatedJintEngine`)
**Primary Dependencies**: S-10 lifecycle types (#4316), S-11 `BackgroundEligibility` (#4725), S-4 body read surface,
S-8 script value bridge
**Storage**: frames and closed-scope rows as S-10 extends them; own-write bases on the activity execution state
**Testing**: xunit only (constitution); runtime drives in `Essentials/tests/Workflows/Runtime/…`, converter tests in
`Extensions/Elsa3/tests/…`
**Target Platform**: Elsa runtime host, all persistence providers (in-memory, EF)
**Performance Goals**: no serialization for read-only variables; write cost measured (FR-035)
**Constraints**: no database transaction held across user code; inline no-gap (spec 1221 FR-021) preserved; Runtime
must not reference Design (§E2.2)
**Scale/Scope**: ~8 projects touched (Runtime Core, Runtime, Activities Runtime, Publishing, Design Validations,
JavaScript expressions/Jint, Scripting activities, Elsa 3 mapping)

**Order and gates.** S-10 (#4316) and S-11 (#4725) merge first; if not, the implementing PR depends on them. Phases
2–4 can start once S-10 is in; Phase 6 (C9) needs S-11 and lands in the same PR as Phase 4's types (ADR 0093
*Consequences*). Phase 5's member changes and S-8 US3's end-to-end test land with whichever of S-7 and S-8 is second.

## Constitution Check

*Loaded: `.specify/memory/constitution.md` and `.specify/memory/constitution-framework.md`.*

| Gate | Status | Notes |
|---|---|---|
| §2.24.2 / §2.24.3 sanctioned patterns | PASS | Extends the activity execution context (an existing contract) and the existing checkpoint commit; the marker is an activity attribute like `[BackgroundExecutionSafe]`. No new extension point. |
| §E2.2 Runtime takes no Design reference | PASS | The session lives in Runtime/Runtime Core; publish analysis lives in Publishing and Design Validations, which already reference Runtime Core. |
| §2.17 duplication beats dependency | PASS | Reuses S-10's commit rule and S-4's resolver instead of copies. |
| ADR 0045 §6 | PASS | `context.Variables` exposes variables only; outputs and outcomes keep their typed result. |
| §E2.6.1 executable-always-runs | ACTION | A pinned background `RunJavaScript` whose body now touches variables is refused at the worker with an incident; before this spec no such body existed, so no published executable changes behaviour. Release notes say so. |
| §E5 / §4.2 versioning | ACTION | New public API (`VariableReference<T>`, `ReadOnlyVariableReference<T>`, `AccessesUndeclaredVariablesAttribute`, `IStepVariables`) in `PublicAPI.Unshipped.txt`; `RunJavaScript` contract gains write metadata → version per its module's rule. |
| §2.22 docs | ACTION | Activity authoring docs (step writes, references, marker), script docs (write forms, copy explicitly with `{...variables.o}`), `EXTENSION_POINTS.md` for the marker, Elsa 3 conversion guide. |
| §2.23 tests | PASS | One test per ADR 0090 *Required evidence* item in scope; one C9 test per signal at publication and run time. |
| ADR 0090 / 0092 / 0093 | PASS | Applied as merged; no amendment with the recommended answers (research R-6, R-7). |

## Decisions

| # | Decision | Detail |
|---|---|---|
| P-1 | Session behind the activity context | R-2. `IActivityExecutionContext.Variables` (`IStepVariables`); implemented in `Elsa.Workflows.Runtime` over the visible frame chain; created per step by the invoke and resume handlers. |
| P-2 | Intents ride the existing checkpoint | Handed to the commit the handler builds for the transition; applied before captures with S-10's `VariableCommitRule`; write records with `SessionChange`. |
| P-3 | Bases | R-3: session read → input read set → blind; own applied writes advance a persisted per-invocation base, restored on resume. |
| P-4 | Script writes pinned at publish | R-4: extend S-4's body analyzer with write forms; pin targets, may-write set, body touch set. |
| P-5 | References in the contract | R-5: contract input kind `VariableReference` + direction; marker pinned in the contract. |
| P-6 | C9 | R-6: `AccessesVariablesByName(Type)` covers the type signals; a new subject fact `TouchesVariables` (from contract and node) covers the contract-visible signals and `RunJavaScript`'s body touch set. |
| P-7 | Converter | R-8: script mode rewrites setters to `variables` writes; new stable refusal codes; expression mode unchanged except guidance. |
| P-8 | Minimal script write path before S-8 | Whole assignments only; member changes come from S-8's bridge. |

## Project Structure

### Documentation (this feature)

```text
specs/1231-step-variable-writes/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/step-variables.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
Essentials/src/Activities/Runtime/Core/
├── Contracts/IActivityExecutionContext.cs          # + Variables
├── Contracts/IStepVariables.cs                     # new
├── Models/VariableReference.cs                     # new: VariableReference<T>, ReadOnlyVariableReference<T>
├── Attributes/AccessesUndeclaredVariablesAttribute.cs  # new
└── Models/ActivityContract.cs                      # input kind + direction; marker
Essentials/src/Workflows/Runtime/Core/
├── Background/BackgroundEligibility.cs             # C9 extension (S-11's file)
└── Models/StepVariableSession.cs, WriteIntent.cs   # new
Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/
├── Services/ActivityExecutions/SimpleActivityExecutionContext.cs  # session wiring
└── Services/Values/RuntimeContainerScopeService.cs                # shared read path
Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/
├── WorkflowInvokeActivitySchedulerWorkHandler.cs   # session per step; intents into the commit
├── WorkflowResumeBookmarkSchedulerWorkHandler.cs   # fresh session on resume; own bases restored
└── RuntimeOutputCaptureProjection.cs               # intents before captures
Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/ExecutableNodeCompiler.cs  # pin may-write set, touch set, marker
Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/Validators/ValueFlowValidator.cs  # may-write set, overlap warning
Essentials/src/Expressions/JavaScript/…             # S-4 body analyzer + write forms; Jint write surface
Essentials/src/Activities/Scripting/Activities/RunJavaScript.cs
Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs
Extensions/Elsa3/src/Models/Elsa3ActivityDiagnosticCodes.cs (or Elsa3ImportDiagnosticCodes)
```

**Structure Decision**: existing projects only; no new project or package.

## Complexity Tracking

None. The one structural addition (`context.Variables`) is the sanctioned exception ADR 0090 names.
