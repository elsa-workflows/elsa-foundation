using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// Coalescing decorator for <see cref="IRuntimeCheckpointCommitStore"/> (E3-6, RT-10). While a coalescing session owns
/// the target workflow execution, deferred checkpoints are buffered into an in-memory working set and folded into a
/// single atomic durable commit at a flush boundary (suspension/fault/cancellation/completion, an operational or
/// bookmark write, the per-segment hop cap, or the end-of-drain quiescence flush). When no session is active it is a
/// byte-for-byte pass-through to the durable inner store, so the default (Immediate) path is completely unaffected.
/// </summary>
/// <remarks>
/// This decorator never bypasses W5 ownership fencing: the folded flush is routed back through
/// <see cref="RuntimeCheckpointCommitter"/> (via the drain scope's quiescence flush), and every boundary commit reaching
/// this store has already been fenced by the committer for the same workflow execution. The durable scheduler queue is
/// advanced only after the folded commit lands (condition B), so a crash mid-segment replays from the last flushed state.
/// </remarks>
public sealed class CoalescingRuntimeCheckpointCommitStore(
    CoalescingInner<IRuntimeCheckpointCommitStore> inner,
    IRuntimeCoalescingSessionAccessor sessionAccessor) : IRuntimeCheckpointCommitStore
{
    private static readonly RuntimeCheckpointPersistenceDecision ImmediateDecision =
        new(RuntimeCheckpointPersistenceMode.Immediate, "Coalesced segment flush.");

    private readonly IRuntimeCheckpointCommitStore _inner = inner.Value;

    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(decision);

        if (sessionAccessor.Current is not { } session || !session.AppliesTo(commit.WorkflowExecutionId))
            return await _inner.CommitAsync(commit, decision, cancellationToken);

        // The synthetic folded quiescence-flush commit built by the drain scope: it already represents the whole
        // segment (routed here through the committer so fencing gates it). Apply straight to inner and end the segment.
        if (commit.Checkpoint.Metadata.ContainsKey(RuntimeCoalescingMetadataKeys.CoalescedFlush))
        {
            var flushResult = await _inner.CommitAsync(commit, decision, cancellationToken);
            await session.AdvanceInnerQueueAsync(cancellationToken);
            session.ClearBuffer();
            session.Deactivate();
            return flushResult;
        }

        var forceImmediate = HasBoundaryState(commit);
        var capReached = session.HopCount + 1 > session.MaxSegmentCheckpoints;

        // Deferrable, non-boundary, under cap: buffer into the overlay working set and acknowledge this commit's own
        // outbox items so the committer's post-commit count assertion is satisfied without durably persisting anything.
        if (decision.Mode == RuntimeCheckpointPersistenceMode.Deferred && !forceImmediate && !capReached)
        {
            session.BufferDeferred(commit);
            return OwnOutbox(commit);
        }

        // Flush boundary (or cap): fold the buffered segment with this commit into one atomic durable write, then
        // advance the durable queue and end the segment (the drain continues with byte-for-byte Immediate semantics).
        if (session.HasBufferedChanges)
        {
            var foldedState = session.FoldBufferedStateChangesWith(commit.StateChanges);
            var outbox = UnionOutbox(session.RemainingPendingOutboxChanges(), commit.StateChanges.PostCommitOutbox);
            var foldedCommit = commit with
            {
                StateChanges = foldedState.WithPostCommitOutbox(outbox),
                PostCommitIntents = [],
            };

            await _inner.CommitAsync(foldedCommit, ImmediateDecision, cancellationToken);
            await session.AdvanceInnerQueueAsync(cancellationToken);
            session.ClearBuffer();
            session.Deactivate();
            return OwnOutbox(commit);
        }

        // Nothing buffered: pass this commit straight through, then reconcile the durable queue with any overlay
        // consumption that happened before the first checkpoint and end the segment.
        var passthrough = await _inner.CommitAsync(commit, decision, cancellationToken);
        await session.AdvanceInnerQueueAsync(cancellationToken);
        session.ClearBuffer();
        session.Deactivate();
        return passthrough;
    }

    // Operational (lease/heartbeat/ownership — condition E), incident, and bookmark writes must never be buffered:
    // they are durability-critical and are flushed immediately even if they ride on a deferrable checkpoint name.
    private static bool HasBoundaryState(RuntimeCheckpointCommit commit) =>
        commit.StateChanges.Operational.Count > 0 ||
        commit.StateChanges.Incidents.Count > 0 ||
        commit.StateChanges.Bookmarks.Count > 0 ||
        commit.StateChanges.WorkflowDispatches.Count > 0 ||
        commit.StateChanges.WorkflowDispatchCancellations.Count > 0;

    private static RuntimeCheckpointCommitStoreResult OwnOutbox(RuntimeCheckpointCommit commit) =>
        new(commit.StateChanges.PostCommitOutbox.Select(change => change.State.OutboxItemId).ToArray());

    private static IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> UnionOutbox(
        IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> remaining,
        IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> own)
    {
        var merged = new Dictionary<string, RuntimeStateChange<RuntimePostCommitOutboxItem>>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var change in remaining.Concat(own))
        {
            if (merged.TryAdd(change.StateId, change))
                order.Add(change.StateId);
            else
                merged[change.StateId] = change;
        }

        return order.Select(id => merged[id]).ToArray();
    }
}
