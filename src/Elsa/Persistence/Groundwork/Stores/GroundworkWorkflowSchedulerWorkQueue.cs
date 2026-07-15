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
public sealed class GroundworkWorkflowSchedulerWorkQueue(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind, boundedStore), IWorkflowSchedulerWorkQueue
{
    public async ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var documentId = PhysicalDocumentId(workItem.WorkflowExecutionId, workItem.WorkItemId);
        var existing = await LoadDocumentAsync<WorkQueueEnvelope, RuntimeSchedulerWorkItem>(
            documentId, envelope => envelope.Item, cancellationToken);
        if (existing is not null)
        {
            EnsureLogicalIdentity(existing, workItem.WorkflowExecutionId, workItem.WorkItemId);
            return existing;
        }

        var document = new WorkQueueEnvelope(
            ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
            workItem.WorkflowExecutionId,
            workItem);
        var result = await SaveDocumentAsync(documentId, document, cancellationToken, expectedVersion: 0);
        if (result.Status == DocumentStoreWriteStatus.Saved)
            return workItem;
        if (result.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
            throw new InvalidOperationException($"Groundwork rejected scheduler work item '{workItem.WorkItemId}' with status '{result.Status}'.");

        existing = await LoadDocumentAsync<WorkQueueEnvelope, RuntimeSchedulerWorkItem>(
            documentId, envelope => envelope.Item, cancellationToken)
            ?? throw new InvalidOperationException($"Scheduler work item '{workItem.WorkItemId}' conflicted during creation but could not be reloaded.");
        EnsureLogicalIdentity(existing, workItem.WorkflowExecutionId, workItem.WorkItemId);

        return existing;
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

        await DeleteDocumentAsync(PhysicalDocumentId(workItem.WorkflowExecutionId, workItem.WorkItemId), cancellationToken);

        return workItem;
    }

    public async ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Pending workflow execution listing limit must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();

        var items = await QueryDocumentsAsync<WorkQueueEnvelope, RuntimeSchedulerWorkItem>(
            ElsaRuntimeStorageManifest.ListAllQuery,
            ElsaRuntimeStorageManifest.CollectionField,
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
            ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery,
            ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
            workflowExecutionId,
            envelope => envelope.Item,
            cancellationToken);

        return items
            .OrderBy(item => item.RecordedAt)
            .ThenBy(item => item.Sequence ?? long.MaxValue)
            .ThenBy(item => item.WorkItemId, StringComparer.Ordinal)
            .ToArray();
    }

    private static string PhysicalDocumentId(string workflowExecutionId, string workItemId) =>
        GroundworkPhysicalDocumentId.FromLogicalId(GroundworkCompositeDocumentId.From(workflowExecutionId, workItemId));

    private static void EnsureLogicalIdentity(RuntimeSchedulerWorkItem item, string workflowExecutionId, string workItemId)
    {
        if (!StringComparer.Ordinal.Equals(item.WorkflowExecutionId, workflowExecutionId)
            || !StringComparer.Ordinal.Equals(item.WorkItemId, workItemId))
        {
            throw new InvalidOperationException(
                $"Groundwork physical document identity collision detected for scheduler work item '{workItemId}' in workflow execution '{workflowExecutionId}'.");
        }
    }

    // The constant collection partition lets the system-wide pending-executions sweep use a keyword
    // equality index instead of a provider-wide scan, mirroring the other list-capable bridges.
    private sealed record WorkQueueEnvelope(string Collection, string WorkflowExecutionId, RuntimeSchedulerWorkItem Item);
}
