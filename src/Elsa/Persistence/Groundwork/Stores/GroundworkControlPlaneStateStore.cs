using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IControlPlaneStateStore"/>. Control-plane state is keyed by its own
/// unique id and may be either workflow-scoped or global (no workflow execution id). The envelope stamps
/// a constant collection partition for <see cref="ListAllAsync"/> and the optional workflow execution id
/// for <see cref="ListForWorkflowExecutionAsync"/>; global states carry a null id and are correctly
/// excluded from the per-workflow index.
/// </summary>
public sealed class GroundworkControlPlaneStateStore(IDocumentStore store) : IControlPlaneStateStore
{
    public async ValueTask<ControlPlaneState> SaveAsync(ControlPlaneState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.ControlPlaneStateId);

        var document = new ControlPlaneStateDocument(
            ElsaRuntimeStorageManifest.ControlPlaneStateDocumentKind,
            state.WorkflowExecutionId,
            state);
        var content = JsonSerializer.Serialize(document, GroundworkRuntimeJson.Options);

        await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.ControlPlaneStateDocumentKind,
                state.ControlPlaneStateId,
                ElsaRuntimeStorageManifest.SchemaVersion,
                content),
            cancellationToken);

        return state;
    }

    public async ValueTask<ControlPlaneState?> FindAsync(string controlPlaneStateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlPlaneStateId);

        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.ControlPlaneStateDocumentKind,
            controlPlaneStateId,
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<IReadOnlyCollection<ControlPlaneState>> ListForWorkflowExecutionAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.ControlPlaneStateDocumentKind,
                ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex,
                workflowExecutionId),
            cancellationToken);

        return envelopes.Select(Map).ToArray();
    }

    public async ValueTask<IReadOnlyCollection<ControlPlaneState>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.ControlPlaneStateDocumentKind,
                ElsaRuntimeStorageManifest.ByCollectionIndex,
                ElsaRuntimeStorageManifest.ControlPlaneStateDocumentKind),
            cancellationToken);

        return envelopes.Select(Map).ToArray();
    }

    private static ControlPlaneState Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<ControlPlaneStateDocument>(envelope.ContentJson, GroundworkRuntimeJson.Options)!.State;

    private sealed record ControlPlaneStateDocument(string Collection, string? WorkflowExecutionId, ControlPlaneState State);
}
