using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Single-node in-memory default <see cref="IWorkflowSchedulerPoisonStore"/>. Durable poison stores (e.g. a
/// Groundwork-backed implementation) are a follow-up, not part of this slice.
/// </summary>
public sealed class InMemoryWorkflowSchedulerPoisonStore : IWorkflowSchedulerPoisonStore
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<PoisonRecordKey, RuntimeSchedulerPoisonRecord> _records = new();

    public ValueTask<RuntimeSchedulerPoisonRecord> RecordAsync(RuntimeSchedulerPoisonRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var key = new PoisonRecordKey(record.WorkflowExecutionId, record.WorkItemId);
            _records[key] = record;
            return new ValueTask<RuntimeSchedulerPoisonRecord>(record);
        }
    }

    public ValueTask<RuntimeSchedulerPoisonRecord?> FindAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _records.TryGetValue(new PoisonRecordKey(workflowExecutionId, workItemId), out var record);
            return new ValueTask<RuntimeSchedulerPoisonRecord?>(record);
        }
    }

    public ValueTask<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var records = _records
                .Where(item => item.Key.WorkflowExecutionId == workflowExecutionId)
                .Select(item => item.Value)
                .ToArray();

            return new ValueTask<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>>(records);
        }
    }

    private readonly record struct PoisonRecordKey(string WorkflowExecutionId, string WorkItemId);
}
