using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IControlPlaneStateStore"/>. Control-plane state is keyed by its own
/// unique id and may be either workflow-scoped or global (no workflow execution id). The envelope stamps
/// a constant collection partition for <see cref="ListAllAsync"/> and the optional workflow execution id
/// for <see cref="ListForWorkflowExecutionAsync"/>; global states carry a null id and are correctly
/// excluded from the per-workflow index.
/// </summary>
public sealed class GroundworkControlPlaneStateStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.ControlPlaneStateDocumentKind), IControlPlaneStateStore
{
    public async ValueTask<ControlPlaneState> SaveAsync(ControlPlaneState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.ControlPlaneStateId);

        var document = new ControlPlaneStateDocument(
            ElsaRuntimeStorageManifest.ControlPlaneStateDocumentKind,
            state.WorkflowExecutionId,
            state);
        await SaveDocumentAsync(state.ControlPlaneStateId, document, cancellationToken);

        return state;
    }

    public async ValueTask<ControlPlaneState?> FindAsync(string controlPlaneStateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlPlaneStateId);

        return await LoadDocumentAsync<ControlPlaneStateDocument, ControlPlaneState>(
            controlPlaneStateId, document => document.State, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<ControlPlaneState>> ListForWorkflowExecutionAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        return await QueryDocumentsAsync<ControlPlaneStateDocument, ControlPlaneState>(
            ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, workflowExecutionId, document => document.State, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<ControlPlaneState>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await QueryDocumentsAsync<ControlPlaneStateDocument, ControlPlaneState>(
            ElsaRuntimeStorageManifest.ByCollectionIndex,
            ElsaRuntimeStorageManifest.ControlPlaneStateDocumentKind,
            document => document.State,
            cancellationToken);

    private sealed record ControlPlaneStateDocument(string Collection, string? WorkflowExecutionId, ControlPlaneState State);
}
