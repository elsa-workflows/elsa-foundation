# Implementation Plan: Elsa 3 Execute Workflow converts to an awaited Dispatch Workflow with the child's output

**Branch**: `agent/claude/3052-spec1245-execute-workflow` | **Date**: 2026-11-26 | **Spec**: [spec.md](spec.md)

## Summary

Add an `Output` output to Elsa 4 Dispatch Workflow. It is the completed child's safe named outputs as one JSON object,
the shape main's kernel already returns. Then change three things in the Elsa 3 activity map:

- An awaited Dispatch `Result` capture becomes an `Output` capture.
- Execute Workflow always waits. Its `Result` capture is refused with guidance.
- `Done` routes to `Completed`, `Faulted` and `Cancelled`, the way Elsa 3 continued after any child end.

## Technical Context

**Language/Version**: C# / .NET (the repository default)
**Primary Dependencies**: the existing Dispatch Workflow runtime (specs 096–103) and the Elsa 3 converter (spec 236
activity map)
**Testing**: xunit only (no FluentAssertions)
**Project Type**: modular framework (Design/Runtime split)
**Constraints**: Runtime never references Design. Values are JSON and pass through unchanged (ADR 0091). Pre-stable: no
compatibility shims.

## Constitution Check

- **Patterns.** No new pattern kind: one more output projection on an existing atomic result record, and data changes
  to existing converter rules.
- **Extension points.** No new contract. The Dispatch Workflow `EXTENSION_POINTS.md` is unchanged; check whether it
  lists outputs and update it if it does.
- **ADRs.** No ADR change (see the spec, "Rule and boundary").

## Design

### Runtime (US1; FR-001 to FR-005)

1. `DispatchWorkflowActivityResult` gets a third property, `Output`: `JsonElement?`, with
   `[Output(Key = "Output", Path = "output", IsRequired = false)]`.
2. **D-1, absent rather than null.** When no output is produced, the property is left out of the serialized result
   (`JsonIgnore(WhenWritingNull)`). A missing path is an absent projection, so a capture leaves its variable unchanged.
   A serialized `null` would write null into the variable (research R-2). The step that checks this is T002.
3. `ResumeAsync` builds `Output` only for `WorkflowDispatchStatus.Completed`. It takes the safe entries of
   `payload.Result.Outputs`, leaves out the redacted ones, and writes name → value in entry order. Names are already
   unique (`DispatchWorkflowResult`). A completed child with no outputs gives `{}`. Every other path, including
   `Dispatched`, passes `null`.
4. **Designer.** Outputs come from the activity contract descriptor, so `Output` appears with the existing ones. Check
   the Design descriptor and Studio capture list, and change nothing else if it shows.

### Converter (US2 to US4; FR-006 to FR-011)

In `Elsa3BuiltInActivityMap.Entries.cs`:

1. **Dispatch Workflow.** `DispatchMembers(executeWorkflow: false)` replaces `Lost("Result")` with
   `CarryOutput("Result", "Output")`, the same rule `Event` uses for `Result → Payload`.
2. **Execute Workflow.**
   - `Result` becomes `SemanticsChanged("Result")`, refused with `ELS3-ACTIVITY-MEMBER-SEMANTICS-CHANGED`. That rule's
     message and guidance are hard-coded for HTTP `ParsedContent` today. Make them rule data: an optional message and
     guidance on `Elsa3MemberRule`, defaulting to the current HTTP text so `ParsedContent` keeps its message. Then give
     the Execute Workflow rule the FR-010 text.
   - `WaitForCompletion` becomes `Structure("WaitForCompletion")`. Implement the existing `ExecuteWorkflowAsDispatch`
     rewrite kind, which is declared but has no `IElsa3NodeRewrite` today, beside the other rewrites in
     `ActivityMap/Rewrites/`. It does two things:
     - It always emits `WaitForCompletion` literal `true`.
     - It raises `ELS3-BEHAVIOUR-EXECUTE-WORKFLOW-DURABLE` itself, adding the sentence "Elsa 4 waits for a child that
       suspends, where Elsa 3 continued" when the Elsa 3 value is literal false or bound (FR-008).

     The entry-level warning moves into the rewrite, so the warning is not raised twice.

3. **Outcomes.**
   - `Catalog("DispatchWorkflow", …, [Outcome("Done", "Dispatched", "Completed", "Faulted", "Cancelled")])`.
   - `Catalog("ExecuteWorkflow", …, [Outcome("Done", "Completed", "Faulted", "Cancelled")])`.
4. **Records.**
   - `extensions/Elsa3/src/Mapping/README.md` gets the rows for FR-006 to FR-011, including the accepted difference
     for a faulted child's outputs.
   - `Elsa3ActivityMapCoverageTests` and the `elsa3-activity-surface.json` fixture are updated to the new rules.

### Tests (FR-012)

- **Runtime**: `essentials/tests/Activities/DispatchWorkflow/Tests`.
  - Contract test: `Output` is declared, optional, and appears in the descriptor.
  - End-to-end tests: a completed child gives the object; a redacted output is left out; faulted, cancelled and
    dispatch-failed children give no `Output`, and a capture keeps its previous value; fire-and-forget gives no
    `Output`.
  - Checkpoint test: a waiting parent resumes after a restart with the same `Output`.
- **Converter**: `extensions/Elsa3/tests/Mapping/Tests`.
  - Dispatch Workflow `Result` capture with literal true, literal false and a bound wait.
  - Execute Workflow capture refused, with message and guidance asserted.
  - Execute Workflow wait forced to literal true, with the warning text for false and for bound.
  - `Done` routing on both entries.
  - The tests that pinned the old rules are updated (spec SC-004).
- **Import integration**: `DispatchWorkflowCallTests` with the `dispatch-caller.json` fixture gets a captured awaited
  result and runs it end to end on the import host.

## Phases

1. Runtime output (T001–T005)
2. Converter rules (T006–T011)
3. Integration and records (T012–T014)

## Risks

- **Absent versus null (D-1).** If the serializer writes `"output": null`, a capture overwrites its variable with null
  where Elsa 3 left it unchanged. T002 asserts that the variable is unchanged on `Dispatched` and `Faulted`.
- **Merge with main's kernel.** The main sync PR brings `NativeActivityShapes`. Its `Result` refusal stays correct. An
  `Output` read is still unmapped there until #4229 adds it (spec, "Raised with #4229" item 1). The coordinator raises
  it.
