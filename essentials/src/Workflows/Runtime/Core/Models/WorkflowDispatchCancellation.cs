using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Query-independent request for a provider to resolve parent cancellation against child admission.</summary>
public sealed class WorkflowDispatchCancellationRequest
{
    [JsonConstructor]
    public WorkflowDispatchCancellationRequest(
        string dispatchId,
        string parentWorkflowExecutionId,
        string parentActivityExecutionId,
        string childWorkflowExecutionId,
        DateTimeOffset requestedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentWorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(childWorkflowExecutionId);
        if (requestedAt == default)
            throw new ArgumentOutOfRangeException(nameof(requestedAt), "A dispatch cancellation request requires a recorded timestamp.");

        var identity = new WorkflowDispatchIdentity(parentWorkflowExecutionId, parentActivityExecutionId);
        if (!StringComparer.Ordinal.Equals(dispatchId, identity.DispatchId))
            throw new ArgumentException("DispatchId must match the deterministic parent/activity identity.", nameof(dispatchId));
        if (!StringComparer.Ordinal.Equals(childWorkflowExecutionId, identity.ChildWorkflowExecutionId))
            throw new ArgumentException("ChildWorkflowExecutionId must match the deterministic parent/activity identity.", nameof(childWorkflowExecutionId));

        DispatchId = dispatchId;
        ParentWorkflowExecutionId = parentWorkflowExecutionId;
        ParentActivityExecutionId = parentActivityExecutionId;
        ChildWorkflowExecutionId = childWorkflowExecutionId;
        RequestedAt = requestedAt;
    }

    public string DispatchId { get; }
    public string ParentWorkflowExecutionId { get; }
    public string ParentActivityExecutionId { get; }
    public string ChildWorkflowExecutionId { get; }
    public DateTimeOffset RequestedAt { get; }

    public static bool Equivalent(
        WorkflowDispatchCancellationRequest left,
        WorkflowDispatchCancellationRequest right) =>
        StringComparer.Ordinal.Equals(left.DispatchId, right.DispatchId) &&
        StringComparer.Ordinal.Equals(left.ParentWorkflowExecutionId, right.ParentWorkflowExecutionId) &&
        StringComparer.Ordinal.Equals(left.ParentActivityExecutionId, right.ParentActivityExecutionId) &&
        StringComparer.Ordinal.Equals(left.ChildWorkflowExecutionId, right.ChildWorkflowExecutionId) &&
        left.RequestedAt == right.RequestedAt;
}

public enum WorkflowDispatchAdmissionDisposition
{
    Admitted,
    AlreadyAdmitted,
    CancelledBeforeAdmission,
    Terminal
}

public sealed record WorkflowDispatchAdmissionResult(
    WorkflowDispatchAdmissionDisposition Disposition,
    WorkflowDispatchRecord Record);

public enum WorkflowDispatchCancellationDisposition
{
    AppliedBeforeAdmission,
    CancellationRequestedAfterAdmission,
    TerminalUnchanged
}

public sealed record WorkflowDispatchCancellationResult(
    WorkflowDispatchCancellationDisposition Disposition,
    WorkflowDispatchRecord Record);
