using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// Coalescing-aware overlay for <see cref="IWorkflowExecutionStateStore"/>. When an active coalescing session owns the
/// target workflow execution, reads and writes go to the session's in-memory overlay; otherwise it is a byte-for-byte
/// pass-through to the durable inner store (the default path is completely unaffected).
/// </summary>
public sealed class CoalescingWorkflowExecutionStateStore(
    CoalescingInner<IWorkflowExecutionStateStore> inner,
    IRuntimeCoalescingSessionAccessor sessionAccessor) : IWorkflowExecutionStateStore, IInMemoryCheckpointTransactionSource
{
    private readonly IWorkflowExecutionStateStore _inner = inner.Value;
    public IEnumerable<object?> GetCheckpointTransactionParticipants() => [_inner];

    public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) =>
        _inner.SaveAsync(state, cancellationToken);

    public async ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        if (sessionAccessor.Current is { } session && session.AppliesTo(workflowExecutionId) &&
            session.TryGetWorkflowExecution(workflowExecutionId, out var overlay))
            return overlay;

        return await _inner.FindAsync(workflowExecutionId, cancellationToken);
    }

    public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) =>
        _inner.ListAsync(cancellationToken);

    public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(
        WorkflowExecutionStatePageQuery query,
        CancellationToken cancellationToken = default) =>
        _inner.QueryPageAsync(query, cancellationToken);

    public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) =>
        _inner.ListPinnedExecutableArtifactIdsAsync(cancellationToken);

    public ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
        _inner.DeleteAsync(workflowExecutionId, cancellationToken);
}

/// <summary>Coalescing-aware overlay for <see cref="IActivityExecutionStateStore"/>. See <see cref="CoalescingWorkflowExecutionStateStore"/>.</summary>
public sealed class CoalescingActivityExecutionStateStore(
    CoalescingInner<IActivityExecutionStateStore> inner,
    IRuntimeCoalescingSessionAccessor sessionAccessor) : IActivityExecutionStateStore, IInMemoryCheckpointTransactionSource
{
    private readonly IActivityExecutionStateStore _inner = inner.Value;
    public IEnumerable<object?> GetCheckpointTransactionParticipants() => [_inner];

    public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default) =>
        _inner.SaveAsync(state, cancellationToken);

    public async ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        if (sessionAccessor.Current is { } session && session.AppliesTo(workflowExecutionId) &&
            session.TryGetActivity(activityExecutionId, out var overlay, out _))
            return overlay;

        return await _inner.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);
    }

    public async ValueTask<long> CountAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        if (sessionAccessor.Current is not { } session || !session.AppliesTo(workflowExecutionId))
            return await _inner.CountAsync(workflowExecutionId, cancellationToken);

        var count = await _inner.CountAsync(workflowExecutionId, cancellationToken);
        foreach (var id in session.GetActivityTombstones())
        {
            if (await _inner.FindAsync(workflowExecutionId, id, cancellationToken) is not null)
                count--;
        }

        foreach (var state in session.GetActivityUpserts())
        {
            if (await _inner.FindAsync(workflowExecutionId, state.Execution.ActivityExecutionId, cancellationToken) is null)
                count++;
        }

        return count;
    }

    public async ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(
        ActivityExecutionStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (sessionAccessor.Current is not { } session || !session.AppliesTo(query.WorkflowExecutionId))
            return await _inner.ListPageAsync(query, cancellationToken);

        return await CoalescingRuntimeStorePageMerger.MergeAsync(
            query,
            $"coalesced-activity-state:workflow:{query.WorkflowExecutionId}",
            (limit, continuation) => _inner.ListPageAsync(
                new ActivityExecutionStatePageQuery(query.WorkflowExecutionId, limit, continuation), cancellationToken),
            state => state.Execution.ActivityExecutionId,
            after => session.GetActivityUpserts()
                .Where(state => after is null || StringComparer.Ordinal.Compare(state.Execution.ActivityExecutionId, after) > 0)
                .OrderBy(state => state.Execution.ActivityExecutionId, StringComparer.Ordinal)
                .FirstOrDefault(),
            id => session.TryGetActivity(id, out _, out _));
    }

    public async ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(
        ActivityExecutionStateParentPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (sessionAccessor.Current is not { } session || !session.AppliesTo(query.WorkflowExecutionId))
            return await _inner.ListByParentPageAsync(query, cancellationToken);

        return await CoalescingRuntimeStorePageMerger.MergeAsync(
            query,
            $"coalesced-activity-state:parent:{query.WorkflowExecutionId}:{query.ParentActivityExecutionId}",
            (limit, continuation) => _inner.ListByParentPageAsync(
                new ActivityExecutionStateParentPageQuery(
                    query.WorkflowExecutionId,
                    query.ParentActivityExecutionId,
                    limit,
                    continuation), cancellationToken),
            state => state.Execution.ActivityExecutionId,
            after => session.GetActivityUpserts()
                .Where(state => StringComparer.Ordinal.Equals(state.ParentActivityExecutionId, query.ParentActivityExecutionId))
                .Where(state => after is null || StringComparer.Ordinal.Compare(state.Execution.ActivityExecutionId, after) > 0)
                .OrderBy(state => state.Execution.ActivityExecutionId, StringComparer.Ordinal)
                .FirstOrDefault(),
            id => session.TryGetActivity(id, out _, out _));
    }
}

/// <summary>Coalescing-aware overlay for <see cref="IDurableValueStateStore"/>. See <see cref="CoalescingWorkflowExecutionStateStore"/>.</summary>
public sealed class CoalescingDurableValueStateStore(
    CoalescingInner<IDurableValueStateStore> inner,
    IRuntimeCoalescingSessionAccessor sessionAccessor) : IDurableValueStateStore, IInMemoryCheckpointTransactionSource
{
    private readonly IDurableValueStateStore _inner = inner.Value;
    public IEnumerable<object?> GetCheckpointTransactionParticipants() => [_inner];

    public ValueTask<DurableValueState> SaveAsync(DurableValueState state, CancellationToken cancellationToken = default) =>
        _inner.SaveAsync(state, cancellationToken);

    public ValueTask<bool> DeleteAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default) =>
        _inner.DeleteAsync(workflowExecutionId, durableValueId, cancellationToken);

    public async ValueTask<DurableValueState?> FindAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default)
    {
        if (sessionAccessor.Current is { } session && session.AppliesTo(workflowExecutionId) &&
            session.TryGetDurableValue(durableValueId, out var overlay, out _))
            return overlay;

        return await _inner.FindAsync(workflowExecutionId, durableValueId, cancellationToken);
    }

    public async ValueTask<RuntimeStorePage<DurableValueState>> ListPageAsync(
        DurableValueStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (sessionAccessor.Current is not { } session || !session.AppliesTo(query.WorkflowExecutionId))
            return await _inner.ListPageAsync(query, cancellationToken);

        return await CoalescingRuntimeStorePageMerger.MergeAsync(
            query,
            $"coalesced-durable-value:{query.WorkflowExecutionId}",
            (limit, continuation) => _inner.ListPageAsync(
                new DurableValueStatePageQuery(query.WorkflowExecutionId, limit, continuation), cancellationToken),
            state => state.DurableValueId,
            after => session.GetDurableValueUpserts()
                .Where(state => after is null || StringComparer.Ordinal.Compare(state.DurableValueId, after) > 0)
                .OrderBy(state => state.DurableValueId, StringComparer.Ordinal)
                .FirstOrDefault(),
            id => session.TryGetDurableValue(id, out _, out _));
    }
}

internal static class CoalescingRuntimeStorePageMerger
{
    public static async ValueTask<RuntimeStorePage<T>> MergeAsync<T>(
        RuntimeStorePageRequest request,
        string binding,
        Func<int, string?, ValueTask<RuntimeStorePage<T>>> readInnerPage,
        Func<T, string> identity,
        Func<string?, T?> nextOverlay,
        Func<string, bool> suppressesInner)
        where T : class
    {
        var cursor = CoalescingRuntimeStoreContinuation.Decode(request.ContinuationToken, binding, nameof(request));
        var lastIdentity = cursor?.LastIdentity;
        var inner = new InnerCursor(cursor?.InnerContinuation, cursor?.InnerExhausted ?? false);
        var items = new List<T>(request.Limit);

        while (items.Count < request.Limit)
        {
            var overlay = nextOverlay(lastIdentity);
            var candidate = await ReadNextInnerAsync(readInnerPage, identity, suppressesInner, lastIdentity, inner);
            inner = candidate?.Before ?? inner;
            if (overlay is null && candidate is null)
                break;

            if (candidate is null || overlay is not null && StringComparer.Ordinal.Compare(identity(overlay), identity(candidate.Item)) <= 0)
            {
                var overlayIdentity = identity(overlay!);
                if (candidate is not null && StringComparer.Ordinal.Equals(overlayIdentity, identity(candidate.Item)))
                    inner = candidate.After;
                items.Add(overlay!);
                lastIdentity = overlayIdentity;
                continue;
            }

            items.Add(candidate.Item);
            lastIdentity = identity(candidate.Item);
            inner = candidate.After;
        }

        var hasNext = false;
        if (items.Count > 0)
        {
            if (nextOverlay(lastIdentity) is not null)
                hasNext = true;
            else
            {
                var candidate = await ReadNextInnerAsync(readInnerPage, identity, suppressesInner, lastIdentity, inner);
                if (candidate is not null)
                {
                    hasNext = true;
                    inner = candidate.Before;
                }
            }
        }

        var next = hasNext
            ? CoalescingRuntimeStoreContinuation.Encode(binding, new(lastIdentity!, inner.Continuation, inner.Exhausted))
            : null;
        return new RuntimeStorePage<T>(request, items, next);
    }

    private static async ValueTask<InnerCandidate<T>?> ReadNextInnerAsync<T>(
        Func<int, string?, ValueTask<RuntimeStorePage<T>>> readInnerPage,
        Func<T, string> identity,
        Func<string, bool> suppressesInner,
        string? lastIdentity,
        InnerCursor current)
        where T : class
    {
        while (!current.Exhausted)
        {
            var page = await readInnerPage(1, current.Continuation);
            if (page.Items.Count == 0)
                return null;

            var item = page.Items[0];
            var after = new InnerCursor(page.NextContinuationToken, page.NextContinuationToken is null);
            var itemIdentity = identity(item);
            if (lastIdentity is not null && StringComparer.Ordinal.Compare(itemIdentity, lastIdentity) <= 0 || suppressesInner(itemIdentity))
            {
                current = after;
                continue;
            }

            return new InnerCandidate<T>(item, current, after);
        }

        return null;
    }

    private sealed record InnerCursor(string? Continuation, bool Exhausted);
    private sealed record InnerCandidate<T>(T Item, InnerCursor Before, InnerCursor After) where T : class;
}

/// <summary>
/// Coalescing-aware overlay for <see cref="IActivityExecutionInspectionStore"/>. A matching active session first
/// serves its ordered logical projection, which gives the next inspection build the same merge input regardless of
/// an equivalent Deferred or Immediate transport boundary. When no logical projection exists, <see cref="FindAsync"/>
/// memoizes the <b>durable baseline</b> per activity execution for the duration of a coalesced window. The memo is
/// invalidated at every durable flush; the single-writer ownership lease guarantees the durable rows cannot change
/// between flushes. Disabled (per-hop durable reads) via
/// <see cref="CoalescingRuntimeCheckpointPersistenceOptions.CoalesceInspectionReads"/>.
/// </summary>
public sealed class CoalescingActivityExecutionInspectionStore(
    CoalescingInner<IActivityExecutionInspectionStore> inner,
    IRuntimeCoalescingSessionAccessor sessionAccessor,
    CoalescingRuntimeCheckpointPersistenceOptions options) : IActivityExecutionInspectionStore, IInMemoryCheckpointTransactionSource
{
    private readonly IActivityExecutionInspectionStore _inner = inner.Value;
    public IEnumerable<object?> GetCheckpointTransactionParticipants() => [_inner];

    public async ValueTask<ActivityExecutionInspectionProjection?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        if (sessionAccessor.Current is not { } session || !session.AppliesTo(workflowExecutionId))
            return await _inner.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);

        if (session.TryGetInspectionProjection(activityExecutionId, out var logicalProjection))
        {
            // Keep the disabled mode's per-build durable-read observability without allowing a physical durable
            // boundary to replace the active session's logical composition input.
            if (!options.CoalesceInspectionReads)
                await _inner.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);

            return logicalProjection;
        }

        if (!options.CoalesceInspectionReads)
            return await _inner.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);

        if (session.TryGetInspectionBaseline(activityExecutionId, out var baseline))
            return baseline;

        var projection = await _inner.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);
        session.CacheInspectionBaseline(activityExecutionId, projection);
        return projection;
    }

    public ValueTask<ActivityExecutionInspectionSummaryPage> ListSummariesPageAsync(
        ActivityExecutionInspectionSummaryPageQuery query,
        CancellationToken cancellationToken = default) =>
        _inner.ListSummariesPageAsync(query, cancellationToken);
}

/// <summary>Coalescing-aware overlay for <see cref="ISchedulerStateStore"/>. See <see cref="CoalescingWorkflowExecutionStateStore"/>.</summary>
public sealed class CoalescingSchedulerStateStore(
    CoalescingInner<ISchedulerStateStore> inner,
    IRuntimeCoalescingSessionAccessor sessionAccessor) : ISchedulerStateStore, IInMemoryCheckpointTransactionSource
{
    private readonly ISchedulerStateStore _inner = inner.Value;
    public IEnumerable<object?> GetCheckpointTransactionParticipants() => [_inner];

    public ValueTask<SchedulerState> SaveAsync(SchedulerState state, CancellationToken cancellationToken = default) =>
        _inner.SaveAsync(state, cancellationToken);

    public async ValueTask<SchedulerState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        if (sessionAccessor.Current is { } session && session.AppliesTo(workflowExecutionId) &&
            session.TryGetScheduler(workflowExecutionId, out var overlay))
            return overlay;

        return await _inner.FindAsync(workflowExecutionId, cancellationToken);
    }

    public ValueTask<IReadOnlyCollection<SchedulerState>> ListAsync(CancellationToken cancellationToken = default) =>
        _inner.ListAsync(cancellationToken);
}
