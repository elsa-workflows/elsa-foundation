using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IActivityExecutionStateStore"/>. The activity execution carries its
/// owning workflow execution id nested under <see cref="ActivityExecutionState.Execution"/>, so the
/// document is wrapped in a thin envelope that stamps a top-level <c>workflowExecutionId</c> for the
/// declared per-workflow index every provider supports.
/// </summary>
public sealed class GroundworkActivityExecutionStateStore(IDocumentStore store, IGroundworkRuntimeDocumentSerializer serializer)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind), IActivityExecutionStateStore
{
    public async ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Execution.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Execution.ActivityExecutionId);

        var document = new ActivityExecutionStateDocument(state.Execution.WorkflowExecutionId, state);
        await SaveDocumentAsync(
            DocumentId.Compose(state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId),
            document,
            cancellationToken);

        return state;
    }

    public async ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);

        return await LoadDocumentAsync<ActivityExecutionStateDocument, ActivityExecutionState>(
            DocumentId.Compose(workflowExecutionId, activityExecutionId), document => document.State, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<ActivityExecutionState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        return await QueryDocumentsAsync<ActivityExecutionStateDocument, ActivityExecutionState>(
            ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, workflowExecutionId, document => document.State, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<ActivityExecutionState>> ListByParentAsync(string workflowExecutionId, string parentActivityExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentActivityExecutionId);

        // Query the single-field parent index (equality on the persisted state.parentActivityExecutionId), then apply a
        // defensive in-memory workflow-execution filter so the full (wf, parent) semantics hold identically across providers
        // without relying on parent activity-execution ids being globally unique. The parent-scoped set is branch-bounded,
        // so the post-filter is over a tiny list.
        var byParent = await QueryDocumentsAsync<ActivityExecutionStateDocument, ActivityExecutionState>(
            ElsaRuntimeStorageManifest.ByParentActivityExecutionIndex, parentActivityExecutionId, document => document.State, cancellationToken);

        return byParent
            .Where(state => StringComparer.Ordinal.Equals(state.Execution.WorkflowExecutionId, workflowExecutionId))
            .ToArray();
    }

    private sealed record ActivityExecutionStateDocument(string WorkflowExecutionId, ActivityExecutionState State);
}
