# Implementation Plan: A step writes its reusable body's own variables

**Branch**: `agent/claude/3052-closed-scope-session-writes` | **Date**: 2026-11-25 | **Spec**: [spec.md](spec.md)

## Summary

Remove the step session's closed-scope refusal and commit a session write into a body variable row through the same
row judgement captures use, ordered ahead of the step's captures in one per-commit row overlay. Give graph draft
validation and compilation the workflow's step-write checks through a new public analyzer contract. Then let the Elsa 3
converter convert reusable script writes, and refuse writes to a reusable's inputs with a dedicated message.

## Technical Context

**Language/Version**: C# / .NET (repo default); TypeScript for Studio (no change expected, see Studio below)
**Primary Dependencies**: Jint (scripts), existing runtime stores and publishing pipeline
**Testing**: xunit only (no FluentAssertions)
**Project Type**: modular framework (Design/Runtime split)
**Constraints**: Runtime never references Design; no `InternalsVisibleTo` across domains (architecture guard); per
ADR 0092 D-9 no new commit rule

## Constitution Check

- Sanctioned patterns: a new public contract in `Elsa.Workflows.Design.Validations.Core` implemented in
  `Elsa.Workflows.Design.Validations` and resolved by the graph provider through its existing scope factory (the way it
  already resolves `IActivityDefinitionLookup`). No new pattern kind.
- Extension-point catalogs updated for the new contract (`EXTENSION_POINTS.md` of Validations.Core / Validations, and
  the Graph.Design consumer line).
- No ADR change (coordinator decision, 2026-11-25).

## Design

### Runtime (US1, US2; research R-1–R-5)

1. `StepVariableSession.Stage`: drop the closed-scope refusal. A closed-scope intent carries the boundary invocation id
   (`ClosedScopeBoundaryId`) so the commit routes it to the row, not to the workflow root frame.
2. `ClosedScopeRowCommit` (new, `Elsa.Activities.Runtime/Services`): one row judgement, extracted from
   `RuntimeOutputCaptureProjector.VersionRowAsync`. Input: overlay, boundary, key, value, base, mode, writer, row
   template. Reads the row from the overlay, else the store; judges with `VariableFrameState.Commit`; stamps version,
   digest and writer; puts the result in the overlay; returns the row change and record.
3. Session row writes: re-encode the envelope with the row's storage driver and keep its metadata and protection
   minimum. Source `SessionChange`.
4. Order: each handler creates one `ClosedScopeRowCommit` per step and passes it to `StepVariableCommit.TryApplyAsync`
   and to `ProjectAsync`. The session's row intents apply once into its overlay, before the capture projection: the
   invoke handler applies them itself just before the projection (`StepVariableCommit.ApplyRowIntentsAsync`, cached), the
   resume handler through `TryApplyAsync`, which runs before its projection; on suspension `TryApplyAsync` applies them.
   A `Fault` lost update or a failure on a session row write skips the capture projection and faults the step through
   `TryApplyAsync` (the step lost-update and failure branches), not `rowLostUpdateFault`.
5. Records: session row records join the invocation's variable writes and advance its bases
   (`InvocationVariableBases.Advance`), so its own capture does not conflict.

### Publish (US3)

1. `IStepVariableWriteAnalyzer` (new, `Validations.Core`): `AnalyzeAsync(root, rootVariables, ct)` (the catalog is
   injected into the implementation) returning
   `StepVariableWriteFacts`, reference errors, overlap warnings and refused literal script writes. The workflow
   `ValueFlowValidator` keeps calling the internal `StepVariableWriteValidation` (the implementation delegates to it).
2. `StepVariableWriteValidation` also reports refused literal script writes (`ScriptVariableReadPlan.RefusedLiteralWrites`)
   when asked (graph mode); workflows keep reporting them through `ScriptVariableReadValidator`, so nothing doubles.
3. `GraphActivityProvider.ValidateWriteOrder` (draft and compile): resolve the analyzer and catalog in a scope, pass
   `graph.Variables` as the root variables, map errors to graph diagnostics (`activity.graph.variable-reference`,
   `activity.graph.script-write`, both Error; `activity.graph.step-write-overlaps-capture`, Warning) with the existing
   `validationType`, and call the step-aware analyzer overload with the facts. Missing analyzer or catalog: today's
   capture-only behaviour.

### Converter (US4)

1. `Elsa3ValueFlowLowerer`: reusable scripts write variables; the rewriter's `scriptWritesVariables` parameter became
   `reusableWorkflow: accessors.IsReusable`, which now only keeps running-workflow state refused.
2. `Elsa3ExpressionRewriter`: delete `ReusableVariableWriteGuidance` and its branches. A write to a reusable workflow's
   input, `setVariable('<input>', v)` or its generated setter, gets a dedicated refusal (`VF-IMP-005`): it writes the
   reusable workflow's input `<input>`; guidance: declare a variable, initialize it from the input, write that variable.
3. Tests: the reusable refusal theory turns into conversion assertions; the input-write cases pin the refusal.
4. FR-010a (added in review): a reusable workflow with a variable and an input of one name refuses a script whose source
   mentions the name, or that `ScriptMayWriteVariables` finds may write variables (anything but a generated setter)
   (`Elsa3ValueFlowLowerer.LowerScript`, `VF-IMP-002`); the rewriter's per-form checks give the exact message for a
   direct write.

### Docs

`docs/runtime-variable-lifecycle.md` (Closed scopes, Step writes, Not covered yet), `docs/reference/variable-write-order.md`
(step writers, Limits), Validations `EXTENSION_POINTS.md`, `extensions/Elsa3/src/Mapping/README.md` refusal row,
spec 1231 T035/T038 notes. Workflow-state lines (#3023) stay.

### Studio

Graph diagnostics already render by code and severity in `ActivityDefinitionDiagnosticsPanel`; new codes appear there
with their locations. Verify in the browser; no code change expected (FR-013).

## Project Structure

```text
essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/
  ClosedScopeRowCommit.cs (new), RuntimeOutputCaptureProjector.cs, StepVariables/StepVariableSession.cs,
  StepVariables/StepVariableCommit.cs, WorkflowInvokeActivitySchedulerWorkHandler.cs,
  WorkflowResumeBookmarkSchedulerWorkHandler.cs
essentials/src/Workflows/Design/Validations/Core/Contracts/IStepVariableWriteAnalyzer.cs (new)
essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/ (implementation, registration)
essentials/src/Activities/Graph/Design/Services/GraphActivityProvider.cs
extensions/Elsa3/src/Mapping/Services/Elsa3ValueFlowLowerer.cs, Elsa3ExpressionRewriter.cs
tests: Activities.Runtime (session + commit), Graph.Design (validation), Elsa3 Mapping, one end-to-end run test
```

## Complexity Tracking

None.
