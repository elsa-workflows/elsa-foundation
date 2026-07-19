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
        OutboxItemId(commitId, intent.IntentId);

    /// <summary>Deterministic outbox item id when only the stable intent id is available.</summary>
    public static string OutboxItemId(string commitId, string intentId)
        => RuntimePostCommitOutboxIdentity.Create(commitId, intentId);

    /// <summary>Builds the pending outbox item for a single intent.</summary>
    public static RuntimePostCommitOutboxItem CreatePending(RuntimeCheckpointCommit commit, RuntimePostCommitIntent intent) =>
        CreatePending(commit, intent, []);

    /// <summary>Builds a pending item using the policy contributed for the intent kind.</summary>
    public static RuntimePostCommitOutboxItem CreatePending(
        RuntimeCheckpointCommit commit,
        RuntimePostCommitIntent intent,
        IEnumerable<RuntimePostCommitIntentHandlerContribution> contributions) =>
        new(
            outboxItemId: OutboxItemId(commit.CommitId, intent),
            intent: intent,
            status: RuntimePostCommitOutboxStatus.Pending,
            recordedAt: intent.RecordedAt,
            availableAt: commit.Checkpoint.OccurredAt,
            retryPolicy: ResolveRetryPolicy(intent.Kind, contributions),
            metadata: commit.Metadata);

    /// <summary>Builds the pending outbox change-set entries for all of a commit's post-commit intents.</summary>
    public static IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> CreatePendingChanges(RuntimeCheckpointCommit commit) =>
        CreatePendingChanges(commit, []);

    public static IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> CreatePendingChanges(
        RuntimeCheckpointCommit commit,
        IEnumerable<RuntimePostCommitIntentHandlerContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(contributions);
        var materialized = contributions.ToArray();
        ValidateContributions(materialized);
        return
        commit.PostCommitIntents
            .Select(intent =>
            {
                var item = CreatePending(commit, intent, materialized);
                return new RuntimeStateChange<RuntimePostCommitOutboxItem>(
                    item.OutboxItemId,
                    RuntimeStateChangeOperation.Upsert,
                    item,
                    commit.Metadata);
            })
            .ToArray();
    }

    private static RuntimePostCommitRetryPolicy ResolveRetryPolicy(
        string intentKind,
        IEnumerable<RuntimePostCommitIntentHandlerContribution> contributions) =>
        contributions.FirstOrDefault(contribution => StringComparer.Ordinal.Equals(contribution.IntentKind, intentKind))?.RetryPolicy
        ?? RuntimePostCommitRetryPolicy.None;

    private static void ValidateContributions(IReadOnlyCollection<RuntimePostCommitIntentHandlerContribution> contributions)
    {
        foreach (var group in contributions.GroupBy(contribution => contribution.IntentKind, StringComparer.Ordinal))
        {
            var first = group.First();
            if (group.Any(contribution =>
                    contribution.HandlerType != first.HandlerType ||
                    !contribution.RetryPolicy.IsEquivalentTo(first.RetryPolicy)))
            {
                throw new InvalidOperationException(
                    $"Runtime post-commit intent kind '{group.Key}' has conflicting handler or retry-policy contributions.");
            }
        }
    }
}
