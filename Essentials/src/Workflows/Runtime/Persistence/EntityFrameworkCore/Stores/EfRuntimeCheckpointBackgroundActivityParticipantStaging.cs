using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Services.Executions;
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
            await EnsureExecutionIsLiveAsync(context, commit, commit.WorkflowExecutionId, scope, cancellationToken);

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
        RuntimeCheckpointCommit commit,
        string workflowExecutionId,
        string scope,
        CancellationToken cancellationToken)
    {
        var ownershipStateId = RuntimeExecutionOwnershipStateId.For(workflowExecutionId);
        var ownershipId = EfRuntimeOperationalStoreSupport.CompositeId(scope, workflowExecutionId, ownershipStateId);
        var ownership = context.ExecutionLivenessStates.Local.FirstOrDefault(row => row.Id == ownershipId)
            ?? throw new RuntimeCheckpointCommitValidationException("Background activity work admission requires the current durable workflow ownership fence.");
        var ownershipState = EfExecutionLivenessStateStore.Read(ownership, scope, workflowExecutionId, ownershipStateId);
        if (ownershipState.Metadata.TryGetValue(RuntimeMetadataKeys.WorkflowExecutionDeletionTombstone, out var deletionTombstone))
        {
            if (!StringComparer.Ordinal.Equals(deletionTombstone, "true"))
                throw new InvalidDataException("The persisted workflow-execution deletion tombstone is corrupt.");
            throw new RuntimeCheckpointCommitValidationException("Background activity work cannot be admitted for a deleted workflow execution.");
        }

        var id = EfWorkflowExecutionStateStore.CreateId(scope, workflowExecutionId);
        var scopeHash = EfRuntimeOperationalStoreSupport.Hash(scope);
        var scopeKey = EfRuntimeOperationalStoreSupport.Encode(scope);
        var workflowHash = EfRuntimeOperationalStoreSupport.Hash(workflowExecutionId);
        var workflowKey = EfRuntimeOperationalStoreSupport.Encode(workflowExecutionId);
        var local = context.WorkflowExecutionStates.Local.FirstOrDefault(row => row.Id == id);
        if (local is not null && context.Entry(local).State == EntityState.Deleted)
            throw new RuntimeCheckpointCommitValidationException("Background activity work cannot be admitted for a deleted workflow execution.");

        // Coalescing can fold the first workflow upsert and its background admission into the same immediate
        // checkpoint. StageWorkflowExecutionAsync has already added that scoped row to this context, but it is not
        // visible to a database query until SaveChanges. Permit only this exact same-commit Upsert; a missing row on
        // its own is still rejected, and the ownership tombstone above prevents post-delete recreation.
        if (local is not null && context.Entry(local).State is EntityState.Added or EntityState.Modified &&
            commit.StateChanges.WorkflowExecution is { Operation: RuntimeStateChangeOperation.Upsert } workflowChange &&
            StringComparer.Ordinal.Equals(workflowChange.StateId, workflowExecutionId))
            return;

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
