using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IIncidentStateStore"/>. Incidents carry a top-level
/// <c>workflowExecutionId</c> and are indexed by it. <see cref="ListBlockingAsync"/> narrows the
/// per-workflow set and filters to the blocking incidents in memory.
/// </summary>
/// <remarks>
/// <see cref="TryAddAsync"/> is insert-only. It uses Groundwork's portable expected-version-zero write,
/// which creates only when the physical identity is absent and reports a concurrency conflict when another
/// writer wins. Reloading after a conflict both preserves first-writer-wins semantics and validates that a
/// bounded physical identity still belongs to the requested logical incident.
/// </remarks>
public sealed class GroundworkIncidentStateStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.IncidentStateDocumentKind), IIncidentStateStore
{
    public async ValueTask<bool> TryAddAsync(IncidentState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.IncidentId);

        var existing = await LoadByLogicalIdentityAsync(state.WorkflowExecutionId, state.IncidentId, cancellationToken);
        if (existing is not null)
            return false;

        var result = await SaveDocumentAsync(
            PhysicalDocumentId(state.WorkflowExecutionId, state.IncidentId),
            state,
            cancellationToken,
            expectedVersion: 0);

        if (result.Status == DocumentStoreWriteStatus.Saved)
            return true;
        if (result.Status != DocumentStoreWriteStatus.ConcurrencyConflict)
            throw new InvalidOperationException($"Groundwork rejected incident '{state.IncidentId}' with status '{result.Status}'.");

        _ = await LoadByLogicalIdentityAsync(state.WorkflowExecutionId, state.IncidentId, cancellationToken)
            ?? throw new InvalidOperationException($"Incident '{state.IncidentId}' conflicted during creation but could not be reloaded.");
        return false;
    }

    public async ValueTask<IncidentState> SaveAsync(IncidentState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.IncidentId);

        var existing = await LoadByLogicalIdentityAsync(state.WorkflowExecutionId, state.IncidentId, cancellationToken);
        var result = await SaveDocumentAsync(
            PhysicalDocumentId(state.WorkflowExecutionId, state.IncidentId),
            state,
            cancellationToken,
            existing?.Version ?? 0);
        if (result.Status != DocumentStoreWriteStatus.Saved)
        {
            if (result.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
                await LoadByLogicalIdentityAsync(state.WorkflowExecutionId, state.IncidentId, cancellationToken);
            throw new InvalidOperationException($"Groundwork rejected incident '{state.IncidentId}' with status '{result.Status}'.");
        }

        return state;
    }

    public async ValueTask<IncidentState?> FindAsync(string workflowExecutionId, string incidentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);

        return (await LoadByLogicalIdentityAsync(workflowExecutionId, incidentId, cancellationToken))?.State;
    }

    public async ValueTask<IReadOnlyCollection<IncidentState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        return await QueryDocumentsAsync<IncidentState, IncidentState>(
            ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, workflowExecutionId, state => state, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<IncidentState>> ListBlockingAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        var states = await ListAsync(workflowExecutionId, cancellationToken);
        return states.Where(state => state.IsBlocking).ToArray();
    }

    private async ValueTask<LoadedIncident?> LoadByLogicalIdentityAsync(
        string workflowExecutionId,
        string incidentId,
        CancellationToken cancellationToken)
    {
        var envelope = await Store.LoadAsync(DocumentKind, PhysicalDocumentId(workflowExecutionId, incidentId), cancellationToken);
        if (envelope is null)
            return null;

        var state = Serializer.Deserialize<IncidentState>(envelope);
        EnsureLogicalIdentity(state, workflowExecutionId, incidentId);
        return new LoadedIncident(state, envelope.Version);
    }

    private static string PhysicalDocumentId(string workflowExecutionId, string incidentId) =>
        GroundworkPhysicalDocumentId.FromLogicalId(DocumentId.Compose(workflowExecutionId, incidentId));

    private static void EnsureLogicalIdentity(IncidentState state, string workflowExecutionId, string incidentId)
    {
        if (!StringComparer.Ordinal.Equals(state.WorkflowExecutionId, workflowExecutionId)
            || !StringComparer.Ordinal.Equals(state.IncidentId, incidentId))
        {
            throw new InvalidOperationException(
                $"Groundwork physical document identity collision detected for incident '{incidentId}' in workflow execution '{workflowExecutionId}'.");
        }
    }

    private sealed record LoadedIncident(IncidentState State, long Version);
}
