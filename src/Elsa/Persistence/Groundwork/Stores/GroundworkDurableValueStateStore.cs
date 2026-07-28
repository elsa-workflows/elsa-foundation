using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IDurableValueStateStore"/>. The durable value carries a top-level
/// <c>workflowExecutionId</c>, so the document is stored directly and indexed by that field for the
/// per-workflow list.
/// </summary>
public sealed class GroundworkDurableValueStateStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.DurableValueStateDocumentKind, boundedStore), IDurableValueStateStore
{
    public async ValueTask<DurableValueState> SaveAsync(DurableValueState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.DurableValueId);

        var existing = await LoadByLogicalIdentityAsync(state.WorkflowExecutionId, state.DurableValueId, cancellationToken);
        var result = await SaveDocumentAsync(
            DocumentId.Compose(state.WorkflowExecutionId, state.DurableValueId),
            state,
            cancellationToken,
            expectedVersion: existing?.Version ?? 0);
        if (result.Status != DocumentStoreWriteStatus.Saved)
        {
            if (result.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
                await LoadByLogicalIdentityAsync(state.WorkflowExecutionId, state.DurableValueId, cancellationToken);
            throw new InvalidOperationException($"Groundwork rejected durable value '{state.DurableValueId}' in workflow execution '{state.WorkflowExecutionId}' with status '{result.Status}'.");
        }

        return state;
    }

    public async ValueTask<bool> DeleteAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableValueId);

        var existing = await LoadByLogicalIdentityAsync(workflowExecutionId, durableValueId, cancellationToken);
        if (existing is null)
            return false;

        var result = await Store.DeleteAsync(
            new DeleteDocumentRequest(DocumentKind, DocumentId.Compose(workflowExecutionId, durableValueId), ExpectedVersion: existing.Version),
            cancellationToken);
        if (result.Status == DocumentStoreWriteStatus.Deleted)
            return true;
        if (result.Status == DocumentStoreWriteStatus.NotFound)
            return false;
        if (result.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
            await LoadByLogicalIdentityAsync(workflowExecutionId, durableValueId, cancellationToken);

        throw new InvalidOperationException($"Groundwork rejected durable value deletion for '{durableValueId}' in workflow execution '{workflowExecutionId}' with status '{result.Status}'.");
    }

    public async ValueTask<DurableValueState?> FindAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableValueId);

        return (await LoadByLogicalIdentityAsync(workflowExecutionId, durableValueId, cancellationToken))?.State;
    }

    public async ValueTask<RuntimeStorePage<DurableValueState>> ListPageAsync(
        DurableValueStatePageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var result = await BoundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                    ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
                    query.WorkflowExecutionId))],
                [new DocumentQueryOrder(ElsaRuntimeStorageManifest.DurableValueIdField)],
                take: query.Limit,
                continuation: query.ContinuationToken),
            cancellationToken);

        return new RuntimeStorePage<DurableValueState>(
            query,
            result.Documents.Select(Serializer.Deserialize<DurableValueState>).ToArray(),
            result.NextContinuation);
    }

    private async ValueTask<LoadedDurableValueState?> LoadByLogicalIdentityAsync(
        string workflowExecutionId,
        string durableValueId,
        CancellationToken cancellationToken)
    {
        var envelope = await Store.LoadAsync(DocumentKind, DocumentId.Compose(workflowExecutionId, durableValueId), cancellationToken);
        if (envelope is null)
            return null;

        var state = Serializer.Deserialize<DurableValueState>(envelope);
        if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, workflowExecutionId)
            || !StringComparer.Ordinal.Equals(state.DurableValueId, durableValueId))
        {
            throw new InvalidOperationException(
                $"Groundwork physical document identity collision detected for durable value '{durableValueId}' in workflow execution '{workflowExecutionId}'.");
        }

        return new LoadedDurableValueState(state, envelope.Version);
    }

    private sealed record LoadedDurableValueState(DurableValueState State, long Version);
}
