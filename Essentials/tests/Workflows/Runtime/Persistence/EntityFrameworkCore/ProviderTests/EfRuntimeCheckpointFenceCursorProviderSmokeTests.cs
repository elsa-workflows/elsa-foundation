using System.Collections.Concurrent;
using System.Data.Common;
using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeCheckpointFenceCursorPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    private static RuntimeDbContext Create(string connection, IInterceptor capture) => new RuntimePostgreSqlDbContext(
        new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).AddInterceptors(capture).Options);

    [SkippableFact]
    public Task PostgreSql_checkpoint_fence_cursor_smoke() =>
        RuntimeCheckpointFenceCursorProviderSmoke.RunAsync(fixture, Create, RuntimePostgreSqlDbContext.ExpectedProviderName);

    [SkippableTheory]
    [InlineData("transaction-scope")]
    [InlineData("enlisted-transaction")]
    public Task PostgreSql_liveness_store_records_no_cursor_for_a_save_inside_a_transaction_it_does_not_own(string transaction) =>
        RuntimeCheckpointFenceCursorProviderSmoke.RecordsNoCursorInsideAsync(fixture, Create, transaction);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeCheckpointFenceCursorSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    private static RuntimeDbContext Create(string connection, IInterceptor capture) => new RuntimeSqlServerDbContext(
        new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).AddInterceptors(capture).Options);

    [SkippableFact]
    public Task SqlServer_checkpoint_fence_cursor_smoke() =>
        RuntimeCheckpointFenceCursorProviderSmoke.RunAsync(fixture, Create, RuntimeSqlServerDbContext.ExpectedProviderName);

    [SkippableTheory]
    [InlineData("transaction-scope")]
    [InlineData("enlisted-transaction")]
    public Task SqlServer_liveness_store_records_no_cursor_for_a_save_inside_a_transaction_it_does_not_own(string transaction) =>
        RuntimeCheckpointFenceCursorProviderSmoke.RecordsNoCursorInsideAsync(fixture, Create, transaction);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeCheckpointFenceCursorMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_checkpoint_fence_cursor_smoke() =>
        RuntimeCheckpointFenceCursorProviderSmoke.RunAsync(
            fixture,
            (connection, capture) => new RuntimeMySqlDbContext(
                new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).AddInterceptors(capture).Options),
            RuntimeMySqlDbContext.ExpectedProviderName);
}

/// <summary>
/// #2564 on a native provider. A fenced commit whose lease has a usable cursor does not read the ownership row: it
/// runs the commands of a commit that reads the row, minus that read, so the bump is written by the save in both. A
/// change the cursor did not hear of makes the next commit read the row: after a heartbeat through a second
/// connection it is admitted, after a takeover through a second connection it is refused as stale.
/// </summary>
internal static class RuntimeCheckpointFenceCursorProviderSmoke
{
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private const string Run = "workflow-fence-cursor";
    private const string SchedulerTable = "elsa_runtime_scheduler_state";

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, IInterceptor, RuntimeDbContext> createContext,
        string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-fence-cursor-{Guid.NewGuid():N}";
        var clock = new FakeTimeProvider(Now);
        var cursors = new EfExecutionFenceCursors(clock);
        var capture = new CommandCapture();
        await using var context = createContext(fixture.ConnectionString, capture);
        Assert.Equal(expectedProvider, context.Database.ProviderName);
        await context.Database.EnsureCreatedAsync();
        // Every statement the other node runs goes through this second connection.
        await using var secondConnection = createContext(fixture.ConnectionString, new CommandCapture());
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor(scope), clock) { FenceCursors = cursors };
        var readingStore = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor(scope), clock);

        // This provider's liveness store records the cursor. The other node's store, on the second connection, has none.
        var lease = await Node(context, scope, clock, "node-a", cursors).AcquireAsync(Run);
        var key = EfExecutionFenceCursors.Key.For(scope, lease);
        Assert.True(cursors.TryGet(key, out var acquired));
        Assert.Equal(new ExecutionFenceCursor(1, lease.ExpiresAt, RuntimeOperationalStateEfModule.SchemaVersion), acquired);

        // A usable cursor: no read. Each commit also changes the run's scheduler row, so the save has a second row.
        Assert.Equal((0, 1), Ownership(await CommitAsync(store, capture, "commit-1", lease)));
        var byCursor = await CommitAsync(store, capture, "commit-2", lease);
        Assert.Equal((0, 1), Ownership(byCursor));
        Assert.True(cursors.TryGet(key, out var afterTwo));
        Assert.Equal(new ExecutionFenceCursor(3, lease.ExpiresAt, RuntimeOperationalStateEfModule.SchemaVersion), afterTwo);

        // The same commit through a store without cursors reads the row first. Its other commands are the same
        // texts in the same order, so the bump is the same statement at the same place in the save.
        var reading = await CommitAsync(readingStore, capture, "commit-3", lease);
        Assert.Equal((1, 1), Ownership(reading));
        Assert.Equal(reading.Where(command => !IsOwnershipRead(command)), byCursor);
        // On this provider the save sends the bump and the scheduler row as one command, so nothing replaces the read.
        Assert.Contains(byCursor, command => IsOwnershipWrite(command) && command.Contains(SchedulerTable, StringComparison.OrdinalIgnoreCase));

        // That commit bumped the row without the cursor hearing of it. The next commit's bump matches no row, the
        // commit runs again reading the row, and is admitted.
        Assert.Equal((1, 2), Ownership(await CommitAsync(store, capture, "commit-4", lease)));
        Assert.Equal(5, await RevisionAsync(secondConnection, scope));

        // The owner's heartbeat through the second connection does the same. The commit after it does not read.
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.True((await Node(secondConnection, scope, clock, "node-a").HeartbeatAsync(lease)).Succeeded);
        Assert.Equal((1, 2), Ownership(await CommitAsync(store, capture, "commit-5", lease)));
        Assert.True(cursors.TryGet(key, out var afterHeartbeat));
        Assert.Equal(new ExecutionFenceCursor(7, lease.ExpiresAt.AddSeconds(20), RuntimeOperationalStateEfModule.SchemaVersion), afterHeartbeat);
        Assert.Equal((0, 1), Ownership(await CommitAsync(store, capture, "commit-6", lease)));

        // A takeover through the second connection: the next commit is refused as stale and writes nothing.
        await Node(secondConnection, scope, clock, "node-b").AcquireAsync(Run);
        var takenOver = await RevisionAsync(secondConnection, scope);
        var before = capture.Count;
        var refusal = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => store.CommitAsync(Commit("commit-7", lease), new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Equal(RuntimeFencingRejectionReason.StaleToken, refusal.Reason);
        Assert.Equal((1, 1), Ownership(capture.Since(before)));
        Assert.Equal(takenOver, await RevisionAsync(secondConnection, scope));
        Assert.True(cursors.TryGet(key, out var afterTakeover));
        Assert.Equal(afterHeartbeat with { Revision = 0 }, afterTakeover);
        var markers = await secondConnection.RuntimeCheckpointCommits.AsNoTracking()
            .Where(row => row.ScopeKey == EfRelationalIdentity.Encode(scope)).Select(row => row.CommitId).ToArrayAsync();
        Assert.Equal(6, markers.Length);
        Assert.DoesNotContain(EfRelationalIdentity.Encode("commit-7"), markers);
    }

    /// <summary>
    /// A save the store does not own the transaction of can still be rolled back, so it is not recorded as a cursor.
    /// SQLite cannot run this: it has no ambient or enlisted transactions.
    /// </summary>
    public static async Task RecordsNoCursorInsideAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, IInterceptor, RuntimeDbContext> createContext,
        string transaction)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-fence-cursor-{Guid.NewGuid():N}";
        var cursors = new EfExecutionFenceCursors(new FakeTimeProvider(Now));
        await using var context = createContext(fixture.ConnectionString, new CommandCapture());
        await context.Database.EnsureCreatedAsync();
        var store = new EfExecutionLivenessStateStore(context, new FixedAccessor(scope), new NoopContinuationCodec()) { FenceCursors = cursors };
        var lease = new RuntimeExecutionLease("lease-a", Run, "node-a", Now, Now.AddMinutes(1), 1);
        var ownership = new ExecutionLivenessState(RuntimeExecutionOwnershipStateId.For(Run), Run, lease, null, null, null);

        if (transaction == "transaction-scope")
        {
            // Disposed without Complete, so the save is rolled back.
            using (new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
                Assert.True((await store.TrySaveAsync(ownership, 0)).Succeeded);
        }
        else
        {
            // A connection enlists only while it is open.
            await context.Database.OpenConnectionAsync();
            using var enlisted = new CommittableTransaction();
            context.Database.EnlistTransaction(enlisted);
            Assert.NotNull(context.Database.GetEnlistedTransaction());
            Assert.Null(Transaction.Current);
            Assert.True((await store.TrySaveAsync(ownership, 0)).Succeeded);
            enlisted.Rollback();
            await context.Database.CloseConnectionAsync();
        }

        Assert.Equal(0, cursors.Count);
        await using var read = createContext(fixture.ConnectionString, new CommandCapture());
        Assert.Empty(await read.ExecutionLivenessStates.AsNoTracking().Where(row => row.ScopeKey == EfRelationalIdentity.Encode(scope)).ToArrayAsync());
    }

    private static async Task<string[]> CommitAsync(
        EfRuntimeCheckpointCommitStore store, CommandCapture capture, string commitId, RuntimeExecutionLease lease)
    {
        var before = capture.Count;
        await store.CommitAsync(Commit(commitId, lease), new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));
        return capture.Since(before);
    }

    private static (int Reads, int Writes) Ownership(string[] commands) => (commands.Count(IsOwnershipRead), commands.Count(IsOwnershipWrite));

    private static bool IsOwnershipRead(string command) =>
        command.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
        command.Contains(RuntimeOperationalStateEfModule.ExecutionLivenessTableName, StringComparison.OrdinalIgnoreCase);

    private static bool IsOwnershipWrite(string command) =>
        !IsOwnershipRead(command) && command.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) &&
        command.Contains(RuntimeOperationalStateEfModule.ExecutionLivenessTableName, StringComparison.OrdinalIgnoreCase);

    private static async Task<long> RevisionAsync(RuntimeDbContext context, string scope) =>
        (await context.ExecutionLivenessStates.AsNoTracking().SingleAsync(row => row.ScopeKey == EfRelationalIdentity.Encode(scope))).Revision;

    private static RuntimeExecutionOwnershipService Node(
        RuntimeDbContext context, string scope, TimeProvider clock, string ownerId, EfExecutionFenceCursors? cursors = null) =>
        new(new EfExecutionLivenessStateStore(context, new FixedAccessor(scope), new NoopContinuationCodec()) { FenceCursors = cursors }, clock,
            new RuntimeExecutionOwnershipOptions { OwnerId = ownerId, LeaseDuration = TimeSpan.FromMinutes(1) });

    private static RuntimeCheckpointCommit Commit(string commitId, RuntimeExecutionLease lease) => new(
        commitId,
        new RuntimeCheckpoint($"checkpoint-{commitId}", "FenceCursorCheckpoint", Run, Now, [], new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(
            null,
            new RuntimeStateChange<SchedulerState>(Run, RuntimeStateChangeOperation.Upsert, new SchedulerState(Run, commitId.Length + commitId[^1]), new Dictionary<string, string>()),
            [], [], [], [], []),
        [],
        new Dictionary<string, string>())
    {
        ExpectedFence = lease.ToFence()
    };

    /// <summary>The text of every command a context runs, in order.</summary>
    private sealed class CommandCapture : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> commands = new();

        public int Count => commands.Count;

        public string[] Since(int from) => commands.Skip(from).ToArray();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class NoopContinuationCodec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token);
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
