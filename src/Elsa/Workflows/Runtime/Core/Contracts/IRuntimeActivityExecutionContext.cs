using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Runtime-owned activity execution context extensions used by activities that schedule child executable nodes.
/// </summary>
public interface IRuntimeActivityExecutionContext : IActivityExecutionContext
{
    string WorkflowExecutionId { get; }
    WorkflowExecutableIdentity PinnedExecutable { get; }
    RuntimeSchedulerWorkItem SchedulerWorkItem { get; }
    ExecutableNode ExecutableNode { get; }
    ActivityExecutionState ActivityExecutionState { get; }

    void ScheduleChildActivity(
        string executableNodeId,
        string? schedulingActivityExecutionId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        ActivitySchedulingProvenance? schedulingProvenance = null,
        LoopIterationScopeRequest? iterationFrame = null);

    IReadOnlyCollection<RuntimeChildActivityScheduleRequest> GetChildActivityScheduleRequests();

    /// <summary>
    /// Stages cancellation of one scheduled child's activity-execution subtree (spec 112). Only valid
    /// during a child-completion/child-fault evaluation with a <c>Defer</c> or <c>Complete</c>
    /// continuation; applied atomically in the same checkpoint commit as the continuation.
    /// </summary>
    void RequestChildSubtreeCancellation(
        string childActivityExecutionId,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null);

    IReadOnlyCollection<RuntimeChildSubtreeCancellationRequest> GetChildSubtreeCancellationRequests();

    /// <summary>
    /// Stages absorption of the child fault this evaluation is processing (spec 115). Only valid
    /// during a child-fault evaluation with a <c>Defer</c> or <c>Complete</c> continuation;
    /// <paramref name="incidentId"/> must name the evaluation's incident. Applied atomically in the
    /// same checkpoint commit as the continuation.
    /// </summary>
    void RequestChildFaultAbsorption(
        string incidentId,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null);

    IReadOnlyCollection<RuntimeChildFaultAbsorptionRequest> GetChildFaultAbsorptionRequests();
}

/// <summary>
/// Narrow activity-facing capability for staging one cross-execution workflow dispatch.
/// This is a specialized orchestration command, not a workflow-value or service-location channel.
/// </summary>
public interface IWorkflowDispatchStager
{
    void StageWorkflowDispatch(WorkflowDispatchCheckpointRequest request);
}

/// <summary>
/// Engine-facing side of the workflow-dispatch staging seam.
/// Requests are isolated by runtime invocation identity because transient activities can be activated in child scopes.
/// </summary>
public interface IWorkflowDispatchStagingAccessor
{
    void Reset(string workflowExecutionId, string activityExecutionId);
    WorkflowDispatchCheckpointRequest? TakeWorkflowDispatch(string workflowExecutionId, string activityExecutionId);
}
