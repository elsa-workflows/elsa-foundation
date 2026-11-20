using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Checkpoint_survives_its_own_lease_heartbeat_landing_between_fence_read_and_save(bool retainedRoot)
    {
        await using var database = await TestDatabase.CreateAsync();
        var lease = await SeedRetainedRootAsync(database);
        var heartbeat = new FenceRowChangeBeforeSave(delete: false);
        await using var context = database.Open("tenant-a", heartbeat);
        var beforeFence = (await context.ExecutionLivenessStates.AsNoTracking().SingleAsync()).Revision;
        var sessions = new AsyncLocalRuntimeCoalescingSessionAccessor();
        using var hint = retainedRoot ? sessions.Push(RootHint()) : null;
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), new PassThroughRootWriteLeaseManager())
        { CoalescingSessions = sessions };

        await store.CommitAsync(RetainedRootCommit(lease), Decision());

        Assert.Equal(1, heartbeat.Executions);
        await using var read = database.Open("tenant-a");
        Assert.Single(await read.RuntimeCheckpointCommits.Where(row => row.CommitId == EfRelationalIdentity.Encode("retained-root-update")).ToArrayAsync());
        Assert.Equal(2, (await read.WorkflowExecutionStates.SingleAsync()).Revision);
        // One renewal plus one fence touch from the retried checkpoint.
        Assert.Equal(beforeFence + 2, (await read.ExecutionLivenessStates.SingleAsync()).Revision);
    }

    [Fact]
    public async Task Checkpoint_retry_still_rejects_a_lease_that_disappeared_during_the_race()
    {
        await using var database = await TestDatabase.CreateAsync();
        var lease = await SeedRetainedRootAsync(database);
        var loss = new FenceRowChangeBeforeSave(delete: true);
        await using var context = database.Open("tenant-a", loss);
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), new PassThroughRootWriteLeaseManager());

        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => store.CommitAsync(RetainedRootCommit(lease), Decision()).AsTask());

        Assert.Equal(1, loss.Executions);
        await using var read = database.Open("tenant-a");
        Assert.Empty(await read.RuntimeCheckpointCommits.Where(row => row.CommitId == EfRelationalIdentity.Encode("retained-root-update")).ToArrayAsync());
        Assert.Equal(1, (await read.WorkflowExecutionStates.SingleAsync()).Revision);
    }

    [Fact]
    public async Task Checkpoint_with_caller_staged_changes_does_not_hide_a_fence_conflict_behind_a_retry()
    {
        await using var database = await TestDatabase.CreateAsync();
        var lease = await SeedRetainedRootAsync(database);
        var heartbeat = new FenceRowChangeBeforeSave(delete: false);
        await using var context = database.Open("tenant-a", heartbeat);
        context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), new PassThroughRootWriteLeaseManager());

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => store.CommitAsync(RetainedRootCommit(lease), Decision()).AsTask());

        Assert.Equal(1, heartbeat.Executions);
        await using var read = database.Open("tenant-a");
        Assert.Empty(await read.RuntimeCheckpointCommits.Where(row => row.CommitId == EfRelationalIdentity.Encode("retained-root-update")).ToArrayAsync());
        Assert.Empty(await read.SchedulerStates.ToArrayAsync());
    }

    // A concurrent heartbeat (or lease removal) committed after this checkpoint read its fence: the first save sees a
    // superseded fence revision and affects no row. The change is applied, durably, by the first statement this test
    // database runs outside a transaction after that attempt rolled back, as another connection's commit would be.
    private sealed class FenceRowChangeBeforeSave(bool delete) : ISaveChangesInterceptor, IDbCommandInterceptor
    {
        private bool pending;
        public int Executions { get; private set; }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var fence = eventData.Context!.ChangeTracker.Entries<Entities.ExecutionLivenessStateEntity>().SingleOrDefault(entry => entry.State == EntityState.Modified);
            if (Executions == 0 && fence is not null)
            {
                Executions++;
                pending = true;
                fence.Property(row => row.Revision).OriginalValue = fence.Property(row => row.Revision).OriginalValue - 1;
            }
            return ValueTask.FromResult(result);
        }

        public async ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(System.Data.Common.DbCommand command, CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (pending && command.Transaction is null)
            {
                pending = false;
                await using var change = command.Connection!.CreateCommand();
                change.CommandText = delete
                    ? $"DELETE FROM \"{RuntimeOperationalStateEfModule.ExecutionLivenessTableName}\""
                    : $"UPDATE \"{RuntimeOperationalStateEfModule.ExecutionLivenessTableName}\" SET \"Revision\" = \"Revision\" + 1";
                Assert.Equal(1, await change.ExecuteNonQueryAsync(cancellationToken));
            }
            return result;
        }
    }
}
