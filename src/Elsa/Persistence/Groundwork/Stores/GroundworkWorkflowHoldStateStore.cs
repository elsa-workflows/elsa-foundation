using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IWorkflowHoldStateStore"/>. Control-plane state is keyed by its own
/// unique id and may be either workflow-scoped or global (no workflow execution id). The envelope stamps
/// a constant collection partition for <see cref="ListAllAsync"/> and the optional workflow execution id
/// for <see cref="ListForWorkflowExecutionAsync"/>; global states carry a null id and are correctly
/// excluded from the per-workflow index.
/// </summary>
public sealed class GroundworkWorkflowHoldStateStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.WorkflowHoldStateDocumentKind), IWorkflowHoldStateStore
{
    public async ValueTask<WorkflowHoldState> SaveAsync(WorkflowHoldState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.ControlPlaneStateId);

        var document = new WorkflowHoldStateDocument(
            ElsaRuntimeStorageManifest.WorkflowHoldStateDocumentKind,
            state.WorkflowExecutionId,
            state);
        await SaveDocumentAsync(state.ControlPlaneStateId, document, cancellationToken);

        return state;
    }

    public async ValueTask<WorkflowHoldState?> FindAsync(string controlPlaneStateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlPlaneStateId);

        return await LoadDocumentAsync<WorkflowHoldStateDocument, WorkflowHoldState>(
            controlPlaneStateId, document => document.State, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowHoldState>> ListForWorkflowExecutionAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        return await QueryDocumentsAsync<WorkflowHoldStateDocument, WorkflowHoldState>(
            ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, workflowExecutionId, document => document.State, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowHoldState>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await QueryDocumentsAsync<WorkflowHoldStateDocument, WorkflowHoldState>(
            ElsaRuntimeStorageManifest.ByCollectionIndex,
            ElsaRuntimeStorageManifest.WorkflowHoldStateDocumentKind,
            document => document.State,
            cancellationToken);

    private sealed record WorkflowHoldStateDocument(string Collection, string? WorkflowExecutionId, WorkflowHoldState State);
}
