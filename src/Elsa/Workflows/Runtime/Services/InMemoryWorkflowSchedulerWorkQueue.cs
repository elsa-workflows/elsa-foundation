using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryWorkflowSchedulerWorkQueue : IWorkflowSchedulerWorkQueue
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, Queue<RuntimeSchedulerWorkItem>> _queuesByWorkflowExecutionId = new(StringComparer.Ordinal);
    private readonly Dictionary<SchedulerWorkItemKey, RuntimeSchedulerWorkItem> _workItemsByScopedId = new();

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
            _workItemsByScopedId.Remove(new SchedulerWorkItemKey(workItem.WorkflowExecutionId, workItem.WorkItemId));

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

    private readonly record struct SchedulerWorkItemKey(string WorkflowExecutionId, string WorkItemId);
}
