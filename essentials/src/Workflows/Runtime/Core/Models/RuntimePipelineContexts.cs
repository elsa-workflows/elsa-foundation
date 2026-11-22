using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Context passed through the workflow runtime execution pipeline for one drained work item.
/// </summary>
/// <remarks>
/// The originating <see cref="WorkItem"/> is always present. The typed runtime state is optional:
/// no full state is loaded at the scheduler dispatch point, some dispatches (e.g. workflow start) precede state
/// creation, and populating <see cref="WorkflowExecution"/>/<see cref="Scheduler"/> belongs to the <c>LoadState</c> slot.
/// The built-in <c>LoadState</c> middleware is a pass-through placeholder today, so both stay null.
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
/// The originating <see cref="WorkItem"/> is always present. The typed runtime state is optional for the
/// same reasons as <see cref="WorkflowRuntimePipelineContext"/>; <see cref="ActivityExecution"/> is additionally not
/// derivable at the dispatch point without per-kind payload parsing. Populating it belongs to the <c>LoadState</c>
/// slot, whose built-in middleware is a pass-through placeholder today, so it stays null.
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
