using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IWorkflowExecutionStateStore"/>. The unfiltered <see cref="ListAsync"/>
/// is served through a constant collection partition stamped on every document, so it relies only on the
/// declared-index equality query every provider supports.
/// </summary>
public sealed class GroundworkWorkflowExecutionStateStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind), IWorkflowExecutionStateStore
{
    public async ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);

        var document = new WorkflowExecutionStateDocument(ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, state);
        await SaveDocumentAsync(state.WorkflowExecutionId, document, cancellationToken);

        return state;
    }

    public async ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        return await LoadDocumentAsync<WorkflowExecutionStateDocument, WorkflowExecutionState>(
            workflowExecutionId, document => document.State, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) =>
        await QueryDocumentsAsync<WorkflowExecutionStateDocument, WorkflowExecutionState>(
            ElsaRuntimeStorageManifest.ByCollectionIndex,
            ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
            document => document.State,
            cancellationToken);

    private sealed record WorkflowExecutionStateDocument(string Collection, WorkflowExecutionState State);
}
