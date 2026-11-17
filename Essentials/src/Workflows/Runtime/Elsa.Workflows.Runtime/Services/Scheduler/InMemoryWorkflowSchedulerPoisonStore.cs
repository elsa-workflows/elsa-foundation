using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Workflows.Runtime.Services.Scheduler;

/// <summary>
/// Single-node in-memory default <see cref="IWorkflowSchedulerPoisonStore"/>. Durable poison stores (e.g. a
/// durable implementation) are a follow-up, not part of this slice.
/// </summary>
/// <remarks>
/// It can record under the run's ownership fence (<see cref="ISchedulerPoisonFenceCapability"/>) only when it is
/// built with the in-memory ownership store, the one store whose lease it can check atomically. The runtime
/// composition root gives it that store when it is the registered <see cref="IExecutionLivenessStateStore"/> and is
/// registered as a singleton. Built without it, <see cref="CanRecordFenced"/> is <see langword="false"/>.
/// </remarks>
[RuntimeDefaultRegistration]
public sealed class InMemoryWorkflowSchedulerPoisonStore : IWorkflowSchedulerPoisonStore, ISchedulerPoisonFenceCapability
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<PoisonRecordKey, RuntimeSchedulerPoisonRecord> _records = new();
    private readonly Lazy<InMemoryExecutionLivenessStateStore?> _ownershipStateStore;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a store that cannot check ownership, so <see cref="CanRecordFenced"/> is <see langword="false"/>.</summary>
    public InMemoryWorkflowSchedulerPoisonStore()
        : this(static () => null, null)
    {
    }

    /// <summary>
    /// Creates a store that checks a fenced record against <paramref name="ownershipStateStore"/>. Pass the store the
    /// runtime's ownership service writes to: any other instance holds no lease, so every fenced record is refused.
    /// </summary>
    public InMemoryWorkflowSchedulerPoisonStore(InMemoryExecutionLivenessStateStore ownershipStateStore, TimeProvider? timeProvider = null)
        : this(() => ownershipStateStore, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(ownershipStateStore);
    }

    /// <summary>
    /// For the runtime composition root, which cannot name the registered ownership store while it builds this
    /// singleton. <paramref name="ownershipStateStore"/> is called the first time <see cref="CanRecordFenced"/> is
    /// read or a fenced record is asked for, and its answer is kept. It returns <see langword="null"/> when the
    /// registered store is not the in-memory one. A call that throws is not kept: the next read calls it again.
    /// </summary>
    internal InMemoryWorkflowSchedulerPoisonStore(Func<InMemoryExecutionLivenessStateStore?> ownershipStateStore, TimeProvider? timeProvider)
    {
        // PublicationOnly: a lookup that throws is tried again by the next caller instead of being remembered.
        _ownershipStateStore = new Lazy<InMemoryExecutionLivenessStateStore?>(ownershipStateStore, LazyThreadSafetyMode.PublicationOnly);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public bool CanRecordFenced => _ownershipStateStore.Value is not null;

    internal void DeleteForWorkflowExecution(string workflowExecutionId)
    {
        lock (_syncRoot)
        {
            foreach (var key in _records.Keys
                         .Where(key => StringComparer.Ordinal.Equals(key.WorkflowExecutionId, workflowExecutionId))
                         .ToArray())
                _records.Remove(key);
        }
    }

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

    /// <inheritdoc />
    public async ValueTask<RuntimeSchedulerPoisonRecord> RecordFencedAsync(
        RuntimeSchedulerPoisonRecord record,
        RuntimeExecutionFence fence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(fence);
        cancellationToken.ThrowIfCancellationRequested();
        if (_ownershipStateStore.Value is not { } ownershipStateStore)
            throw new NotSupportedException("This scheduler-poison store was built without the in-memory ownership store, so it cannot check an execution fence.");

        // The ownership store runs every lease acquisition, renewal and release under this gate, so no other owner
        // can take the run between the check and the write.
        return await ownershipStateStore.ExecuteOwnershipAtomicAsync(async token =>
        {
            var ownership = await ownershipStateStore.FindAsync(
                record.WorkflowExecutionId,
                RuntimeExecutionOwnershipStateId.For(record.WorkflowExecutionId),
                token);
            RuntimeExecutionFenceValidator.EnsureCurrent(record.WorkflowExecutionId, fence, ownership, _timeProvider.GetUtcNow());
            return await RecordAsync(record, token);
        }, cancellationToken);
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
