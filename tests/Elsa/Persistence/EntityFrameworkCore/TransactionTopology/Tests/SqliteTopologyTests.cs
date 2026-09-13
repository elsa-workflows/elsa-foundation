using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.TransactionTopology.Tests;

public sealed class SqliteTopologyTests
{
    [Fact]
    public async Task Three_borrowers_commit_together_after_borrower_disposal()
    {
        var database = await CreateDatabaseAsync();
        try
        {
            var ownerConnection = NewConnection(database);
            await ConfigureSqliteAsync(ownerConnection);
            await using var operation = await TopologyOperation.BeginAsync(
                ownerConnection, "shell-a", "tenant-a", IsolationLevel.Serializable);

            await using (var runtime = Enlist(operation, ownerConnection, TopologyLane.Runtime))
            await using (var design = Enlist(operation, ownerConnection, TopologyLane.Design))
            await using (var publishing = Enlist(operation, ownerConnection, TopologyLane.Publishing))
            {
                runtime.Rows.Add(Row("commit-runtime", TopologyLane.Runtime, "tenant-a"));
                design.Rows.Add(Row("commit-design", TopologyLane.Design, "tenant-a"));
                publishing.Rows.Add(Row("commit-publishing", TopologyLane.Publishing, "tenant-a"));
                await runtime.SaveChangesAsync();
                await design.SaveChangesAsync();
                await publishing.SaveChangesAsync();
            }

            Assert.Equal(ConnectionState.Open, ownerConnection.State);
            Assert.Same(ownerConnection, operation.Transaction.Connection);
            await operation.CommitAsync();
            Assert.Equal(
                ["commit-design", "commit-publishing", "commit-runtime"],
                await ReadIdsAsync(database));
        }
        finally
        {
            DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task Explicit_rollback_and_exception_after_earlier_save_leave_no_partial_rows()
    {
        var database = await CreateDatabaseAsync();
        try
        {
            {
                var ownerConnection = NewConnection(database);
                await ConfigureSqliteAsync(ownerConnection);
                await using var operation = await TopologyOperation.BeginAsync(ownerConnection, "shell-a", "tenant-a");
                await using var runtime = Enlist(operation, ownerConnection, TopologyLane.Runtime);
                runtime.Rows.Add(Row("explicit-rollback", TopologyLane.Runtime, "tenant-a"));
                await runtime.SaveChangesAsync();
                await operation.RollbackAsync();
            }

            Assert.DoesNotContain("explicit-rollback", await ReadIdsAsync(database));

            {
                var ownerConnection = NewConnection(database);
                await ConfigureSqliteAsync(ownerConnection);
                await using var operation = await TopologyOperation.BeginAsync(ownerConnection, "shell-a", "tenant-a");
                await using var runtime = Enlist(operation, ownerConnection, TopologyLane.Runtime);
                runtime.Rows.Add(Row("forced-failure", TopologyLane.Runtime, "tenant-a"));
                await runtime.SaveChangesAsync();
                try
                {
                    throw new InvalidOperationException("forced partial failure after Runtime SaveChanges");
                }
                catch (InvalidOperationException exception)
                {
                    Assert.Contains("forced partial failure", exception.Message, StringComparison.Ordinal);
                    await operation.RollbackAsync();
                }
            }

            Assert.DoesNotContain("forced-failure", await ReadIdsAsync(database));
        }
        finally
        {
            DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task Abandoned_operation_dispose_rolls_back_and_closes_owned_connection_once()
    {
        var database = await CreateDatabaseAsync();
        try
        {
            var ownerConnection = NewConnection(database);
            await ConfigureSqliteAsync(ownerConnection);
            var operation = await TopologyOperation.BeginAsync(ownerConnection, "shell-a", "tenant-a");
            await using (var runtime = Enlist(operation, ownerConnection, TopologyLane.Runtime))
            {
                runtime.Rows.Add(Row("abandoned", TopologyLane.Runtime, "tenant-a"));
                await runtime.SaveChangesAsync();
            }

            await operation.DisposeAsync();
            await operation.DisposeAsync();

            Assert.Equal(ConnectionState.Closed, ownerConnection.State);
            Assert.Equal(1, operation.ConnectionDisposeCount);
            Assert.Empty(await ReadIdsAsync(database));
        }
        finally
        {
            DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task Failed_commit_is_terminal_and_wraps_unknown_outcome_once()
    {
        var database = await CreateDatabaseAsync();
        try
        {
            var ownerConnection = NewConnection(database);
            await ConfigureSqliteAsync(ownerConnection);
            var commitCalls = 0;
            var operation = await TopologyOperation.BeginAsync(
                ownerConnection,
                "shell-a",
                "tenant-a",
                commit: _ =>
                {
                    commitCalls++;
                    return Task.FromException(new IOException("simulated connection loss after commit request"));
                });

            var wrapped = await Assert.ThrowsAsync<CommitOutcomeUnknownException>(() => operation.CommitAsync());
            Assert.IsType<IOException>(wrapped.InnerException);
            await using (var postCommitBorrower = TopologyContexts.Create(ownerConnection, TopologyProvider.Sqlite, TopologyLane.Runtime))
            {
                var enlistment = Assert.Throws<InvalidOperationException>(() =>
                    operation.Enlist(postCommitBorrower, "shell-a", "tenant-a"));
                Assert.Contains("terminal", enlistment.Message, StringComparison.Ordinal);
            }

            var terminal = await Assert.ThrowsAsync<InvalidOperationException>(() => operation.CommitAsync());
            Assert.Contains("terminal", terminal.Message, StringComparison.Ordinal);
            Assert.Equal(1, commitCalls);
            Assert.True(operation.CommitWasAttempted);

            await operation.DisposeAsync();
            Assert.Equal(ConnectionState.Closed, ownerConnection.State);
        }
        finally
        {
            DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task Explicit_rollback_is_terminal_and_idempotent()
    {
        var database = await CreateDatabaseAsync();
        try
        {
            var ownerConnection = NewConnection(database);
            await ConfigureSqliteAsync(ownerConnection);
            await using var operation = await TopologyOperation.BeginAsync(ownerConnection, "shell-a", "tenant-a");

            await operation.RollbackAsync();
            await operation.RollbackAsync();

            await using (var postRollbackBorrower = TopologyContexts.Create(ownerConnection, TopologyProvider.Sqlite, TopologyLane.Runtime))
            {
                var enlistment = Assert.Throws<InvalidOperationException>(() =>
                    operation.Enlist(postRollbackBorrower, "shell-a", "tenant-a"));
                Assert.Contains("terminal", enlistment.Message, StringComparison.Ordinal);
            }

            var commit = await Assert.ThrowsAsync<InvalidOperationException>(() => operation.CommitAsync());
            Assert.Contains("terminal", commit.Message, StringComparison.Ordinal);
            Assert.Empty(await ReadIdsAsync(database));
        }
        finally
        {
            DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task Dispose_attempts_all_owner_cleanup_and_preserves_primary_failure()
    {
        var connection = new FaultingTopologyConnection(
            failRollback: true,
            failTransactionDispose: true,
            failConnectionDispose: true);
        var operation = await TopologyOperation.BeginAsync(connection, "shell-a", "tenant-a");

        var failure = await Assert.ThrowsAsync<IOException>(() => operation.DisposeAsync().AsTask());

        Assert.Equal("rollback failed", failure.Message);
        Assert.Equal(1, connection.Transaction.DisposeAsyncCalls);
        Assert.Equal(1, connection.DisposeAsyncCalls);
        await operation.DisposeAsync();
        Assert.Equal(1, connection.Transaction.DisposeAsyncCalls);
        Assert.Equal(1, connection.DisposeAsyncCalls);
    }

    [Fact]
    public async Task Failed_open_closes_transferred_connection()
    {
        var connection = new FaultingTopologyConnection(failOpen: true);

        var failure = await Assert.ThrowsAsync<IOException>(() =>
            TopologyOperation.BeginAsync(connection, "shell-a", "tenant-a"));

        Assert.Equal("open failed", failure.Message);
        Assert.Equal(1, connection.DisposeAsyncCalls);
    }

    [Fact]
    public async Task Split_connection_target_and_tenant_mismatches_refuse_before_writes()
    {
        var database = await CreateDatabaseAsync();
        try
        {
            var ownerConnection = NewConnection(database);
            await ConfigureSqliteAsync(ownerConnection);
            await using var operation = await TopologyOperation.BeginAsync(ownerConnection, "shell-a", "tenant-a");

            await using (var splitConnection = NewConnection(database))
            await using (var splitBorrower = TopologyContexts.Create(splitConnection, TopologyProvider.Sqlite, TopologyLane.Runtime))
            {
                var split = Assert.Throws<InvalidOperationException>(() =>
                    operation.Enlist(splitBorrower, "shell-a", "tenant-a"));
                Assert.Contains("different physical connection", split.Message, StringComparison.Ordinal);
            }

            await using (var wrongTarget = TopologyContexts.Create(ownerConnection, TopologyProvider.Sqlite, TopologyLane.Design))
            {
                var target = Assert.Throws<InvalidOperationException>(() =>
                    operation.Enlist(wrongTarget, "shell-b", "tenant-a"));
                Assert.Contains("target mismatch", target.Message, StringComparison.Ordinal);
            }

            await using (var wrongTenant = TopologyContexts.Create(ownerConnection, TopologyProvider.Sqlite, TopologyLane.Publishing))
            {
                var tenant = Assert.Throws<InvalidOperationException>(() =>
                    operation.Enlist(wrongTenant, "shell-a", "tenant-b"));
                Assert.Contains("tenant mismatch", tenant.Message, StringComparison.Ordinal);
            }

            await operation.RollbackAsync();
            Assert.Empty(await ReadIdsAsync(database));
        }
        finally
        {
            DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task Sqlite_savepoint_rolls_back_one_lane_without_escaping_the_outer_unit()
    {
        var database = await CreateDatabaseAsync();
        try
        {
            var ownerConnection = NewConnection(database);
            await ConfigureSqliteAsync(ownerConnection);
            await using var operation = await TopologyOperation.BeginAsync(ownerConnection, "shell-a", "tenant-a");
            await using var runtime = Enlist(operation, ownerConnection, TopologyLane.Runtime);
            await using var design = Enlist(operation, ownerConnection, TopologyLane.Design);
            await using var publishing = Enlist(operation, ownerConnection, TopologyLane.Publishing);

            runtime.Rows.Add(Row("savepoint-runtime", TopologyLane.Runtime, "tenant-a"));
            await runtime.SaveChangesAsync();
            var sqliteTransaction = Assert.IsType<SqliteTransaction>(operation.Transaction);
            sqliteTransaction.Save("after-runtime");

            design.Rows.Add(Row("savepoint-design-rolled-back", TopologyLane.Design, "tenant-a"));
            await design.SaveChangesAsync();
            sqliteTransaction.Rollback("after-runtime");

            publishing.Rows.Add(Row("savepoint-publishing", TopologyLane.Publishing, "tenant-a"));
            await publishing.SaveChangesAsync();
            await operation.CommitAsync();

            Assert.Equal(
                ["savepoint-publishing", "savepoint-runtime"],
                await ReadIdsAsync(database));
        }
        finally
        {
            DeleteDatabase(database);
        }
    }

    [Fact]
    public async Task Failed_begin_transaction_closes_transferred_connection()
    {
        await using var ownerConnection = new SqliteConnection("Data Source=:memory:");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            TopologyOperation.BeginAsync(ownerConnection, "shell-a", "tenant-a", IsolationLevel.Chaos));

        Assert.Contains("isolation", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ConnectionState.Closed, ownerConnection.State);
    }

    [Fact]
    public async Task Sqlite_WAL_busy_configuration_exposes_writer_contention_without_timing_claim()
    {
        var database = await CreateDatabaseAsync();
        try
        {
            var writerConnection = NewConnection(database);
            await ConfigureSqliteAsync(writerConnection);
            await using var operation = await TopologyOperation.BeginAsync(writerConnection, "shell-a", "tenant-a");
            await using var writer = Enlist(operation, writerConnection, TopologyLane.Runtime);
            writer.Rows.Add(Row("contention-writer", TopologyLane.Runtime, "tenant-a"));
            await writer.SaveChangesAsync();

            await using var contenderConnection = NewConnection(database);
            await ConfigureSqliteAsync(contenderConnection);
            Assert.Equal("wal", await ScalarAsync(contenderConnection, "PRAGMA journal_mode;"));
            Assert.True(Convert.ToInt32(await ScalarAsync(contenderConnection, "PRAGMA busy_timeout;")) > 0);
            await using var contender = TopologyContexts.Create(contenderConnection, TopologyProvider.Sqlite, TopologyLane.Design);
            contender.Rows.Add(Row("contention-contender", TopologyLane.Design, "tenant-a"));

            using var livenessGuard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var exception = await Assert.ThrowsAnyAsync<Exception>(() => contender.SaveChangesAsync(livenessGuard.Token));
            Assert.True(ContainsBusySqliteError(exception), $"Expected SQLITE_BUSY/LOCKED, got {exception}");
            await operation.RollbackAsync();
            Assert.Empty(await ReadIdsAsync(database));
        }
        finally
        {
            DeleteDatabase(database);
        }
    }

    [Fact]
    public void Whole_unit_retry_and_commit_ambiguity_are_explicit_policy()
    {
        Assert.Equal("whole-operation", TopologyPolicy.RetryScope);
        Assert.Equal("external-owner-only", TopologyPolicy.ExecutionStrategy);
        Assert.Equal("unknown-outcome-recovery", TopologyPolicy.CommitAmbiguity);
        Assert.Throws<CommitOutcomeUnknownException>(ThrowCommitAmbiguity);
    }

    private static TopologyDbContext Enlist(TopologyOperation operation, SqliteConnection connection, TopologyLane lane)
    {
        var context = TopologyContexts.Create(connection, TopologyProvider.Sqlite, lane);
        operation.Enlist(context, "shell-a", "tenant-a");
        return context;
    }

    private static async Task<string> CreateDatabaseAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-ef-topology-{Guid.NewGuid():N}.db");
        await using var connection = NewConnection(path);
        await ConfigureSqliteAsync(connection);
        await using var context = TopologyContexts.Create(connection, TopologyProvider.Sqlite, TopologyLane.Runtime);
        await context.Database.EnsureCreatedAsync();
        return path;
    }

    private static SqliteConnection NewConnection(string path) =>
        new($"Data Source={path};Cache=Shared;Default Timeout=1");

    private static async Task ConfigureSqliteAsync(SqliteConnection connection)
    {
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync() ?? throw new InvalidOperationException($"No result for {sql}");
    }

    private static async Task<string[]> ReadIdsAsync(string path)
    {
        await using var connection = NewConnection(path);
        await ConfigureSqliteAsync(connection);
        await using var context = TopologyContexts.Create(connection, TopologyProvider.Sqlite, TopologyLane.Runtime);
        return await context.Rows.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();
    }

    private static TopologyRow Row(string id, TopologyLane lane, string tenant) => new()
    {
        Id = id,
        Lane = lane.ToString(),
        TenantId = tenant,
        Payload = $"payload-{id}"
    };

    private static bool ContainsBusySqliteError(Exception exception) =>
        exception switch
        {
            SqliteException sqlite => sqlite.SqliteErrorCode is 5 or 6,
            _ when exception.InnerException is not null => ContainsBusySqliteError(exception.InnerException),
            _ => false
        };

    private static void ThrowCommitAmbiguity() => throw new CommitOutcomeUnknownException(
        "The commit outcome is unknown; recovery must inspect durable markers.",
        new IOException("connection dropped after commit request"));

    private static void DeleteDatabase(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var candidate = path + suffix;
            if (File.Exists(candidate))
                File.Delete(candidate);
        }
    }
}

internal static class TopologyPolicy
{
    public const string RetryScope = "whole-operation";
    public const string ExecutionStrategy = "external-owner-only";
    public const string CommitAmbiguity = "unknown-outcome-recovery";
}

internal sealed class FaultingTopologyConnection(
    bool failOpen = false,
    bool failRollback = false,
    bool failTransactionDispose = false,
    bool failConnectionDispose = false) : DbConnection
{
    private ConnectionState state = ConnectionState.Closed;

    public FaultingTopologyTransaction Transaction { get; } = new(failRollback, failTransactionDispose);
    public int DisposeAsyncCalls { get; private set; }

    [AllowNull]
    public override string ConnectionString { get; set; } = string.Empty;
    public override string Database => "faulting";
    public override string DataSource => "faulting";
    public override string ServerVersion => "faulting";
    public override ConnectionState State => state;

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        Transaction.ConnectionOwner = this;
        return Transaction;
    }

    protected override DbCommand CreateDbCommand() => throw new NotSupportedException();

    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

    public override void Close() => state = ConnectionState.Closed;

    public override void Open()
    {
        if (failOpen)
            throw new IOException("open failed");

        state = ConnectionState.Open;
    }

    public override ValueTask DisposeAsync()
    {
        DisposeAsyncCalls++;
        state = ConnectionState.Closed;
        return failConnectionDispose
            ? ValueTask.FromException(new InvalidOperationException("connection dispose failed"))
            : ValueTask.CompletedTask;
    }
}

internal sealed class FaultingTopologyTransaction(bool failRollback, bool failDispose) : DbTransaction
{
    public DbConnection? ConnectionOwner { get; set; }
    public int DisposeAsyncCalls { get; private set; }

    protected override DbConnection? DbConnection => ConnectionOwner;
    public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

    public override void Commit() => throw new NotSupportedException();

    public override void Rollback()
    {
        if (failRollback)
            throw new IOException("rollback failed");
    }

    public override ValueTask DisposeAsync()
    {
        DisposeAsyncCalls++;
        return failDispose
            ? ValueTask.FromException(new InvalidOperationException("transaction dispose failed"))
            : ValueTask.CompletedTask;
    }
}
