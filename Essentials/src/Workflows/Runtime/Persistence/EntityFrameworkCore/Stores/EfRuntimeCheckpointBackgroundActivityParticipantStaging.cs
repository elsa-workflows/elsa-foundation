using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Stages background admissions, cancellations, and receipt acceptance in the checkpoint transaction.</summary>
internal static class EfRuntimeCheckpointBackgroundActivityParticipantStaging
{
    public static async ValueTask StageAsync(
        RuntimeDbContext context,
        RuntimeCheckpointCommit commit,
        string scope,
        CancellationToken cancellationToken)
    {
        var changes = commit.StateChanges.BackgroundActivityWork;
        if (changes.Admissions.Count > 0)
            await EnsureExecutionIsLiveAsync(context, commit.WorkflowExecutionId, scope, cancellationToken);

        foreach (var request in changes.Admissions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = EfBackgroundActivityWorkStore.RowId(scope, request.WorkflowExecutionId, request.WorkId);
            var existing = await context.BackgroundActivityWorks.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
            if (existing is not null)
            {
                _ = EfBackgroundActivityWorkStore.ReadChecked(existing, scope, request.WorkflowExecutionId, request.WorkId);
                throw new BackgroundActivityWorkConflictException(request.WorkflowExecutionId, request.WorkId, "admission is create-only and the identity already exists");
            }

            context.BackgroundActivityWorks.Add(EfBackgroundActivityWorkStore.ToEntity(
                BackgroundActivityWorkLifecycle.CreatePending(request, commit.Checkpoint.OccurredAt), scope));
        }

        foreach (var acceptance in changes.Acceptances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = EfBackgroundActivityWorkStore.RowId(scope, acceptance.WorkflowExecutionId, acceptance.WorkId);
            var row = await context.BackgroundActivityWorks.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
                      ?? throw new BackgroundActivityWorkConflictException(acceptance.WorkflowExecutionId, acceptance.WorkId, "the receipt acceptance has no durable work item");
            var current = EfBackgroundActivityWorkStore.ReadChecked(row, scope, acceptance.WorkflowExecutionId, acceptance.WorkId);
            var accepted = BackgroundActivityWorkLifecycle.Accept(current, acceptance);
            if (!ReferenceEquals(current, accepted))
                EfBackgroundActivityWorkStore.Copy(row, accepted, scope);
        }

        // A receipt accepted by this workflow checkpoint must survive its terminal workflow cancellation.
        foreach (var request in changes.Cancellations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
            var workflowHash = EfRuntimeOperationalStoreSupport.Hash(request.WorkflowExecutionId);
            var pending = (int)BackgroundActivityWorkStatus.Pending;
            var claimed = (int)BackgroundActivityWorkStatus.Claimed;
            var resultReady = (int)BackgroundActivityWorkStatus.ResultReady;
            var query = context.BackgroundActivityWorks.Where(row =>
                row.ScopeKeyHash == scopeHash && row.WorkflowExecutionIdHash == workflowHash &&
                (row.Status == pending || row.Status == claimed || row.Status == resultReady));
            if (request.ActivityExecutionIds is { } activityIds)
            {
                var hashes = activityIds.Select(EfRuntimeOperationalStoreSupport.Hash).Distinct(StringComparer.Ordinal).ToArray();
                query = query.Where(row => hashes.Contains(row.ActivityExecutionIdHash));
            }

            foreach (var row in await query.ToArrayAsync(cancellationToken))
            {
                var item = EfBackgroundActivityWorkStore.ReadChecked(row, scope, request.WorkflowExecutionId, row.WorkId);
                if (request.ActivityExecutionIds is { } explicitIds && !explicitIds.Contains(item.Request.ActivityExecutionId, StringComparer.Ordinal))
                    continue;
                EfBackgroundActivityWorkStore.Copy(
                    row,
                    BackgroundActivityWorkLifecycle.Cancel(item, commit.Checkpoint.OccurredAt),
                    scope);
            }
        }
    }

    private static async ValueTask EnsureExecutionIsLiveAsync(
        RuntimeDbContext context,
        string workflowExecutionId,
        string scope,
        CancellationToken cancellationToken)
    {
        var id = EfWorkflowExecutionStateStore.CreateId(scope, workflowExecutionId);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var workflowHash = EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId);
        var workflowKey = EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId);
        var local = context.WorkflowExecutionStates.Local.FirstOrDefault(row => row.Id == id);
        if (local is not null && context.Entry(local).State == EntityState.Deleted)
            throw new RuntimeCheckpointCommitValidationException("Background activity work cannot be admitted for a deleted workflow execution.");

        // The workflow must already exist in the database before this checkpoint begins. A workflow upsert in the same
        // checkpoint is not proof that a deleted/missing execution is live and must not reopen admission after deletion.
        var row = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.Id == id &&
            candidate.ScopeKeyHash == scopeHash && candidate.ScopeKey == scopeKey &&
            candidate.WorkflowExecutionIdHash == workflowHash && candidate.WorkflowExecutionId == workflowKey,
            cancellationToken);
        if (row is null)
            throw new RuntimeCheckpointCommitValidationException("Background activity work cannot be admitted without a live workflow execution state.");
        _ = EfWorkflowExecutionStateStore.ReadChecked(row, scope, workflowExecutionId);
    }
}
