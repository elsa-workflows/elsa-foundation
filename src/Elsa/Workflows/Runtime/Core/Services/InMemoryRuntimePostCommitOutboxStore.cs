using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryRuntimePostCommitOutboxStore : IRuntimePostCommitOutboxStore
{
    private readonly object _syncRoot = new();

    // Terminal items stay resident so duplicate delivery results can be rejected; durable providers own retention policy.
    private readonly Dictionary<string, RuntimePostCommitOutboxItem> _items = new(StringComparer.Ordinal);

    public ValueTask SavePendingAsync(RuntimePostCommitOutboxItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();

        if (item.Status != RuntimePostCommitOutboxStatus.Pending)
            throw new InvalidOperationException("Only pending post-commit outbox items can be saved as pending.");

        lock (_syncRoot)
        {
            if (_items.TryGetValue(item.OutboxItemId, out var existing))
            {
                if (IsSamePendingIntent(existing, item))
                    return ValueTask.CompletedTask;

                throw new InvalidOperationException($"Post-commit outbox item '{item.OutboxItemId}' already exists with a different intent or status.");
            }

            _items.Add(item.OutboxItemId, item);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.OwnerId is not null)
            throw new NotSupportedException("The in-memory post-commit outbox store does not implement delivery ownership filtering.");

        lock (_syncRoot)
        {
            var items = _items.Values
                .Where(item => IsDeliverable(item, query))
                .OrderBy(item => item.AvailableAt ?? DateTimeOffset.MinValue)
                .ThenBy(item => item.RecordedAt)
                .ThenBy(item => item.OutboxItemId, StringComparer.Ordinal)
                .Take(query.Limit)
                .ToArray();

            return new ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>>(items);
        }
    }

    public ValueTask RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!_items.TryGetValue(result.OutboxItemId, out var existing))
                throw new InvalidOperationException($"Post-commit outbox item '{result.OutboxItemId}' was not found.");

            if (existing.IsTerminal)
                throw new InvalidOperationException($"Post-commit outbox item '{result.OutboxItemId}' is already terminal.");

            var deliveryAttemptCount = existing.DeliveryAttemptCount + 1;
            var status = NormalizeDeliveryStatus(existing, result.Status, deliveryAttemptCount);
            DateTimeOffset? availableAt = status == RuntimePostCommitOutboxStatus.FailedRetryable
                ? NextRetryAvailableAt(existing, result.RecordedAt)
                : null;

            _items[result.OutboxItemId] = new RuntimePostCommitOutboxItem(
                outboxItemId: existing.OutboxItemId,
                intent: existing.Intent,
                status: status,
                recordedAt: existing.RecordedAt,
                availableAt: availableAt,
                retryPolicy: existing.RetryPolicy,
                deliveryAttemptCount: deliveryAttemptCount,
                deliveringOwnerId: null,
                deliveryStartedAt: null,
                deliveredAt: status == RuntimePostCommitOutboxStatus.Delivered ? result.RecordedAt : null,
                lastFailureMessage: result.FailureMessage,
                metadata: existing.Metadata);
        }

        return ValueTask.CompletedTask;
    }

    private static bool IsSamePendingIntent(RuntimePostCommitOutboxItem existing, RuntimePostCommitOutboxItem item) =>
        existing.Status == RuntimePostCommitOutboxStatus.Pending
        && StringComparer.Ordinal.Equals(existing.Intent.IntentId, item.Intent.IntentId)
        && StringComparer.Ordinal.Equals(existing.Intent.WorkflowExecutionId, item.Intent.WorkflowExecutionId)
        && StringComparer.Ordinal.Equals(existing.Intent.Kind, item.Intent.Kind)
        && StringComparer.Ordinal.Equals(existing.Intent.ActivityExecutionId, item.Intent.ActivityExecutionId)
        && StringComparer.Ordinal.Equals(existing.Intent.IdempotencyKey, item.Intent.IdempotencyKey)
        && StringComparer.Ordinal.Equals(existing.Intent.DependsOnWaitRegistrationId, item.Intent.DependsOnWaitRegistrationId)
        && existing.Intent.WaitFailurePolicy == item.Intent.WaitFailurePolicy;

    private static bool IsDeliverable(RuntimePostCommitOutboxItem item, RuntimePostCommitOutboxQuery query)
    {
        if (query.WorkflowExecutionId is not null && !StringComparer.Ordinal.Equals(item.Intent.WorkflowExecutionId, query.WorkflowExecutionId))
            return false;

        if (item.AvailableAt is { } availableAt && availableAt > query.Now)
            return false;

        if (item.Status == RuntimePostCommitOutboxStatus.Pending)
            return true;

        if (item.Status == RuntimePostCommitOutboxStatus.FailedRetryable)
            return item.RetryPolicy.MaxAttempts > 0 && item.DeliveryAttemptCount < item.RetryPolicy.MaxAttempts;

        return false;
    }

    private static RuntimePostCommitOutboxStatus NormalizeDeliveryStatus(
        RuntimePostCommitOutboxItem existing,
        RuntimePostCommitOutboxStatus status,
        int deliveryAttemptCount)
    {
        if (status != RuntimePostCommitOutboxStatus.FailedRetryable)
            return status;

        return deliveryAttemptCount >= existing.RetryPolicy.MaxAttempts
            ? RuntimePostCommitOutboxStatus.FailedFinal
            : RuntimePostCommitOutboxStatus.FailedRetryable;
    }

    private static DateTimeOffset NextRetryAvailableAt(RuntimePostCommitOutboxItem existing, DateTimeOffset recordedAt) =>
        existing.RetryPolicy.Delay is { } delay ? recordedAt.Add(delay) : recordedAt;
}
