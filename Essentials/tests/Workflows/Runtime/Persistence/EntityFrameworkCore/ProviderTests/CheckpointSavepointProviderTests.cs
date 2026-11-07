using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

public sealed partial class RuntimeEntityFrameworkCoreAggregatePostgreSqlSmokeTests
{
    [SkippableTheory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancel")]
    [InlineData("ambiguous")]
    public Task PostgreSql_checkpoint_savepoint_suppression_preserves_atomicity(string outcome) =>
        RuntimeEntityFrameworkCoreAggregateProviderSmoke.RunSavepointAsync(fixture,
            (connection, interceptor) => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>()
                .UseNpgsql(connection).AddInterceptors(interceptor).Options), outcome);
}

public sealed partial class RuntimeEntityFrameworkCoreAggregateSqlServerSmokeTests
{
    [SkippableTheory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancel")]
    [InlineData("ambiguous")]
    public Task SqlServer_checkpoint_savepoint_suppression_preserves_atomicity(string outcome) =>
        RuntimeEntityFrameworkCoreAggregateProviderSmoke.RunSavepointAsync(fixture,
            (connection, interceptor) => new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>()
                .UseSqlServer(connection).AddInterceptors(interceptor).Options), outcome);
}

public sealed partial class RuntimeEntityFrameworkCoreAggregateMySqlSmokeTests
{
    [SkippableTheory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancel")]
    [InlineData("ambiguous")]
    public Task MySql_checkpoint_savepoint_suppression_preserves_atomicity(string outcome) =>
        RuntimeEntityFrameworkCoreAggregateProviderSmoke.RunSavepointAsync(fixture,
            (connection, interceptor) => new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>()
                .UseMySQL(connection).AddInterceptors(interceptor).Options), outcome);
}

internal static partial class RuntimeEntityFrameworkCoreAggregateProviderSmoke
{
    public static async Task RunSavepointAsync(RuntimeBookmarksProviderFixture fixture,
        Func<string, IInterceptor, RuntimeDbContext> open, string outcome)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Provider unavailable.");
        var scope = $"savepoint-{Guid.NewGuid():N}";
        var faults = new SavepointFaults(outcome);
        await using var context = open(fixture.ConnectionString, faults);
        await context.Database.EnsureCreatedAsync();
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor(scope),
            rootWriteLeaseManager: new PassThroughRootWriteLeaseManager());
        var commit = InspectionCommit(scope, ActivityExecutionStatus.Completed);
        faults.Armed = true;
        if (outcome == "failure")
            await Assert.ThrowsAsync<DbUpdateException>(() => store.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        else if (outcome == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        else
            await store.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.True(context.Database.AutoSavepointsEnabled);
        Assert.Equal(0, faults.Savepoints);
        await using var verification = open(fixture.ConnectionString, new SavepointFaults("success"));
        var encodedScope = EfRelationalIdentity.Encode(scope);
        var expected = outcome is "success" or "ambiguous" ? 1 : 0;
        Assert.Equal(expected, await verification.ActivityExecutionInspections.CountAsync(row => row.ScopeKey == encodedScope));
        Assert.Equal(expected, await verification.RuntimeCheckpointCommits.CountAsync(row => row.ScopeKey == encodedScope));
        // A failed transaction leaves the same writer usable; the durable outcome can be replayed or retried.
        await store.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.True(context.Database.AutoSavepointsEnabled);
        Assert.Equal(0, faults.Savepoints);
        Assert.Single(await verification.RuntimeCheckpointCommits.AsNoTracking().Where(row => row.ScopeKey == encodedScope).ToArrayAsync());
    }

    private sealed class SavepointFaults(string outcome) : DbCommandInterceptor, IDbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public int Savepoints { get; private set; }

        public Task CreatedSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
        {
            Savepoints++;
            return Task.CompletedTask;
        }

        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Armed && outcome == "ambiguous")
            {
                Armed = false;
                throw new IOException("Lost checkpoint commit acknowledgement.");
            }
            return Task.CompletedTask;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Before(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Before(command);
            return ValueTask.FromResult(result);
        }

        private void Before(DbCommand command)
        {
            if (!Armed || !command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) ||
                !command.CommandText.Contains("elsa_runtime_checkpoint_commit", StringComparison.OrdinalIgnoreCase)) return;
            if (outcome == "failure")
            {
                Armed = false;
                command.CommandText = "INSERT INTO elsa_missing_checkpoint_failure_probe VALUES (1)";
            }
            else if (outcome == "cancel")
            {
                Armed = false;
                throw new OperationCanceledException("Cancel after participant writes, before marker write.");
            }
        }
    }
}
