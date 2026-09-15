using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Stages the ordinary workflow-dispatch lifecycle in the checkpoint writer's EF transaction.</summary>
internal static class EfRuntimeCheckpointDispatchParticipantStaging
{
    public static async ValueTask StageAsync(
        BookmarkStateDbContext context,
        RuntimeCheckpointCommit commit,
        string scope,
        Dictionary<string, WorkflowTestScope> touchedTestScopes,
        CancellationToken cancellationToken)
    {
        var scopeKey = EfRelationalIdentity.Encode(scope);
        var scopeHash = EfRelationalIdentity.Hash(scope);
        var staged = new Dictionary<string, WorkflowDispatchRecord>(StringComparer.Ordinal);

        foreach (var change in commit.StateChanges.WorkflowDispatches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = change.State;
            if (staged.TryGetValue(change.StateId, out var duplicate))
            {
                if (!WorkflowDispatchLifecycle.RecordsEqual(duplicate, candidate))
                    throw new InvalidOperationException(
                        $"Workflow dispatch '{change.StateId}' occurs more than once with conflicting state.");
                continue;
            }

            staged.Add(change.StateId, candidate);
            var row = await LoadAsync(context, scope, scopeKey, scopeHash, candidate.DispatchId, cancellationToken);
            if (row is null)
            {
                WorkflowDispatchLifecycle.ValidateNew(candidate);
                if (candidate.TestScope is { } testScope)
                    await EfRuntimeCheckpointTestScopeParticipantStaging.AssertOpenAndStageAsync(
                        context, testScope, candidate.ChildWorkflowExecutionId, commit.Checkpoint.OccurredAt,
                        scope, touchedTestScopes, cancellationToken);
                context.WorkflowDispatches.Add(WorkflowDispatchEfSupport.ToEntity(
                    candidate, scope, WorkflowDispatchEfSupport.RowId(scope, candidate.DispatchId), 1));
                continue;
            }

            var current = WorkflowDispatchEfSupport.ReadChecked(row, scope, candidate.DispatchId);
            WorkflowDispatchLifecycle.ValidateTransition(current, candidate);
            if (!WorkflowDispatchLifecycle.RecordsEqual(current, candidate))
                WorkflowDispatchEfSupport.Copy(row, candidate, scope, checked(row.Revision + 1));
        }

        foreach (var request in commit.StateChanges.WorkflowDispatchCancellations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (staged.ContainsKey(request.DispatchId))
                throw new NotSupportedException(
                    "The bounded EF checkpoint slice does not combine a dispatch upsert and cancellation for the same ID.");

            var row = await LoadAsync(context, scope, scopeKey, scopeHash, request.DispatchId, cancellationToken)
                      ?? throw new InvalidOperationException(
                          $"Workflow dispatch '{request.DispatchId}' was not found for parent cancellation.");
            var current = WorkflowDispatchEfSupport.ReadChecked(row, scope, request.DispatchId);
            if (!StringComparer.Ordinal.Equals(current.ParentActivityExecutionId, request.ParentActivityExecutionId) ||
                !StringComparer.Ordinal.Equals(current.ChildWorkflowExecutionId, request.ChildWorkflowExecutionId))
                throw new InvalidOperationException(
                    $"Workflow dispatch cancellation request '{request.DispatchId}' conflicts with the persisted dispatch identity.");
            if (!WorkflowDispatchLifecycle.IsCancellationPropagationEnabled(current))
                throw new InvalidOperationException(
                    $"Workflow dispatch '{request.DispatchId}' does not permit parent cancellation propagation.");

            WorkflowDispatchRecord? replacement = null;
            if (current.Status == WorkflowDispatchStatus.Pending &&
                !WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(current))
                replacement = WorkflowDispatchLifecycle.CancelBeforeAdmission(current, request.RequestedAt);
            else if (current.Status == WorkflowDispatchStatus.Started &&
                     !WorkflowDispatchLifecycle.IsCancellationRequested(current))
                replacement = WorkflowDispatchLifecycle.MarkCancellationRequested(current, request.RequestedAt);

            if (replacement is not null)
                WorkflowDispatchEfSupport.Copy(row, replacement, scope, checked(row.Revision + 1));
        }
    }

    private static Task<WorkflowDispatchEntity?> LoadAsync(
        BookmarkStateDbContext context,
        string scope,
        string scopeKey,
        string scopeHash,
        string dispatchId,
        CancellationToken cancellationToken)
    {
        var id = WorkflowDispatchEfSupport.RowId(scope, dispatchId);
        var key = EfRelationalIdentity.Encode(dispatchId);
        var hash = EfRelationalIdentity.Hash(dispatchId);
        return context.WorkflowDispatches.SingleOrDefaultAsync(row =>
            row.Id == id && row.ScopeKey == scopeKey && row.ScopeKeyHash == scopeHash &&
            row.DispatchId == key && row.DispatchIdHash == hash,
            cancellationToken);
    }
}
