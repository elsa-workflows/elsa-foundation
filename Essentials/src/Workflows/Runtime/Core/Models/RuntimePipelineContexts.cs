using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Context passed through the workflow runtime execution pipeline for one drained work item.
/// </summary>
/// <remarks>
/// The originating <see cref="WorkItem"/> is always present. The typed runtime state is optional in Move 1:
/// no full state is loaded at the scheduler dispatch point, some dispatches (e.g. workflow start) precede state
/// creation, and populating <see cref="WorkflowExecution"/>/<see cref="Scheduler"/> is the <c>LoadState</c> slot's
/// responsibility in Move 2. See ADR 0029 and specs/082-runtime-pipeline-execution-spine.
/// </remarks>
public sealed record WorkflowRuntimePipelineContext(
    RuntimeSchedulerWorkItem WorkItem,
    WorkflowExecutionState? WorkflowExecution = null,
    SchedulerState? Scheduler = null) : IRuntimePipelineContext
{
    /// <summary>The workflow execution this dispatch belongs to.</summary>
    public string WorkflowExecutionId => WorkItem.WorkflowExecutionId;

    /// <summary>Mutable per-dispatch workspace shared between the handler (run in the <c>Invoke</c> slot) and the slot middleware.</summary>
    public RuntimePipelineWorkspace Workspace { get; init; } = new();
}

/// <summary>
/// Context passed through the activity runtime execution pipeline for one drained work item.
/// </summary>
/// <remarks>
/// The originating <see cref="WorkItem"/> is always present. The typed runtime state is optional in Move 1 for the
/// same reasons as <see cref="WorkflowRuntimePipelineContext"/>; <see cref="ActivityExecution"/> is additionally not
/// derivable at the dispatch point without per-kind payload parsing, so it is left null until the <c>LoadState</c>
/// slot populates it in Move 2.
/// </remarks>
public sealed record ActivityRuntimePipelineContext(
    RuntimeSchedulerWorkItem WorkItem,
    WorkflowExecutionState? WorkflowExecution = null,
    ActivityExecutionState? ActivityExecution = null,
    SchedulerState? Scheduler = null) : IRuntimePipelineContext
{
    /// <summary>The workflow execution this dispatch belongs to.</summary>
    public string WorkflowExecutionId => WorkItem.WorkflowExecutionId;

    /// <summary>Mutable per-dispatch workspace shared between the handler (run in the <c>Invoke</c> slot) and the slot middleware.</summary>
    public RuntimePipelineWorkspace Workspace { get; init; } = new();
}
