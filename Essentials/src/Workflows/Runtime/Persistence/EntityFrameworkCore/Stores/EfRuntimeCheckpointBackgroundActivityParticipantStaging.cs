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
}
