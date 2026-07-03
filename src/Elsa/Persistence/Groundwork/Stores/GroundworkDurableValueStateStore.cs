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
public sealed class GroundworkDurableValueStateStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.DurableValueStateDocumentKind), IDurableValueStateStore
{
    public async ValueTask<DurableValueState> SaveAsync(DurableValueState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.DurableValueId);

        await SaveDocumentAsync(DocumentId.Compose(state.WorkflowExecutionId, state.DurableValueId), state, cancellationToken);

        return state;
    }

    public async ValueTask<bool> DeleteAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableValueId);

        var result = await DeleteDocumentAsync(DocumentId.Compose(workflowExecutionId, durableValueId), cancellationToken);

        return result.Status == DocumentStoreWriteStatus.Deleted;
    }

    public async ValueTask<DurableValueState?> FindAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableValueId);

        return await LoadDocumentAsync<DurableValueState, DurableValueState>(
            DocumentId.Compose(workflowExecutionId, durableValueId), state => state, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<DurableValueState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        return await QueryDocumentsAsync<DurableValueState, DurableValueState>(
            ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, workflowExecutionId, state => state, cancellationToken);
    }
}
