using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IActivityExecutionStateStore"/>. The activity execution carries its
/// owning workflow execution id nested under <see cref="ActivityExecutionState.Execution"/>, so the
/// document is wrapped in a thin envelope that stamps a top-level <c>workflowExecutionId</c> for the
/// declared per-workflow index every provider supports.
/// </summary>
public sealed class GroundworkActivityExecutionStateStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind, boundedStore), IActivityExecutionStateStore
{
    public async ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Execution.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Execution.ActivityExecutionId);
        state.EnsureValueFlowCompatible();
        state.EnsureSupersessionCompatible();

        var existing = await LoadByLogicalIdentityAsync(
            state.Execution.WorkflowExecutionId,
            state.Execution.ActivityExecutionId,
            cancellationToken);
        var document = new ActivityExecutionStateDocument(
            state.Execution.WorkflowExecutionId,
            state.ExecutionScopeId ?? state.Provenance.ExecutionScopeId,
            state.Attempt ?? state.Provenance.Attempt,
            state);
        var result = await SaveDocumentAsync(
            DocumentId.Compose(state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId),
            document,
            cancellationToken,
            expectedVersion: existing?.Version ?? 0);
        if (result.Status != DocumentStoreWriteStatus.Saved)
        {
            if (result.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
                await LoadByLogicalIdentityAsync(state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId, cancellationToken);
            throw new InvalidOperationException($"Groundwork rejected activity execution state '{state.Execution.ActivityExecutionId}' in workflow execution '{state.Execution.WorkflowExecutionId}' with status '{result.Status}'.");
        }

        return state;
    }

    public async ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);

        return (await LoadByLogicalIdentityAsync(workflowExecutionId, activityExecutionId, cancellationToken))?.State;
    }

    public ValueTask<long> CountAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        return new(BoundedStore.CountAsync(
            new DocumentQuery(
                DocumentKind,
                ElsaRuntimeStorageManifest.PageActivityExecutionStatesByWorkflowExecutionQuery,
                [Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, workflowExecutionId)],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.ActivityExecutionIdField)],
                take: 1),
            cancellationToken));
    }

    public async ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(
        ActivityExecutionStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await QueryPageAsync(
            ElsaRuntimeStorageManifest.PageActivityExecutionStatesByWorkflowExecutionQuery,
            query,
            [
                Equal(
                    ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
                    query.WorkflowExecutionId)
            ],
            cancellationToken);
    }

    public async ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(
        ActivityExecutionStateParentPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await QueryPageAsync(
            ElsaRuntimeStorageManifest.PageActivityExecutionStatesByParentQuery,
            query,
            [
                Equal(
                    ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
                    query.WorkflowExecutionId),
                Equal(
                    ElsaRuntimeStorageManifest.ParentActivityExecutionIdField,
                    query.ParentActivityExecutionId)
            ],
            cancellationToken);
    }

    private async ValueTask<LoadedActivityExecutionState?> LoadByLogicalIdentityAsync(
        string workflowExecutionId,
        string activityExecutionId,
        CancellationToken cancellationToken)
    {
        var envelope = await Store.LoadAsync(DocumentKind, DocumentId.Compose(workflowExecutionId, activityExecutionId), cancellationToken);
        if (envelope is null)
            return null;

        var document = Serializer.Deserialize<ActivityExecutionStateDocument>(envelope);
        var state = document.State;
        state.EnsureSupersessionCompatible();
        if (!StringComparer.Ordinal.Equals(state.Execution.WorkflowExecutionId, workflowExecutionId)
            || !StringComparer.Ordinal.Equals(state.Execution.ActivityExecutionId, activityExecutionId))
        {
            throw new InvalidOperationException(
                $"Groundwork physical document identity collision detected for activity execution state '{activityExecutionId}' in workflow execution '{workflowExecutionId}'.");
        }

        return new LoadedActivityExecutionState(state, envelope.Version);
    }

    private sealed record ActivityExecutionStateDocument(
        string WorkflowExecutionId,
        string? ExecutionScopeId,
        ActivityExecutionAttemptLineage? Attempt,
        ActivityExecutionState State);

    private sealed record LoadedActivityExecutionState(ActivityExecutionState State, long Version);

    private async ValueTask<RuntimeStorePage<ActivityExecutionState>> QueryPageAsync(
        string queryIdentity,
        RuntimeStorePageRequest query,
        IReadOnlyList<DocumentQueryClause> clauses,
        CancellationToken cancellationToken)
    {
        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                queryIdentity,
                clauses,
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.ActivityExecutionIdField)],
                take: query.Limit,
                continuation: query.ContinuationToken),
            cancellationToken);
        return new RuntimeStorePage<ActivityExecutionState>(
            query,
            result.Documents
                .Select(envelope =>
                {
                    var state = Serializer.Deserialize<ActivityExecutionStateDocument>(envelope).State;
                    state.EnsureSupersessionCompatible();
                    return state;
                })
                .ToArray(),
            result.NextContinuation);
    }

    private static DocumentQueryClause Equal(string fieldPath, string value) =>
        DocumentQueryClause.Of(DocumentQueryComparison.Equal(fieldPath, value));
}
