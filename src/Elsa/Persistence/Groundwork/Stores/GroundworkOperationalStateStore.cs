using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IOperationalStateStore"/>. The document is wrapped in an envelope that
/// stamps both a top-level <c>workflowExecutionId</c> (for the per-workflow list) and a constant
/// collection partition (for the unfiltered <see cref="ListAllAsync"/>), so both lists run through the
/// declared-index equality query every provider supports.
/// </summary>
public sealed class GroundworkOperationalStateStore(IDocumentStore store) : IOperationalStateStore
{
    public async ValueTask<OperationalState> SaveAsync(OperationalState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.OperationalStateId);

        var document = new OperationalStateDocument(
            ElsaRuntimeStorageManifest.OperationalStateDocumentKind,
            state.WorkflowExecutionId,
            state);
        var content = JsonSerializer.Serialize(document, GroundworkRuntimeJson.Options);

        await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.OperationalStateDocumentKind,
                DocumentId.Compose(state.WorkflowExecutionId, state.OperationalStateId),
                ElsaRuntimeStorageManifest.SchemaVersion,
                content),
            cancellationToken);

        return state;
    }

    public async ValueTask<OperationalState?> FindAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationalStateId);

        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.OperationalStateDocumentKind,
            DocumentId.Compose(workflowExecutionId, operationalStateId),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<IReadOnlyCollection<OperationalState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.OperationalStateDocumentKind,
                ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex,
                workflowExecutionId),
            cancellationToken);

        return envelopes.Select(Map).ToArray();
    }

    public async ValueTask<IReadOnlyCollection<OperationalState>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.OperationalStateDocumentKind,
                ElsaRuntimeStorageManifest.ByCollectionIndex,
                ElsaRuntimeStorageManifest.OperationalStateDocumentKind),
            cancellationToken);

        return envelopes.Select(Map).ToArray();
    }

    private static OperationalState Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<OperationalStateDocument>(envelope.ContentJson, GroundworkRuntimeJson.Options)!.State;

    private sealed record OperationalStateDocument(string Collection, string WorkflowExecutionId, OperationalState State);
}
