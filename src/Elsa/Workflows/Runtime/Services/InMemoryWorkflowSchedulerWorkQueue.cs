using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryWorkflowSchedulerWorkQueue : IWorkflowSchedulerWorkQueue
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, Queue<RuntimeSchedulerWorkItem>> _queuesByWorkflowExecutionId = new(StringComparer.Ordinal);
    private readonly Dictionary<SchedulerWorkItemKey, RuntimeSchedulerWorkItem> _workItemsByScopedId = new();
    private readonly Dictionary<SchedulerWorkItemKey, ClaimState> _claimsByScopedId = new();

    public bool SupportsClaimTransitions => true;

    public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var scopedWorkItemKey = new SchedulerWorkItemKey(workItem.WorkflowExecutionId, workItem.WorkItemId);
            if (_workItemsByScopedId.TryGetValue(scopedWorkItemKey, out var existing))
                return new ValueTask<RuntimeSchedulerWorkItem>(existing);

            if (!_queuesByWorkflowExecutionId.TryGetValue(workItem.WorkflowExecutionId, out var queue))
            {
                queue = new Queue<RuntimeSchedulerWorkItem>();
                _queuesByWorkflowExecutionId.Add(workItem.WorkflowExecutionId, queue);
            }

            queue.Enqueue(workItem);
            _workItemsByScopedId.Add(scopedWorkItemKey, workItem);
            _claimsByScopedId.Add(scopedWorkItemKey, new ClaimState());

            return new ValueTask<RuntimeSchedulerWorkItem>(workItem);
        }
    }

    public ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!_queuesByWorkflowExecutionId.TryGetValue(query.WorkflowExecutionId, out var queue))
                return new ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkItem>>(Array.Empty<RuntimeSchedulerWorkItem>());

            var items = query.Limit is { } limit
                ? queue.Take(limit).ToArray()
                : queue.ToArray();

            return new ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkItem>>(items);
        }
    }

    public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!_queuesByWorkflowExecutionId.TryGetValue(workflowExecutionId, out var queue) || queue.Count == 0)
                return new ValueTask<RuntimeSchedulerWorkItem?>((RuntimeSchedulerWorkItem?)null);

            var workItem = queue.Dequeue();
            var key = new SchedulerWorkItemKey(workItem.WorkflowExecutionId, workItem.WorkItemId);
            _workItemsByScopedId.Remove(key);
            _claimsByScopedId.Remove(key);

            if (queue.Count == 0)
                _queuesByWorkflowExecutionId.Remove(workflowExecutionId);

            return new ValueTask<RuntimeSchedulerWorkItem?>(workItem);
        }
    }

    public ValueTask<bool> DeleteAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var key = new SchedulerWorkItemKey(workflowExecutionId, workItemId);
            if (!_workItemsByScopedId.Remove(key))
                return new ValueTask<bool>(false);
            _claimsByScopedId.Remove(key);
            if (!_queuesByWorkflowExecutionId.TryGetValue(workflowExecutionId, out var queue))
                return new ValueTask<bool>(false);

            var retained = queue.Where(item => !StringComparer.Ordinal.Equals(item.WorkItemId, workItemId)).ToArray();
            if (retained.Length == 0)
                _queuesByWorkflowExecutionId.Remove(workflowExecutionId);
            else
                _queuesByWorkflowExecutionId[workflowExecutionId] = new Queue<RuntimeSchedulerWorkItem>(retained);
            return new ValueTask<bool>(true);
        }
    }

    public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Pending workflow execution listing limit must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var executionIds = _queuesByWorkflowExecutionId.Keys
                .Order(StringComparer.Ordinal)
                .Take(limit)
                .ToArray();

            return new ValueTask<IReadOnlyCollection<string>>(executionIds);
        }
    }

    public ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(
        RuntimeSchedulerWorkClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!_queuesByWorkflowExecutionId.TryGetValue(request.WorkflowExecutionId, out var queue) || queue.Count == 0)
                return new ValueTask<RuntimeSchedulerWorkClaim?>((RuntimeSchedulerWorkClaim?)null);

            var item = queue.Peek();
            var key = new SchedulerWorkItemKey(item.WorkflowExecutionId, item.WorkItemId);
            var state = _claimsByScopedId[key];
            if (state.VisibleAfter is { } visibleAfter && visibleAfter > request.Now)
                return new ValueTask<RuntimeSchedulerWorkClaim?>((RuntimeSchedulerWorkClaim?)null);

            state.OwnerId = request.OwnerId;
            state.FencingToken = checked(state.FencingToken + 1);
            state.Revision = checked(state.Revision + 1);
            state.ClaimedAt = request.Now;
            state.VisibleAfter = request.Now.Add(request.VisibilityTimeout);

            return new ValueTask<RuntimeSchedulerWorkClaim?>(
                NewClaim(item, state));
        }
    }

    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "Scheduler work visibility timeout must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!TryGetCurrentState(claim, out var state))
                return new ValueTask<RuntimeSchedulerWorkClaimTransitionResult>(RuntimeSchedulerWorkClaimTransitionResult.Stale);

            state.Revision = checked(state.Revision + 1);
            state.VisibleAfter = now.Add(visibilityTimeout);
            return new ValueTask<RuntimeSchedulerWorkClaimTransitionResult>(
                RuntimeSchedulerWorkClaimTransitionResult.Applied(NewClaim(claim.Item, state)));
        }
    }

    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var key = KeyOf(claim);
            if (!_workItemsByScopedId.ContainsKey(key))
                return new ValueTask<RuntimeSchedulerWorkClaimTransitionResult>(RuntimeSchedulerWorkClaimTransitionResult.AlreadyApplied);
            if (!TryGetCurrentState(claim, out _))
                return new ValueTask<RuntimeSchedulerWorkClaimTransitionResult>(RuntimeSchedulerWorkClaimTransitionResult.Stale);
            if (!_queuesByWorkflowExecutionId.TryGetValue(claim.Item.WorkflowExecutionId, out var queue) ||
                queue.Count == 0 ||
                !StringComparer.Ordinal.Equals(queue.Peek().WorkItemId, claim.Item.WorkItemId))
            {
                return new ValueTask<RuntimeSchedulerWorkClaimTransitionResult>(RuntimeSchedulerWorkClaimTransitionResult.Stale);
            }

            queue.Dequeue();
            _workItemsByScopedId.Remove(key);
            _claimsByScopedId.Remove(key);
            if (queue.Count == 0)
                _queuesByWorkflowExecutionId.Remove(claim.Item.WorkflowExecutionId);

            return new ValueTask<RuntimeSchedulerWorkClaimTransitionResult>(
                RuntimeSchedulerWorkClaimTransitionResult.Applied());
        }
    }

    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        DateTimeOffset visibleAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var key = KeyOf(claim);
            if (!_workItemsByScopedId.ContainsKey(key))
                return new ValueTask<RuntimeSchedulerWorkClaimTransitionResult>(RuntimeSchedulerWorkClaimTransitionResult.AlreadyApplied);
            if (!TryGetCurrentState(claim, out var state))
                return new ValueTask<RuntimeSchedulerWorkClaimTransitionResult>(RuntimeSchedulerWorkClaimTransitionResult.Stale);

            state.OwnerId = null;
            state.ClaimedAt = null;
            state.VisibleAfter = visibleAt;
            state.Revision = checked(state.Revision + 1);
            return new ValueTask<RuntimeSchedulerWorkClaimTransitionResult>(
                RuntimeSchedulerWorkClaimTransitionResult.Applied());
        }
    }

    private bool TryGetCurrentState(RuntimeSchedulerWorkClaim claim, out ClaimState state)
    {
        if (_claimsByScopedId.TryGetValue(KeyOf(claim), out state!) &&
            state.Revision == claim.Revision &&
            state.FencingToken == claim.FencingToken &&
            StringComparer.Ordinal.Equals(state.OwnerId, claim.OwnerId))
        {
            return true;
        }

        state = null!;
        return false;
    }

    private static RuntimeSchedulerWorkClaim NewClaim(RuntimeSchedulerWorkItem item, ClaimState state) =>
        new(
            item,
            state.OwnerId!,
            state.FencingToken,
            state.Revision,
            state.ClaimedAt!.Value,
            state.VisibleAfter!.Value);

    private static SchedulerWorkItemKey KeyOf(RuntimeSchedulerWorkClaim claim) =>
        new(claim.Item.WorkflowExecutionId, claim.Item.WorkItemId);

    private readonly record struct SchedulerWorkItemKey(string WorkflowExecutionId, string WorkItemId);

    private sealed class ClaimState
    {
        public string? OwnerId { get; set; }
        public long FencingToken { get; set; }
        public long Revision { get; set; } = 1;
        public DateTimeOffset? ClaimedAt { get; set; }
        public DateTimeOffset? VisibleAfter { get; set; }
    }
}
