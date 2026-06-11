using System.Collections.ObjectModel;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class RuntimePostCommitOutboxProcessRequest
{
    public RuntimePostCommitOutboxProcessRequest(
        int limit,
        string? workflowExecutionId = null)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Outbox processing limit must be greater than zero.");

        if (workflowExecutionId is not null && string.IsNullOrWhiteSpace(workflowExecutionId))
            throw new ArgumentException("Outbox workflow execution filter cannot be blank.", nameof(workflowExecutionId));

        Limit = limit;
        WorkflowExecutionId = workflowExecutionId;
    }

    public int Limit { get; }
    public string? WorkflowExecutionId { get; }
}

public sealed class RuntimePostCommitOutboxProcessResult
{
    public RuntimePostCommitOutboxProcessResult(IReadOnlyCollection<RuntimePostCommitOutboxProcessedItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Items = new ReadOnlyCollection<RuntimePostCommitOutboxProcessedItem>(items.ToArray());
    }

    public IReadOnlyCollection<RuntimePostCommitOutboxProcessedItem> Items { get; }
    public int AttemptedCount => Items.Count;
    public int DeliveredCount => Items.Count(item => item.RequestedDeliveryResultStatus == RuntimePostCommitOutboxStatus.Delivered);
    public int FailedCount => Items.Count(item => item.RequestedDeliveryResultStatus is RuntimePostCommitOutboxStatus.FailedRetryable or RuntimePostCommitOutboxStatus.FailedFinal);
}

public sealed record RuntimePostCommitOutboxProcessedItem(
    string OutboxItemId,
    string IntentId,
    RuntimePostCommitOutboxStatus RequestedDeliveryResultStatus,
    string? FailureMessage);
