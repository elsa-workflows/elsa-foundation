namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Durable delivery state for a post-commit intent. Intent payloads are delivered only after checkpoint commit.
/// </summary>
public sealed class RuntimePostCommitOutboxItem
{
    public RuntimePostCommitOutboxItem(
        string outboxItemId,
        RuntimePostCommitIntent intent,
        RuntimePostCommitOutboxStatus status,
        DateTimeOffset recordedAt,
        DateTimeOffset? availableAt,
        RuntimePostCommitRetryPolicy? retryPolicy = null,
        int deliveryAttemptCount = 0,
        string? deliveringOwnerId = null,
        DateTimeOffset? deliveryStartedAt = null,
        DateTimeOffset? deliveredAt = null,
        string? lastFailureMessage = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        long deliveryFencingToken = 0,
        DateTimeOffset? deliveryVisibleAfter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxItemId);
        ArgumentNullException.ThrowIfNull(intent);

        if (deliveryAttemptCount < 0)
            throw new ArgumentOutOfRangeException(nameof(deliveryAttemptCount), "Delivery attempt count cannot be negative.");
        if (deliveryFencingToken < 0)
            throw new ArgumentOutOfRangeException(nameof(deliveryFencingToken), "Delivery fencing token cannot be negative.");

        Validate(status, deliveringOwnerId, deliveryStartedAt, deliveredAt, deliveryFencingToken, deliveryVisibleAfter);

        OutboxItemId = outboxItemId;
        Intent = intent;
        Status = status;
        RecordedAt = recordedAt;
        AvailableAt = availableAt;
        RetryPolicy = retryPolicy ?? RuntimePostCommitRetryPolicy.None;
        DeliveryAttemptCount = deliveryAttemptCount;
        DeliveringOwnerId = deliveringOwnerId;
        DeliveryStartedAt = deliveryStartedAt;
        DeliveredAt = deliveredAt;
        LastFailureMessage = lastFailureMessage;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
        DeliveryFencingToken = deliveryFencingToken;
        DeliveryVisibleAfter = deliveryVisibleAfter;
    }

    public string OutboxItemId { get; }
    public RuntimePostCommitIntent Intent { get; }
    public RuntimePostCommitOutboxStatus Status { get; }
    public DateTimeOffset RecordedAt { get; }
    public DateTimeOffset? AvailableAt { get; }
    public RuntimePostCommitRetryPolicy RetryPolicy { get; }
    public int DeliveryAttemptCount { get; }
    public string? DeliveringOwnerId { get; }
    public DateTimeOffset? DeliveryStartedAt { get; }
    public DateTimeOffset? DeliveredAt { get; }
    public string? LastFailureMessage { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public long DeliveryFencingToken { get; }
    public DateTimeOffset? DeliveryVisibleAfter { get; }

    public bool IsTerminal => Status is RuntimePostCommitOutboxStatus.Delivered
        or RuntimePostCommitOutboxStatus.FailedFinal
        or RuntimePostCommitOutboxStatus.Cancelled;

    private static void Validate(
        RuntimePostCommitOutboxStatus status,
        string? deliveringOwnerId,
        DateTimeOffset? deliveryStartedAt,
        DateTimeOffset? deliveredAt,
        long deliveryFencingToken,
        DateTimeOffset? deliveryVisibleAfter)
    {
        if (status == RuntimePostCommitOutboxStatus.Delivering)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(deliveringOwnerId);

            if (deliveryStartedAt is null)
                throw new ArgumentException("A delivering outbox item requires a delivery start time.", nameof(deliveryStartedAt));

            // Token zero with no visibility is accepted only as the legacy v1 Delivering representation. Every v2
            // claim created through the claim contract carries both a positive token and visibility deadline.
            if ((deliveryFencingToken > 0) != (deliveryVisibleAfter is not null))
                throw new ArgumentException("A claimed outbox item requires both a positive fencing token and visibility deadline.", nameof(deliveryFencingToken));
            if (deliveryVisibleAfter is { } visibleAfter && visibleAfter <= deliveryStartedAt)
                throw new ArgumentOutOfRangeException(nameof(deliveryVisibleAfter), "Delivery visibility must follow the claim time.");
        }
        else
        {
            if (deliveringOwnerId is not null)
                throw new ArgumentException("Only delivering outbox items can carry active delivery ownership.", nameof(deliveringOwnerId));

            if (deliveryStartedAt is not null)
                throw new ArgumentException("Only delivering outbox items can carry a delivery start time.", nameof(deliveryStartedAt));

            if (deliveryVisibleAfter is not null)
                throw new ArgumentException("Only delivering outbox items can carry a delivery visibility deadline.", nameof(deliveryVisibleAfter));
        }

        if (status == RuntimePostCommitOutboxStatus.Delivered && deliveredAt is null)
            throw new ArgumentException("A delivered outbox item requires a delivered time.", nameof(deliveredAt));

        if (status != RuntimePostCommitOutboxStatus.Delivered && deliveredAt is not null)
            throw new ArgumentException("Only delivered outbox items can carry a delivered time.", nameof(deliveredAt));
    }
}

public sealed class RuntimePostCommitOutboxClaimRequest
{
    public RuntimePostCommitOutboxClaimRequest(
        string ownerId,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        int limit,
        string? workflowExecutionId = null,
        string? intentKind = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "Outbox claim visibility timeout must be greater than zero.");
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Outbox claim limit must be greater than zero.");
        ValidateOptional(workflowExecutionId, nameof(workflowExecutionId));
        ValidateOptional(intentKind, nameof(intentKind));

        OwnerId = ownerId;
        Now = now;
        VisibilityTimeout = visibilityTimeout;
        Limit = limit;
        WorkflowExecutionId = workflowExecutionId;
        IntentKind = intentKind;
    }

    public string OwnerId { get; }
    public DateTimeOffset Now { get; }
    public TimeSpan VisibilityTimeout { get; }
    public int Limit { get; }
    public string? WorkflowExecutionId { get; }
    public string? IntentKind { get; }

    private static void ValidateOptional(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Outbox claim filters cannot be blank.", parameterName);
    }
}

public sealed class RuntimePostCommitOutboxClaim
{
    public RuntimePostCommitOutboxClaim(
        RuntimePostCommitOutboxItem item,
        string ownerId,
        long fencingToken,
        DateTimeOffset claimedAt,
        DateTimeOffset visibleAfter)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (fencingToken <= 0)
            throw new ArgumentOutOfRangeException(nameof(fencingToken), "Outbox claim fencing token must be positive.");
        if (visibleAfter <= claimedAt)
            throw new ArgumentOutOfRangeException(nameof(visibleAfter), "Outbox claim visibility must follow the claim time.");

        Item = item;
        OwnerId = ownerId;
        FencingToken = fencingToken;
        ClaimedAt = claimedAt;
        VisibleAfter = visibleAfter;
    }

    public string OutboxItemId => Item.OutboxItemId;
    public RuntimePostCommitOutboxItem Item { get; }
    public string OwnerId { get; }
    public long FencingToken { get; }
    public DateTimeOffset ClaimedAt { get; }
    public DateTimeOffset VisibleAfter { get; }
}

/// <summary>One fenced outbox completion plus its optional atomic dispatch lifecycle projection.</summary>
public sealed class RuntimePostCommitOutboxClaimCompletion
{
    public RuntimePostCommitOutboxClaimCompletion(
        RuntimePostCommitOutboxClaim claim,
        RuntimePostCommitOutboxDeliveryResult deliveryResult,
        WorkflowDispatchRecord? workflowDispatch = null)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(deliveryResult);
        if (!StringComparer.Ordinal.Equals(claim.OutboxItemId, deliveryResult.OutboxItemId))
            throw new ArgumentException("The outbox delivery result must identify the claimed item.", nameof(deliveryResult));
        if (workflowDispatch is not null)
        {
            var becomesFinal = deliveryResult.Status == RuntimePostCommitOutboxStatus.FailedFinal ||
                deliveryResult.Status == RuntimePostCommitOutboxStatus.FailedRetryable &&
                claim.Item.DeliveryAttemptCount + 1 >= claim.Item.RetryPolicy.MaxAttempts;
            if (!becomesFinal ||
                workflowDispatch.Status != WorkflowDispatchStatus.DispatchFailed)
            {
                throw new ArgumentException("An outbox claim failure projection requires a failed delivery result and DispatchFailed status.", nameof(workflowDispatch));
            }

            var identity = new WorkflowDispatchIdentity(
                workflowDispatch.ParentWorkflowExecutionId,
                workflowDispatch.ParentActivityExecutionId);
            var authorized = StringComparer.Ordinal.Equals(claim.Item.Intent.WorkflowExecutionId, workflowDispatch.ParentWorkflowExecutionId) &&
                StringComparer.Ordinal.Equals(claim.Item.Intent.ActivityExecutionId, workflowDispatch.ParentActivityExecutionId) &&
                StringComparer.Ordinal.Equals(claim.Item.Intent.IntentId, identity.StartIntentId) &&
                StringComparer.Ordinal.Equals(claim.Item.Intent.IdempotencyKey, identity.StartIdempotencyKey) &&
                claim.Item.Intent.Metadata.TryGetValue("runtime.dispatchId", out var dispatchId) &&
                StringComparer.Ordinal.Equals(dispatchId, workflowDispatch.DispatchId) &&
                StringComparer.Ordinal.Equals(workflowDispatch.DispatchId, identity.DispatchId) &&
                WorkflowDispatchLifecycle.ReadSafeDiagnosticCode(workflowDispatch) is not null &&
                WorkflowDispatchLifecycle.ReadSafeDiagnosticCategory(workflowDispatch) is not null;
            if (!authorized)
                throw new ArgumentException("The claimed outbox intent does not authorize this workflow-dispatch failure projection.", nameof(workflowDispatch));
        }

        Claim = claim;
        DeliveryResult = deliveryResult;
        WorkflowDispatch = workflowDispatch;
    }

    public RuntimePostCommitOutboxClaim Claim { get; }
    public RuntimePostCommitOutboxDeliveryResult DeliveryResult { get; }
    public WorkflowDispatchRecord? WorkflowDispatch { get; }
}

/// <summary>Shared claim and completion transitions for in-memory and durable outbox stores.</summary>
public static class RuntimePostCommitOutboxClaimTransitions
{
    public static bool CanClaim(RuntimePostCommitOutboxItem item, RuntimePostCommitOutboxClaimRequest request)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(request);

        if (request.WorkflowExecutionId is not null &&
            !StringComparer.Ordinal.Equals(item.Intent.WorkflowExecutionId, request.WorkflowExecutionId))
            return false;
        if (request.IntentKind is not null && !StringComparer.Ordinal.Equals(item.Intent.Kind, request.IntentKind))
            return false;
        if (item.Status == RuntimePostCommitOutboxStatus.Delivering)
            return item.DeliveryVisibleAfter is { } visibleAfter && visibleAfter <= request.Now;
        if (item.AvailableAt is { } availableAt && availableAt > request.Now)
            return false;
        if (item.Status == RuntimePostCommitOutboxStatus.Pending)
            return true;
        return item.Status == RuntimePostCommitOutboxStatus.FailedRetryable &&
               item.RetryPolicy.MaxAttempts > 0 &&
               item.DeliveryAttemptCount < item.RetryPolicy.MaxAttempts;
    }

    public static RuntimePostCommitOutboxClaim Claim(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxClaimRequest request)
    {
        if (!CanClaim(item, request))
            throw new InvalidOperationException($"Post-commit outbox item '{item.OutboxItemId}' is not claimable.");

        var token = checked(item.DeliveryFencingToken + 1);
        var visibleAfter = request.Now.Add(request.VisibilityTimeout);
        var claimedItem = Copy(
            item,
            RuntimePostCommitOutboxStatus.Delivering,
            item.AvailableAt,
            item.DeliveryAttemptCount,
            request.OwnerId,
            request.Now,
            deliveredAt: null,
            item.LastFailureMessage,
            token,
            visibleAfter);
        return new RuntimePostCommitOutboxClaim(claimedItem, request.OwnerId, token, request.Now, visibleAfter);
    }

    public static RuntimePostCommitOutboxItem Complete(
        RuntimePostCommitOutboxItem current,
        RuntimePostCommitOutboxClaim claim,
        RuntimePostCommitOutboxDeliveryResult result)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);

        if (!StringComparer.Ordinal.Equals(current.OutboxItemId, claim.OutboxItemId) ||
            !StringComparer.Ordinal.Equals(result.OutboxItemId, claim.OutboxItemId) ||
            current.Status != RuntimePostCommitOutboxStatus.Delivering ||
            !StringComparer.Ordinal.Equals(current.DeliveringOwnerId, claim.OwnerId) ||
            current.DeliveryFencingToken != claim.FencingToken)
        {
            throw new InvalidOperationException($"Post-commit outbox claim for '{claim.OutboxItemId}' is stale or not owned by this claimant.");
        }

        var attemptCount = current.DeliveryAttemptCount + 1;
        var status = result.Status == RuntimePostCommitOutboxStatus.FailedRetryable &&
                     attemptCount >= current.RetryPolicy.MaxAttempts
            ? RuntimePostCommitOutboxStatus.FailedFinal
            : result.Status;
        DateTimeOffset? availableAt = status == RuntimePostCommitOutboxStatus.FailedRetryable
            ? result.RecordedAt.Add(current.RetryPolicy.Delay ?? TimeSpan.Zero)
            : null;
        return Copy(
            current,
            status,
            availableAt,
            attemptCount,
            deliveringOwnerId: null,
            deliveryStartedAt: null,
            deliveredAt: status == RuntimePostCommitOutboxStatus.Delivered ? result.RecordedAt : null,
            result.FailureMessage,
            current.DeliveryFencingToken,
            deliveryVisibleAfter: null);
    }

    private static RuntimePostCommitOutboxItem Copy(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxStatus status,
        DateTimeOffset? availableAt,
        int deliveryAttemptCount,
        string? deliveringOwnerId,
        DateTimeOffset? deliveryStartedAt,
        DateTimeOffset? deliveredAt,
        string? lastFailureMessage,
        long deliveryFencingToken,
        DateTimeOffset? deliveryVisibleAfter) =>
        new(
            item.OutboxItemId,
            item.Intent,
            status,
            item.RecordedAt,
            availableAt,
            item.RetryPolicy,
            deliveryAttemptCount,
            deliveringOwnerId,
            deliveryStartedAt,
            deliveredAt,
            lastFailureMessage,
            item.Metadata,
            deliveryFencingToken,
            deliveryVisibleAfter);
}

public sealed class RuntimePostCommitRetryPolicy
{
    public RuntimePostCommitRetryPolicy(
        int maxAttempts,
        TimeSpan? delay,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (maxAttempts < 0)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Maximum delivery attempts cannot be negative.");

        if (maxAttempts == 0 && delay is not null)
            throw new ArgumentException("A retry policy with no attempts cannot carry a retry delay.", nameof(delay));

        if (maxAttempts > 0 && delay is null)
            throw new ArgumentException("A retry policy with attempts requires a retry delay.", nameof(delay));

        if (delay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay), "Retry delay must be greater than zero.");

        MaxAttempts = maxAttempts;
        Delay = delay;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public static RuntimePostCommitRetryPolicy None { get; } = new(0, null);

    public int MaxAttempts { get; }
    public TimeSpan? Delay { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class RuntimePostCommitOutboxQuery
{
    public RuntimePostCommitOutboxQuery(
        DateTimeOffset now,
        int limit,
        string? workflowExecutionId = null,
        string? ownerId = null,
        string? intentKind = null)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Outbox query limit must be greater than zero.");

        if (workflowExecutionId is not null && string.IsNullOrWhiteSpace(workflowExecutionId))
            throw new ArgumentException("Outbox workflow execution filter cannot be blank.", nameof(workflowExecutionId));

        if (ownerId is not null && string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("Outbox owner filter cannot be blank.", nameof(ownerId));

        if (intentKind is not null && string.IsNullOrWhiteSpace(intentKind))
            throw new ArgumentException("Outbox intent kind filter cannot be blank.", nameof(intentKind));

        Now = now;
        Limit = limit;
        WorkflowExecutionId = workflowExecutionId;
        OwnerId = ownerId;
        IntentKind = intentKind;
    }

    public DateTimeOffset Now { get; }
    public int Limit { get; }
    public string? WorkflowExecutionId { get; }
    public string? OwnerId { get; }
    public string? IntentKind { get; }
}

public sealed class RuntimePostCommitOutboxDeliveryResult
{
    public RuntimePostCommitOutboxDeliveryResult(
        string outboxItemId,
        RuntimePostCommitOutboxStatus status,
        DateTimeOffset recordedAt,
        string? failureMessage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxItemId);

        if (status is RuntimePostCommitOutboxStatus.Pending or RuntimePostCommitOutboxStatus.Delivering)
            throw new ArgumentException("Outbox delivery results must record a completed delivery outcome.", nameof(status));

        if (status is RuntimePostCommitOutboxStatus.FailedRetryable or RuntimePostCommitOutboxStatus.FailedFinal)
            ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        if (status is not (RuntimePostCommitOutboxStatus.FailedRetryable or RuntimePostCommitOutboxStatus.FailedFinal) && failureMessage is not null)
            throw new ArgumentException("Only failed outbox delivery results can carry a failure message.", nameof(failureMessage));

        OutboxItemId = outboxItemId;
        Status = status;
        RecordedAt = recordedAt;
        FailureMessage = failureMessage;
    }

    public string OutboxItemId { get; }
    public RuntimePostCommitOutboxStatus Status { get; }
    public DateTimeOffset RecordedAt { get; }
    public string? FailureMessage { get; }
}

public enum RuntimePostCommitOutboxStatus
{
    Pending,
    Delivering,
    Delivered,
    FailedRetryable,
    FailedFinal,
    Cancelled
}
