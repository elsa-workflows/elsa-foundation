using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Durable <see cref="IWorkflowSchedulerWorkQueue"/> for the Groundwork bridge, backed by the portable
/// <see cref="IDocumentStore"/>. Each queued work item is one document, so queued work survives process
/// restarts — closing the gap where a post-commit outbox item was delivered into a process-local queue
/// and lost on crash.
/// </summary>
/// <remarks>
/// <para>
/// Semantics mirror the in-memory queue: enqueue is idempotent by <c>(WorkflowExecutionId, WorkItemId)</c>
/// (an existing item wins and is returned), queues are isolated per workflow execution, and dequeue removes
/// the item. Ordering is deterministic FIFO by <c>(RecordedAt, Sequence, WorkItemId)</c> — the in-memory
/// queue's insertion order cannot be observed across restarts, and within one workflow execution producers
/// are serialized by the agent mailbox, so recorded time (with sequence and ID tiebreakers) reproduces it.
/// </para>
/// <para>
/// Dequeue reads the head document and then deletes it; a crash between those steps redelivers the item on
/// the next drain. That is consistent with the queue's at-least-once delivery contract
/// (<see cref="WorkflowExecutionCommandDeliveryMode.AtLeastOnce"/>) — consumers dedupe by idempotency key.
/// </para>
/// </remarks>
public sealed class GroundworkWorkflowSchedulerWorkQueue(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind), IWorkflowSchedulerWorkQueue
{
    public async ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var documentId = GroundworkCompositeDocumentId.From(workItem.WorkflowExecutionId, workItem.WorkItemId);
        var existing = await LoadDocumentAsync<WorkQueueEnvelope, RuntimeSchedulerWorkItem>(
            documentId, envelope => envelope.Item, cancellationToken);
        if (existing is not null)
            return existing;

        var document = new WorkQueueEnvelope(
            ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
            workItem.WorkflowExecutionId,
            workItem.ExecutionScopeId,
            workItem.Attempt,
            workItem);
        await SaveDocumentAsync(documentId, document, cancellationToken);

        return workItem;
    }

    public async ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var items = await ListOrderedAsync(query.WorkflowExecutionId, cancellationToken);

        return query.Limit is { } limit
            ? items.Take(limit).ToArray()
            : items;
    }

    public async ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        var items = await ListOrderedAsync(workflowExecutionId, cancellationToken);
        var workItem = items.FirstOrDefault();
        if (workItem is null)
            return null;

        await DeleteDocumentAsync(GroundworkCompositeDocumentId.From(workItem.WorkflowExecutionId, workItem.WorkItemId), cancellationToken);

        return workItem;
    }

    public async ValueTask<bool> DeleteAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        cancellationToken.ThrowIfCancellationRequested();
        var existing = await LoadDocumentAsync<WorkQueueEnvelope, RuntimeSchedulerWorkItem>(
            GroundworkCompositeDocumentId.From(workflowExecutionId, workItemId), envelope => envelope.Item, cancellationToken);
        if (existing is null)
            return false;
        await DeleteDocumentAsync(GroundworkCompositeDocumentId.From(workflowExecutionId, workItemId), cancellationToken);
        return true;
    }

    public async ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Pending workflow execution listing limit must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();

        var items = await QueryDocumentsAsync<WorkQueueEnvelope, RuntimeSchedulerWorkItem>(
            ElsaRuntimeStorageManifest.ByCollectionIndex,
            ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
            envelope => envelope.Item,
            cancellationToken);

        return items
            .Select(item => item.WorkflowExecutionId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    private async ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkItem>> ListOrderedAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        var items = await QueryDocumentsAsync<WorkQueueEnvelope, RuntimeSchedulerWorkItem>(
            ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, workflowExecutionId, envelope => envelope.Item, cancellationToken);

        return items
            .OrderBy(item => item.RecordedAt)
            .ThenBy(item => item.Sequence ?? long.MaxValue)
            .ThenBy(item => item.WorkItemId, StringComparer.Ordinal)
            .ToArray();
    }

    // The constant collection partition lets the system-wide pending-executions sweep use a keyword
    // equality index instead of a provider-wide scan, mirroring the other list-capable bridges.
    private sealed record WorkQueueEnvelope(
        string Collection,
        string WorkflowExecutionId,
        string? ExecutionScopeId,
        ActivityExecutionAttemptLineage? Attempt,
        RuntimeSchedulerWorkItem Item);
}
