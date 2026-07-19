using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

public sealed class GroundworkActivityExecutionHierarchyStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IActivityExecutionHierarchyCursorCodec? cursorCodec = null,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind, boundedStore),
        IActivityExecutionHierarchyStore
{
    public async ValueTask SaveAsync(ActivityExecutionHierarchyRecord record, CancellationToken cancellationToken = default)
    {
        Validate(record);
        var documentId = DocumentId.Compose(record.WorkflowExecutionId, record.ActivityExecutionId);
        var existing = await LoadByLogicalIdentityAsync(record.WorkflowExecutionId, record.ActivityExecutionId, cancellationToken);
        var result = await SaveDocumentAsync(
            documentId,
            new HierarchyDocument(record.WorkflowExecutionId, record.ExecutionScopeId, record.ActivityExecutionId, record.ExecutionSequence, record),
            cancellationToken,
            expectedVersion: existing?.Version ?? 0);
        if (result.Status != DocumentStoreWriteStatus.Saved)
        {
            if (result.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
                await LoadByLogicalIdentityAsync(record.WorkflowExecutionId, record.ActivityExecutionId, cancellationToken);
            throw new InvalidOperationException($"Groundwork rejected activity execution hierarchy record '{record.ActivityExecutionId}' in workflow execution '{record.WorkflowExecutionId}' with status '{result.Status}'.");
        }
    }

    public async ValueTask<ActivityExecutionHierarchyPage?> ReadPageAsync(ActivityExecutionHierarchyQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var records = await ListWorkflowAsync(query.WorkflowExecutionId, cancellationToken);
        return ActivityExecutionHierarchyPager.Read(query, records, cursorCodec ?? throw new InvalidOperationException("A hierarchy cursor codec is required for hierarchy reads."));
    }

    public async ValueTask<ActivityExecutionBoundary?> FindBoundaryAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        var records = await ListWorkflowAsync(workflowExecutionId, cancellationToken);
        return ActivityExecutionHierarchyProjector.FindBoundary(records, activityExecutionId);
    }

    public async ValueTask<ActivityExecutionAttemptNavigation?> FindAttemptNavigationAsync(
        string workflowExecutionId,
        string activityExecutionId,
        CancellationToken cancellationToken = default)
    {
        var records = await ListWorkflowAsync(workflowExecutionId, cancellationToken);
        return ActivityExecutionHierarchyProjector.FindAttemptNavigation(records, activityExecutionId);
    }

    public ValueTask<ActivityExecutionLayout?> FindLayoutAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ActivityExecutionLayout?>(null);

    private async ValueTask<IReadOnlyCollection<ActivityExecutionHierarchyRecord>> ListWorkflowAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        return await QueryDocumentsAsync<HierarchyDocument, ActivityExecutionHierarchyRecord>(
            ElsaRuntimeStorageManifest.ListActivityExecutionHierarchyByWorkflowExecutionQuery,
            ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
            workflowExecutionId,
            document => document.Record,
            cancellationToken);
    }

    private async ValueTask<LoadedActivityExecutionHierarchyRecord?> LoadByLogicalIdentityAsync(
        string workflowExecutionId,
        string activityExecutionId,
        CancellationToken cancellationToken)
    {
        var envelope = await Store.LoadAsync(DocumentKind, DocumentId.Compose(workflowExecutionId, activityExecutionId), cancellationToken);
        if (envelope is null)
            return null;

        var record = Serializer.Deserialize<HierarchyDocument>(envelope).Record;
        if (!StringComparer.Ordinal.Equals(record.WorkflowExecutionId, workflowExecutionId)
            || !StringComparer.Ordinal.Equals(record.ActivityExecutionId, activityExecutionId))
        {
            throw new InvalidOperationException(
                $"Groundwork physical document identity collision detected for activity execution hierarchy record '{activityExecutionId}' in workflow execution '{workflowExecutionId}'.");
        }

        return new LoadedActivityExecutionHierarchyRecord(record, envelope.Version);
    }

    private static void Validate(ActivityExecutionHierarchyRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!StringComparer.Ordinal.Equals(record.WorkflowExecutionId, record.Item.WorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(record.ActivityExecutionId, record.Item.ActivityExecutionId) ||
            record.ExecutionSequence != record.Item.ExecutionSequence)
            throw new ArgumentException("Hierarchy record envelope fields must match the item.", nameof(record));
    }

    private sealed record HierarchyDocument(
        string WorkflowExecutionId,
        string ExecutionScopeId,
        string ActivityExecutionId,
        long ExecutionSequence,
        ActivityExecutionHierarchyRecord Record);

    private sealed record LoadedActivityExecutionHierarchyRecord(ActivityExecutionHierarchyRecord Record, long Version);
}
