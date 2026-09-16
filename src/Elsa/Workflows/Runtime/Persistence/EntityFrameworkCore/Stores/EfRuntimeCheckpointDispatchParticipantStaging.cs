using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
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
        Dictionary<string, WorkflowTestScopeRecord> touchedTestScopes,
        CancellationToken cancellationToken)
    {
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
            var row = await LoadAsync(context, scope, candidate.DispatchId, cancellationToken);
            if (row is null)
            {
                WorkflowDispatchLifecycle.ValidateNew(candidate);
                if (WorkflowTestScopeAdmission.ScopeRequiredToAdd(candidate, dispatchExists: false) is { } testScope)
                    await EfRuntimeCheckpointTestScopeParticipantStaging.AssertOpenAndStageAsync(
                        context, testScope, commit.Checkpoint.OccurredAt, scope, touchedTestScopes, cancellationToken);
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

            var row = await LoadAsync(context, scope, request.DispatchId, cancellationToken);
            var current = row is null ? null : WorkflowDispatchEfSupport.ReadChecked(row, scope, request.DispatchId);
            var resolved = WorkflowDispatchLifecycle.ResolveParentCancellation(current, request).Record;
            if (!WorkflowDispatchLifecycle.RecordsEqual(current!, resolved))
                WorkflowDispatchEfSupport.Copy(row!, resolved, scope, checked(row!.Revision + 1));
        }
    }

    private static Task<WorkflowDispatchEntity?> LoadAsync(
        BookmarkStateDbContext context,
        string scope,
        string dispatchId,
        CancellationToken cancellationToken)
    {
        var id = WorkflowDispatchEfSupport.RowId(scope, dispatchId);
        // The projection is checked by ReadChecked after the immutable identity lookup. A corrupt scope or
        // dispatch-ID projection must not look like an absent row or be replaced by a second insert.
        return context.WorkflowDispatches.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
    }
}
