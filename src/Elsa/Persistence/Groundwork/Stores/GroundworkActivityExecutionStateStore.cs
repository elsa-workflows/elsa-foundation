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
public sealed class GroundworkActivityExecutionStateStore(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind, boundedStore), IActivityExecutionStateStore
{
    public async ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Execution.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state.Execution.ActivityExecutionId);
        state.EnsureValueFlowCompatible();

        var existing = await LoadByLogicalIdentityAsync(
            state.Execution.WorkflowExecutionId,
            state.Execution.ActivityExecutionId,
            cancellationToken);
        var document = new ActivityExecutionStateDocument(
            state.Execution.WorkflowExecutionId,
            state.ExecutionScopeId ?? state.Provenance.ExecutionScopeId,
            state.Attempt ?? state.Provenance.Attempt,
            state);
        var result = await SaveDocumentAsync(
            DocumentId.Compose(state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId),
            document,
            cancellationToken,
            expectedVersion: existing?.Version ?? 0);
        if (result.Status != DocumentStoreWriteStatus.Saved)
        {
            if (result.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
                await LoadByLogicalIdentityAsync(state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId, cancellationToken);
            throw new InvalidOperationException($"Groundwork rejected activity execution state '{state.Execution.ActivityExecutionId}' in workflow execution '{state.Execution.WorkflowExecutionId}' with status '{result.Status}'.");
        }

        return state;
    }

    public async ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);

        return (await LoadByLogicalIdentityAsync(workflowExecutionId, activityExecutionId, cancellationToken))?.State;
    }

    public async ValueTask<IReadOnlyCollection<ActivityExecutionState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        return await QueryDocumentsAsync<ActivityExecutionStateDocument, ActivityExecutionState>(
            ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery,
            ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
            workflowExecutionId,
            document => document.State,
            cancellationToken);
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
            ElsaRuntimeStorageManifest.ListByParentActivityExecutionQuery,
            ElsaRuntimeStorageManifest.ParentActivityExecutionIdField,
            parentActivityExecutionId,
            document => document.State,
            cancellationToken);

        return byParent
            .Where(state => StringComparer.Ordinal.Equals(state.Execution.WorkflowExecutionId, workflowExecutionId))
            .ToArray();
    }

    private async ValueTask<LoadedActivityExecutionState?> LoadByLogicalIdentityAsync(
        string workflowExecutionId,
        string activityExecutionId,
        CancellationToken cancellationToken)
    {
        var envelope = await Store.LoadAsync(DocumentKind, DocumentId.Compose(workflowExecutionId, activityExecutionId), cancellationToken);
        if (envelope is null)
            return null;

        var document = Serializer.Deserialize<ActivityExecutionStateDocument>(envelope);
        var state = document.State;
        if (!StringComparer.Ordinal.Equals(state.Execution.WorkflowExecutionId, workflowExecutionId)
            || !StringComparer.Ordinal.Equals(state.Execution.ActivityExecutionId, activityExecutionId))
        {
            throw new InvalidOperationException(
                $"Groundwork physical document identity collision detected for activity execution state '{activityExecutionId}' in workflow execution '{workflowExecutionId}'.");
        }

        return new LoadedActivityExecutionState(state, envelope.Version);
    }

    private sealed record ActivityExecutionStateDocument(
        string WorkflowExecutionId,
        string? ExecutionScopeId,
        ActivityExecutionAttemptLineage? Attempt,
        ActivityExecutionState State);

    private sealed record LoadedActivityExecutionState(ActivityExecutionState State, long Version);
}
