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
public sealed class GroundworkWorkflowExecutionStateStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer) : IWorkflowExecutionStateStore
{
    public async ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);

        var document = new WorkflowExecutionStateDocument(ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, state);
        var (schemaVersion, content) = serializer.Serialize(ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, document);

        await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
                state.WorkflowExecutionId,
                schemaVersion,
                content),
            cancellationToken);

        return state;
    }

    public async ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
            workflowExecutionId,
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default)
    {
        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
                ElsaRuntimeStorageManifest.ByCollectionIndex,
                ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind),
            cancellationToken);

        return envelopes.Select(Map).ToArray();
    }

    private WorkflowExecutionState Map(DocumentEnvelope envelope) =>
        serializer.Deserialize<WorkflowExecutionStateDocument>(envelope).State;

    private sealed record WorkflowExecutionStateDocument(string Collection, WorkflowExecutionState State);
}
