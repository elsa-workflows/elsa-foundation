using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IActivityExecutionStateStore"/>. The activity execution carries its
/// owning workflow execution id nested under <see cref="ActivityExecutionState.Execution"/>, so the
/// document is wrapped in a thin envelope that stamps a top-level <c>workflowExecutionId</c> for the
/// declared per-workflow index every provider supports.
/// </summary>
public sealed class GroundworkActivityExecutionStateStore(IDocumentStore store) : IActivityExecutionStateStore
{
    public async ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Execution.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Execution.ActivityExecutionId);

        var document = new ActivityExecutionStateDocument(state.Execution.WorkflowExecutionId, state);
        var content = JsonSerializer.Serialize(document, GroundworkRuntimeJson.Options);

        await store.SaveAsync(
            new SaveDocumentRequest(
                ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind,
                DocumentId.Compose(state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId),
                ElsaRuntimeStorageManifest.SchemaVersion,
                content),
            cancellationToken);

        return state;
    }

    public async ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);

        var envelope = await store.LoadAsync(
            ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind,
            DocumentId.Compose(workflowExecutionId, activityExecutionId),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<IReadOnlyCollection<ActivityExecutionState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind,
                ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex,
                workflowExecutionId),
            cancellationToken);

        return envelopes.Select(Map).ToArray();
    }

    private static ActivityExecutionState Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<ActivityExecutionStateDocument>(envelope.ContentJson, GroundworkRuntimeJson.Options)!.State;

    private sealed record ActivityExecutionStateDocument(string WorkflowExecutionId, ActivityExecutionState State);
}
