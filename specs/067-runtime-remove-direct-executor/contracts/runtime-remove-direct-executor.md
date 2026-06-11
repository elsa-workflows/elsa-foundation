# Contract: Runtime Remove Direct Executor

## Removed Contracts

- `Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutor`
- `Elsa.Workflows.Runtime.Core.Services.SequentialWorkflowExecutor`
- `WorkflowExecutionResult`
- `ActivityExecutionResult`
- `WorkflowExecutionResultStatus`
- `ActivityExecutionResultStatus`

These types are intentionally removed because they encode direct inline execution of a `WorkflowExecutable` and return an immediate summary. That path bypasses the runtime-owned workflow execution agent, scheduler work queue, checkpoint writer, split continuation state stores, bookmark creation, incidents, and post-commit intents.

## Replacement Boundary

Workflow execution entry is represented by:

- `IWorkflowExecutionStartDispatcher`
- `IWorkflowExecutionAgentProvider`
- `WorkflowExecutionCommandEnvelope`
- `IWorkflowExecutionCommandProcessor`
- `IWorkflowSchedulerWorkQueue`
- `IWorkflowSchedulerDrainer`

The API execute request returns a start-dispatch view that reports command acceptance and pinned artifact identity. Runtime progress and results are projected through state/diagnostics surfaces, not direct executor return DTOs.
