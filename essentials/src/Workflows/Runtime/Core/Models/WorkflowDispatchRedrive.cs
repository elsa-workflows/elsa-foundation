using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Provider-neutral request to atomically redrive one eligible detached dispatch.</summary>
public sealed record WorkflowDispatchRedriveRequest
{
    public const int MaximumRequestIdLength = 128;

    public WorkflowDispatchRedriveRequest(string dispatchId, string requestId, DateTimeOffset requestedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        if (requestId.Length > MaximumRequestIdLength || requestId.Any(char.IsControl))
            throw new ArgumentException($"A workflow dispatch redrive request ID must be at most {MaximumRequestIdLength} non-control characters.", nameof(requestId));
        if (requestedAt == default)
            throw new ArgumentOutOfRangeException(nameof(requestedAt), "A workflow dispatch redrive requires a recorded request time.");

        DispatchId = dispatchId;
        RequestId = requestId;
        RequestedAt = requestedAt;
    }

    public string DispatchId { get; }
    public string RequestId { get; }
    public DateTimeOffset RequestedAt { get; }
}

/// <summary>Safe result of an atomic workflow-dispatch redrive decision.</summary>
public sealed record WorkflowDispatchRedriveResult
{
    public WorkflowDispatchRedriveResult(
        string dispatchId,
        WorkflowDispatchRedriveDisposition disposition,
        WorkflowDispatchStatus? status,
        int generation,
        string? incidentId = null,
        string? deadLetterId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        if (!Enum.IsDefined(disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        if (status is not null && !Enum.IsDefined(status.Value))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (generation < 0)
            throw new ArgumentOutOfRangeException(nameof(generation), "A workflow dispatch delivery generation cannot be negative.");
        ValidateOptional(incidentId, nameof(incidentId));
        ValidateOptional(deadLetterId, nameof(deadLetterId));
        if (disposition == WorkflowDispatchRedriveDisposition.NotFound &&
            (status is not null || generation != 0 || incidentId is not null || deadLetterId is not null))
        {
            throw new ArgumentException("A not-found redrive result cannot expose dispatch lifecycle or delivery evidence.", nameof(disposition));
        }
        if (disposition != WorkflowDispatchRedriveDisposition.NotFound && status is null)
            throw new ArgumentException("A redrive result for a visible dispatch requires its current lifecycle status.", nameof(status));

        DispatchId = dispatchId;
        Disposition = disposition;
        Status = status;
        Generation = generation;
        IncidentId = incidentId;
        DeadLetterId = deadLetterId;
    }

    public string DispatchId { get; }
    public WorkflowDispatchRedriveDisposition Disposition { get; }
    public WorkflowDispatchStatus? Status { get; }
    public int Generation { get; }
    public string? IncidentId { get; }
    public string? DeadLetterId { get; }

    private static void ValidateOptional(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Optional workflow dispatch redrive identifiers cannot be blank.", parameterName);
    }
}

public enum WorkflowDispatchRedriveDisposition
{
    Accepted = 0,
    AlreadyApplied = 1,
    ActiveConflict = 2,
    NotFound = 3,
    NotEligible = 4
}

/// <summary>Provider-neutral decision and optional atomic state replacement for one redrive request.</summary>
public sealed record WorkflowDispatchRedriveTransition(
    WorkflowDispatchRedriveResult Result,
    WorkflowDispatchRecord? WorkflowDispatch = null,
    RuntimePostCommitOutboxItem? OutboxItem = null)
{
    public bool HasMutation => WorkflowDispatch is not null && OutboxItem is not null;
}

/// <summary>Shared validation and state construction used by every atomic redrive provider.</summary>
public static class WorkflowDispatchRedriveTransitions
{
    public static WorkflowDispatchRedriveTransition Evaluate(
        WorkflowDispatchRedriveRequest request,
        WorkflowDispatchRecord? dispatch,
        RuntimePostCommitOutboxItem? deadLetter)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (dispatch is null)
            return Result(request.DispatchId, WorkflowDispatchRedriveDisposition.NotFound, null);

        var currentRequestId = WorkflowDispatchLifecycle.ReadDeliveryRedriveRequestId(dispatch);
        if (StringComparer.Ordinal.Equals(currentRequestId, request.RequestId))
            return Result(dispatch, WorkflowDispatchRedriveDisposition.AlreadyApplied);
        if (currentRequestId is not null && dispatch.Status is WorkflowDispatchStatus.Pending or WorkflowDispatchStatus.Started)
            return Result(dispatch, WorkflowDispatchRedriveDisposition.ActiveConflict);
        if (!WorkflowDispatchLifecycle.IsRedriveEligible(dispatch) || !MatchesDeadLetter(dispatch, deadLetter))
            return Result(dispatch, WorkflowDispatchRedriveDisposition.NotEligible);

        var redrivenDispatch = WorkflowDispatchLifecycle.RedriveDelivery(dispatch, request.RequestId, request.RequestedAt);
        var redrivenOutbox = RuntimePostCommitOutboxClaimTransitions.RedriveFailedFinal(deadLetter!, request.RequestedAt);
        return new WorkflowDispatchRedriveTransition(
            new WorkflowDispatchRedriveResult(
                dispatch.DispatchId,
                WorkflowDispatchRedriveDisposition.Accepted,
                redrivenDispatch.Status,
                WorkflowDispatchLifecycle.ReadDeliveryGeneration(redrivenDispatch),
                WorkflowDispatchLifecycle.ReadDeliveryIncidentId(dispatch),
                WorkflowDispatchLifecycle.ReadDeliveryDeadLetterId(dispatch)),
            redrivenDispatch,
            redrivenOutbox);
    }

    private static bool MatchesDeadLetter(
        WorkflowDispatchRecord dispatch,
        RuntimePostCommitOutboxItem? deadLetter)
    {
        if (deadLetter is null || deadLetter.Status != RuntimePostCommitOutboxStatus.FailedFinal)
            return false;
        var identity = new WorkflowDispatchIdentity(
            dispatch.ParentWorkflowExecutionId,
            dispatch.ParentActivityExecutionId);
        return StringComparer.Ordinal.Equals(deadLetter.OutboxItemId, WorkflowDispatchLifecycle.ReadDeliveryDeadLetterId(dispatch)) &&
               StringComparer.Ordinal.Equals(deadLetter.Intent.Kind, WorkflowDispatchLifecycle.StartChildIntentKind) &&
               StringComparer.Ordinal.Equals(deadLetter.Intent.IntentId, identity.StartIntentId) &&
               StringComparer.Ordinal.Equals(deadLetter.Intent.IdempotencyKey, identity.StartIdempotencyKey) &&
               StringComparer.Ordinal.Equals(deadLetter.Intent.WorkflowExecutionId, dispatch.ParentWorkflowExecutionId) &&
               StringComparer.Ordinal.Equals(deadLetter.Intent.ActivityExecutionId, dispatch.ParentActivityExecutionId) &&
               deadLetter.Intent.Metadata.TryGetValue(RuntimeMetadataKeys.DispatchId, out var dispatchId) &&
               StringComparer.Ordinal.Equals(dispatchId, dispatch.DispatchId) &&
               deadLetter.RetryPolicy.MaxAttempts > 0 &&
               !deadLetter.RetryPolicy.RetryUntilAcknowledged;
    }

    private static WorkflowDispatchRedriveTransition Result(
        WorkflowDispatchRecord dispatch,
        WorkflowDispatchRedriveDisposition disposition) =>
        new(new WorkflowDispatchRedriveResult(
            dispatch.DispatchId,
            disposition,
            dispatch.Status,
            WorkflowDispatchLifecycle.ReadDeliveryGeneration(dispatch),
            WorkflowDispatchLifecycle.ReadDeliveryIncidentId(dispatch),
            WorkflowDispatchLifecycle.ReadDeliveryDeadLetterId(dispatch)));

    private static WorkflowDispatchRedriveTransition Result(
        string dispatchId,
        WorkflowDispatchRedriveDisposition disposition,
        WorkflowDispatchStatus? status) =>
        new(new WorkflowDispatchRedriveResult(dispatchId, disposition, status, generation: 0));
}
