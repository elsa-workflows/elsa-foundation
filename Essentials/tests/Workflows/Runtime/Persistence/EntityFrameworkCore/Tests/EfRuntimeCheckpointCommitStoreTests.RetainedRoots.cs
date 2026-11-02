using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Fact]
    public async Task Retained_root_update_avoids_a_new_lease_and_replay_still_uses_its_marker()
    {
        await using var database = await TestDatabase.CreateAsync();
        var lease = await SeedRetainedRootAsync(database);
        await using var context = database.Open("tenant-a");
        var sessions = new AsyncLocalRuntimeCoalescingSessionAccessor();
        using var hint = sessions.Push(RootHint());
        var manager = new PassThroughRootWriteLeaseManager { FailFirstAttempt = true };
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), manager)
        { CoalescingSessions = sessions };
        var commit = RetainedRootCommit(lease);
        await store.CommitAsync(commit, Decision());
        await store.CommitAsync(commit, Decision());
        Assert.Empty(manager.LeaseIds);
        await using var read = database.Open("tenant-a");
        var state = await new EfWorkflowExecutionStateStore(read, new FixedAccessor("tenant-a"), new NoopContinuationCodec()).FindAsync("workflow-a");
        Assert.Equal("new", state!.SystemMetadata["business-value"]);
        Assert.Equal(2, (await read.WorkflowExecutionStates.SingleAsync()).Revision);
        Assert.Single(await read.RuntimeCheckpointCommits.Where(row => row.CommitId == EfRelationalIdentity.Encode("retained-root-update")).ToArrayAsync());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("artifact")]
    [InlineData("hash")]
    [InlineData("no-record")]
    [InlineData("other-execution")]
    [InlineData("unfenced")]
    [InlineData("dirty-context")]
    public async Task Retained_root_hints_cannot_bypass_new_or_changed_roots_or_ineligible_calls(string scenario)
    {
        await using var database = await TestDatabase.CreateAsync();
        var lease = await SeedRetainedRootAsync(database, workflowExists: scenario != "missing");
        await using var context = database.Open("tenant-a");
        var sessions = new AsyncLocalRuntimeCoalescingSessionAccessor();
        using var hint = sessions.Push(RootHint(scenario != "no-record", scenario == "other-execution" ? "other" : "workflow-a"));
        var manager = new PassThroughRootWriteLeaseManager { FailFirstAttempt = true };
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), manager)
        { CoalescingSessions = sessions };
        var commit = RetainedRootCommit(lease);
        if (scenario is "artifact" or "hash")
        {
            var state = commit.StateChanges.WorkflowExecution!.State;
            var pin = scenario == "artifact" ? state.PinnedExecutable with { ArtifactId = "another" } : state.PinnedExecutable with { ArtifactHash = "another-hash" };
            commit = commit with { StateChanges = new(new("workflow-a", RuntimeStateChangeOperation.Upsert,
                state with { PinnedExecutable = pin }, new Dictionary<string, string>()), null, [], [], [], [], []) };
        }
        if (scenario == "unfenced") commit = commit with { ExpectedFence = null };
        if (scenario == "dirty-context") context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));
        await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseUnavailableException>(() => store.CommitAsync(commit, Decision()).AsTask());
        Assert.Single(manager.LeaseIds);
        await using var read = database.Open("tenant-a");
        Assert.Empty(await read.RuntimeCheckpointCommits.Where(row => row.CommitId == EfRelationalIdentity.Encode("retained-root-update")).ToArrayAsync());
        Assert.Equal(scenario == "missing" ? 0 : 1, await read.WorkflowExecutionStates.CountAsync());
        if (scenario != "missing") Assert.Equal(1, (await read.WorkflowExecutionStates.SingleAsync()).Revision);
        Assert.Empty(await read.SchedulerStates.ToArrayAsync());
    }

    [Fact]
    public async Task Retained_root_update_still_rejects_a_superseded_execution_fence()
    {
        await using var database = await TestDatabase.CreateAsync();
        var lease = await SeedRetainedRootAsync(database);
        await using var context = database.Open("tenant-a");
        var sessions = new AsyncLocalRuntimeCoalescingSessionAccessor();
        using var hint = sessions.Push(RootHint());
        var manager = new PassThroughRootWriteLeaseManager { FailFirstAttempt = true };
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), manager)
        { CoalescingSessions = sessions };
        var commit = RetainedRootCommit(lease) with { ExpectedFence = new(lease.LeaseId, lease.OwnerId, lease.FencingToken - 1) };
        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => store.CommitAsync(commit, Decision()).AsTask());
        Assert.Empty(manager.LeaseIds);
        Assert.Empty(await context.RuntimeCheckpointCommits.Where(row => row.CommitId == EfRelationalIdentity.Encode("retained-root-update")).ToArrayAsync());
        Assert.Equal(1, (await context.WorkflowExecutionStates.SingleAsync()).Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retained_root_cannot_overwrite_a_replaced_or_deleted_row_after_staging(bool delete)
    {
        await using var database = await TestDatabase.CreateAsync();
        var lease = await SeedRetainedRootAsync(database);
        var race = new ChangeRetainedRootBeforeSave(delete);
        await using var context = database.Open("tenant-a", race);
        var beforeFence = (await context.ExecutionLivenessStates.AsNoTracking().SingleAsync()).Revision;
        var sessions = new AsyncLocalRuntimeCoalescingSessionAccessor();
        using var hint = sessions.Push(RootHint());
        var manager = new PassThroughRootWriteLeaseManager { FailFirstAttempt = true };
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), manager)
        { CoalescingSessions = sessions };
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => store.CommitAsync(RetainedRootCommit(lease), Decision()).AsTask());
        Assert.True(race.Executed);
        Assert.Empty(manager.LeaseIds);
        await using var read = database.Open("tenant-a");
        Assert.Equal(1, (await read.WorkflowExecutionStates.SingleAsync()).Revision);
        Assert.Equal(beforeFence, (await read.ExecutionLivenessStates.SingleAsync()).Revision);
        Assert.Empty(await read.RuntimeCheckpointCommits.Where(row => row.CommitId == EfRelationalIdentity.Encode("retained-root-update")).ToArrayAsync());
    }

    [Fact]
    public async Task Retained_root_update_reconciles_an_uncertain_commit_without_reacquiring_a_lease()
    {
        await using var database = await TestDatabase.CreateAsync();
        var lease = await SeedRetainedRootAsync(database);
        var loss = new CommitAcknowledgementLossInterceptor();
        await using var context = database.Open("tenant-a", loss);
        var sessions = new AsyncLocalRuntimeCoalescingSessionAccessor();
        using var hint = sessions.Push(RootHint());
        var manager = new PassThroughRootWriteLeaseManager { FailFirstAttempt = true };
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), manager)
        { CoalescingSessions = sessions };
        loss.Arm();
        await store.CommitAsync(RetainedRootCommit(lease), Decision());
        Assert.Empty(manager.LeaseIds);
        await using var read = database.Open("tenant-a");
        Assert.Single(await read.RuntimeCheckpointCommits.Where(row => row.CommitId == EfRelationalIdentity.Encode("retained-root-update")).ToArrayAsync());
        Assert.Equal(2, (await read.WorkflowExecutionStates.SingleAsync()).Revision);
    }

    private static RuntimeCoalescingSession RootHint(bool exists = true, string executionId = "workflow-a") =>
        new(executionId, new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions(), mode: new(PersistenceMode.Durable, exists));

    private static async Task<RuntimeExecutionLease> SeedRetainedRootAsync(TestDatabase database, bool workflowExists = true)
    {
        var access = new FixedAccessor("tenant-a");
        await using var seed = database.Open("tenant-a");
        if (workflowExists)
        {
            var creation = Commit("retained-root-seed") with
            {
                StateChanges = new(new("workflow-a", RuntimeStateChangeOperation.Upsert, Execution("workflow-a", "tenant-a"),
                    new Dictionary<string, string>()), null, [], [], [], [], [])
            };
            await new EfRuntimeCheckpointCommitStore(seed, access, new FixedTimeProvider(OccurredAt), new PassThroughRootWriteLeaseManager())
                .CommitAsync(creation, Decision());
        }
        var lease = new RuntimeExecutionLease("retained-root-lease", "workflow-a", "owner", OccurredAt, OccurredAt.AddHours(1), 2);
        await new EfExecutionLivenessStateStore(seed, access, new NoopContinuationCodec()).SaveAsync(new("ownership:workflow-a", "workflow-a", lease, null, null, null));
        return lease;
    }

    private static RuntimeCheckpointCommit RetainedRootCommit(RuntimeExecutionLease lease) => Commit("retained-root-update") with
    {
        ExpectedFence = lease.ToFence(),
        StateChanges = new(new("workflow-a", RuntimeStateChangeOperation.Upsert,
            Execution("workflow-a", "tenant-a") with { SystemMetadata = new Dictionary<string, string> { ["business-value"] = "new" } },
            new Dictionary<string, string>()), null, [], [], [], [], [])
    };

    private sealed class ChangeRetainedRootBeforeSave(bool delete) : SaveChangesInterceptor
    {
        public bool Executed { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Executed)
            {
                Executed = true;
                var context = (RuntimeDbContext)eventData.Context!;
                if (delete) await context.WorkflowExecutionStates.ExecuteDeleteAsync(cancellationToken);
                else await context.WorkflowExecutionStates.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Revision, row => row.Revision + 1), cancellationToken);
            }
            return result;
        }
    }
}
