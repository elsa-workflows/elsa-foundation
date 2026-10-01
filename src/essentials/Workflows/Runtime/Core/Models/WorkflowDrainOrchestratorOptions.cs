namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class WorkflowDrainOrchestratorOptions
{
    public const int DefaultMaxDrainCycles = 64;
    public const int DefaultOutboxDeliveryBatchSize = 64;

    /// <summary>
    /// The default <see cref="ContinuationClaimWaitLimit"/>: the post-commit outbox processor's one-minute claim visibility
    /// timeout, so a claim whose deliverer died lapses and the drain delivers the item itself, plus a 30-second margin.
    /// </summary>
    public static readonly TimeSpan DefaultContinuationClaimWaitLimit = TimeSpan.FromSeconds(90);

    public WorkflowDrainOrchestratorOptions(
        int maxDrainCycles = DefaultMaxDrainCycles,
        int outboxDeliveryBatchSize = DefaultOutboxDeliveryBatchSize,
        TimeSpan? continuationClaimWaitLimit = null)
    {
        if (maxDrainCycles <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDrainCycles), "Maximum drain cycles must be greater than zero.");

        if (outboxDeliveryBatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(outboxDeliveryBatchSize), "Outbox delivery batch size must be greater than zero.");

        if (continuationClaimWaitLimit <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(continuationClaimWaitLimit), "The continuation claim wait limit must be greater than zero.");

        MaxDrainCycles = maxDrainCycles;
        OutboxDeliveryBatchSize = outboxDeliveryBatchSize;
        ContinuationClaimWaitLimit = continuationClaimWaitLimit ?? DefaultContinuationClaimWaitLimit;
    }

    public int MaxDrainCycles { get; }
    public int OutboxDeliveryBatchSize { get; }

    /// <summary>
    /// How long a drain waits, each time, for another deliverer that holds a claim on one of its execution's continuations
    /// (#2225). When the limit passes with the claim still held, the drain stops with
    /// <see cref="RuntimeSchedulerDrainStopReason.OutboxDeliveryFailed"/> rather than reporting quiescence.
    /// </summary>
    public TimeSpan ContinuationClaimWaitLimit { get; }
}
