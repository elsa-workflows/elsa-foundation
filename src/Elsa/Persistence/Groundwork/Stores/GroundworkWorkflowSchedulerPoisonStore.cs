using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Durable <see cref="IWorkflowSchedulerPoisonStore"/> for the Groundwork bridge. Each crashed scheduler work item
/// is stored as one scoped document keyed by <c>(WorkflowExecutionId, WorkItemId)</c>, so poison records survive
/// process restart and remain queryable by workflow execution.
/// </summary>
public sealed class GroundworkWorkflowSchedulerPoisonStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.SchedulerPoisonDocumentKind, boundedStore), IWorkflowSchedulerPoisonStore
{
    private const int MaxRecordAttempts = 16;

    public async ValueTask<RuntimeSchedulerPoisonRecord> RecordAsync(RuntimeSchedulerPoisonRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        var documentId = DocumentIdentity(record.WorkflowExecutionId, record.WorkItemId);
        for (var attempt = 0; attempt < MaxRecordAttempts; attempt++)
        {
            var existing = await Store.LoadAsync(DocumentKind, documentId, cancellationToken);
            if (existing is not null)
                EnsureLogicalIdentity(Serializer.Deserialize<SchedulerPoisonEnvelope>(existing).Record, record.WorkflowExecutionId, record.WorkItemId);

            var document = new SchedulerPoisonEnvelope(
                ElsaRuntimeStorageManifest.SchedulerPoisonDocumentKind,
                record.WorkflowExecutionId,
                record);
            var result = await SaveDocumentAsync(documentId, document, cancellationToken, existing?.Version ?? 0);
            if (result.Status == DocumentStoreWriteStatus.Saved)
                return record;

            if (result.Status is DocumentStoreWriteStatus.ConcurrencyConflict or DocumentStoreWriteStatus.NotFound)
                continue;

            throw new InvalidOperationException($"Groundwork rejected scheduler poison record '{record.WorkItemId}' with status '{result.Status}'.");
        }

        throw new InvalidOperationException(
            $"Recording scheduler poison record '{record.WorkItemId}' in workflow execution '{record.WorkflowExecutionId}' did not settle after {MaxRecordAttempts} compare-and-swap attempts.");
    }

    public async ValueTask<RuntimeSchedulerPoisonRecord?> FindAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        cancellationToken.ThrowIfCancellationRequested();

        var record = await LoadDocumentAsync<SchedulerPoisonEnvelope, RuntimeSchedulerPoisonRecord>(
            DocumentIdentity(workflowExecutionId, workItemId),
            envelope => envelope.Record,
            cancellationToken);
        if (record is not null)
            EnsureLogicalIdentity(record, workflowExecutionId, workItemId);

        return record;
    }

    public async ValueTask<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        var records = await QueryDocumentsAsync<SchedulerPoisonEnvelope, RuntimeSchedulerPoisonRecord>(
            ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery,
            ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
            workflowExecutionId,
            envelope => envelope.Record,
            cancellationToken);

        return records
            .OrderBy(record => record.FirstFailedAt)
            .ThenBy(record => record.LastFailedAt)
            .ThenBy(record => record.WorkItemId, StringComparer.Ordinal)
            .ToArray();
    }

    private static string DocumentIdentity(string workflowExecutionId, string workItemId) =>
        GroundworkPhysicalDocumentId.FromLogicalId(GroundworkCompositeDocumentId.From(workflowExecutionId, workItemId));

    private static void EnsureLogicalIdentity(RuntimeSchedulerPoisonRecord record, string workflowExecutionId, string workItemId)
    {
        if (!StringComparer.Ordinal.Equals(record.WorkflowExecutionId, workflowExecutionId)
            || !StringComparer.Ordinal.Equals(record.WorkItemId, workItemId))
        {
            throw new InvalidOperationException(
                $"Groundwork physical document identity collision detected for scheduler poison record '{workItemId}' in workflow execution '{workflowExecutionId}'.");
        }
    }

    private sealed record SchedulerPoisonEnvelope(
        string Collection,
        string WorkflowExecutionId,
        RuntimeSchedulerPoisonRecord Record);
}
