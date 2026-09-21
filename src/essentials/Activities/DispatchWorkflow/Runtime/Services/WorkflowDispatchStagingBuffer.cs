using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>
/// Invocation-keyed hand-off between transient DispatchWorkflow activations and runtime checkpoint owners.
/// </summary>
public sealed class WorkflowDispatchStagingBuffer :
    IWorkflowDispatchStager,
    IWorkflowDispatchStagingAccessor
{
    private readonly ConcurrentDictionary<(string WorkflowExecutionId, string ActivityExecutionId), WorkflowDispatchCheckpointRequest> _requests = new();

    public void StageWorkflowDispatch(WorkflowDispatchCheckpointRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = (request.Record.ParentWorkflowExecutionId, request.Record.ParentActivityExecutionId);
        if (!_requests.TryAdd(key, request))
            throw new InvalidOperationException("An activity attempt can stage only one workflow dispatch.");
    }

    public void Reset(string workflowExecutionId, string activityExecutionId)
    {
        ValidateIdentity(workflowExecutionId, activityExecutionId);
        _requests.TryRemove((workflowExecutionId, activityExecutionId), out _);
    }

    public WorkflowDispatchCheckpointRequest? TakeWorkflowDispatch(string workflowExecutionId, string activityExecutionId)
    {
        ValidateIdentity(workflowExecutionId, activityExecutionId);
        _requests.TryRemove((workflowExecutionId, activityExecutionId), out var request);
        return request;
    }

    private static void ValidateIdentity(string workflowExecutionId, string activityExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
    }
}
