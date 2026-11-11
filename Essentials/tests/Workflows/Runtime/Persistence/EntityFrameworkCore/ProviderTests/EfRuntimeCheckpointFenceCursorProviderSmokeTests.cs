using System.Collections.Concurrent;
using System.Data.Common;
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
    [SkippableFact]
    public Task PostgreSql_checkpoint_fence_cursor_smoke() =>
        RuntimeCheckpointFenceCursorProviderSmoke.RunAsync(
            fixture,
            (connection, capture) => new RuntimePostgreSqlDbContext(
                new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).AddInterceptors(capture).Options),
            RuntimePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeCheckpointFenceCursorSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_checkpoint_fence_cursor_smoke() =>
        RuntimeCheckpointFenceCursorProviderSmoke.RunAsync(
            fixture,
            (connection, capture) => new RuntimeSqlServerDbContext(
                new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).AddInterceptors(capture).Options),
            RuntimeSqlServerDbContext.ExpectedProviderName);
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
/// #2564 on a native provider: a fenced commit whose lease has a current cursor is admitted by one conditional
/// statement and does not read the ownership row. A heartbeat and a takeover that arrive through a second connection
/// leave the cursor behind the row, so the next commit reads it: the first is admitted, the second refused as stale.
/// </summary>
internal static class RuntimeCheckpointFenceCursorProviderSmoke
{
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private const string Run = "workflow-fence-cursor";

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, IInterceptor, RuntimeDbContext> createContext,
        string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-fence-cursor-{Guid.NewGuid():N}";
        var clock = new FakeTimeProvider(Now);
        var cursors = new EfExecutionFenceCursors(clock);
        var capture = new OwnershipCommandCapture();
        await using var context = createContext(fixture.ConnectionString, capture);
        Assert.Equal(expectedProvider, context.Database.ProviderName);
        await context.Database.EnsureCreatedAsync();
        // Every statement the other node runs goes through this second connection.
        await using var secondConnection = createContext(fixture.ConnectionString, new OwnershipCommandCapture());
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor(scope), clock) { FenceCursors = cursors };

        var lease = await Node(context, scope, clock, "node-a").AcquireAsync(Run);
        var key = EfExecutionFenceCursors.Key.For(scope, lease);
        cursors.Hold(key, new(await RevisionAsync(secondConnection, scope), lease.AcquiredAt, lease.ExpiresAt));

        // A current cursor: one statement, no read.
        Assert.Equal((0, 1), await CommitAndCountAsync(store, capture, "commit-1", lease));
        Assert.Equal((0, 1), await CommitAndCountAsync(store, capture, "commit-2", lease));
        Assert.True(cursors.TryGet(key, out var afterTwo));
        Assert.Equal(new ExecutionFenceCursor(3, lease.AcquiredAt, lease.ExpiresAt), afterTwo);
        Assert.Equal(3, await RevisionAsync(secondConnection, scope));

        // The owner's heartbeat through the second connection: the statement matches no row, the row is read, and
        // the commit is admitted. The commit after it is one statement again.
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.True((await Node(secondConnection, scope, clock, "node-a").HeartbeatAsync(lease)).Succeeded);
        Assert.Equal((1, 2), await CommitAndCountAsync(store, capture, "commit-3", lease));
        Assert.True(cursors.TryGet(key, out var afterHeartbeat));
        Assert.Equal(new ExecutionFenceCursor(5, lease.AcquiredAt, lease.ExpiresAt.AddSeconds(20)), afterHeartbeat);
        Assert.Equal((0, 1), await CommitAndCountAsync(store, capture, "commit-4", lease));

        // A takeover through the second connection: the next commit is refused as stale and writes nothing.
        await Node(secondConnection, scope, clock, "node-b").AcquireAsync(Run);
        var takenOver = await RevisionAsync(secondConnection, scope);
        var before = capture.Count;
        var refusal = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => store.CommitAsync(Commit("commit-5", lease), new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Equal(RuntimeFencingRejectionReason.StaleToken, refusal.Reason);
        Assert.Equal((1, 1), capture.Since(before));
        Assert.Equal(takenOver, await RevisionAsync(secondConnection, scope));
        Assert.True(cursors.TryGet(key, out var afterTakeover));
        Assert.Equal(afterHeartbeat with { Revision = 6 }, afterTakeover);
        var markers = await secondConnection.RuntimeCheckpointCommits.AsNoTracking()
            .Where(row => row.ScopeKey == EfRelationalIdentity.Encode(scope)).Select(row => row.CommitId).ToArrayAsync();
        Assert.Equal(4, markers.Length);
        Assert.DoesNotContain(EfRelationalIdentity.Encode("commit-5"), markers);
    }

    private static async Task<(int Reads, int Writes)> CommitAndCountAsync(
        EfRuntimeCheckpointCommitStore store, OwnershipCommandCapture capture, string commitId, RuntimeExecutionLease lease)
    {
        var before = capture.Count;
        await store.CommitAsync(Commit(commitId, lease), new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));
        return capture.Since(before);
    }

    private static async Task<long> RevisionAsync(RuntimeDbContext context, string scope) =>
        (await context.ExecutionLivenessStates.AsNoTracking().SingleAsync(row => row.ScopeKey == EfRelationalIdentity.Encode(scope))).Revision;

    private static RuntimeExecutionOwnershipService Node(RuntimeDbContext context, string scope, TimeProvider clock, string ownerId) =>
        new(new EfExecutionLivenessStateStore(context, new FixedAccessor(scope), new NoopContinuationCodec()), clock,
            new RuntimeExecutionOwnershipOptions { OwnerId = ownerId, LeaseDuration = TimeSpan.FromMinutes(1) });

    private static RuntimeCheckpointCommit Commit(string commitId, RuntimeExecutionLease lease) => new(
        commitId,
        new RuntimeCheckpoint($"checkpoint-{commitId}", "EmptyCheckpoint", Run, Now, [], new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
        [],
        new Dictionary<string, string>())
    {
        ExpectedFence = lease.ToFence()
    };

    /// <summary>Counts the reads (SELECT) and writes (UPDATE) of the ownership table a context runs.</summary>
    private sealed class OwnershipCommandCapture : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> commands = new();

        public int Count => commands.Count;

        public (int Reads, int Writes) Since(int from)
        {
            var captured = commands.Skip(from).ToArray();
            return (captured.Count(command => command.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)),
                captured.Count(command => command.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)));
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        private void Record(DbCommand command)
        {
            if (command.CommandText.Contains(RuntimeOperationalStateEfModule.ExecutionLivenessTableName, StringComparison.OrdinalIgnoreCase))
                commands.Enqueue(command.CommandText);
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
