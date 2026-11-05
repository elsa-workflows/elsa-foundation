using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>Serializes owner observations and retained-history purge against checkpoint allocation and run deletion.</summary>
internal sealed class InMemoryRuntimeHistoryWriter(InMemoryRuntimeCheckpointStoreState state,
    IPersistenceAccessContextAccessor access, IWorkflowExecutionStateStore executions, TimeProvider clock)
    : IRuntimeHistoryObservationWriter, IRuntimeHistoryRetentionStore
{
    public bool CanRecordHistory(IRuntimeHistoryStore selectedStore) => selectedStore is InMemoryRuntimeHistoryStore reader &&
        reader.Uses(state, access.Current.RequireScope()) && reader.UsesAccessor(access);

    public ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var context = access.Current;
        var scope = context.RequireScope().Value;
        var e = observation.Event;
        RuntimeHistorySafety.ValidateMetadata(e);
        if (e.Authority != RuntimeHistoryAuthority.DeliveryObservation || e.Sequence != 0 || e.PhysicalCommitId is not null ||
            e.RecordedAt is not null || e.CheckpointOrdinal is not null || e.CheckpointId is not null ||
            !observation.Policy.SupportedKinds.Contains(e.Name, StringComparer.Ordinal) ||
            !RuntimeHistoryDeliveryFacts.Kinds.Concat(RuntimeHistoryDeliveryFacts.OwnerObservationKinds).Concat(DurableTimerHistoryFacts.Kinds).Contains(e.Name, StringComparer.Ordinal))
            throw new InvalidOperationException("Owner observations must be safe, unpositioned, independently attributed and admitted by the pinned policy.");
        var fingerprint = "observation:" + RuntimeHistoryIdentity.Compute(e);
        return state.ExecuteWriteGateAsync(async token =>
        {
            var execution = await executions.FindAsync(e.WorkflowExecutionId, token);
            if (execution is null) return new RuntimeHistoryObservationResult(false, null);
            context.EnsureTenantScope(execution.TenantId);
            if (access.Current != context) throw new InvalidOperationException("History persistence scope changed during observation admission.");
            lock (state.SyncRoot)
            {
                var key = (scope, e.WorkflowExecutionId);
                if (state.HistoryDeletedRuns.Contains(key) || !state.HistoryHeads.TryGetValue(key, out var head))
                    return new(false, null);
                if (!head.Policy.IsEquivalentTo(observation.Policy) || !head.Policy.IsEquivalentTo(execution.HistoryCapturePolicy))
                    throw new InvalidOperationException("Observation policy differs from its retained execution.");
                if (state.HistoryCandidateFingerprints.TryGetValue((scope, e.WorkflowExecutionId, e.HistoryEventId), out var existing))
                {
                    if (existing != fingerprint) throw new InvalidOperationException("An immutable owner observation has conflicting content.");
                    if (!state.HistoryReceiptPositions.TryGetValue((scope, e.WorkflowExecutionId, e.HistoryEventId), out var position) ||
                        position <= 0 || position > head.LastSequence || position > head.PurgedThroughSequence &&
                        !state.HistoryEventsById.ContainsKey((scope, e.WorkflowExecutionId, e.HistoryEventId)))
                        throw new InvalidOperationException("The durable observation receipt has unacknowledged missing content.");
                    return new(true, state.HistoryEventsById.GetValueOrDefault((scope, e.WorkflowExecutionId, e.HistoryEventId)));
                }
                var now = clock.GetUtcNow();
                var stored = RuntimeHistoryIdentity.Position(e, checked(head.LastSequence + 1), "owner-observation:" + e.HistoryEventId, now);
                state.PublishHistoryEvent(scope, stored, fingerprint);
                state.HistoryHeads[key] = head with { LastSequence = stored.Sequence,
                    FirstSequence = head.FirstSequence == 0 || head.FirstSequence > head.LastSequence ? stored.Sequence : head.FirstSequence };
                return new(true, stored);
            }
        }, cancellationToken);
    }

    public ValueTask<long> PurgeThroughAsync(string workflowExecutionId, long throughSequence, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        if (throughSequence < 0) throw new ArgumentOutOfRangeException(nameof(throughSequence));
        var context = access.Current;
        var scope = context.RequireScope().Value;
        return state.ExecuteWriteGateAsync(async token =>
        {
            var execution = await executions.FindAsync(workflowExecutionId, token);
            if (execution is null) return 0L;
            context.EnsureTenantScope(execution.TenantId);
            if (access.Current != context) throw new InvalidOperationException("History persistence scope changed during purge admission.");
            lock (state.SyncRoot)
            {
                var key = (scope, workflowExecutionId);
                if (!state.HistoryHeads.TryGetValue(key, out var head)) return 0L;
                if (throughSequence <= head.PurgedThroughSequence) return head.PurgedThroughSequence;
                var through = Math.Min(throughSequence, head.LastSequence);
                var removed = 0;
                if (state.HistoryEvents.TryGetValue(key, out var events))
                {
                    foreach (var e in events.TakeWhile(e => e.Sequence <= through))
                    {
                        state.HistoryEventsById.Remove((scope, workflowExecutionId, e.HistoryEventId));
                        if (e.PhysicalCommitId is { } commitId) state.ScrubHistoryCommitContent(commitId);
                    }
                    removed = events.RemoveAll(e => e.Sequence <= through);
                }
                if (state.HistoryActivityIds.TryGetValue(key, out var activities))
                    foreach (var activity in activities)
                        if (state.HistoryActivityEvents.TryGetValue((scope, workflowExecutionId, activity), out var own)) own.RemoveAll(e => e.Sequence <= through);
                if (state.HistoryNames.TryGetValue(key, out var names))
                    foreach (var name in names) state.HistoryNameEvents[(scope, workflowExecutionId, name)].RemoveAll(e => e.Sequence <= through);
                if (state.HistoryCategories.TryGetValue(key, out var categories))
                    foreach (var category in categories) state.HistoryCategoryEvents[(scope, workflowExecutionId, category)].RemoveAll(e => e.Sequence <= through);
                if (state.HistoryBookmarkOrder.TryGetValue(key, out var bookmarks))
                    foreach (var bookmark in bookmarks)
                    {
                        var bookmarkKey = (scope, workflowExecutionId, bookmark);
                        if (state.HistoryBookmarkEvents.TryGetValue(bookmarkKey, out var own)) own.RemoveAll(e => e.Sequence <= through);
                        if (state.HistoryBookmarks.TryGetValue(bookmarkKey, out var projection) && projection.CreatedHistoryEventId is { } created &&
                            !state.HistoryEventsById.ContainsKey((scope, workflowExecutionId, created)))
                            state.HistoryBookmarks[bookmarkKey] = projection with { PriorState = RuntimeHistoryPriorState.CreationMetadata };
                    }
                var first = events is { Count: > 0 } ? events[0].Sequence : head.LastSequence == long.MaxValue ? 0 : head.LastSequence + 1;
                state.HistoryHeads[key] = head with { FirstSequence = first, PurgedThroughSequence = through };
                // Fingerprint receipts, counters, bookmark metadata and ancestry survive until owning run deletion.
                return through;
            }
        }, cancellationToken);
    }
}
