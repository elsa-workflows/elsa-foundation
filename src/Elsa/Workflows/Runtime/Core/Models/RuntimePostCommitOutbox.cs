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
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxItemId);
        ArgumentNullException.ThrowIfNull(intent);

        if (deliveryAttemptCount < 0)
            throw new ArgumentOutOfRangeException(nameof(deliveryAttemptCount), "Delivery attempt count cannot be negative.");

        Validate(status, deliveringOwnerId, deliveryStartedAt, deliveredAt);

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

    public bool IsTerminal => Status is RuntimePostCommitOutboxStatus.Delivered
        or RuntimePostCommitOutboxStatus.FailedFinal
        or RuntimePostCommitOutboxStatus.Cancelled;

    private static void Validate(
        RuntimePostCommitOutboxStatus status,
        string? deliveringOwnerId,
        DateTimeOffset? deliveryStartedAt,
        DateTimeOffset? deliveredAt)
    {
        if (status == RuntimePostCommitOutboxStatus.Delivering)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(deliveringOwnerId);

            if (deliveryStartedAt is null)
                throw new ArgumentException("A delivering outbox item requires a delivery start time.", nameof(deliveryStartedAt));
        }
        else
        {
            if (deliveringOwnerId is not null)
                throw new ArgumentException("Only delivering outbox items can carry active delivery ownership.", nameof(deliveringOwnerId));

            if (deliveryStartedAt is not null)
                throw new ArgumentException("Only delivering outbox items can carry a delivery start time.", nameof(deliveryStartedAt));
        }

        if (status == RuntimePostCommitOutboxStatus.Delivered && deliveredAt is null)
            throw new ArgumentException("A delivered outbox item requires a delivered time.", nameof(deliveredAt));

        if (status != RuntimePostCommitOutboxStatus.Delivered && deliveredAt is not null)
            throw new ArgumentException("Only delivered outbox items can carry a delivered time.", nameof(deliveredAt));
    }
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
        string? ownerId = null)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Outbox query limit must be greater than zero.");

        Now = now;
        Limit = limit;
        WorkflowExecutionId = workflowExecutionId;
        OwnerId = ownerId;
    }

    public DateTimeOffset Now { get; }
    public int Limit { get; }
    public string? WorkflowExecutionId { get; }
    public string? OwnerId { get; }
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
