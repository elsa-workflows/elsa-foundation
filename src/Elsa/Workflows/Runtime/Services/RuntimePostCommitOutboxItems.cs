using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Single source of truth for translating a checkpoint commit's post-commit intents into pending outbox
/// items. The committer uses this to fold the items into the applied change-set so every persistence
/// provider applies them through the same uniform path — no provider re-derives item identity or lifecycle.
/// </summary>
public static class RuntimePostCommitOutboxItems
{
    /// <summary>Deterministic outbox item id for an intent within a commit. Stable across providers and replays.</summary>
    public static string OutboxItemId(string commitId, RuntimePostCommitIntent intent) =>
        $"{commitId}:{intent.IntentId}";

    /// <summary>Builds the pending outbox item for a single intent.</summary>
    public static RuntimePostCommitOutboxItem CreatePending(RuntimeCheckpointCommit commit, RuntimePostCommitIntent intent) =>
        new(
            outboxItemId: OutboxItemId(commit.CommitId, intent),
            intent: intent,
            status: RuntimePostCommitOutboxStatus.Pending,
            recordedAt: intent.RecordedAt,
            availableAt: commit.Checkpoint.OccurredAt,
            retryPolicy: RuntimePostCommitRetryPolicy.None,
            metadata: commit.Metadata);

    /// <summary>Builds the pending outbox change-set entries for all of a commit's post-commit intents.</summary>
    public static IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> CreatePendingChanges(RuntimeCheckpointCommit commit) =>
        commit.PostCommitIntents
            .Select(intent =>
            {
                var item = CreatePending(commit, intent);
                return new RuntimeStateChange<RuntimePostCommitOutboxItem>(
                    item.OutboxItemId,
                    RuntimeStateChangeOperation.Upsert,
                    item,
                    commit.Metadata);
            })
            .ToArray();
}
