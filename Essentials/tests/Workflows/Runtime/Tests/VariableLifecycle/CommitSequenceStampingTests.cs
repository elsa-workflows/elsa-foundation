using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.VariableLifecycle;

/// <summary>
/// Spec 1221 FR-024 (T036): the committer numbers each logical checkpoint under the execution lease and stamps the
/// number on the write records and lifecycle transitions it commits.
/// </summary>
public sealed class CommitSequenceStampingTests
{
    private const string WorkflowExecutionId = "wfexec-1";
    private const string ActivityExecutionId = "actexec-1";
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Each_commit_under_a_lease_gets_a_growing_number_made_of_the_fencing_token_and_an_ordinal()
    {
        var (committer, store, accessor) = NewCommitter();
        using var _ = accessor.Push(Lease(fencingToken: 7));

        await committer.CommitAsync(Commit("commit-1", Activity(ActivityExecutionStatus.Running)));
        await committer.CommitAsync(Commit("commit-2", Activity(ActivityExecutionStatus.Completed, Record())));

        var started = Assert.Single(Transitions(store.Commits[0]));
        var transitions = Transitions(store.Commits[1]);
        Assert.Equal(ActivityLifecycleTransitionKind.Started, started.Kind);
        Assert.Equal(RuntimeCommitSequence.Compose(7, 1), started.CommitSequence);
        // The second commit's copy was built from the unstamped first copy: the lease's earlier stamp comes back.
        Assert.Equal([ActivityLifecycleTransitionKind.Started, ActivityLifecycleTransitionKind.Completed], transitions.Select(item => item.Kind));
        Assert.Equal(RuntimeCommitSequence.Compose(7, 2), transitions[1].CommitSequence);
        Assert.Equal(RuntimeCommitSequence.Compose(7, 2), Assert.Single(Activity(store.Commits[1]).VariableWrites!).CommitSequence);
        Assert.Equal(7, RuntimeCommitSequence.FencingTokenOf(transitions[1].CommitSequence));
    }

    [Fact]
    public async Task A_record_carried_into_a_later_commit_keeps_its_first_number()
    {
        var (committer, store, accessor) = NewCommitter();
        using var _ = accessor.Push(Lease(fencingToken: 1));
        var record = Record();

        await committer.CommitAsync(Commit("commit-1", Activity(ActivityExecutionStatus.Suspended, record)));
        await committer.CommitAsync(Commit("commit-2", Activity(ActivityExecutionStatus.Completed, record)));

        Assert.Equal(RuntimeCommitSequence.Compose(1, 1), Assert.Single(Activity(store.Commits[1]).VariableWrites!).CommitSequence);
        Assert.Equal(
            [ActivityLifecycleTransitionKind.Suspended, ActivityLifecycleTransitionKind.Completed],
            Transitions(store.Commits[1]).Select(item => item.Kind));
    }

    [Fact]
    public async Task The_same_commit_submitted_twice_gets_the_same_number()
    {
        var (committer, store, accessor) = NewCommitter();
        using var _ = accessor.Push(Lease(fencingToken: 3));
        var commit = Commit("commit-1", Activity(ActivityExecutionStatus.Completed, Record()));

        await committer.CommitAsync(commit);
        await committer.CommitAsync(commit);

        Assert.Equal(Transitions(store.Commits[0]), Transitions(store.Commits[1]));
        Assert.Equal(Activity(store.Commits[0]).VariableWrites, Activity(store.Commits[1]).VariableWrites);
    }

    [Fact]
    public async Task A_suspended_invocation_that_runs_again_is_resumed()
    {
        var (committer, store, accessor) = NewCommitter();
        using var _ = accessor.Push(Lease(fencingToken: 1));

        await committer.CommitAsync(Commit("commit-1", Activity(ActivityExecutionStatus.Running)));
        await committer.CommitAsync(Commit("commit-2", Activity(ActivityExecutionStatus.Suspended)));
        await committer.CommitAsync(Commit("commit-3", Activity(ActivityExecutionStatus.Running)));
        await committer.CommitAsync(Commit("commit-4", Activity(ActivityExecutionStatus.Running)));

        Assert.Equal(
            [ActivityLifecycleTransitionKind.Started, ActivityLifecycleTransitionKind.Suspended, ActivityLifecycleTransitionKind.Resumed],
            Transitions(store.Commits[3]).Select(item => item.Kind));
    }

    // A commit the store did not persist leaves no stamp behind for a later commit to merge in.
    [Fact]
    public async Task Stamps_of_a_commit_that_failed_are_not_merged_into_a_later_one()
    {
        var (committer, store, accessor) = NewCommitter();
        using var _ = accessor.Push(Lease(fencingToken: 1));
        store.FailNext = true;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await committer.CommitAsync(Commit("commit-1", Activity(ActivityExecutionStatus.Completed))));
        await committer.CommitAsync(Commit("commit-2", Activity(ActivityExecutionStatus.Faulted)));

        var transitions = Transitions(Assert.Single(store.Commits));
        Assert.Equal(ActivityLifecycleTransitionKind.Faulted, Assert.Single(transitions).Kind);
    }

    [Fact]
    public async Task A_commit_outside_a_lease_is_not_numbered()
    {
        var (committer, store, _) = NewCommitter();

        await committer.CommitAsync(Commit("commit-1", Activity(ActivityExecutionStatus.Completed, Record())));

        Assert.Null(Activity(store.Commits[0]).Transitions);
        Assert.Null(Assert.Single(Activity(store.Commits[0]).VariableWrites!).CommitSequence);
    }

    private static (RuntimeCheckpointCommitter Committer, RecordingStore Store, AsyncLocalRuntimeExecutionOwnershipContextAccessor Accessor) NewCommitter()
    {
        var store = new RecordingStore();
        var accessor = new AsyncLocalRuntimeExecutionOwnershipContextAccessor();
        return (new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), store, accessor, [], []), store, accessor);
    }

    private static RuntimeExecutionLease Lease(long fencingToken) =>
        new($"lease-{fencingToken}", WorkflowExecutionId, "owner-1", Now, Now.AddMinutes(5), fencingToken);

    private static RuntimeCheckpointCommit Commit(string commitId, ActivityExecutionState activity) => new(
        commitId,
        new RuntimeCheckpoint($"checkpoint-{commitId}", "Test", WorkflowExecutionId, Now, [], new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(null, null,
            [new RuntimeStateChange<ActivityExecutionState>(ActivityExecutionId, RuntimeStateChangeOperation.Upsert, activity, new Dictionary<string, string>())],
            [], [], [], []),
        [],
        new Dictionary<string, string>());

    private static ActivityExecutionState Activity(ActivityExecutionStatus status, VariableWriteRecord? record = null) =>
        new ActivityExecutionState(
            new ActivityExecution(ActivityExecutionId, WorkflowExecutionId, "node-1", "node-1", "Test.Activity", "1.0.0"),
            status,
            null,
            Now,
            Now,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            [],
            0,
            0,
            new Dictionary<string, string>()) with { VariableWrites = record is null ? null : [record] };

    private static VariableWriteRecord Record() =>
        new("scope-1", "count", new VariableWriter(VariableWriteSource.Capture, "node-1", ActivityExecutionId),
            VariableCommitOutcome.Current, baseVersion: 0, currentVersion: 0, newVersion: 1, commitOrder: 1, Now);

    private static ActivityExecutionState Activity(RuntimeCheckpointCommit commit) =>
        Assert.Single(commit.StateChanges.ActivityExecutions).State!;

    private static IReadOnlyList<ActivityLifecycleStamp> Transitions(RuntimeCheckpointCommit commit) => Activity(commit).Transitions!;

    private sealed class RecordingStore : IRuntimeCheckpointCommitStore
    {
        public List<RuntimeCheckpointCommit> Commits { get; } = [];
        public bool FailNext { get; set; }

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("The store did not persist the commit.");
            }

            Commits.Add(commit);
            return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]));
        }
    }
}
