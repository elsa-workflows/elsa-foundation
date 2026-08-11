using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeCheckpointEnricherOrderTests
{
    [Fact]
    public async Task CommitterExecutesEnrichersByOrderThenRegistrationOrder()
    {
        var calls = new List<string>();
        var store = new RecordingCommitStore();
        var committer = new RuntimeCheckpointCommitter(
            new ImmediateRuntimeCheckpointPersistencePolicy(),
            store,
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),
            enrichers:
            [
                new RecordingEnricher("late-first", 100, calls),
                new RecordingEnricher("base-first", 0, calls),
                new RecordingEnricher("base-second", 0, calls)
            ],
            intentHandlerContributions: []);
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        var commit = new RuntimeCheckpointCommit(
            "commit-order",
            new RuntimeCheckpoint("checkpoint-order", "Order", "workflow-order", now, [], new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
            [],
            new Dictionary<string, string>());

        var result = await committer.CommitAsync(commit);

        Assert.True(result.Succeeded);
        Assert.Equal(["base-first", "base-second", "late-first"], calls);
        Assert.Same(commit, store.Commit);
    }

    private sealed class RecordingEnricher(string name, int order, ICollection<string> calls)
        : IRuntimeCheckpointCommitEnricher
    {
        public int Order { get; } = order;

        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(
            RuntimeCheckpointCommit commit,
            CancellationToken cancellationToken = default)
        {
            calls.Add(name);
            return ValueTask.FromResult(commit);
        }
    }

    private sealed class RecordingCommitStore : IRuntimeCheckpointCommitStore
    {
        public RuntimeCheckpointCommit? Commit { get; private set; }

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            Commit = commit;
            return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]));
        }
    }
}
