using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IDurableValueStateStore"/>. The durable value carries a top-level
/// <c>workflowExecutionId</c>, so the document is stored directly and indexed by that field for the
/// per-workflow list.
/// </summary>
public sealed class GroundworkDurableValueStateStore(IDocumentStore store) : IDurableValueStateStore
{
    public async ValueTask<DurableValueState> SaveAsync(DurableValueState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.DurableValueId);

        var content = JsonSerializer.Serialize(state, GroundworkRuntimeJson.Options);

        await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.DurableValueStateDocumentKind,
                GroundworkDocumentId.Compose(state.WorkflowExecutionId, state.DurableValueId),
                ElsaRuntimeStorageManifest.SchemaVersion,
                content),
            cancellationToken);

        return state;
    }

    public async ValueTask<bool> DeleteAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableValueId);

        var result = await store.DeleteAsync(
            new DeleteDocumentRequest(
                ElsaRuntimeStorageManifest.DurableValueStateDocumentKind,
                GroundworkDocumentId.Compose(workflowExecutionId, durableValueId)),
            cancellationToken);

        return result.Status == DocumentStoreWriteStatus.Deleted;
    }

    public async ValueTask<DurableValueState?> FindAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(durableValueId);

        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.DurableValueStateDocumentKind,
            GroundworkDocumentId.Compose(workflowExecutionId, durableValueId),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<IReadOnlyCollection<DurableValueState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.DurableValueStateDocumentKind,
                ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex,
                workflowExecutionId),
            cancellationToken);

        return envelopes.Select(Map).ToArray();
    }

    private static DurableValueState Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<DurableValueState>(envelope.ContentJson, GroundworkRuntimeJson.Options)!;
}
