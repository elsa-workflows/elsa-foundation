using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using System.Text.Json;

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
public sealed class GroundworkWorkflowSchedulerWorkQueue(IDocumentStore store) : IWorkflowSchedulerWorkQueue
{
    public async ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var documentId = BuildId(workItem.WorkflowExecutionId, workItem.WorkItemId);
        var existing = await store.LoadAsync(
            ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
            documentId,
            cancellationToken);
        if (existing is not null)
            return Map(existing);

        var envelope = new WorkQueueEnvelope(
            ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
            workItem.WorkflowExecutionId,
            workItem);
        var content = JsonSerializer.Serialize(envelope, GroundworkRuntimeJson.Options);
        await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
                documentId,
                ElsaRuntimeStorageManifest.SchemaVersion,
                content),
            cancellationToken);

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

        await store.DeleteAsync(
            new DeleteDocumentRequest(
                ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
                BuildId(workItem.WorkflowExecutionId, workItem.WorkItemId)),
            cancellationToken);

        return workItem;
    }

    public async ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Pending workflow execution listing limit must be greater than zero.");
        cancellationToken.ThrowIfCancellationRequested();

        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
                ElsaRuntimeStorageManifest.ByCollectionIndex,
                ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind),
            cancellationToken);

        return envelopes
            .Select(envelope => Map(envelope).WorkflowExecutionId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    private async ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkItem>> ListOrderedAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind,
                ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex,
                workflowExecutionId),
            cancellationToken);

        return envelopes
            .Select(Map)
            .OrderBy(item => item.RecordedAt)
            .ThenBy(item => item.Sequence ?? long.MaxValue)
            .ThenBy(item => item.WorkItemId, StringComparer.Ordinal)
            .ToArray();
    }

    private static RuntimeSchedulerWorkItem Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<WorkQueueEnvelope>(envelope.ContentJson, GroundworkRuntimeJson.Options)!.Item;

    // Deterministic, collision-free composite document id. Parts are escaped so a separator inside
    // an id cannot forge a different (workflowExecutionId, workItemId) pair.
    private static string BuildId(string workflowExecutionId, string workItemId) =>
        $"{Escape(workflowExecutionId)}:{Escape(workItemId)}";

    private static string Escape(string value) => value.Replace("%", "%25").Replace(":", "%3A");

    // The constant collection partition lets the system-wide pending-executions sweep use a keyword
    // equality index instead of a provider-wide scan, mirroring the other list-capable bridges.
    private sealed record WorkQueueEnvelope(string Collection, string WorkflowExecutionId, RuntimeSchedulerWorkItem Item);
}
