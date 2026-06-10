namespace Elsa.Workflows.Runtime.Core.Models;

public sealed record WorkflowRuntimePipelineContext(
    WorkflowExecutionState WorkflowExecution,
    SchedulerState? Scheduler);

public sealed record ActivityRuntimePipelineContext(
    WorkflowExecutionState WorkflowExecution,
    ActivityExecutionState ActivityExecution,
    SchedulerState? Scheduler);
