using System.Collections.ObjectModel;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class RuntimePostCommitOutboxProcessRequest
{
    public RuntimePostCommitOutboxProcessRequest(
        int limit,
        string? workflowExecutionId = null,
        string? intentKind = null)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Outbox processing limit must be greater than zero.");

        if (workflowExecutionId is not null && string.IsNullOrWhiteSpace(workflowExecutionId))
            throw new ArgumentException("Outbox workflow execution filter cannot be blank.", nameof(workflowExecutionId));

        if (intentKind is not null && string.IsNullOrWhiteSpace(intentKind))
            throw new ArgumentException("Outbox intent kind filter cannot be blank.", nameof(intentKind));

        Limit = limit;
        WorkflowExecutionId = workflowExecutionId;
        IntentKind = intentKind;
    }

    public int Limit { get; }
    public string? WorkflowExecutionId { get; }
    public string? IntentKind { get; }
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

    /// <summary>
    /// Items this processor actually delivered. A superseded item is excluded: another deliverer owns it, and counting it
    /// here would inflate the drain orchestrator's loop-continuation signal with work this processor did not do.
    /// </summary>
    public int DeliveredCount => Items.Count(item =>
        !item.IsSuperseded && item.RequestedDeliveryResultStatus == RuntimePostCommitOutboxStatus.Delivered);

    /// <summary>
    /// Items whose delivery failed and whose failure this processor recorded. A superseded item is excluded: the failure
    /// was never persisted, and the owning deliverer reports the item's real outcome.
    /// </summary>
    public int FailedCount => Items.Count(item =>
        !item.IsSuperseded && item.RequestedDeliveryResultStatus is RuntimePostCommitOutboxStatus.FailedRetryable or RuntimePostCommitOutboxStatus.FailedFinal);

    /// <summary>Items another deliverer owned, which this processor neither delivered nor failed.</summary>
    public int SupersededCount => Items.Count(item => item.IsSuperseded);
}

/// <param name="RequestedDeliveryResultStatus">
/// The status this processor asked the store to record. It is the REQUESTED status, not necessarily the persisted one —
/// when <paramref name="Outcome"/> is
/// <see cref="RuntimePostCommitOutboxClaimCompletionOutcome.SupersededByOtherOwner"/> nothing was persisted at all.
/// </param>
/// <param name="Outcome">What the store reported back. Defaults to
/// <see cref="RuntimePostCommitOutboxClaimCompletionOutcome.Persisted"/> so existing construction sites keep their meaning.</param>
public sealed record RuntimePostCommitOutboxProcessedItem(
    string OutboxItemId,
    string IntentId,
    RuntimePostCommitOutboxStatus RequestedDeliveryResultStatus,
    string? FailureMessage,
    RuntimePostCommitOutboxClaimCompletionOutcome Outcome = RuntimePostCommitOutboxClaimCompletionOutcome.Persisted)
{
    /// <summary>True when another deliverer owned this item, so this processor persisted nothing for it.</summary>
    public bool IsSuperseded => Outcome == RuntimePostCommitOutboxClaimCompletionOutcome.SupersededByOtherOwner;
}
