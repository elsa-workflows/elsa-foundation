# Implementation Plan: Explicit outcomes

**Branch**: `agent/claude/3052-s3-explicit-outcomes` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)

**Status**: follows the spec. The ADR it applies is merged (spec *Approval*). Implementation starts on the initiative
branch once this spec is approved (D-18).

## Summary

Replace "one outcome per completion" with an explicit outcome selection, as the #3389 draft of ADR 0045 §6 says. The
technical approach (research R-1 to R-11):

- **One selection type.** `OutcomeSelection` (unspecified, named, empty) replaces the outcome string in
  `ActivityTransition.Complete`, `RuntimeStructuralContinuation`, `ActivityCompletion` and background receipts.
- **The engine keeps its shape.** The Flowchart already routes several names (R-3). Empty stops meaning `Done`, and two
  taken connections to one node become two arrivals at its implicit join instead of a fault.
- **Outcome sets carry a multi-outcome flag.** The publish compiler pins it with the set, per node. RunJavaScript's set
  is `Done` plus its possible outcomes; `Unmatched` goes.
- **Scripts select by returning `complete(result, outcomes)`.** The evaluator recognizes the returned completion
  value and returns a selection next to the result (FR-007, Q1). There are no setters.
- **Two validation layers.** An Acornima pass at publication checks literal names; the completion projector checks
  computed names and the multi-outcome flag, and faults with the `VF-ACT-006` code.
- **Connection ports are checked at publication** (FR-019, Q9).
- **Switch gains "match any"; Complete and the graph boundary carry several outcomes.**
- **The Elsa 3 converter** maps `Done` → `Done`, converts `setOutcome` / `setOutcomes`, Switch "match any" and Complete
  with several outcomes, and loses `ELS3-BEHAVIOUR-SCRIPT-OUTCOME-ROUTING`.

## Technical Context

**Language/Version**: C# / .NET 10 (`global.json` SDK 10.0.300); Studio TypeScript/React.

**Primary Dependencies**: Jint 4.9.1 (script runtime), Acornima 1.6.1 (script analysis), System.Text.Json, EF Core,
CShells features.

**Storage**: runtime EF store (activity execution state: `ActivityCompletion`, the `runtime.completionOutcomeNames`
metadata, background work receipts). A document version for the completion record; no schema change (Q11).

**Testing**: xUnit (no FluentAssertions); activity and scripting tests; Flowchart engine tests; publishing compiler
tests; behavioural drives; the activity contract surface baseline; Elsa 3 mapping and integration tests (convert,
publish, run); Studio Vitest (`dynamicOutcomePorts.test.ts`).

**Target Platform**: Elsa 4 server hosts (Foundation host, Workbench) and Studio.

**Project Type**: libraries (activities, workflows runtime and publishing, Flowchart, scripting) plus Studio and the
Elsa 3 extension.

**Performance Goals**: no extra scheduling cost; the literal-name pass parses each RunJavaScript script once per
publication, which the expression tooling already does for expressions.

**Constraints**:
- Records written before the change stay readable (Q11).
- Runtime takes no Design reference (§E2.2): the multi-outcome flag reaches runtime through the pinned executable.
- Elsa 3 knowledge stays in `Extensions/Elsa3` (§E2.7).
- The script form is `complete(result, outcomes)` (Q1, FR-007).

**Scale/Scope**: about 15 runtime sites build a one-name list (R-2); one compiler method (R-4); two Flowchart
normalizers and one duplicate-target check (R-3); one activity (RunJavaScript), one mode (Switch), one boundary
(Graph); five converter places (R-7); about ten documents (R-10).

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md` and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §2.24.2 / §2.24.3 sanctioned patterns | PASS | No new pattern. The outcome set and its providers already exist (`[ActivityValueOutcomes]`, `ResolveOutcomes`); the selection is a value type on an existing contract. |
| §E2.2 Runtime takes no Design reference | PASS | The flag is pinned in the executable by Publishing; Runtime reads it from there. |
| §2.17 duplication beats dependency | PASS | `OutcomeSelection` lives once in `Elsa.Activities.Runtime.Core`, which every caller already references. |
| §E2.6.1 executable-always-runs | PASS | Unknown names are refused at publication when literal and fault the step when computed, as D-4 says. Executables published before stay runnable: a one-name record reads as a named selection (Q11). |
| §E5 / §4.2 versioning | ACTION | `ActivityTransition.Complete` changes its signature and `ActivityCompletion` its shape. This is a breaking change to a public contract before GA. The completion record gets a new document version with an upcast from the old one (T005). |
| §E7 CI tiers | PASS | No new tier; the Elsa 3 convert-publish-run tests already run in the Full tier. |
| §2.22 docs | ACTION | The documents in R-10 change in this delivery (FR-033). |
| §2.23.1–§2.23.2 tests | PASS | One test per acceptance scenario; failure paths (unknown names, multi-outcome on a single-outcome node, fault after a selection). |
| ADR 0064 joins | PASS | Unchanged; two arrivals from one node are handled by the existing implicit join (Q4). |
| ADR 0045 §6 (#3389) | WAIT | Applied as drafted at `18cccb00b`. The `Done` rule and the findings are applied to #3389 in `18cccb00b`; this plan follows any change before agreement. |

## Decisions

| # | Decision | Detail |
|---|---|---|
| P-1 | `OutcomeSelection` value type | FR-001 to FR-006. `Unspecified`, `Named(names)`, `Empty`; names distinct, ordered, non-blank. `ResolveNames()` gives `[Done]`, the names, or `[]`. In `Elsa.Activities.Runtime.Core`. |
| P-2 | Transition and records | FR-006, FR-025, Q11. `ActivityTransition.Complete(result, OutcomeSelection selection = Unspecified)` plus convenience overloads for one name and several. `ActivityCompletion` stores the selection; document version +1, the old `OutcomeKey` upcast to `Named([key])`. Background receipts and `BackgroundActivityCompletionCodec` follow. |
| P-3 | Runtime handlers | FR-003, FR-024. The one-name lists in R-2 become `selection.ResolveNames()`. `NormalizeOutcomeNames` loses `defaultToDone` for selections: empty stays empty. Non-routing parents ignore the selection (Q3). |
| P-4 | Flowchart | FR-021, FR-022. `NormalizeOutcomes` keeps empty empty (the default is resolved before, by P-1). The duplicate-target fault goes; each taken connection is an arrival (Q4). |
| P-5 | Outcome set with flag | FR-010 to FR-014, Q2, Q10. `ResolveOutcomes` returns the set and `AllowsMultiple`. An activity declares the flag statically (`[ActivityOutcomes(AllowsMultiple = true)]`) or through a provider (Switch: mode). The `Done` rule (model A, Q2): `Done` exactly when a completion may choose no branch; SendHttpRequest with expected codes drops it (FR-012a). `ActivityValueOutcomesAttribute.UnmatchedOutcome` becomes optional; RunJavaScript sets none. Names are pinned as written. |
| P-6 | Script outcome API | FR-007 to FR-008, Q1. `JintJavaScriptScriptEvaluator` installs the built-in `complete(result, outcomes)` for script activities only (not in the `binding-readonly-v1` surface). It returns a frozen object carrying a brand only the evaluator holds (for example a host-created object type), so script data cannot forge it. The evaluator checks the brand on the top-level returned value only and returns `(JsonElement? Value, OutcomeSelection Selection)`: `outcomes` omitted → `Unspecified`, `[]` → `Empty`, names → `Named` (repeats counted once). A brand found nested in the returned value, or an invalid `outcomes`, faults with `VF-ACT-006`. `complete` joins the reserved built-in names (D-5). |
| P-7 | Validation | FR-015 to FR-018, FR-020, Q6. A publication diagnostic `VF-AUTH-010` from an Acornima pass over RunJavaScript scripts. `ActivityCompletionProjector` checks each name and the flag and faults with code `VF-ACT-006`. Activity-declared catch-alls are ordinary declared outcomes; no node-level fallback. Publication adds `VF-AUTH-012` (warning: returned string literal equal to a connected outcome) and `VF-AUTH-013` (no `Done`, no guaranteed selection). |
| P-8 | Connection ports | FR-019, Q9. `FlowchartStructureHandler.CompileExecutableStructure` checks each connection's source port against the source node's pinned set: `VF-AUTH-011`. |
| P-9 | Switch "match any" | FR-031. A `Mode` input (`MatchFirst` default, `MatchAny`); the provider sets the flag for `MatchAny`. |
| P-10 | Complete and graph boundary | FR-032. Graph `Complete` and Finish accept several literal outcomes when the graph declares the flag; `GraphActivity` maps every selected outcome. |
| P-11 | Studio | FR-009, FR-013. `readValueOutcomePorts` adds `Done` instead of the unmatched port for RunJavaScript; connections on vanished ports are flagged. |
| P-12 | Elsa 3 converter | FR-026 to FR-030, Q7. Remove the `Done` → `Unmatched` rule; rewrite `setOutcome` / `setOutcomes` to the returned form: a converter-owned local holds the pending selection, each setter call replaces it, and each return (and a final completion value) becomes `return complete(<value>, <pending>)`, refusing where that rewrite cannot be shown faithful; preserve routing per Q7 (a converter-declared catch-all with no connection for names outside the known set) or refuse; convert Switch `MatchAny` and Complete with several literal outcomes; remove `ELS3-BEHAVIOUR-SCRIPT-OUTCOME-ROUTING`. |
| P-13 | Diagnostics | [contracts/outcome-selection.md](contracts/outcome-selection.md). |

## Project Structure

### Documentation (this feature)

```text
specs/1208-explicit-outcomes/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/outcome-selection.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
Essentials/src/Activities/Runtime/Core/Models/
  ├── OutcomeSelection.cs                  # new
  └── ActivityTransition.cs                # Complete(result, selection)
Essentials/src/Activities/Runtime/Core/Attributes/ActivityValueOutcomesAttribute.cs  # optional unmatched
Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/
  ├── ActivityCompletionProjector.cs       # per-name check, flag, VF-ACT-006 code
  └── BackgroundActivityCompletionCodec.cs
Essentials/src/Workflows/Runtime/Core/Models/{ActivityExecutionValueFlowRecords,RuntimeStructuralContinuation,BackgroundActivityWork}.cs
Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/WorkHandlers/*      # ResolveNames()
Essentials/src/Activities/Flowchart/Internal/{FlowchartExecutionEngine,FlowchartGraph,FlowchartStructureHandler}.cs
Essentials/src/Activities/Flowchart/Internal/Policies/FlowchartPolicyConnectionSelector.cs
Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/ExecutableNodeCompiler.cs
Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/ScriptOutcomeLiteralValidator.cs  # new
Essentials/src/Activities/Scripting/Activities/RunJavaScript.cs
Essentials/src/Expressions/JavaScript/{Core/Contracts,Jint/Services}/*               # complete(result, outcomes)
Essentials/src/Activities/ControlFlow/Switch/*                                       # MatchAny
Essentials/src/Activities/Graph/Runtime/Activities/{GraphActivity,Complete}.cs
Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/WorkflowIntrinsicExecutor.cs  # Finish
Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflowAdapter.ts
Extensions/Elsa3/src/Mapping/{ActivityMap,Conversion,Services,Models}/*
docs/…, specs/{095,236,239,247,267}/…                                                # R-10
```

**Structure Decision**: existing projects only. Two new source files (`OutcomeSelection`,
`ScriptOutcomeLiteralValidator`).

## Complexity Tracking

| Violation | Why needed | Simpler alternative rejected because |
|---|---|---|
| Breaking change to `ActivityTransition.Complete` and `ActivityCompletion` (§E5) | D-4 makes the selection three-state and plural | Keeping a string plus a side list would let the two disagree, the problem #3285 describes |
