using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeExecutionOwnershipTests
{
    private const string WorkflowExecutionId = "wfexec-1";
    private readonly DateTimeOffset _now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AcquireAsync_IssuesStrictlyIncreasingFencingTokens()
    {
        var ownership = NewOwnership(out _, out _);

        var first = await ownership.AcquireAsync(WorkflowExecutionId);
        var second = await ownership.AcquireAsync(WorkflowExecutionId);
        var third = await ownership.AcquireAsync(WorkflowExecutionId);

        Assert.Equal(1, first.FencingToken);
        Assert.Equal(2, second.FencingToken);
        Assert.Equal(3, third.FencingToken);
    }

    [Fact]
    public async Task ConcurrentIndependentServices_IssueUniqueStrictlyIncreasingFencingTokens()
    {
        var store = new InMemoryExecutionLivenessStateStore();
        var clock = new FixedTimeProvider(_now);
        var first = new RuntimeExecutionOwnershipService(
            store,
            clock,
            new RuntimeExecutionOwnershipOptions { OwnerId = "owner-a", LeaseDuration = TimeSpan.FromMinutes(1) });
        var second = new RuntimeExecutionOwnershipService(
            store,
            clock,
            new RuntimeExecutionOwnershipOptions { OwnerId = "owner-b", LeaseDuration = TimeSpan.FromMinutes(1) });

        var leases = await Task.WhenAll(
            Enumerable.Range(0, 64)
                .Select(index => (index & 1) == 0 ? first.AcquireAsync(WorkflowExecutionId).AsTask() : second.AcquireAsync(WorkflowExecutionId).AsTask()));

        Assert.Equal(Enumerable.Range(1, 64).Select(x => (long)x), leases.Select(x => x.FencingToken).Order());
        var current = (await store.FindAsync(WorkflowExecutionId, $"ownership:{WorkflowExecutionId}"))!.ExecutionLease!;
        Assert.Equal(64, current.FencingToken);
    }

    [Fact]
    public async Task AcquireAsync_AfterRelease_NeverReusesAToken()
    {
        var ownership = NewOwnership(out _, out _);

        var first = await ownership.AcquireAsync(WorkflowExecutionId);
        await ownership.ReleaseAsync(first);
        var second = await ownership.AcquireAsync(WorkflowExecutionId);

        Assert.True(second.FencingToken > first.FencingToken, "Release must preserve the counter so tokens are never reused.");
        Assert.Equal(2, second.FencingToken);
    }

    [Fact]
    public async Task AcquireAsync_RejectsCounterThatDoesNotMatchTheActiveLease()
    {
        var store = new InMemoryExecutionLivenessStateStore();
        await store.SaveAsync(new ExecutionLivenessState(
            operationalStateId: $"ownership:{WorkflowExecutionId}",
            workflowExecutionId: WorkflowExecutionId,
            executionLease: new RuntimeExecutionLease(
                leaseId: "lease-corrupt",
                workflowExecutionId: WorkflowExecutionId,
                ownerId: "owner-corrupt",
                acquiredAt: _now,
                expiresAt: _now.AddMinutes(1),
                fencingToken: 2),
            heartbeat: null,
            drain: null,
            interruptedExecution: null,
            metadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.OwnershipFencingToken] = "1"
            }));
        var ownership = new RuntimeExecutionOwnershipService(
            store,
            new FixedTimeProvider(_now),
            new RuntimeExecutionOwnershipOptions { OwnerId = "owner-under-test", LeaseDuration = TimeSpan.FromMinutes(1) });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ownership.AcquireAsync(WorkflowExecutionId).AsTask());

        Assert.Contains("does not match its active lease", exception.Message, StringComparison.Ordinal);
        var unchanged = await store.FindAsync(WorkflowExecutionId, $"ownership:{WorkflowExecutionId}");
        Assert.Equal(2, unchanged!.ExecutionLease!.FencingToken);
    }

    [Fact]
    public async Task EnsureCurrentAsync_PassesForCurrentOwner_AndRejectsStaleToken()
    {
        var ownership = NewOwnership(out _, out _);

        var lease = await ownership.AcquireAsync(WorkflowExecutionId);

        await ownership.EnsureCurrentAsync(WorkflowExecutionId, lease.FencingToken);

        var exception = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => ownership.EnsureCurrentAsync(WorkflowExecutionId, lease.FencingToken - 1).AsTask());
        Assert.Equal(lease.FencingToken, exception.CurrentFencingToken);
        Assert.Equal(lease.FencingToken - 1, exception.PresentedFencingToken);
    }

    [Fact]
    public async Task EnsureCurrentAsync_RejectsPreReleaseWriter_AfterReAcquire()
    {
        var ownership = NewOwnership(out _, out _);

        var stale = await ownership.AcquireAsync(WorkflowExecutionId);
        await ownership.ReleaseAsync(stale);
        var current = await ownership.AcquireAsync(WorkflowExecutionId);

        await ownership.EnsureCurrentAsync(WorkflowExecutionId, current.FencingToken);
        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => ownership.EnsureCurrentAsync(WorkflowExecutionId, stale.FencingToken).AsTask());
    }

    [Fact]
    public async Task EnsureCurrentAsync_RejectsReleasedLease_BeforeReAcquire()
    {
        var ownership = NewOwnership(out _, out _);

        var released = await ownership.AcquireAsync(WorkflowExecutionId);
        await ownership.ReleaseAsync(released);

        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => ownership.EnsureCurrentAsync(WorkflowExecutionId, released.FencingToken).AsTask());
    }

    [Fact]
    public async Task StaleHeartbeat_DoesNotReplaceSuccessorLease()
    {
        var ownership = NewOwnership(out var store, out _);
        var stale = await ownership.AcquireAsync(WorkflowExecutionId);
        var current = await ownership.AcquireAsync(WorkflowExecutionId);

        var result = await ownership.HeartbeatAsync(stale);

        Assert.Equal(RuntimeExecutionOwnershipTransitionStatus.Stale, result.Status);
        var state = await store.FindAsync(WorkflowExecutionId, $"ownership:{WorkflowExecutionId}");
        Assert.Equal(current.LeaseId, state!.ExecutionLease!.LeaseId);
        Assert.Equal(current.FencingToken, state.ExecutionLease.FencingToken);
    }

    [Fact]
    public async Task StaleRelease_DoesNotClearSuccessorLease()
    {
        var ownership = NewOwnership(out var store, out _);
        var stale = await ownership.AcquireAsync(WorkflowExecutionId);
        var current = await ownership.AcquireAsync(WorkflowExecutionId);

        var result = await ownership.ReleaseAsync(stale);

        Assert.Equal(RuntimeExecutionOwnershipTransitionStatus.Stale, result.Status);
        var state = await store.FindAsync(WorkflowExecutionId, $"ownership:{WorkflowExecutionId}");
        Assert.Equal(current.LeaseId, state!.ExecutionLease!.LeaseId);
        Assert.Equal(current.FencingToken, state.ExecutionLease.FencingToken);
    }

    [Fact]
    public async Task EnsureCurrentAsync_IsNoOp_WhenOwnershipNeverEstablished()
    {
        var ownership = NewOwnership(out _, out _);

        // No acquisition for this execution: nothing to fence against.
        await ownership.EnsureCurrentAsync("wfexec-unowned", 1);
    }

    [Fact]
    public async Task EnsureCurrentAsync_RejectsExpiredLeaseBeforeTakeover()
    {
        var store = new InMemoryExecutionLivenessStateStore();
        var clock = new MutableTimeProvider(_now);
        var ownership = new RuntimeExecutionOwnershipService(
            store,
            clock,
            new RuntimeExecutionOwnershipOptions
            {
                OwnerId = "owner-under-test",
                LeaseDuration = TimeSpan.FromMinutes(1)
            });
        var lease = await ownership.AcquireAsync(WorkflowExecutionId);
        clock.Now = lease.ExpiresAt;

        var exception = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => ownership.EnsureCurrentAsync(WorkflowExecutionId, lease.FencingToken).AsTask());

        Assert.Equal(RuntimeFencingRejectionReason.ExpiredLease, exception.Reason);
    }

    [Fact]
    public async Task Committer_FencesStaleWriter_AndAdmitsCurrentWriter()
    {
        var ownership = NewOwnership(out var livenessStore, out _);
        var accessor = new AsyncLocalRuntimeExecutionOwnershipContextAccessor();
        var commitStore = new InMemoryRuntimeCheckpointCommitStore(
            operationalStateStore: livenessStore,
            timeProvider: new FixedTimeProvider(_now));
        var committer = new RuntimeCheckpointCommitter(
            new ImmediateRuntimeCheckpointPersistencePolicy(),
            commitStore,
            accessor);

        // Two racing writers: A acquires first, then B supersedes it.
        var staleWriter = await ownership.AcquireAsync(WorkflowExecutionId);
        var currentWriter = await ownership.AcquireAsync(WorkflowExecutionId);
        Assert.True(currentWriter.FencingToken > staleWriter.FencingToken);

        using (accessor.Push(staleWriter))
        {
            await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
                () => committer.CommitAsync(NewCommit("commit-stale")).AsTask());
        }

        Assert.Empty(commitStore.ListCommits());

        RuntimeCheckpointCommitResult result;
        using (accessor.Push(currentWriter))
        {
            result = await committer.CommitAsync(NewCommit("commit-current"));
        }

        Assert.True(result.Succeeded);
        var persisted = Assert.Single(commitStore.ListCommits());
        Assert.Equal("commit-current", persisted.Commit.CommitId);
    }

    [Fact]
    public async Task Committer_WithoutOwnershipScope_IsBehaviorPreserving()
    {
        var ownership = NewOwnership(out _, out _);
        var accessor = new AsyncLocalRuntimeExecutionOwnershipContextAccessor();
        var commitStore = new InMemoryRuntimeCheckpointCommitStore();
        var committer = new RuntimeCheckpointCommitter(
            new ImmediateRuntimeCheckpointPersistencePolicy(),
            commitStore,
            accessor);

        // No active scope pushed: the fencing check is skipped entirely.
        var result = await committer.CommitAsync(NewCommit("commit-unscoped"));

        Assert.True(result.Succeeded);
        Assert.Single(commitStore.ListCommits());
    }

    [Fact]
    public async Task Acquire_LeavesLease_ThatRecoveryScannerDetects_WhenNotReleased()
    {
        var ownership = NewOwnership(out var store, out var options);

        // A writer acquires and then "crashes": the drain never releases, leaving the lease in operational state.
        await ownership.AcquireAsync(WorkflowExecutionId);
        var scanner = new InMemoryRuntimeRecoveryScanner(store);

        // Scan after the lease would have expired — this is W2's window C: dequeue-delete happened, no checkpoint
        // commit, and the interrupted execution must be recoverable via the persisted lease.
        var request = new RuntimeRecoveryScanRequest(
            now: _now + options.LeaseDuration + TimeSpan.FromSeconds(1),
            leaseTimeout: options.LeaseDuration,
            heartbeatTimeout: options.LeaseDuration,
            limit: 10);

        var candidates = await scanner.ScanAsync(request);

        var candidate = Assert.Single(candidates);
        Assert.Equal(WorkflowExecutionId, candidate.WorkflowExecutionId);
    }

    [Fact]
    public async Task Release_ClearsLease_SoRecoveryScannerDoesNotFlagCompletedExecution()
    {
        var ownership = NewOwnership(out var store, out var options);

        var lease = await ownership.AcquireAsync(WorkflowExecutionId);
        await ownership.ReleaseAsync(lease);
        var scanner = new InMemoryRuntimeRecoveryScanner(store);

        var request = new RuntimeRecoveryScanRequest(
            now: _now + options.LeaseDuration + TimeSpan.FromSeconds(1),
            leaseTimeout: options.LeaseDuration,
            heartbeatTimeout: options.LeaseDuration,
            limit: 10);

        var candidates = await scanner.ScanAsync(request);

        Assert.Empty(candidates);
    }

    private RuntimeExecutionOwnershipService NewOwnership(out InMemoryExecutionLivenessStateStore store, out RuntimeExecutionOwnershipOptions options)
    {
        store = new InMemoryExecutionLivenessStateStore();
        options = new RuntimeExecutionOwnershipOptions
        {
            OwnerId = "owner-under-test",
            LeaseDuration = TimeSpan.FromMinutes(1)
        };
        return new RuntimeExecutionOwnershipService(store, new FixedTimeProvider(_now), options);
    }

    private RuntimeCheckpointCommit NewCommit(string commitId) =>
        new(
            CommitId: commitId,
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint:{commitId}",
                Name: "test-checkpoint",
                WorkflowExecutionId: WorkflowExecutionId,
                OccurredAt: _now,
                ActivityExecutionIds: [],
                Metadata: new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: []),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
