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
    IRuntimeCoalescingSessionAccessor sessionAccessor) : IWorkflowExecutionStateStore
{
    private readonly IWorkflowExecutionStateStore _inner = inner.Value;

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
}

/// <summary>Coalescing-aware overlay for <see cref="IActivityExecutionStateStore"/>. See <see cref="CoalescingWorkflowExecutionStateStore"/>.</summary>
public sealed class CoalescingActivityExecutionStateStore(
    CoalescingInner<IActivityExecutionStateStore> inner,
    IRuntimeCoalescingSessionAccessor sessionAccessor) : IActivityExecutionStateStore
{
    private readonly IActivityExecutionStateStore _inner = inner.Value;

    public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default) =>
        _inner.SaveAsync(state, cancellationToken);

    public async ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        if (sessionAccessor.Current is { } session && session.AppliesTo(workflowExecutionId) &&
            session.TryGetActivity(activityExecutionId, out var overlay, out _))
            return overlay;

        return await _inner.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<ActivityExecutionState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        var innerList = await _inner.ListAsync(workflowExecutionId, cancellationToken);

        if (sessionAccessor.Current is { } session && session.AppliesTo(workflowExecutionId))
            return session.MergeActivityList(innerList);

        return innerList;
    }
}

/// <summary>Coalescing-aware overlay for <see cref="IDurableValueStateStore"/>. See <see cref="CoalescingWorkflowExecutionStateStore"/>.</summary>
public sealed class CoalescingDurableValueStateStore(
    CoalescingInner<IDurableValueStateStore> inner,
    IRuntimeCoalescingSessionAccessor sessionAccessor) : IDurableValueStateStore
{
    private readonly IDurableValueStateStore _inner = inner.Value;

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

    public async ValueTask<IReadOnlyCollection<DurableValueState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        var innerList = await _inner.ListAsync(workflowExecutionId, cancellationToken);

        if (sessionAccessor.Current is { } session && session.AppliesTo(workflowExecutionId))
            return session.MergeDurableValueList(innerList);

        return innerList;
    }
}

/// <summary>Coalescing-aware overlay for <see cref="ISchedulerStateStore"/>. See <see cref="CoalescingWorkflowExecutionStateStore"/>.</summary>
public sealed class CoalescingSchedulerStateStore(
    CoalescingInner<ISchedulerStateStore> inner,
    IRuntimeCoalescingSessionAccessor sessionAccessor) : ISchedulerStateStore
{
    private readonly ISchedulerStateStore _inner = inner.Value;

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
