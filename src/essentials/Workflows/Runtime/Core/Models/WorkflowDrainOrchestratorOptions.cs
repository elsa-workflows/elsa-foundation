namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class WorkflowDrainOrchestratorOptions
{
    public const int DefaultMaxDrainCycles = 64;
    public const int DefaultOutboxDeliveryBatchSize = 64;

    /// <summary>
    /// What <see cref="DefaultContinuationClaimWaitLimit"/> adds to the claim visibility timeout: time for the drain to see
    /// the lapse and deliver the item itself.
    /// </summary>
    public static readonly TimeSpan ContinuationClaimWaitMargin = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The default <see cref="ContinuationClaimWaitLimit"/>: the post-commit outbox processor's claim visibility timeout
    /// (<see cref="RuntimePostCommitOutboxProcessing.ClaimVisibilityTimeout"/>), so a claim whose deliverer died lapses
    /// within the wait and the drain delivers the item itself, plus <see cref="ContinuationClaimWaitMargin"/>.
    /// </summary>
    public static readonly TimeSpan DefaultContinuationClaimWaitLimit =
        RuntimePostCommitOutboxProcessing.ClaimVisibilityTimeout + ContinuationClaimWaitMargin;

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
    /// How long one drain request waits, in total, for other deliverers that hold its execution's continuations (#2225).
    /// The deadline starts at the drain's first wait and every later wait in the same request shares it, so the drain's
    /// cycles cannot multiply the limit. When it passes with a continuation still held, the drain stops with
    /// <see cref="RuntimeSchedulerDrainStopReason.OutboxDeliveryFailed"/> rather than reporting quiescence.
    /// </summary>
    public TimeSpan ContinuationClaimWaitLimit { get; }
}
