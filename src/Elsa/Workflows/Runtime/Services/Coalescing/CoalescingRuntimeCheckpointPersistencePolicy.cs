using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// Burst-coalescing checkpoint persistence policy (E3-6, RT-10). Non-suspending intra-drain checkpoints are folded
/// into one atomic commit at quiescence; every durability-critical boundary flushes immediately so its semantics are
/// never coalesced away. This is the selectable, opt-in alternative to <see cref="ImmediateRuntimeCheckpointPersistencePolicy"/>;
/// the default runtime keeps the Immediate policy, so enabling coalescing is a deliberate durability/throughput trade.
/// </summary>
/// <remarks>
/// The policy is stateless and only maps a checkpoint name to <c>Immediate</c>/<c>Deferred</c>. The per-segment
/// hop cap (<see cref="CoalescingRuntimeCheckpointPersistenceOptions.MaxSegmentCheckpoints"/>) and the actual buffering
/// live in the coalescing commit store, which owns the ambient session state.
/// </remarks>
public sealed class CoalescingRuntimeCheckpointPersistencePolicy : IRuntimeCheckpointPersistencePolicy
{
    private static readonly RuntimeCheckpointPersistenceDecision Immediate =
        new(RuntimeCheckpointPersistenceMode.Immediate);

    private static readonly RuntimeCheckpointPersistenceDecision Deferred =
        new(RuntimeCheckpointPersistenceMode.Deferred, "Folded into the coalesced drain segment.");

    /// <summary>
    /// Checkpoint names that must always flush immediately: a suspension makes the workflow externally observable and
    /// resumable, a fault/incident/cancellation/completion is a durability-critical terminal transition, and a bookmark
    /// creation is a durable suspend point an external stimulus must be able to find. Coalescing these away would let a
    /// crash lose a decided fault, or let a stimulus race an in-memory-only bookmark.
    /// </summary>
    public static readonly IReadOnlySet<string> MandatoryFlushCheckpointNames = new HashSet<string>(StringComparer.Ordinal)
    {
        RuntimeCheckpointNames.WorkflowSuspended,
        RuntimeCheckpointNames.WorkflowCompleted,
        RuntimeCheckpointNames.WorkflowFaulted,
        RuntimeCheckpointNames.WorkflowCancelled,
        RuntimeCheckpointNames.IncidentRecorded,
        RuntimeCheckpointNames.IncidentResolutionBatchApplied,
        RuntimeCheckpointNames.ActivitySuspended,
        RuntimeCheckpointNames.ActivityCancelled,
        RuntimeCheckpointNames.BookmarkCreated,
        RuntimeCheckpointNames.RuntimeAlterationJob,
    };

    public ValueTask<RuntimeCheckpointPersistenceDecision> DecideAsync(RuntimeCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        // A folded quiescence-flush commit is synthetic and already represents the whole segment: persist it now.
        if (checkpoint.Metadata.ContainsKey(RuntimeCoalescingMetadataKeys.CoalescedFlush))
            return ValueTask.FromResult(Immediate);

        if (MandatoryFlushCheckpointNames.Contains(checkpoint.Name))
            return ValueTask.FromResult(Immediate);

        // ADR 0032 R2 / spec 107: the pre-activation attempt-claim boundary is no longer unconditionally
        // mandatory. It protects at-most-once attempt VISIBILITY (crash attribution, poison/retry accounting) —
        // diagnostic fidelity for a replay-safe activity, not correctness. An author-declared ReplaySafe profile
        // lets the claim defer (its state still enters the coalesced working set and folds forward atomically at
        // the next flush; Deferred is not Skip). External or an absent profile keeps the immediate flush exactly
        // as before, so an activity with an external effect always has its claim durable before its body runs.
        if (StringComparer.Ordinal.Equals(checkpoint.Name, RuntimeCheckpointNames.ActivityAttemptClaimed))
            return ValueTask.FromResult(IsReplaySafe(checkpoint) ? Deferred : Immediate);

        return ValueTask.FromResult(Deferred);
    }

    private static bool IsReplaySafe(RuntimeCheckpoint checkpoint) =>
        checkpoint.Metadata.TryGetValue(RuntimeMetadataKeys.CheckpointSideEffectProfile, out var profile) &&
        StringComparer.Ordinal.Equals(profile, RuntimeMetadataKeys.CheckpointSideEffectProfileReplaySafe);
}
