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

        foreach (var change in commit.StateChanges.PostCommitOutbox)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = change.State;
            EfRuntimePostCommitOutboxStore.ValidatePending(candidate);
            if (staged.TryGetValue(change.StateId, out var duplicate))
            {
                if (!duplicate.IsEquivalentPendingItem(candidate))
                    throw new InvalidOperationException(
                        $"Post-commit outbox item '{change.StateId}' occurs more than once with conflicting intent.");
                continue;
            }

            staged.Add(change.StateId, candidate);
            var id = EfRuntimePostCommitOutboxStore.RowId(scope, candidate.OutboxItemId);
            // Only the physical identity participates in admission. ReadChecked must reject a persisted row with
            // damaged scope/content projections rather than letting a filtered query treat it as a missing row.
            var existing = await context.RuntimePostCommitOutbox.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
            if (existing is null)
            {
                context.RuntimePostCommitOutbox.Add(EfRuntimePostCommitOutboxStore.ToEntity(candidate, scope, id, 1));
                continue;
            }

            var current = EfRuntimePostCommitOutboxStore.ReadChecked(existing, scope, candidate.OutboxItemId);
            if (!current.IsEquivalentPendingItem(candidate))
                throw new InvalidOperationException(
                    $"Post-commit outbox item '{change.StateId}' already exists with a different intent or status.");
        }
    }
}
