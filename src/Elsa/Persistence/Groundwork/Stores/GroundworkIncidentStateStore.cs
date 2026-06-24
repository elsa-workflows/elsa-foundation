using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IIncidentStateStore"/>. Incidents carry a top-level
/// <c>workflowExecutionId</c> and are indexed by it. <see cref="ListBlockingAsync"/> narrows the
/// per-workflow set and filters to the blocking incidents in memory.
/// </summary>
/// <remarks>
/// <see cref="TryAddAsync"/> is insert-only. The Groundwork document store does not (in the
/// <c>0.0.1-preview.4</c> SQLite provider) expose an atomic create primitive: an
/// <c>ExpectedVersion</c> of <c>0</c> against an absent document returns <see cref="DocumentStoreWriteStatus.NotFound"/>
/// rather than creating it, so there is no portable "insert if absent" write. The bridge therefore
/// emulates it with a read-then-create, which is correct for the single-writer checkpoint path but is
/// not atomic under concurrent inserters of the same key. Promoting Groundwork to the default provider
/// should track an atomic insert-only primitive (or unique-index conflict surfacing) in the provider.
/// </remarks>
public sealed class GroundworkIncidentStateStore(IDocumentStore store) : IIncidentStateStore
{
    public async ValueTask<bool> TryAddAsync(IncidentState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.IncidentId);

        var id = DocumentId.Compose(state.WorkflowExecutionId, state.IncidentId);

        var existing = await store.LoadAsync(ElsaRuntimeStorageManifest.IncidentStateDocumentKind, id, cancellationToken);
        if (existing is not null)
            return false;

        var content = JsonSerializer.Serialize(state, GroundworkRuntimeJson.Options);
        var result = await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.IncidentStateDocumentKind,
                id,
                ElsaRuntimeStorageManifest.SchemaVersion,
                content),
            cancellationToken);

        return result.Status == DocumentStoreWriteStatus.Saved;
    }

    public async ValueTask<IncidentState> SaveAsync(IncidentState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.IncidentId);

        var content = JsonSerializer.Serialize(state, GroundworkRuntimeJson.Options);

        await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.IncidentStateDocumentKind,
                DocumentId.Compose(state.WorkflowExecutionId, state.IncidentId),
                ElsaRuntimeStorageManifest.SchemaVersion,
                content),
            cancellationToken);

        return state;
    }

    public async ValueTask<IncidentState?> FindAsync(string workflowExecutionId, string incidentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);

        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.IncidentStateDocumentKind,
            DocumentId.Compose(workflowExecutionId, incidentId),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<IReadOnlyCollection<IncidentState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.IncidentStateDocumentKind,
                ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex,
                workflowExecutionId),
            cancellationToken);

        return envelopes.Select(Map).ToArray();
    }

    public async ValueTask<IReadOnlyCollection<IncidentState>> ListBlockingAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        var states = await ListAsync(workflowExecutionId, cancellationToken);
        return states.Where(state => state.IsBlocking).ToArray();
    }

    private static IncidentState Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<IncidentState>(envelope.ContentJson, GroundworkRuntimeJson.Options)!;
}
