using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeCheckpointPreparedLedgerCapabilityDelegationTests
{
    [Fact]
    public async Task Coalescing_delegates_preparation_commit_and_paging_to_an_explicit_capability_provider()
    {
        Assert.All(
            new[] { nameof(IRuntimeCheckpointCommitStore.PrepareAsync), nameof(IRuntimeCheckpointCommitStore.CommitPreparedAsync) },
            operation => Assert.True(
                typeof(IRuntimeCheckpointPreparedLedgerStore).GetMethod(operation)!.IsAbstract,
                $"Prepared-ledger '{operation}' must override the generic commit-store compatibility default."));

        var durable = new CountingPreparedLedgerStore();
        var store = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(durable),
            new FixedSessionAccessor(null));
        var request = Request("commit-delegation");
        var prepared = await store.PrepareAsync(request);
        var commit = Attach(request.Commit, prepared.Token!);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);

        await store.CommitPreparedAsync(prepared.Token!, commit, decision);
        await store.PagePreparedAsync(new RuntimeCheckpointPreparedQuery(commit.WorkflowExecutionId, 1));

        Assert.Equal(1, durable.PrepareCalls);
        Assert.Equal(1, durable.CommitPreparedCalls);
        Assert.Equal(1, durable.PageCalls);
        Assert.Equal(0, durable.LegacyCommitCalls);
    }

    [Fact]
    public async Task Buffered_prepared_entries_fail_closed_at_a_boundary_without_using_legacy_commit_or_mutating_the_durable_provider()
    {
        var durable = new CountingPreparedLedgerStore();
        var session = new RuntimeCoalescingSession(
            "workflow-capability",
            new InMemoryWorkflowSchedulerWorkQueue(),
            new CoalescingRuntimeCheckpointPersistenceOptions { MaxSegmentCheckpoints = 10 });
        var store = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(durable),
            new FixedSessionAccessor(session));
        var request = Request("commit-buffered");

        var prepared = await store.PrepareAsync(request);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Deferred, prepared.Token!.InitialPersistenceMode);
        var deferred = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Deferred);
        await store.CommitPreparedAsync(prepared.Token, Attach(request.Commit, prepared.Token), deferred);

        Assert.Single(session.BufferedPreparedCommits);
        Assert.Equal(0, durable.LegacyCommitCalls);
        Assert.Equal(0, durable.CommitPreparedCalls);
        Assert.Equal(0, durable.MarkerOrStateMutationCalls);

        var boundaryRequest = Request("commit-boundary");
        var boundaryPrepared = await store.PrepareAsync(boundaryRequest);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CommitPreparedAsync(
            boundaryPrepared.Token!,
            Attach(boundaryRequest.Commit, boundaryPrepared.Token!),
            new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());

        Assert.Equal(0, durable.LegacyCommitCalls);
        Assert.Equal(0, durable.CommitPreparedCalls);
        Assert.Equal(0, durable.MarkerOrStateMutationCalls);
    }

    private static RuntimeCheckpointPrepareRequest Request(string commitId) =>
        new(
            new RuntimeCheckpointCommit(
                commitId,
                new RuntimeCheckpoint($"checkpoint-{commitId}", "Capability", "workflow-capability", DateTimeOffset.UnixEpoch, [], new Dictionary<string, string>()),
                new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
                [],
                new Dictionary<string, string>()),
            "capability-source",
            $"operation-{commitId}",
            RuntimeExecutionContextSnapshot.Empty);

    private static RuntimeCheckpointCommit Attach(RuntimeCheckpointCommit commit, RuntimeCheckpointPreparationToken token) =>
        commit with
        {
            Checkpoint = commit.Checkpoint with { Provenance = token.Provenance },
            ExpectedFence = token.ExpectedFence
        };

    private sealed class FixedSessionAccessor(RuntimeCoalescingSession? session) : IRuntimeCoalescingSessionAccessor
    {
        public RuntimeCoalescingSession? Current => session;
        public IDisposable Push(RuntimeCoalescingSession? pushedSession) => throw new NotSupportedException();
    }

    /// <summary>
    /// Deliberately implements each capability member itself: inheriting the generic commit-store compatibility
    /// defaults cannot satisfy the optional durable prepared-ledger contract.
    /// </summary>
    private sealed class CountingPreparedLedgerStore : IRuntimeCheckpointCommitStore, IRuntimeCheckpointPreparedLedgerStore
    {
        public int PrepareCalls { get; private set; }
        public int CommitPreparedCalls { get; private set; }
        public int LegacyCommitCalls { get; private set; }
        public int PageCalls { get; private set; }
        public int MarkerOrStateMutationCalls { get; private set; }

        public ValueTask<RuntimeCheckpointPreparationResult> PrepareAsync(RuntimeCheckpointPrepareRequest request, CancellationToken cancellationToken = default)
        {
            PrepareCalls++;
            var token = new RuntimeCheckpointPreparationToken(
                request.Commit.CommitId,
                $"ledger:{request.Commit.CommitId}",
                new RuntimeCheckpointProvenance(request.RequestedExecutionContext, PrepareCalls),
                PrepareCalls - 1,
                0,
                request.Commit.ExpectedFence,
                new RuntimeCheckpointPreparationPayloadCodec().Encode(request).PayloadSha256,
                $"canonical:{request.Commit.CommitId}",
                request.InitialPersistenceMode);
            return ValueTask.FromResult(RuntimeCheckpointPreparationResult.Prepared(token));
        }

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitPreparedAsync(
            RuntimeCheckpointPreparationToken token,
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            CommitPreparedCalls++;
            MarkerOrStateMutationCalls++;
            return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]));
        }

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            LegacyCommitCalls++;
            MarkerOrStateMutationCalls++;
            return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]));
        }

        public ValueTask<RuntimeCheckpointPreparedPage> PagePreparedAsync(RuntimeCheckpointPreparedQuery query, CancellationToken cancellationToken = default)
        {
            PageCalls++;
            return ValueTask.FromResult(new RuntimeCheckpointPreparedPage(query.Validate(), [], null));
        }

        public ValueTask<RuntimeCheckpointPreparedFoldResult> CommitPreparedFoldAsync(
            RuntimeCheckpointPreparedFoldRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
