# Research: spec 1245

## R-1 Elsa 3 (elsa-core tag 3.6.3)

Paths are relative to `src/modules/Elsa.Workflows.Runtime`.

**Execute Workflow** (`Activities/ExecuteWorkflow.cs`) is an `Activity<ExecuteWorkflowResult>`.

- Inputs: `WorkflowDefinitionId`, `CorrelationId`, `Input` (dictionary) and `WaitForCompletion`.
- The child is the published version. It runs in process through `IWorkflowInvoker.InvokeAsync`, as a new instance
  with `ParentWorkflowInstanceId` set. The correlation id is the explicit input only; it is not inherited.
- If `WaitForCompletion` is false, or the child finished inline, `Result` is set to an `ExecuteWorkflowResult` and the
  activity completes. The result holds `WorkflowInstanceId`, `Status`, `SubStatus` and `Output`, the child's output
  dictionary (`Models/ExecuteWorkflowResult.cs`, set at `ExecuteWorkflow.cs:115-121`).
- Otherwise the activity bookmarks on `ExecuteWorkflowStimulus`. `Handlers/ResumeExecuteWorkflowActivity.cs` resumes it
  on `WorkflowExecuted` with the child's `Output` dictionary as workflow input. The resume callback then sets `Result`
  to that bare dictionary (`ExecuteWorkflow.cs:126-130`), so `Result` has two shapes.
- A Faulted sub-status counts as `Finished` (`WorkflowRunner` raises `WorkflowExecuted`), so the parent continues on
  `Done` and nothing checks the sub-status.
- A cancelled child raises `WorkflowCancelled` (`Services/WorkflowCanceler.cs`). No resume handler listens to it, so an
  awaited parent stays suspended.
- Both activities add `ParentInstanceId` to the child's input (`ExecuteWorkflow.cs:103`, `DispatchWorkflow.cs:123`).
- The output dictionary the parent resumes with ignores case (`WorkflowExecutionContext` input), so a read with other
  casing also worked.

**Dispatch Workflow** (`Activities/DispatchWorkflow.cs`) is an `Activity<object>`.

- It has the same four inputs, plus `StartNewTrace` and `ChannelName`.
- The child starts through `IWorkflowDispatcher`.
- With `WaitForCompletion` true it always bookmarks on `DispatchWorkflowStimulus`. The resume handler, a copy of the
  Execute Workflow one, sets `Result` to the child's output dictionary.
- With `WaitForCompletion` false the activity completes at once and `Result` is never set.
- Faults and cancellations behave as for Execute Workflow.

**Workflow-definition activity** (`Elsa.Workflows.Management/Activities/WorkflowDefinitionActivity`) runs the child
graph inline in the parent's instance, with typed outputs. Execute Workflow is a separate instance. That is why the
coordinator chose an awaited dispatch over a reusable graph activity.

## R-2 Elsa 4 on the initiative branch (current core)

`essentials/src/Activities/DispatchWorkflow/Runtime`:

- Inputs: `WorkflowDefinitionId`, which is pinned to an exact child executable at publish; `Inputs`;
  `WaitForCompletion`, default false; `CancelChildOnParentCancellation`, default true; `CorrelationId`, which defaults
  to the parent's.
- Outcomes: `Dispatched`, `Completed`, `Faulted`, `Cancelled` and `DispatchFailed`.
- Outputs (`DispatchWorkflowActivityResult`): `ChildWorkflowExecutionId` and `Result`. `Result` is a
  `DispatchWorkflowResult` holding the child id, the status, `Outputs` as `{Name, Value, DeclaredType, IsRedacted}`
  entries (only a completed child carries them, spec 099 FR-010/FR-012) and diagnostic metadata.
- A waited dispatch resumes through `ResumeAsync` with a `WorkflowDispatchParentResumePayload`. Its output entries are
  the diagnostics view (below), so `Output` needs a separate read without the policy (plan D-2).
- A child's Set Output keeps the written value's protection policy on the durable row (`IsSensitive`,
  `RequiresEncryption`, `RedactionMode`, `RetentionPolicy`; `WorkflowIntrinsicExecutor`). `DurableValuePolicies.Read`
  reads it back.
- A Sequence parent runs its next step after any child outcome except Break (`Sequence`). So outcome mapping matters only
  in a Flowchart.

Child outputs in `Result.outputs` come from `WorkflowOutputSource`, which projects them through
`IRuntimePayloadCapturePolicy` (`RuntimeWorkflowOutputStateProjection.ProjectOne`). The default level for
`WorkflowOutput` is `DiagnosticSnapshot` (`DefaultRuntimePayloadCapturePolicy`, `RuntimeDiagnosticsSettings`).

- By default each value is a preview envelope (`DefaultDiagnosticSnapshotFactory`), with strings cut at 256 characters
  and objects at 32 properties, depth 6 or 16 KB.
- A name containing `token`, `password` or `secret` is redacted.

`Result.outputs` is therefore a diagnostics view, and `Output` must read the committed values (plan D-2). The existing
dispatch tests use stub output sources, so nothing showed this.

Output projection (`ActivityCompletionProjector`):

- A result path that is missing gives an absent projection. `RuntimeOutputCaptureProjector` skips an absent capture, so
  the variable is unchanged.
- A JSON `null` at the path gives an explicit null, and the capture writes null.
- So `Output` must be left out of the serialized result when it is not produced, not written as `null` (plan D-1).

Converter (`extensions/Elsa3/src/Mapping/ActivityMap/Elsa3BuiltInActivityMap.Entries.cs`):

- `DispatchMembers` maps `Result` as `Lost("Result")`. Any capture is refused with
  `ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE`, in `Elsa3ActivityNormalizer` (lost-output handling).
- `Elsa3NodeRewriteKind.ExecuteWorkflowAsDispatch` is declared on the Execute Workflow entry, but no rewrite implements
  it. The warning comes from the generic entry-warning path.
- The only rule for a captured output whose Elsa 4 counterpart means something else is `SemanticsChanged`
  (`ELS3-ACTIVITY-MEMBER-SEMANTICS-CHANGED`). Its message and guidance are hard-coded for HTTP `ParsedContent`.
- Execute Workflow takes `WaitForCompletion` as a `DefaultFlip`, so a bound expression or a literal false is carried
  over. Elsa 3's inline-run semantics are then lost: a false makes the Elsa 4 dispatch fire-and-forget.
- The outcome maps are `Done → Dispatched, Completed` (Dispatch Workflow) and `Done → Completed` (Execute Workflow). A
  faulted child therefore ends the branch, where Elsa 3 continued.
- The converter always emits `CancelChildOnParentCancellation` false (spec 236). The kernel refuses that value.

## R-3 main's execution kernel (#4229), not yet on the initiative branch

- `Native.DispatchWorkflow` (`essentials/src/Workflows/Runtime/Native/Model/Activities.cs`) completes with
  `DispatchResult(JsonNode? Output, string Status)`. `Output` is the child's `output` object of named workflow outputs.
- `NativeActivityShapes` maps the current activity onto the Native one:
  - `WorkflowDefinitionId` maps to `Workflow`, and `Inputs` maps to `Input`.
  - `CancelChildOnParentCancellation` must be true, and `CorrelationId` must be null.
  - A `Result` read is refused: "the old result reads differently. Remove this read until the shapes match."
- PR #5794 (commits `5908bfc275`, `0bba7bf6d4`, merged 2026-11-25) contains two changes:
  - PostgreSQL root dispatch delivers output to held HTTP callers. This is root message dispatch, not Dispatch Workflow.
  - K10 adds `KernelHostCapabilities`. The Workbench Core Preview passes `Children: false`, so with
    `Elsa:CorePreview:Publications` on, publishing a Dispatch Workflow fails as `definition_unsupported`: "Dispatch
    Workflow doesn't run on this host yet."
- Children run only on the in-memory reference foundation. PostgreSQL root dispatch refuses a root with children.
- The PR body names the slice "PostgreSQL child workflows (Dispatch)", which is not filed.
- Related open issues: #4859 (a native child's business error reaches its parent) and #4667 (a held child-finished
  notice delays a sibling's completion).

Conclusion: the kernel already returns the child's output as a named-output object. Spec 1245 gives the current activity
that shape. The kernel's own mapping, its children on PostgreSQL and its correlation id are #4229's work, listed in the
spec under "Raised with #4229".

## R-4 Options considered for the Execute Workflow result

The cause is that Elsa 3's `ExecuteWorkflowResult` wraps the output (`Result.Output.X`) with `WorkflowInstanceId`,
`Status` and `SubStatus`. On a deferred resume it is instead the bare output.

| Option | Result | Verdict |
|---|---|---|
| A. Refuse the capture with guidance | No silent wrong read. The JTest Execute Workflow root stays refused, and unblocking it needs an adjusted copy. | **Recommended** (refuse over workaround) |
| B. Capture `Output` instead | Every `Result.Output.X` read silently becomes `undefined` | Rejected |
| C. Rewrite reads of the captured variable when every read is provably `.Output.…` | A complex script analysis for one activity. The JTest root reads the whole object (`toJson(getSubworkflowOutput())`), so it would still be refused. | Rejected |
| D. An Elsa 4 output shaped like `ExecuteWorkflowResult` | Puts an Elsa 3 shape into the Elsa 4 core. The names and status values differ anyway (`Finished`/`SubStatus` against `Completed`). | Rejected |
