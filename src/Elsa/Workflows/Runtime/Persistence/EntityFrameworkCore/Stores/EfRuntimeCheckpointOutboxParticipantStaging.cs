using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Stages pending post-commit outbox rows in the checkpoint writer's existing EF transaction.</summary>
internal static class EfRuntimeCheckpointOutboxParticipantStaging
{
    public static async ValueTask StageAsync(
        BookmarkStateDbContext context,
        RuntimeCheckpointCommit commit,
        string scope,
        CancellationToken cancellationToken)
    {
        var staged = new Dictionary<string, RuntimePostCommitOutboxItem>(StringComparer.Ordinal);
        var scopeKey = EfRelationalIdentity.Encode(scope);
        var scopeHash = EfRelationalIdentity.Hash(scope);

        foreach (var change in commit.StateChanges.PostCommitOutbox)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = change.State;
            EfRuntimePostCommitOutboxStore.ValidatePending(candidate);
            if (staged.TryGetValue(change.StateId, out var duplicate))
            {
                if (!EfRuntimePostCommitOutboxStore.PendingItemsEquivalent(duplicate, candidate))
                    throw new InvalidOperationException(
                        $"Post-commit outbox item '{change.StateId}' occurs more than once with conflicting intent.");
                continue;
            }

            staged.Add(change.StateId, candidate);
            var id = EfRuntimePostCommitOutboxStore.RowId(scope, candidate.OutboxItemId);
            var existing = await context.RuntimePostCommitOutbox.AsNoTracking().SingleOrDefaultAsync(row =>
                row.Id == id && row.ScopeKey == scopeKey && row.ScopeKeyHash == scopeHash,
                cancellationToken);
            if (existing is null)
            {
                context.RuntimePostCommitOutbox.Add(EfRuntimePostCommitOutboxStore.ToEntity(candidate, scope, id, 1));
                continue;
            }

            var current = EfRuntimePostCommitOutboxStore.ReadChecked(existing, scope, candidate.OutboxItemId);
            if (!EfRuntimePostCommitOutboxStore.PendingItemsEquivalent(current, candidate))
                throw new InvalidOperationException(
                    $"Post-commit outbox item '{change.StateId}' already exists with conflicting intent or delivery state.");
        }
    }
}
