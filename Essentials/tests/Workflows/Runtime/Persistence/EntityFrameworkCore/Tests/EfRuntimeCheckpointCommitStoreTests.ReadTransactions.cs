using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Theory]
    [InlineData("history")] [InlineData("browse")] [InlineData("summaries")]
    [Trait("Feature", "RunHistory")]
    public async Task Consistent_read_returns_committed_evidence_while_native_writer_is_held(string readerKind)
    {
        await using var database = await ReadTransactionDatabase.CreateAsync();
        var barrier = new ReadWriterCommitBarrier();
        await using var writer = database.Open(barrier);
        var append = new EfRuntimeCheckpointCommitStore(writer, new FixedAccessor("tenant-a"))
            .CommitAsync(ReadTransactionCommit("read-held-append", false, "b", 2), Decision()).AsTask();
        try
        {
            var first = await Task.WhenAny(barrier.Arrived.Task, append).WaitAsync(TimeSpan.FromSeconds(30));
            if (ReferenceEquals(first, append))
            {
                await append; // Report the actual native fixture failure instead of an arrival timeout.
                Assert.Fail("The native append completed without reaching its held commit boundary.");
            }
            await barrier.Arrived.Task;
            await using var reader = database.Open();
            var snapshot = await ReadTransactionImageAsync(reader, readerKind);
            Assert.Equal(1, snapshot.Total);
            Assert.Equal(["a"], snapshot.Items);
            Assert.Null(reader.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Closed, reader.Database.GetDbConnection().State);
            Assert.False(append.IsCompleted); // The writer remains owed until this test releases its real commit.
        }
        finally
        {
            barrier.Release.TrySetResult();
            await append;
        }
        await using var verify = database.Open();
        var current = await ReadTransactionImageAsync(verify, readerKind);
        Assert.Equal(2, current.Total);
        Assert.Equal(["a", "b"], current.Items);
        Assert.Equal(2, await verify.RuntimeCheckpointCommits.CountAsync());
    }

    [Theory]
    [InlineData("history")] [InlineData("browse")] [InlineData("summaries")]
    [Trait("Feature", "RunHistory")]
    public async Task Consistent_read_keeps_one_WAL_snapshot_while_native_append_commits(string readerKind)
    {
        await using var database = await ReadTransactionDatabase.CreateAsync();
        var appended = false;
        await using var connection = new ReadProbeConnection(database.ConnectionString, async () =>
        {
            await using var writer = database.Open();
            await new EfRuntimeCheckpointCommitStore(writer, new FixedAccessor("tenant-a"))
                .CommitAsync(ReadTransactionCommit("read-snapshot-append", false, "b", 2), Decision());
            appended = true;
        });
        await using var reader = database.Open(connection);
        var snapshot = await ReadTransactionImageAsync(reader, readerKind);
        Assert.True(appended); // The actual writer completed while the first read snapshot was still open.
        Assert.Equal(1, snapshot.Total);
        Assert.Equal(["a"], snapshot.Items);
        Assert.Null(reader.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, connection.State);
        await using var verify = database.Open();
        var current = await ReadTransactionImageAsync(verify, readerKind);
        Assert.Equal(2, current.Total);
        Assert.Equal(["a", "b"], current.Items);
        if (snapshot.History is { } old)
        {
            var tail = await HistoryStore(verify, new FixedAccessor("tenant-a"))
                .ReadPageAsync(new("workflow-a", continuationToken: old.CatchUpToken));
            Assert.Equal(2, Assert.Single(tail.Items).Sequence);
            Assert.Equal("b", tail.Items[0].ActivityExecutionId);
        }
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Consistent_read_honors_supplied_transaction_and_releases_only_its_owned_references()
    {
        await using var database = await ReadTransactionDatabase.CreateAsync();
        await using var connection = new SqliteConnection(database.ConnectionString);
        var supplied = new ReadSuppliedTransaction();
        var disposed = 0;
        var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).AddInterceptors(supplied)
            .LogTo((eventId, _) => eventId.Id == RelationalEventId.TransactionDisposed.Id, _ => disposed++).Options;
        await using var reader = new RuntimeSqliteDbContext(options);
        await reader.Database.OpenConnectionAsync(); // The caller owns this open and must keep it after both reads.
        var store = HistoryStore(reader, new FixedAccessor("tenant-a"));
        await store.FindHeadAsync("workflow-a");
        Assert.Equal(1, disposed); // EF owns and disposes even a transaction supplied by an earlier interceptor.
        Assert.Equal(1, supplied.Supplied);
        Assert.Equal(1, supplied.NestedRefusals);
        Assert.Null(reader.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Open, connection.State);
        await store.FindHeadAsync("workflow-a"); // A sequential read gets its own request and transaction.
        Assert.Equal(2, disposed);
        Assert.Null(reader.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Open, connection.State);

        // The read request must not turn the next ordinary write transaction into a deferred reader.
        await using var writer = database.Open();
        await using var held = await writer.Database.BeginTransactionAsync();
        var blocked = await Assert.ThrowsAsync<SqliteException>(() => reader.Database.BeginTransactionAsync());
        Assert.Equal(5, blocked.SqliteErrorCode);
        await reader.Database.CloseConnectionAsync(); // Balance the failed ordinary EF Begin's own open reference.
        Assert.Equal(ConnectionState.Open, connection.State);
        await held.RollbackAsync();
        await using (var caller = await reader.Database.BeginTransactionAsync())
        {
            var inspection = new EfActivityExecutionInspectionStore(reader, new FixedAccessor("tenant-a"), new NoopContinuationCodec());
            Assert.Single((await inspection.ListSummariesPageAsync(new("workflow-a"))).Items);
            Assert.Same(caller, reader.Database.CurrentTransaction);
            await caller.RollbackAsync();
        }
        await reader.Database.CloseConnectionAsync();
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Consistent_read_failure_and_cancellation_preserve_cause_cleanup_and_next_native_writer()
    {
        await using var database = await ReadTransactionDatabase.CreateAsync();
        foreach (var stage in new[] { "starting", "later-starting", "started", "cancelled", "immutable-cleanup" })
        {
            using var stop = new CancellationTokenSource();
            var cause = stage == "cancelled" ? (Exception)new OperationCanceledException("Constructed read-start cancellation.", null, stop.Token)
                : stage == "immutable-cleanup" ? new ReadImmutableProviderFailure() : new ReadProviderFailure();
            var failure = new ReadStartFailure(stage, cause, stop);
            await using var reader = stage == "later-starting" ? database.OpenAfterReadFactory(failure) : database.Open(failure);
            var store = HistoryStore(reader, new FixedAccessor("tenant-a"));
            if (stage == "cancelled")
                Assert.Same(cause, await Assert.ThrowsAsync<OperationCanceledException>(() => store.FindHeadAsync("workflow-a", stop.Token).AsTask()));
            else
                Assert.Same(cause, (await Assert.ThrowsAsync<RuntimeHistoryEntityFrameworkPersistenceException>(() => store.FindHeadAsync("workflow-a").AsTask())).InnerException);
            Assert.Equal(1, failure.Failures);
            Assert.Null(reader.Database.CurrentTransaction);
            Assert.Empty(reader.ChangeTracker.Entries());
            if (stage == "immutable-cleanup")
            {
                Assert.Equal(1, failure.CleanupFailures);
                // The synthetic close failed before closing the actual connection. A subsequent close
                // finishes cleanup; immutable exception Data never substitutes another exception.
                await reader.Database.CloseConnectionAsync();
            }
            Assert.Equal(ConnectionState.Closed, reader.Database.GetDbConnection().State);
            Assert.NotNull(await store.FindHeadAsync("workflow-a"));
            Assert.Equal(ConnectionState.Closed, reader.Database.GetDbConnection().State);
            await using (var blocker = database.Open())
            await using (var held = await blocker.Database.BeginTransactionAsync())
            {
                var blocked = await Assert.ThrowsAsync<SqliteException>(() => reader.Database.BeginTransactionAsync());
                Assert.Equal(5, blocked.SqliteErrorCode);
                await reader.Database.CloseConnectionAsync();
                await held.RollbackAsync();
            }
            await using var writable = await reader.Database.BeginTransactionAsync();
            await writable.RollbackAsync();
        }
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Consistent_read_pooled_context_reuse_keeps_read_snapshots_and_native_writer_acquisition()
    {
        await using var database = await ReadTransactionDatabase.CreateAsync();
        var services = new ServiceCollection();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new FixedAccessor("tenant-a"));
        services.AddWorkflowRuntime(); services.AddRuntimeHistory();
        services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
        {
            Provider = "Sqlite", ConnectionString = database.ConnectionString, Pooling = true,
            RecoveryContinuationSigningKey = "pooled-consistent-read-signing-key-32",
            HierarchyCursorSigningKey = "pooled-consistent-read-signing-key-32"
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        RuntimeDbContext first;
        await using (var lease = provider.CreateAsyncScope())
        {
            first = lease.ServiceProvider.GetRequiredService<RuntimeDbContext>();
            Assert.Single((await lease.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>().ReadPageAsync(new("workflow-a"))).Items);
            Assert.Null(first.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Closed, first.Database.GetDbConnection().State);
        }
        await using var writer = database.Open();
        await using var held = await writer.Database.BeginTransactionAsync();
        await using (var lease = provider.CreateAsyncScope())
        {
            var reused = lease.ServiceProvider.GetRequiredService<RuntimeDbContext>();
            Assert.Same(first, reused);
            Assert.Single((await lease.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>().ReadPageAsync(new("workflow-a"))).Items);
            Assert.Null(reused.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Closed, reused.Database.GetDbConnection().State);
            var blocked = await Assert.ThrowsAsync<SqliteException>(() => reused.Database.BeginTransactionAsync());
            Assert.Equal(5, blocked.SqliteErrorCode);
            await reused.Database.CloseConnectionAsync();
        }
        await held.RollbackAsync();
        await using (var lease = provider.CreateAsyncScope())
        {
            var reused = lease.ServiceProvider.GetRequiredService<RuntimeDbContext>();
            Assert.Same(first, reused);
            await using var native = await reused.Database.BeginTransactionAsync();
            await native.RollbackAsync();
            Assert.Null(reused.Database.CurrentTransaction);
        }
    }

    private static RuntimeCheckpointCommit ReadTransactionCommit(string id, bool fromStart, string activity, long sequence)
    {
        var projection = new ActivityExecutionInspectionProjection(activity, "workflow-a", "node-" + activity, "authored-" + activity,
            "Test.Activity", "1", ActivityExecutionStatus.Completed, null, sequence, OccurredAt, OccurredAt, OccurredAt,
            "checkpoint-" + id, "checkpoint-" + id, OccurredAt,
            ActivitySchedulingProvenance.From("workflow-a", null, null, null, null, null, "root", "fixture"),
            ["Done"], [], [], [], new Dictionary<string, string>(), "root");
        return HistoryCommit(id, fromStart, [HistoryCandidate("checkpoint-" + id, 0, "ActivityCompleted", activity)]) with
        {
            StateChanges = new(null, null, [], [], [], [], [], activityExecutionInspections:
                [new(activity, RuntimeStateChangeOperation.Upsert, projection, new Dictionary<string, string>())])
        };
    }

    private sealed record ReadImage(long Total, IReadOnlyList<string> Items, RuntimeHistoryPage? History = null);

    private static async Task<ReadImage> ReadTransactionImageAsync(RuntimeDbContext context, string readerKind)
    {
        var access = new FixedAccessor("tenant-a");
        if (readerKind == "history")
        {
            var page = await HistoryStore(context, access).ReadPageAsync(new("workflow-a"));
            return new(page.Head!.LastSequence, page.Items.Select(item => item.ActivityExecutionId!).ToArray(), page);
        }
        var inspection = new EfActivityExecutionInspectionStore(context, access, new NoopContinuationCodec());
        if (readerKind == "browse")
        {
            var page = await inspection.BrowseAsync(new("workflow-a", offset: 0));
            return new(page.TotalCount, page.Items.Select(item => item.Execution.ActivityExecutionId).ToArray());
        }
        var summaries = await inspection.ListSummariesPageAsync(new("workflow-a"));
        return new(summaries.TotalCount, summaries.Items.Select(item => item.ActivityExecutionId).ToArray());
    }

    private sealed class ReadTransactionDatabase(string path, string connectionString) : IAsyncDisposable
    {
        public string ConnectionString => connectionString;
        public static async Task<ReadTransactionDatabase> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), "elsa-read-transaction-" + Guid.NewGuid().ToString("N") + ".sqlite");
            // One private-cache, non-pooled file per case. The finite engine busy timeout makes a
            // reservation conflict fail deterministically; these tests never assert elapsed time.
            var database = new ReadTransactionDatabase(path, new SqliteConnectionStringBuilder
                { DataSource = path, Cache = SqliteCacheMode.Private, Pooling = false, DefaultTimeout = 1 }.ToString());
            await using var context = database.Open();
            await context.Database.EnsureCreatedAsync();
            await context.Database.OpenConnectionAsync();
            await using (var command = context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode=WAL";
                Assert.Equal("wal", (string?)await command.ExecuteScalarAsync());
            }
            await context.Database.CloseConnectionAsync();
            await SeedHistoryRunAsync(context);
            await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"))
                .CommitAsync(ReadTransactionCommit("read-initial", true, "a", 1), Decision());
            return database;
        }
        public RuntimeSqliteDbContext Open(params IInterceptor[] interceptors) => new(
            new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connectionString).AddInterceptors(interceptors).Options);
        public RuntimeSqliteDbContext Open(SqliteConnection connection) => new(
            new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        public RuntimeSqliteDbContext OpenAfterReadFactory(IInterceptor interceptor)
        {
            var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connectionString);
            EfRuntimeReadTransaction.Configure(options);
            return new RuntimeSqliteDbContext(options.AddInterceptors(interceptor).Options);
        }
        public ValueTask DisposeAsync()
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReadWriterCommitBarrier : DbTransactionInterceptor
    {
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Arrived.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class ReadSuppliedTransaction : DbTransactionInterceptor
    {
        public int Supplied { get; private set; }
        public int NestedRefusals { get; private set; }
        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (Supplied == 0)
            {
                Supplied++;
                await Assert.ThrowsAsync<InvalidOperationException>(() => HistoryStore((RuntimeDbContext)eventData.Context!, new FixedAccessor("tenant-a"))
                    .FindHeadAsync("workflow-a").AsTask());
                NestedRefusals++;
                return InterceptionResult<DbTransaction>.SuppressWithResult(((SqliteConnection)connection).BeginTransaction(IsolationLevel.Serializable, true));
            }
            return result;
        }
    }

    private class ReadProviderFailure : DbException { }
    private sealed class ReadImmutableProviderFailure : ReadProviderFailure
    {
        public override IDictionary Data { get; } = new ReadOnlyDictionary<object, object>(new Dictionary<object, object>());
    }

    private sealed class ReadStartFailure(string stage, Exception cause, CancellationTokenSource stop)
        : DbTransactionInterceptor, IDbConnectionInterceptor
    {
        public int Failures { get; private set; }
        public int CleanupFailures { get; private set; }
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (Failures == 0 && stage is "starting" or "later-starting" or "cancelled")
            {
                Failures++;
                if (stage == "cancelled") stop.Cancel();
                throw cause;
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData,
            DbTransaction result, CancellationToken cancellationToken = default)
        {
            if (Failures == 0 && stage is "started" or "immutable-cleanup") { Failures++; throw cause; }
            return ValueTask.FromResult(result);
        }
        public ValueTask<InterceptionResult> ConnectionClosingAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            if (stage == "immutable-cleanup" && Failures == 1 && CleanupFailures == 0)
            { CleanupFailures++; throw new IOException("Constructed read cleanup failure after provider rollback."); }
            return ValueTask.FromResult(result);
        }
    }

    // Browse issues a provider-owned raw command, so this real connection probe observes all three
    // first SELECTs without relying on EF command interception or replacing their SQL/results.
    private sealed class ReadProbeConnection(string connectionString, Func<Task> afterFirstReader) : SqliteConnection(connectionString)
    {
        private int reads;
        protected override DbCommand CreateDbCommand() => new ReadProbeCommand(base.CreateDbCommand(), async () =>
        {
            if (Interlocked.Increment(ref reads) == 1) await afterFirstReader();
        });
    }

    private sealed class ReadProbeCommand(DbCommand inner, Func<Task> afterReader) : DbCommand
    {
        [AllowNull] public override string CommandText { get => inner.CommandText; set => inner.CommandText = value; }
        public override int CommandTimeout { get => inner.CommandTimeout; set => inner.CommandTimeout = value; }
        public override CommandType CommandType { get => inner.CommandType; set => inner.CommandType = value; }
        public override bool DesignTimeVisible { get => inner.DesignTimeVisible; set => inner.DesignTimeVisible = value; }
        public override UpdateRowSource UpdatedRowSource { get => inner.UpdatedRowSource; set => inner.UpdatedRowSource = value; }
        protected override DbConnection? DbConnection { get => inner.Connection; set => inner.Connection = value; }
        protected override DbTransaction? DbTransaction { get => inner.Transaction; set => inner.Transaction = value; }
        protected override DbParameterCollection DbParameterCollection => inner.Parameters;
        public override void Cancel() => inner.Cancel();
        public override int ExecuteNonQuery() => inner.ExecuteNonQuery();
        public override object? ExecuteScalar() => inner.ExecuteScalar();
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => inner.ExecuteNonQueryAsync(cancellationToken);
        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) => inner.ExecuteScalarAsync(cancellationToken);
        public override void Prepare() => inner.Prepare();
        protected override DbParameter CreateDbParameter() => inner.CreateParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => inner.ExecuteReader(behavior);
        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        {
            var reader = await inner.ExecuteReaderAsync(behavior, cancellationToken);
            try { await afterReader(); return reader; }
            catch { await reader.DisposeAsync(); throw; }
        }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
