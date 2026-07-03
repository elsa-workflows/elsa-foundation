using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IOperationalStateStore"/>. The document is wrapped in an envelope that
/// stamps both a top-level <c>workflowExecutionId</c> (for the per-workflow list) and a constant
/// collection partition (for the unfiltered <see cref="ListAllAsync"/>), so both lists run through the
/// declared-index equality query every provider supports.
/// </summary>
public sealed class GroundworkOperationalStateStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.OperationalStateDocumentKind), IOperationalStateStore
{
    public async ValueTask<OperationalState> SaveAsync(OperationalState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.OperationalStateId);

        var document = new OperationalStateDocument(
            ElsaRuntimeStorageManifest.OperationalStateDocumentKind,
            state.WorkflowExecutionId,
            state);
        await SaveDocumentAsync(DocumentId.Compose(state.WorkflowExecutionId, state.OperationalStateId), document, cancellationToken);

        return state;
    }

    public async ValueTask<OperationalState?> FindAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationalStateId);

        return await LoadDocumentAsync<OperationalStateDocument, OperationalState>(
            DocumentId.Compose(workflowExecutionId, operationalStateId), document => document.State, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<OperationalState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        return await QueryDocumentsAsync<OperationalStateDocument, OperationalState>(
            ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, workflowExecutionId, document => document.State, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<OperationalState>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await QueryDocumentsAsync<OperationalStateDocument, OperationalState>(
            ElsaRuntimeStorageManifest.ByCollectionIndex,
            ElsaRuntimeStorageManifest.OperationalStateDocumentKind,
            document => document.State,
            cancellationToken);

    private sealed record OperationalStateDocument(string Collection, string WorkflowExecutionId, OperationalState State);
}
