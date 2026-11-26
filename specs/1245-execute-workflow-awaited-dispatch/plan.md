# Implementation Plan: Elsa 3 Execute Workflow converts to an awaited Dispatch Workflow with the child's output

**Branch**: `agent/claude/3052-spec1245-execute-workflow` | **Date**: 2026-11-26 | **Spec**: [spec.md](spec.md)

## Summary

Add an `Output` output to Elsa 4 Dispatch Workflow. It is the completed child's committed named output values as one
JSON object, the shape main's kernel already returns. It replaces the `Result` output, which is removed: its terminal
status and safe diagnostics stay in the stored result as a member that is not an output, and its diagnostics-view output
entries go. Then change three things in the Elsa 3 activity map:

- An awaited Dispatch `Result` capture becomes an `Output` capture.
- Execute Workflow always waits. Its `Result` capture is refused with guidance.
- `Done` routes to `Completed` and `Faulted`, the way Elsa 3 continued after a child that finished or faulted.

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
- **Extension points.** One public addition: `ActivityResumeContext<TState, TTrigger>.Warnings`
  (`IExecutionWarningSink?`) in `Activities.Runtime.Core`, copied from the attempt's `Warnings`, which the resume handler
  already sets. Update that project's `EXTENSION_POINTS.md` if it lists context members. Check whether the Dispatch
  Workflow `EXTENSION_POINTS.md` lists outputs and update it if it does.
- **ADRs.** No ADR change (see the spec, "Rule and boundary").

## Design

### Runtime (US1; FR-001 to FR-005)

1. `DispatchWorkflowActivityResult` keeps `ChildWorkflowExecutionId` and gets `Output`: `JsonElement?`, with
   `[Output(Key = "Output", Path = "output", IsRequired = false)]`.
   - Its `Result` property loses its `[Output]` attribute and becomes `Terminal`, a stored member that is not an
     output. Projections come only from `[Output]` properties (`ExecutableNodeCompiler`,
     `RegisterActivityTypesStartupTask`), so it stays in the stored result for inspection, and nothing can capture it.
   - `IRuntimeChildControlCompletion.ChildControl` reads `Terminal?.Control`.
   - `DispatchWorkflowResult` loses `Outputs`. It keeps the child id, the status, the allow-listed diagnostics and the
     control transport. `DispatchWorkflowOutput` is deleted.
   - Specs 099 and 100 are amended in place where they say the activity sets `Result` (FR-004).
2. **D-1, absent rather than null.** When no output is produced, the property is left out of the serialized result
   (`JsonIgnore(WhenWritingNull)`). A missing path is an absent projection, so a capture leaves its variable unchanged.
   A serialized `null` would write null into the variable (research R-2). The step that checks this is T002.
3. **D-2, values, not the diagnostics view.**
   - `WorkflowDispatchCompletionEnricher.CreateIntent` builds today's `Result.outputs` from `IWorkflowOutputSource`,
     which runs the diagnostics capture policy (research R-2). That read is replaced: `Output` needs the child's
     committed values, and the diagnostics-view entries are removed.
   - Add a policy-free read of the child's workflow outputs: the same latest-per-name selection over the child's durable
     values as `RuntimeWorkflowOutputStateProjection.Project`, with the same pending checkpoint changes. It never
     consults `IRuntimePayloadCapturePolicy`. It leaves out two kinds of value:
     - a value whose `DurableValuePolicies.Read(row)` is protected: `IsSensitive`, `RequiresEncryption`, a
       `RedactionMode` or a `RetentionPolicy`, the same test as `StepWorkflowState.Protected` plus retention;
     - a value not stored inline.
   - Raw values now reach three surfaces: the outbox intent payload, the bookmark-resume stimulus input
     (`ParentResumeExecutor`) and the parent's activity result envelope. The protected-value exclusion keeps protected
     values off them. Removing the diagnostics-view entries also removes today's leak of redaction-mode and retention
     previews onto those surfaces (#5851).
   - "Not stored inline" means `!row.InlineValue.HasValue || DurableValuePolicies.Read(row).Storage != Inline`. That
     covers a null row whose minimum storage is External, which the existing projection reports as inline.
   - The payload also carries the names left out (`WithheldOutputs`, names only, sorted ordinally).
     - `ResumeAsync` reports one plain `ExecutionWarning` (`dispatch.child-output-withheld`, Warning, `Details`:
       `outputs`) through the new `ActivityResumeContext.Warnings`.
     - It is not a `ProtectedForm`: that form names roots only and would drop the output names. The names are contract
       names, not values.
     - Dispatch knowledge stays in the activity, not in the generic resume handler.
   - Both new payload members are constructor parameters, with one convention: null unless `Completed`; `WithheldOutputs`
     is an empty list when nothing is left out.
     - Payload `Output` stays `JsonElement?`. A dictionary type would fail `PersistableActivityValue.ValidateType`.
     - Ordinal ordering of both makes the canonical-JSON comparisons stable at all four points: the enricher's
       `ValidateIntent`, `ParentResumeExecutor`, `DispatchWorkflowResumeGuard`, and trigger materialization.
   - Update the "payload-safe" doc comment on `WorkflowDispatchParentResumePayload` to say what `Output` carries. Fix
     the two stale comments in `RuntimeWorkflowOutputStateProjection` ("nothing writes it yet" and "nothing writes
     externalized workflow outputs yet"): Set Output writes both.
   - The omission is declared (ADR 0046 V6, in the contract description) and reported in each run (ADR 0045 §9, the
     warning). Carrying a per-value policy through a capture would be new ADR 0045 §9 work, and it is not built
     here.
   - Expose that read as a separate method or contract beside the existing source, so the diagnostics read and the data
     read cannot be confused. Use the existing contracts in `Workflows.Runtime.Core`; no new pattern.
4. The enricher puts that object on `WorkflowDispatchParentResumePayload` as a new `Output` (`JsonElement?`). It is set
   only for `Completed`, ordered by name, and is `{}` when the child has no outputs.
   - The intent comparison already compares the whole payload as canonical JSON (`RuntimeCheckpointCommit`), so
     `Output` is covered: a re-enriched checkpoint that differs fails closed (spec 099 FR-005, FR-008).
   - The payload constructor gets an invariant: `Output` and `WithheldOutputs` may be set only when the terminal
     status is `Completed`.
   - The other call site, `WorkflowDispatchDeliveryFailureProjector`, passes null. The deserializer,
     `DispatchWorkflowResumeGuard`, reads the new member.
   - The payload's terminal result (`DispatchWorkflowResult`) keeps its status, diagnostics and control, and no
     longer has output entries (FR-004).
5. `ResumeAsync` sets `Terminal` from the payload's terminal result and copies `payload.Output` into
   `DispatchWorkflowActivityResult.Output` for `Completed`. Fire-and-forget sets `Terminal` to null. Every other path,
   including `Dispatched`, passes `null`, which D-1 leaves out of the serialized result.
6. **Designer and consumers.** Outputs come from the activity contract descriptor, so `Output` appears and `Result`
   disappears. Find every consumer that names the Dispatch `Result` output and update it: the BPMN interchange tests,
   the Studio client tests, `e2e-tests/composition/Test-DispatchWorkflowOutcomes.ps1`, and the dispatch tests (FR-005).

### Converter (US2 to US4; FR-006 to FR-011)

In `Elsa3BuiltInActivityMap.Entries.cs`:

1. **Dispatch Workflow.** `DispatchMembers(executeWorkflow: false)` replaces `Lost("Result")` with
   `CarryOutput("Result", "Output")`, the same rule `Event` uses for `Result → Payload`.
2. **Execute Workflow.**
   - `Result` stays `Lost("Result")`, refused with `ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE`. Its message ("has no such
     output") is now true. Give `Lost` optional per-rule message and guidance on `Elsa3MemberRule`, defaulting to
     today's text, and give the Execute Workflow rule the FR-010 text.
   - `WaitForCompletion` becomes `Structure("WaitForCompletion", Elsa3MemberKind.Input)`. Implement the existing
     `ExecuteWorkflowAsDispatch` rewrite kind, which is declared but has no `IElsa3NodeRewrite` today, beside the other
     rewrites in `ActivityMap/Rewrites/`. Register it in `Elsa3MappingFeature` like the others. The rewrite must emit
     the value with `SetProperty`, because `RemoveConsumedStructure` otherwise deletes a structure member of the same
     name. It does two things:
     - It always emits `WaitForCompletion` literal `true`.
     - It raises `ELS3-BEHAVIOUR-EXECUTE-WORKFLOW-DURABLE` itself, adding the sentence "Elsa 4 waits for a child that
       suspends, where Elsa 3 continued" unless the Elsa 3 value is literal true. Elsa 3 ran an unset value as false
       (FR-008).

     The entry-level warning moves into the rewrite, so the warning is not raised twice.

3. **Outcomes.**
   - `Catalog("DispatchWorkflow", …, [Outcome("Done", "Dispatched", "Completed", "Faulted")])`.
   - `Catalog("ExecuteWorkflow", …, [Outcome("Done", "Completed", "Faulted")])`.
   - `Cancelled` stays unmapped. An Elsa 3 cancelled child never resumed its parent (FR-011).
4. **Records.**
   - `extensions/Elsa3/src/Mapping/README.md` gets the rows for FR-006 to FR-011, including the five accepted
     differences.
   - Spec 236's `member-map.md` rows for `DispatchWorkflow.Result`, the `Done` outcome and `ExecuteWorkflow`, and the
     null-member row for `ExecuteWorkflow.WaitForCompletion`, are amended in place, citing spec 1245 (FR-011a).
   - `Elsa3ActivityMapCoverageTests` and the `elsa3-activity-surface.json` fixture are updated to the new rules.

### Tests (FR-012)

- **Runtime**: `essentials/tests/Activities/DispatchWorkflow/Tests`.
  - Contract test: `Output` is declared, optional, and appears in the descriptor.
  - End-to-end tests run on the real `WorkflowOutputSource` with default diagnostics settings.
    - A completed child gives the object, including a 1,000-character string and an output named `token`, both
      unchanged.
    - An output with each protection kind (sensitive, encryption, redaction mode, retention) is left out, one test per
      kind.
    - Faulted, cancelled and dispatch-failed children give no `Output`, and a capture keeps its previous value.
    - Fire-and-forget gives no `Output`.
  - Enricher test: the resume payload's `Output` is replay-stable, and a conflicting re-enrichment fails closed.
  - Checkpoint test: a waiting parent resumes after a restart with the same `Output`.
- **Converter**: `extensions/Elsa3/tests/Mapping/Tests`.
  - Dispatch Workflow `Result` capture with literal true, literal false and a bound wait.
  - Execute Workflow capture refused, with message and guidance asserted.
  - Execute Workflow wait forced to literal true, with the warning text for false and for bound.
  - `Done` routing on both entries; no `Cancelled` routing.
  - The tests that pinned the old rules are updated (spec SC-004).
- **Import integration**: `DispatchWorkflowCallTests` with the `dispatch-caller.json` fixture gets a captured awaited
  result and runs it end to end on the import host.

## Phases

1. Runtime output (T001–T005)
2. Converter rules (T006–T011)
3. Integration and records (T012–T014)

## Risks

- **Diagnostics leaking into data (D-2).** If `Output` is ever built from `IWorkflowOutputSource`, values are truncated
  or redacted by a diagnostics setting. The `token` and 1,000-character tests guard against this.
- **Absent versus null (D-1).** If the serializer writes `"output": null`, a capture overwrites its variable with null
  where Elsa 3 left it unchanged. T002 asserts that the variable is unchanged on `Dispatched` and `Faulted`.
- **Merge with main's kernel.** The main sync PR brings `NativeActivityShapes`. Its `Result` rule names an output that
  no longer exists, so it is harmless, and #4229 can drop it. An `Output` read is still unmapped there until #4229
  adds it (spec, "Raised with #4229" items 1 to 4). The coordinator
  raises them.
