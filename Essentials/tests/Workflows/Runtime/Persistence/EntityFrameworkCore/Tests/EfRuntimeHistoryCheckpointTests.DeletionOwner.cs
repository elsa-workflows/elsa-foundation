using System.Data.Common;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeHistoryCheckpointTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_native_deletion_retires_all_history_shapes_and_prevents_observation_recreation(bool terminal)
    {
        await using var db = await CapturedDeletionDatabase(); await using var context = db.Open(); var access = new Accessor();
        var executions = new EfWorkflowExecutionStateStore(context, access, MutationCodec);
        if (terminal)
            Assert.Equal(WorkflowExecutionDeletionResult.Deleted, await executions.TryDeleteTerminalAsync((await executions.ReadDeletionCandidateAsync(Run))!));
        else Assert.True(await executions.DeleteAsync(Run));
        Assert.Null(await executions.FindAsync(Run)); Assert.True(await executions.IsDeletionTombstonedAsync(Run));
        await AssertDeletedHistory(context);
        Assert.False((await MutationOwner(context, access).RecordAsync(Observation("after-deletion"))).Recorded);
        Assert.Equal(0, await MutationOwner(context, access).PurgeThroughAsync(Run, long.MaxValue));
        await AssertDeletedHistory(context);
    }

    [Fact]
    public async Task Native_deletion_removes_purged_receipts_that_prefix_retention_deliberately_keeps()
    {
        await using var db = await CapturedDeletionDatabase(); await using var context = db.Open();
        await MutationOwner(context).PurgeThroughAsync(Run, long.MaxValue);
        Assert.NotEmpty(await context.Set<RuntimeHistoryEventEntity>().ToArrayAsync()); context.ChangeTracker.Clear();
        Assert.True(await new EfWorkflowExecutionStateStore(context, new Accessor(), MutationCodec).DeleteAsync(Run));
        await AssertDeletedHistory(context);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task History_delete_sql_failure_preserves_native_root_ownership_and_every_history_shape(bool terminal)
    {
        await using var db = await CapturedDeletionDatabase(); await using var context = db.Open();
        var executions = new EfWorkflowExecutionStateStore(context, new Accessor(), MutationCodec);
        await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER refuse_history_retirement BEFORE DELETE ON elsa_runtime_history_event BEGIN SELECT RAISE(ABORT, 'history retirement refused'); END;");
        if (terminal)
        {
            var candidate = (await executions.ReadDeletionCandidateAsync(Run))!;
            await Assert.ThrowsAsync<WorkflowExecutionStateEntityFrameworkPersistenceException>(() => executions.TryDeleteTerminalAsync(candidate).AsTask());
        }
        else await Assert.ThrowsAsync<WorkflowExecutionStateEntityFrameworkPersistenceException>(() => executions.DeleteAsync(Run).AsTask());
        Assert.Empty(context.ChangeTracker.Entries()); Assert.Null(context.Database.CurrentTransaction);
        Assert.NotNull(await executions.FindAsync(Run)); Assert.False(await executions.IsDeletionTombstonedAsync(Run));
        await AssertRetainedHistory(context); context.ChangeTracker.Clear();
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER refuse_history_retirement;");
        Assert.True(await executions.DeleteAsync(Run)); await AssertDeletedHistory(context);
    }

    [Fact]
    public async Task Native_delete_failure_after_history_retirement_rolls_back_the_complete_history_family()
    {
        await using var db = await CapturedDeletionDatabase(); await using var context = db.Open();
        var executions = new EfWorkflowExecutionStateStore(context, new Accessor(), MutationCodec);
        await context.Database.ExecuteSqlRawAsync($"CREATE TRIGGER refuse_native_retirement BEFORE DELETE ON {RuntimeWorkflowExecutionEfModule.TableName} BEGIN SELECT RAISE(ABORT, 'native retirement refused'); END;");
        await Assert.ThrowsAsync<WorkflowExecutionStateEntityFrameworkPersistenceException>(() => executions.DeleteAsync(Run).AsTask());
        Assert.Empty(context.ChangeTracker.Entries()); Assert.NotNull(await executions.FindAsync(Run));
        Assert.False(await executions.IsDeletionTombstonedAsync(Run)); await AssertRetainedHistory(context);
    }

    [Fact]
    public async Task Access_retirement_during_set_based_history_delete_rolls_back_and_cleans_staged_native_tombstone()
    {
        await using var db = await CapturedDeletionDatabase(); var access = new Accessor();
        await using (var context = db.Open(new RetireDeletionAccess(access)))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new EfWorkflowExecutionStateStore(context, access, MutationCodec).DeleteAsync(Run).AsTask());
            Assert.Empty(context.ChangeTracker.Entries()); Assert.Null(context.Database.CurrentTransaction);
        }
        await using var read = db.Open(); Assert.NotNull(await new EfWorkflowExecutionStateStore(read, new Accessor(), MutationCodec).FindAsync(Run));
        await AssertRetainedHistory(read);
    }

    [Fact]
    public async Task Native_deletion_keeps_the_same_run_identity_in_a_case_distinct_foreign_scope()
    {
        await using var db = await CapturedDeletionDatabase(); const string foreignScope = "Tenant";
        var foreignAccess = new Accessor { Current = PersistenceAccessContext.Scoped(new(foreignScope)) };
        await using (var seed = db.Open())
        {
            seed.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(Workflow(Policy, WorkflowExecutionStatus.Completed) with { TenantId = foreignScope },
                foreignScope, EfWorkflowExecutionStateStore.CreateId(foreignScope, Run), 1));
            await seed.SaveChangesAsync(); await using var transaction = await seed.Database.BeginTransactionAsync();
            await EfRuntimeHistoryCheckpoint.StageAsync(seed, foreignAccess, DeletionHistoryCommit(), foreignScope, Now, Chain, CancellationToken.None);
            await seed.SaveChangesAsync(); await transaction.CommitAsync();
        }
        await using var context = db.Open(); Assert.True(await new EfWorkflowExecutionStateStore(context, new Accessor(), MutationCodec).DeleteAsync(Run));
        Assert.NotNull(await new EfWorkflowExecutionStateStore(context, foreignAccess, MutationCodec).FindAsync(Run));
        await AssertRetainedHistory(context);
        var retained = context.ChangeTracker.Entries().Where(entry => entry.Metadata.ClrType.Name.StartsWith("RuntimeHistory", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(retained);
        Assert.All(retained, entry => Assert.Equal(Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode(foreignScope), entry.Property("ScopeKey").CurrentValue));
        Assert.Single(await context.Set<RuntimeHistoryHeadEntity>().ToArrayAsync());
    }

    [Fact]
    public async Task Uncaptured_direct_root_deletion_remains_independent_of_absent_history_storage()
    {
        await using var db = await Database.CreateAsync(explicitLegacy: true); await using var context = db.Open();
        await context.Database.ExecuteSqlRawAsync("DROP TABLE elsa_runtime_history_head;");
        var executions = new EfWorkflowExecutionStateStore(context, new Accessor(), MutationCodec);
        Assert.True(await executions.DeleteAsync(Run)); Assert.Null(await executions.FindAsync(Run));
    }

    private static async Task<Database> CapturedDeletionDatabase()
    {
        var db = await Database.CreateAsync(status: WorkflowExecutionStatus.Completed);
        await PersistAsync(db, DeletionHistoryCommit());
        await using var read = db.Open(); await AssertRetainedHistory(read); return db;
    }
    private static RuntimeCheckpointCommit DeletionHistoryCommit() => Commit("native-delete-seed", [Batch("native-delete-seed",
            Candidate("native-delete-seed", "delete-started", 0),
            Candidate("native-delete-seed", "delete-bookmark", 1, new(Bookmark(), RuntimeHistoryBookmarkStatus.Active)))]);

    private static async Task AssertRetainedHistory(RuntimeDbContext context)
    {
        Assert.NotEmpty(await context.Set<RuntimeHistoryHeadEntity>().ToArrayAsync()); Assert.NotEmpty(await context.Set<RuntimeHistoryEventEntity>().ToArrayAsync());
        Assert.NotEmpty(await context.Set<RuntimeHistoryBookmarkEntity>().ToArrayAsync()); Assert.NotEmpty(await context.Set<RuntimeHistoryBookmarkCountEntity>().ToArrayAsync());
        Assert.NotEmpty(await context.Set<RuntimeHistoryActivityEntity>().ToArrayAsync()); Assert.NotEmpty(await context.Set<RuntimeHistoryOccurrenceEntity>().ToArrayAsync());
        Assert.NotEmpty(await context.Set<RuntimeHistoryOccurrenceMembershipEntity>().ToArrayAsync()); Assert.NotEmpty(await context.Set<RuntimeHistoryOccurrenceCounterEntity>().ToArrayAsync());
    }
    private static async Task AssertDeletedHistory(RuntimeDbContext context)
    {
        Assert.Empty(await context.Set<RuntimeHistoryHeadEntity>().ToArrayAsync()); Assert.Empty(await context.Set<RuntimeHistoryEventEntity>().ToArrayAsync());
        Assert.Empty(await context.Set<RuntimeHistoryBookmarkEntity>().ToArrayAsync()); Assert.Empty(await context.Set<RuntimeHistoryBookmarkCountEntity>().ToArrayAsync());
        Assert.Empty(await context.Set<RuntimeHistoryActivityEntity>().ToArrayAsync()); Assert.Empty(await context.Set<RuntimeHistoryOccurrenceEntity>().ToArrayAsync());
        Assert.Empty(await context.Set<RuntimeHistoryOccurrenceMembershipEntity>().ToArrayAsync()); Assert.Empty(await context.Set<RuntimeHistoryOccurrenceCounterEntity>().ToArrayAsync());
    }
    private sealed class RetireDeletionAccess(Accessor access) : DbCommandInterceptor
    {
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("elsa_runtime_history_", StringComparison.Ordinal))
                access.Current = PersistenceAccessContext.Scoped(new(Scope));
            return ValueTask.FromResult(result);
        }
    }
}
