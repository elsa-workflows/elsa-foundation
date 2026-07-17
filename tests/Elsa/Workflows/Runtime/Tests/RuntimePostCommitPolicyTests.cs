using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimePostCommitPolicyTests
{
    [Fact]
    public void ExistingConstructorsRemainAvailableAndDefaultToNoRetry()
    {
        Assert.NotNull(typeof(RuntimePostCommitRetryPolicy).GetConstructor(
            [typeof(int), typeof(TimeSpan?), typeof(IReadOnlyDictionary<string, string>)]));
        Assert.NotNull(typeof(RuntimePostCommitIntentHandlerContribution).GetConstructor(
            [typeof(string), typeof(Type)]));
        Assert.NotNull(typeof(RuntimePostCommitOutboxProcessor).GetConstructor(
        [
            typeof(IRuntimePostCommitOutboxStore),
            typeof(IRuntimePostCommitIntentDispatcher),
            typeof(TimeProvider),
            typeof(IRuntimeFaultCapturePolicy),
            typeof(IWorkflowDispatchStore)
        ]));

        var contribution = new RuntimePostCommitIntentHandlerContribution("Test.Marker", typeof(MarkerHandler));

        Assert.False(contribution.RetryPolicy.RetryUntilAcknowledged);
        Assert.Same(RuntimePostCommitRetryPolicy.None, contribution.RetryPolicy);
    }

    [Fact]
    public void LookupIsAdditiveAndDoesNotWidenTheBaseStoreContract()
    {
        Assert.DoesNotContain(typeof(IRuntimePostCommitOutboxStore).GetMethods(), method => method.Name == nameof(IPostCommitOutboxLookupStore.FindAsync));
        Assert.True(typeof(IPostCommitOutboxLookupStore).IsAssignableFrom(typeof(InMemoryRuntimeCheckpointCommitStore)));
    }

    [Fact]
    public void PendingOutboxItems_SelectContributedPolicyAndDefaultMissingKindToNone()
    {
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        var contributed = new RuntimePostCommitIntent(
            "intent-resume", "child-1", "Resume.Parent", now, null, "resume-1", null);
        var unsupported = new RuntimePostCommitIntent(
            "intent-unsupported", "child-1", "Unsupported.Kind", now, null, "unsupported-1", null);
        var commit = new RuntimeCheckpointCommit(
            "commit-1",
            new RuntimeCheckpoint("checkpoint-1", "Completed", "child-1", now, [], new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            [contributed, unsupported],
            new Dictionary<string, string>());
        var policy = RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(5));
        var contribution = new RuntimePostCommitIntentHandlerContribution("Resume.Parent", typeof(MarkerHandler), policy);

        var items = RuntimePostCommitOutboxItems.CreatePendingChanges(commit, [contribution])
            .Select(change => change.State)
            .ToDictionary(item => item.Intent.Kind, StringComparer.Ordinal);

        Assert.Same(policy, items["Resume.Parent"].RetryPolicy);
        Assert.Same(RuntimePostCommitRetryPolicy.None, items["Unsupported.Kind"].RetryPolicy);
        Assert.Equal("commit-1:intent-resume", RuntimePostCommitOutboxItems.OutboxItemId("commit-1", "intent-resume"));
    }

    private sealed class MarkerHandler : IRuntimePostCommitIntentHandler
    {
        public ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
