using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Validator rules that no commit built through <see cref="RuntimeCheckpointCommitter"/> can break: the committer folds a
/// commit's outbox from the commit's own intents, so intents and outbox always agree there. These rules guard that
/// folding, and are exercised against the validator directly. Every rule a committer-built commit can break is covered
/// against every store by the checkpoint validation contract tests.
/// </summary>
public sealed class RuntimeCheckpointCommitValidatorTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private readonly RuntimePostCommitIntent _intent = Intent("intent-a", "test.intent");

    [Fact]
    public void Intents_with_their_folded_pending_outbox_are_valid()
    {
        var commit = Commit([_intent]);

        RuntimeCheckpointCommitValidator.Validate(commit with
        {
            StateChanges = commit.StateChanges.WithPostCommitOutbox(RuntimePostCommitOutboxItems.CreatePendingChanges(commit))
        });
    }

    [Fact]
    public void Intents_without_their_folded_outbox_are_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => RuntimeCheckpointCommitValidator.Validate(Commit([_intent])));

        Assert.Equal("A checkpoint with post-commit intents must include their pending outbox state changes in the same atomic unit.", exception.Message);
    }

    [Fact]
    public void A_folded_outbox_item_that_differs_from_its_intent_is_rejected()
    {
        var commit = Commit([_intent]);
        var folded = Assert.Single(RuntimePostCommitOutboxItems.CreatePendingChanges(commit));
        var substituted = folded with
        {
            State = new RuntimePostCommitOutboxItem(folded.StateId, Intent("intent-a", "other.kind"), RuntimePostCommitOutboxStatus.Pending, OccurredAt, OccurredAt)
        };

        var exception = Assert.Throws<InvalidOperationException>(() => RuntimeCheckpointCommitValidator.Validate(commit with
        {
            StateChanges = commit.StateChanges.WithPostCommitOutbox([substituted])
        }));

        Assert.Equal($"Post-commit outbox item '{folded.StateId}' does not match its checkpoint intent.", exception.Message);
    }

    [Fact]
    public void Conflicting_intents_with_the_same_identity_are_rejected()
    {
        var commit = Commit([_intent, Intent("intent-a", "other.kind")]);

        var exception = Assert.Throws<InvalidOperationException>(() => RuntimeCheckpointCommitValidator.Validate(commit));

        Assert.Equal(
            $"Post-commit intent '{RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, "intent-a")}' occurs more than once with conflicting content.",
            exception.Message);
    }

    private static RuntimeCheckpointCommit Commit(IReadOnlyList<RuntimePostCommitIntent> intents) => new(
        "commit-a",
        new RuntimeCheckpoint("checkpoint-a", "Checkpoint", "wfexec-1", OccurredAt, [], new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
        intents,
        new Dictionary<string, string>());

    private static RuntimePostCommitIntent Intent(string intentId, string kind) =>
        new(intentId, "wfexec-1", kind, OccurredAt, null, null, null);
}
