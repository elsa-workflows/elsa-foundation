using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IWorkflowExecutionStateStore"/>. The unfiltered <see cref="ListAsync"/>
/// is served through a constant collection partition stamped on every document, so it relies only on the
/// declared-index equality query every provider supports.
/// </summary>
public sealed class GroundworkWorkflowExecutionStateStore(IDocumentStore store) : IWorkflowExecutionStateStore
{
    public async ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);

        var document = new WorkflowExecutionStateDocument(ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind, state);
        var content = JsonSerializer.Serialize(document, GroundworkRuntimeJson.Options);

        await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind,
                state.WorkflowExecutionId,
                ElsaRuntimeStorageManifest.SchemaVersion,
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

    private static WorkflowExecutionState Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<WorkflowExecutionStateDocument>(envelope.ContentJson, GroundworkRuntimeJson.Options)!.State;

    private sealed record WorkflowExecutionStateDocument(string Collection, WorkflowExecutionState State);
}
