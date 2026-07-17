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
public sealed class GroundworkWorkflowHoldStateStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.WorkflowHoldStateDocumentKind, boundedStore), IWorkflowHoldStateStore
{
    public async ValueTask<WorkflowHoldState> SaveAsync(WorkflowHoldState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.ControlPlaneStateId);

        var existing = await LoadByControlPlaneStateIdAsync(state.ControlPlaneStateId, cancellationToken);
        var document = new WorkflowHoldStateDocument(
            ElsaRuntimeStorageManifest.WorkflowHoldStateDocumentKind,
            state.WorkflowExecutionId,
            state);
        var result = await SaveDocumentAsync(
            state.ControlPlaneStateId,
            document,
            cancellationToken,
            expectedVersion: existing?.Version ?? 0);
        if (result.Status != DocumentStoreWriteStatus.Saved)
        {
            if (result.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
                await LoadByControlPlaneStateIdAsync(state.ControlPlaneStateId, cancellationToken);
            throw new InvalidOperationException($"Groundwork rejected workflow hold state '{state.ControlPlaneStateId}' with status '{result.Status}'.");
        }

        return state;
    }

    public async ValueTask<WorkflowHoldState?> FindAsync(string controlPlaneStateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlPlaneStateId);

        return (await LoadByControlPlaneStateIdAsync(controlPlaneStateId, cancellationToken))?.State;
    }

    public async ValueTask<IReadOnlyCollection<WorkflowHoldState>> ListForWorkflowExecutionAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        return await QueryDocumentsAsync<WorkflowHoldStateDocument, WorkflowHoldState>(
            ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery,
            ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
            workflowExecutionId,
            document => document.State,
            cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<WorkflowHoldState>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await QueryDocumentsAsync<WorkflowHoldStateDocument, WorkflowHoldState>(
            ElsaRuntimeStorageManifest.ListAllQuery,
            ElsaRuntimeStorageManifest.CollectionField,
            ElsaRuntimeStorageManifest.WorkflowHoldStateDocumentKind,
            document => document.State,
            cancellationToken);

    private async ValueTask<LoadedWorkflowHoldState?> LoadByControlPlaneStateIdAsync(
        string controlPlaneStateId,
        CancellationToken cancellationToken)
    {
        var envelope = await Store.LoadAsync(DocumentKind, controlPlaneStateId, cancellationToken);
        if (envelope is null)
            return null;

        var document = Serializer.Deserialize<WorkflowHoldStateDocument>(envelope);
        if (!StringComparer.Ordinal.Equals(document.State.ControlPlaneStateId, controlPlaneStateId))
            throw new InvalidOperationException(
                $"Groundwork physical document identity collision detected for workflow hold state '{controlPlaneStateId}'.");

        return new LoadedWorkflowHoldState(document.State, envelope.Version);
    }

    private sealed record WorkflowHoldStateDocument(string Collection, string? WorkflowExecutionId, WorkflowHoldState State);

    private sealed record LoadedWorkflowHoldState(WorkflowHoldState State, long Version);
}
