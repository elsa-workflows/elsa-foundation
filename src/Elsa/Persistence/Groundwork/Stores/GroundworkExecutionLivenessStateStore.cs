using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IExecutionLivenessStateStore"/>. The document is wrapped in an envelope that
/// stamps both a top-level <c>workflowExecutionId</c> (for the per-workflow list) and a constant
/// collection partition (for the unfiltered <see cref="ListAllAsync"/>), so both lists run through the
/// declared-index equality query every provider supports.
/// </summary>
public sealed class GroundworkExecutionLivenessStateStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind, boundedStore), IExecutionLivenessStateStore
{
    public async ValueTask<ExecutionLivenessState> SaveAsync(ExecutionLivenessState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.OperationalStateId);

        var document = new ExecutionLivenessStateDocument(
            ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind,
            state.WorkflowExecutionId,
            state);
        await SaveDocumentAsync(DocumentId.Compose(state.WorkflowExecutionId, state.OperationalStateId), document, cancellationToken);

        return state;
    }

    public async ValueTask<ExecutionLivenessState?> FindAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationalStateId);

        return await LoadDocumentAsync<ExecutionLivenessStateDocument, ExecutionLivenessState>(
            DocumentId.Compose(workflowExecutionId, operationalStateId), document => document.State, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<ExecutionLivenessState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        return await QueryDocumentsAsync<ExecutionLivenessStateDocument, ExecutionLivenessState>(
            ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery,
            ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
            workflowExecutionId,
            document => document.State,
            cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<ExecutionLivenessState>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await QueryDocumentsAsync<ExecutionLivenessStateDocument, ExecutionLivenessState>(
            ElsaRuntimeStorageManifest.ListAllQuery,
            ElsaRuntimeStorageManifest.CollectionField,
            ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind,
            document => document.State,
            cancellationToken);

    private sealed record ExecutionLivenessStateDocument(string Collection, string WorkflowExecutionId, ExecutionLivenessState State);
}
