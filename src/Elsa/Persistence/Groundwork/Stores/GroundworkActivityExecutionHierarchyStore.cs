using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

public sealed class GroundworkActivityExecutionHierarchyStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IActivityExecutionHierarchyCursorCodec? cursorCodec = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind),
        IActivityExecutionHierarchyStore
{
    public async ValueTask SaveAsync(ActivityExecutionHierarchyRecord record, CancellationToken cancellationToken = default)
    {
        Validate(record);
        await SaveDocumentAsync(
            DocumentId.Compose(record.WorkflowExecutionId, record.ActivityExecutionId),
            new HierarchyDocument(record.WorkflowExecutionId, record.ExecutionScopeId, record.ActivityExecutionId, record.ExecutionSequence, record),
            cancellationToken);
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

    public ValueTask<ActivityExecutionLayout?> FindLayoutAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ActivityExecutionLayout?>(null);

    private async ValueTask<IReadOnlyCollection<ActivityExecutionHierarchyRecord>> ListWorkflowAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        return await QueryDocumentsAsync<HierarchyDocument, ActivityExecutionHierarchyRecord>(
            ElsaRuntimeStorageManifest.ActivityExecutionHierarchyByWorkflowExecution,
            workflowExecutionId,
            document => document.Record,
            cancellationToken);
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
}
