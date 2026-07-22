using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using System.Security.Cryptography;
using System.Text;

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

    public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!_queuesByWorkflowExecutionId.TryGetValue(query.WorkflowExecutionId, out var queue))
                return ValueTask.FromResult(
                    new RuntimeStorePage<RuntimeSchedulerWorkItem>(query, []));

            var queryBinding = $"scheduler-work:workflow:{query.WorkflowExecutionId}";
            var lastOrderKey = InMemoryRuntimeStoreContinuation.Decode(
                query.ContinuationToken,
                queryBinding,
                nameof(query));
            var remaining = queue
                .Select(item => (Item: item, OrderKey: OrderKey(item)))
                .Where(entry =>
                    lastOrderKey is null ||
                    StringComparer.Ordinal.Compare(entry.OrderKey, lastOrderKey) > 0)
                .OrderBy(entry => entry.OrderKey, StringComparer.Ordinal)
                .Take(query.Limit + 1)
                .ToArray();
            var page = remaining.Take(query.Limit).ToArray();
            var nextContinuation = remaining.Length > page.Length
                ? InMemoryRuntimeStoreContinuation.Encode(queryBinding, page[^1].OrderKey)
                : null;

            return ValueTask.FromResult(
                new RuntimeStorePage<RuntimeSchedulerWorkItem>(
                    query,
                    page.Select(entry => entry.Item).ToArray(),
                    nextContinuation));
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
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
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

    // Keyset continuation order key: mirrors the queue ordering (RecordedAt, Sequence, hashed WorkItemId)
    // as one ordinal-sortable string, so a continuation resumes strictly after the last returned row even
    // when the queue mutates between pages; the hashed-id segment makes the key unique per item, so no
    // separate tie-break is needed.
    private static string OrderKey(RuntimeSchedulerWorkItem item) =>
        $"{item.RecordedAt.UtcTicks:D19}.{item.Sequence ?? long.MaxValue:D20}.{StableHash(item.WorkItemId)}";

    private static string StableHash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// Returns the FIFO-earliest queued item whose id is not in <paramref name="skipWorkItemIds"/>, without claiming
    /// it — true insertion order, the same order <see cref="ClaimAsync"/> serves the head. Not part of
    /// <see cref="IWorkflowSchedulerWorkQueue"/>: it exists for the coalescing session's single-writer inline fused
    /// pump (spec 123 D2), which reads past the drain loop's live head claim and removes dispatched items with
    /// <see cref="DeleteAsync"/>. The strict-FIFO claim contract for concurrent drainers is unaffected.
    /// </summary>
    public ValueTask<RuntimeSchedulerWorkItem?> PeekFirstAvailableAsync(
        string workflowExecutionId,
        IReadOnlySet<string> skipWorkItemIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(skipWorkItemIds);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!_queuesByWorkflowExecutionId.TryGetValue(workflowExecutionId, out var queue))
                return new ValueTask<RuntimeSchedulerWorkItem?>((RuntimeSchedulerWorkItem?)null);

            foreach (var item in queue)
            {
                if (!skipWorkItemIds.Contains(item.WorkItemId))
                    return new ValueTask<RuntimeSchedulerWorkItem?>(item);
            }

            return new ValueTask<RuntimeSchedulerWorkItem?>((RuntimeSchedulerWorkItem?)null);
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

    public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ConsumeClaimedAsync(
        ConsumedSchedulerWorkItem consumed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consumed);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var key = new SchedulerWorkItemKey(consumed.WorkflowExecutionId, consumed.WorkItemId);
            // Fence on owner + fencing token only (renewal-stable): a renewal advances the revision but keeps these, so a
            // renewed claim still consumes its item, while a successor reclaim (token advanced) or a completed/absent item
            // is claim-lost.
            if (!_claimsByScopedId.TryGetValue(key, out var state) ||
                state.OwnerId is null ||
                !StringComparer.Ordinal.Equals(state.OwnerId, consumed.ClaimOwnerId) ||
                state.FencingToken != consumed.FencingToken)
            {
                return new ValueTask<RuntimeSchedulerWorkClaimTransitionResult>(RuntimeSchedulerWorkClaimTransitionResult.Stale);
            }

            _workItemsByScopedId.Remove(key);
            _claimsByScopedId.Remove(key);
            if (_queuesByWorkflowExecutionId.TryGetValue(consumed.WorkflowExecutionId, out var queue))
            {
                var retained = queue.Where(item => !StringComparer.Ordinal.Equals(item.WorkItemId, consumed.WorkItemId)).ToArray();
                if (retained.Length == 0)
                    _queuesByWorkflowExecutionId.Remove(consumed.WorkflowExecutionId);
                else
                    _queuesByWorkflowExecutionId[consumed.WorkflowExecutionId] = new Queue<RuntimeSchedulerWorkItem>(retained);
            }

            return new ValueTask<RuntimeSchedulerWorkClaimTransitionResult>(RuntimeSchedulerWorkClaimTransitionResult.Applied());
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
