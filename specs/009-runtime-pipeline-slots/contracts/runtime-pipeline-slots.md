# Runtime Pipeline Slots Contract

## Workflow Pipeline

Workflow middleware implements `IWorkflowRuntimeMiddleware` and receives `WorkflowRuntimePipelineContext`.

Stable workflow slots:

- `LoadState`
- `Scheduling`
- `Checkpoint`
- `PostCommit`

## Activity Pipeline

Activity middleware implements `IActivityRuntimeMiddleware` and receives `ActivityRuntimePipelineContext`.

Stable activity slots:

- `LoadState`
- `InputEvaluation`
- `Invoke`
- `OutputCapture`
- `Scheduling`
- `Checkpoint`
- `PostCommit`

## Ordering

Ordering is slot sort order, then registration order within the slot, then registration index. Before/after dependency constraints are intentionally deferred.

## Introspection

`WorkflowRuntimePipelineBuilder.BuildPlan()` and `ActivityRuntimePipelineBuilder.BuildPlan()` return `RuntimePipelinePlan`.
