using System.Data;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The production shared-transaction owner, proven with the real module contexts it enlists: one owner
/// controls the connection and transaction, constructs and disposes the enlisted contexts, commits or rolls
/// back once, and refuses split targets and provider mismatches.
/// </summary>
public sealed class EfSharedTransactionTests : IAsyncLifetime
{
    private readonly MutableAccess access = MutableAccess.Tenant("tenant-a");
    private SqliteImportHarness harness = null!;

    private ImportDatabase Db => harness.Database;

    public async Task InitializeAsync() => harness = await SqliteImportHarness.CreateAsync();

    public async Task DisposeAsync() => await harness.DisposeAsync();

    [Fact]
    public async Task Enlisted_contexts_are_fresh_share_one_connection_and_transaction_and_commit_together()
    {
        DbContext[] configured = [Db.Import(), Db.Activities(), Db.Workflows()];
        var shared = await EfSharedTransaction.BeginAsync(configured);
        var connection = shared.Connection;
        DbContext[] enlisted = [shared.Context<Elsa3ImportDbContext>(), shared.Context<ActivitiesDesignDbContext>(), shared.Context<WorkflowsDesignDbContext>()];

        Assert.All(enlisted, context =>
        {
            Assert.DoesNotContain(context, configured);
            Assert.Same(connection, context.Database.GetDbConnection());
            Assert.Same(shared.Transaction, context.Database.CurrentTransaction!.GetDbTransaction());
        });
        await WriteOneRowPerLaneAsync(shared);
        await shared.CommitAsync();
        await shared.DisposeAsync();

        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Null(shared.CleanupFailure);
        var counts = await Db.CountAsync();
        Assert.Equal((1, 1), (counts.ActivityDefinitions, counts.WorkflowDefinitions));
        Assert.All(configured, context => Assert.Null(context.Database.CurrentTransaction));
    }

    [Fact]
    public async Task Disposal_without_commit_rolls_back_every_enlisted_write()
    {
        await using (var shared = await EfSharedTransaction.BeginAsync([Db.Import(), Db.Activities(), Db.Workflows()]))
            await WriteOneRowPerLaneAsync(shared);

        Assert.True((await Db.CountAsync()).HasNoImportWrites);
    }

    [Fact]
    public async Task Split_target_is_refused_before_any_connection_opens()
    {
        var otherPath = Path.Combine(Path.GetTempPath(), $"elsa3-import-ef-unopened-{Guid.NewGuid():N}.db");
        await using var other = ImportDatabase.Sqlite(SqliteImportHarness.ConnectionStringFor(otherPath));

        var exception = await Assert.ThrowsAsync<EfSharedTransactionTargetMismatchException>(() =>
            EfSharedTransaction.BeginAsync([Db.Import(), other.Activities(), Db.Workflows()]));

        Assert.False(File.Exists(otherPath));
        Assert.DoesNotContain(otherPath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_mismatch_is_refused_before_any_connection_opens()
    {
        var sqlServer = new ActivitiesDesignSqlServerDbContext(new DbContextOptionsBuilder<ActivitiesDesignSqlServerDbContext>()
            .UseSqlServer(harness.ConnectionString).Options);

        var exception = await Assert.ThrowsAsync<EfSharedTransactionTargetMismatchException>(() =>
            EfSharedTransaction.BeginAsync([Db.Import(), sqlServer, Db.Workflows()]));

        Assert.Contains("provider", exception.Message, StringComparison.Ordinal);
        await sqlServer.DisposeAsync();
    }

    [Fact]
    public async Task Enlisted_operation_commit_defers_to_the_owner_which_alone_makes_it_durable()
    {
        await using (var abandoned = await EfSharedTransaction.BeginAsync([Db.Import(), Db.Activities(), Db.Workflows()]))
        {
            var result = await Writer(abandoned).ExecuteAsync(Request("deferred"), AddDefinition("deferred"));
            Assert.Equal(EfDesignAtomicWriteStatus.Committed, result.Status);
            Assert.False(abandoned.IsRollbackOnly);
        }
        var afterAbandon = await Db.CountAsync();
        Assert.Equal((0, 0), (afterAbandon.ActivityDefinitions, afterAbandon.ActivityOperations));

        await using (var committed = await EfSharedTransaction.BeginAsync([Db.Import(), Db.Activities(), Db.Workflows()]))
        {
            await Writer(committed).ExecuteAsync(Request("deferred"), AddDefinition("deferred"));
            await committed.CommitAsync();
        }
        var afterCommit = await Db.CountAsync();
        Assert.Equal((1, 1), (afterCommit.ActivityDefinitions, afterCommit.ActivityOperations));
    }

    [Fact]
    public async Task Enlisted_operation_rollback_makes_the_owner_rollback_only_and_discards_earlier_lanes()
    {
        await using (var shared = await EfSharedTransaction.BeginAsync([Db.Import(), Db.Activities(), Db.Workflows()]))
        {
            var workflows = shared.Context<WorkflowsDesignDbContext>();
            workflows.Definitions.Add(new WorkflowDefinition { Id = "earlier", TenantId = "tenant-a", Name = "Earlier lane" });
            await workflows.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => Writer(shared).ExecuteAsync(Request("failing"), (context, _) =>
            {
                context.Db.ActivityDefinitions.Add(Definition("failing"));
                throw new InvalidOperationException("stage failed");
            }));

            Assert.True(shared.IsRollbackOnly);
            await Assert.ThrowsAsync<EfSharedTransactionRollbackOnlyException>(() => shared.CommitAsync());
            await Assert.ThrowsAsync<EfSharedTransactionRollbackOnlyException>(() => shared.BeginOperationAsync());
        }

        Assert.True((await Db.CountAsync()).HasNoImportWrites);
    }

    [Fact]
    public async Task Enlisted_operation_disposed_without_commit_is_treated_as_a_rollback()
    {
        await using var shared = await EfSharedTransaction.BeginAsync([Db.Import(), Db.Activities(), Db.Workflows()]);
        var operation = await shared.BeginOperationAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => shared.BeginOperationAsync());

        await operation.DisposeAsync();

        Assert.True(shared.IsRollbackOnly);
    }

    [Fact]
    public async Task Commit_refuses_unsaved_changes_instead_of_silently_dropping_them()
    {
        await using (var shared = await EfSharedTransaction.BeginAsync([Db.Import(), Db.Activities(), Db.Workflows()]))
        {
            shared.Context<ActivitiesDesignDbContext>().ActivityDefinitions.Add(Definition("unsaved"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => shared.CommitAsync());

            Assert.Contains("unsaved changes", exception.Message, StringComparison.Ordinal);
            Assert.False(shared.CommitWasAttempted);
        }

        Assert.Equal(0, (await Db.CountAsync()).ActivityDefinitions);
    }

    [Fact]
    public async Task Commit_is_once_and_the_owner_is_terminal_afterwards()
    {
        await using var shared = await EfSharedTransaction.BeginAsync([Db.Import(), Db.Activities(), Db.Workflows()]);
        await WriteOneRowPerLaneAsync(shared);
        await shared.CommitAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => shared.CommitAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => shared.BeginOperationAsync());
        await shared.RollbackAsync();

        Assert.Equal(1, (await Db.CountAsync()).ActivityDefinitions);
    }

    [Fact]
    public async Task Commit_failure_is_an_unknown_outcome_and_terminal()
    {
        var failure = new CommitFailureInterceptor(afterCommit: false);
        await using (var shared = await EfSharedTransaction.BeginAsync([Db.Import(failure), Db.Activities(), Db.Workflows()]))
        {
            await WriteOneRowPerLaneAsync(shared);

            var exception = await Assert.ThrowsAsync<EfCommitOutcomeUnknownException>(() => shared.CommitAsync());

            Assert.IsType<IOException>(exception.InnerException);
            Assert.True(shared.CommitWasAttempted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => shared.CommitAsync());
        }

        Assert.True((await Db.CountAsync()).HasNoImportWrites);
    }

    private async Task WriteOneRowPerLaneAsync(EfSharedTransaction shared)
    {
        var activities = shared.Context<ActivitiesDesignDbContext>();
        activities.ActivityDefinitions.Add(Definition("owner-definition"));
        await activities.SaveChangesAsync();
        var workflows = shared.Context<WorkflowsDesignDbContext>();
        workflows.Definitions.Add(new WorkflowDefinition { Id = "owner-workflow", TenantId = "tenant-a", Name = "Owner" });
        await workflows.SaveChangesAsync();
    }

    private EfDesignAtomicWrite Writer(EfSharedTransaction shared) =>
        new(shared.Context<ActivitiesDesignDbContext>(), access, shared.BeginOperationAsync);

    private static EfDesignAtomicWriteRequest Request(string key) =>
        new(new EfDesignOperationIdentity("owner-test.v1", key), "request-fingerprint", ["activityDefinition"], "tenant-a");

    private static Func<EfDesignAtomicWriteContext, CancellationToken, Task<EfDesignAtomicWriteStageResult>> AddDefinition(string id) =>
        (context, _) =>
        {
            context.Db.ActivityDefinitions.Add(Definition(id));
            return Task.FromResult(EfDesignAtomicWriteStageResult.Accepted("result-fingerprint", "{}"));
        };

    private static ActivityDefinition Definition(string id) => new()
    {
        Id = id,
        TenantId = "tenant-a",
        ActivityTypeKey = $"key-{id}",
        Category = "Owner tests",
        CreatedAt = DateTimeOffset.UnixEpoch
    };
}
