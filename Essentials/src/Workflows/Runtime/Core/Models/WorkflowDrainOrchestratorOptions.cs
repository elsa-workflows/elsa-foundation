using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class WorkflowDrainOrchestratorOptions
{
    public const int DefaultMaxCyclesWithoutProgress = 64;
    public const int DefaultDrainCycleWarningThreshold = 1000;
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
        int maxCyclesWithoutProgress = DefaultMaxCyclesWithoutProgress,
        int outboxDeliveryBatchSize = DefaultOutboxDeliveryBatchSize,
        TimeSpan? continuationClaimWaitLimit = null,
        int? maxTotalDrainCycles = null,
        int? drainCycleWarningThreshold = DefaultDrainCycleWarningThreshold)
    {
        if (maxCyclesWithoutProgress <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCyclesWithoutProgress), "The maximum number of drain cycles without progress must be greater than zero.");

        if (outboxDeliveryBatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(outboxDeliveryBatchSize), "Outbox delivery batch size must be greater than zero.");

        if (continuationClaimWaitLimit <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(continuationClaimWaitLimit), "The continuation claim wait limit must be greater than zero.");

        if (maxTotalDrainCycles <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTotalDrainCycles), "The maximum total drain cycles must be greater than zero, or null for no limit.");

        if (drainCycleWarningThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(drainCycleWarningThreshold), "The drain cycle warning threshold must be greater than zero, or null for no warning.");

        MaxCyclesWithoutProgress = maxCyclesWithoutProgress;
        OutboxDeliveryBatchSize = outboxDeliveryBatchSize;
        ContinuationClaimWaitLimit = continuationClaimWaitLimit ?? DefaultContinuationClaimWaitLimit;
        MaxTotalDrainCycles = maxTotalDrainCycles;
        DrainCycleWarningThreshold = drainCycleWarningThreshold;
    }

    /// <summary>
    /// How many drain cycles in a row may pass without progress before the drain fails with
    /// <see cref="DrainCycleLimitExceededException"/>. A cycle makes progress when it dispatches a work item that this
    /// drain has not dispatched before. Each follow-up work item id is derived from the hop that produced it, so a run
    /// that advances dispatches new ids, while a drain that keeps delivering work that dispatches nothing, or keeps
    /// dispatching the same work item again, does not. Cycles that make progress reset the count, so this does not limit
    /// how long a workflow can be. Each drain starts its own count.
    /// </summary>
    public int MaxCyclesWithoutProgress { get; }

    public int OutboxDeliveryBatchSize { get; }

    /// <summary>
    /// How long one drain request waits, in total, for other deliverers that hold its execution's continuations (#2225).
    /// The deadline starts at the drain's first wait and every later wait in the same request shares it, so the drain's
    /// cycles cannot multiply the limit. When it passes with a continuation still held, the drain stops with
    /// <see cref="RuntimeSchedulerDrainStopReason.OutboxDeliveryFailed"/> rather than reporting quiescence.
    /// </summary>
    public TimeSpan ContinuationClaimWaitLimit { get; }

    /// <summary>
    /// An optional limit on all the cycles of one drain, whether they make progress or not. A drain that needs more fails
    /// with <see cref="DrainCycleLimitExceededException"/>. Null, the default, sets no limit, so a long drain that keeps
    /// making progress runs to its end.
    /// </summary>
    public int? MaxTotalDrainCycles { get; }

    /// <summary>
    /// The cycle count past which a drain logs one warning naming its workflow execution, so a long drain that keeps making
    /// progress stays visible. Null disables the warning.
    /// </summary>
    public int? DrainCycleWarningThreshold { get; }
}
