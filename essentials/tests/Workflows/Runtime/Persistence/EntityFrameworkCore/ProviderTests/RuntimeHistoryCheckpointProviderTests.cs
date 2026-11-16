using System.Data.Common;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeHistoryCheckpointPostgreSqlTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableTheory]
    [InlineData("replay")] [InlineData("required-missing")] [InlineData("uncaptured")]
    [InlineData("marker-rollback")] [InlineData("derived-rollback")] [InlineData("lost-ack")]
    [InlineData("stale-fence")] [InlineData("marker-fence")] [InlineData("first-head-heartbeat")] [InlineData("first-head-takeover")]
    public Task PostgreSql_captured_native_checkpoint(string scenario) => RuntimeHistoryCheckpointProviderContract.Run(fixture, "PostgreSql", scenario,
        (connection, interceptors) => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>()
            .UseNpgsql(connection, sql => sql.MigrationsHistoryTable(RuntimeEfModule.HistoryTableName)).AddInterceptors(interceptors).Options));
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeHistoryCheckpointSqlServerTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableTheory]
    [InlineData("replay")] [InlineData("required-missing")] [InlineData("uncaptured")]
    [InlineData("marker-rollback")] [InlineData("derived-rollback")] [InlineData("lost-ack")]
    [InlineData("stale-fence")] [InlineData("marker-fence")] [InlineData("first-head-heartbeat")] [InlineData("first-head-takeover")]
    public Task SqlServer_captured_native_checkpoint(string scenario) => RuntimeHistoryCheckpointProviderContract.Run(fixture, "SqlServer", scenario,
        (connection, interceptors) => new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsHistoryTable(RuntimeEfModule.HistoryTableName)).AddInterceptors(interceptors).Options));
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeHistoryCheckpointMySqlTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableTheory]
    [InlineData("replay")] [InlineData("required-missing")] [InlineData("uncaptured")]
    [InlineData("marker-rollback")] [InlineData("derived-rollback")] [InlineData("lost-ack")]
    [InlineData("stale-fence")] [InlineData("marker-fence")] [InlineData("first-head-heartbeat")] [InlineData("first-head-takeover")]
    public Task MySql_captured_native_checkpoint(string scenario) => RuntimeHistoryCheckpointProviderContract.Run(fixture, "MySql", scenario,
        (connection, interceptors) => new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>()
            .UseMySQL(connection, sql => sql.MigrationsHistoryTable(RuntimeEfModule.HistoryTableName)).AddInterceptors(interceptors).Options));
}

// Controlled admitted native headers and contributions exercise the actual migrated provider,
// native writer and SQL transactions. They do not qualify authored capture, DI or composed UX.
internal static class RuntimeHistoryCheckpointProviderContract
{
    private const string Scope = "Case|tenant";
    private const string ForeignScope = "case|tenant";
    private const string RunId = "captured-run";
    private static readonly DateTimeOffset At = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly RuntimeHistoryCapturePolicy Policy = new("native-provider-history", 1, true, ["ActivityStarted", "BookmarkCreated"]);
    private static readonly RuntimeCheckpointPersistenceDecision Immediate = new(RuntimeCheckpointPersistenceMode.Immediate);

    internal static async Task Run(RuntimeBookmarksProviderFixture provider, string engine, string scenario,
        Func<string, IInterceptor[], RuntimeDbContext> open)
    {
        Skip.IfNot(provider.IsAvailable, provider.SkipReason ?? "The native provider is unavailable.");
        var connection = new DbConnectionStringBuilder { ConnectionString = provider.ConnectionString };
        foreach (var key in connection.Keys.Cast<string>().Where(key => key.Equals("Database", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase)).ToArray()) connection.Remove(key);
        connection["Database"] = "history_checkpoint_" + Guid.NewGuid().ToString("N");
        var database = new Database(connection.ConnectionString, open);
        try
        {
            await database.Create(scenario != "uncaptured");
            switch (scenario)
            {
                case "replay":
                    await using (var context = database.Open())
                    {
                        var store = database.Store(context); var first = Captured("original");
                        await store.CommitAsync(first, Immediate); await store.CommitAsync(first, Immediate);
                        database.Clock.Advance(TimeSpan.FromMinutes(1));
                        await store.CommitAsync(Plain("later") with { HistoryBatches = first.HistoryBatches }, Immediate);
                        Assert.False(context.ChangeTracker.HasChanges()); Assert.Null(context.Database.CurrentTransaction);
                    }
                    await database.AssertCaptured(2, "later");
                    break;
                case "required-missing":
                    await using (var context = database.Open())
                        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => database.Store(context).CommitAsync(WithBookmark("missing"), Immediate).AsTask());
                    await database.AssertAbsent();
                    break;
                case "uncaptured":
                    await using (var setup = database.Open()) await setup.Database.ExecuteSqlRawAsync("DROP TABLE elsa_runtime_history_head");
                    var commands = new Commands();
                    await using (var context = database.Open(commands)) await database.Store(context).CommitAsync(WithBookmark("uncaptured"), Immediate);
                    Assert.DoesNotContain(commands.Texts, sql => sql.Contains("elsa_runtime_history_", StringComparison.OrdinalIgnoreCase));
                    await database.AssertAbsent(headExists: false, nativeCount: 1);
                    break;
                case "marker-rollback":
                case "derived-rollback":
                    var target = scenario == "marker-rollback" ? "elsa_runtime_checkpoint_commit" : "elsa_runtime_history_occurrence_member";
                    await database.CreateFailureTrigger(engine, target);
                    await using (var context = database.Open())
                    {
                        var error = await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() => database.Store(context).CommitAsync(Captured("original"), Immediate).AsTask());
                        Assert.Equal(RunId, error.WorkflowExecutionId);
                        Assert.Contains(Causes(error), cause => cause is DbException);
                        Assert.Empty(context.ChangeTracker.Entries()); Assert.Null(context.Database.CurrentTransaction);
                    }
                    await database.AssertAbsent(); await database.DropFailureTrigger(engine, target);
                    await using (var retry = database.Open()) await database.Store(retry).CommitAsync(Captured("original"), Immediate);
                    await database.AssertCaptured(1, "original");
                    break;
                case "lost-ack":
                    var lost = new LostAcknowledgement(); var queries = new Commands();
                    await using (var context = database.Open(lost, queries))
                    {
                        lost.Arm(); var store = database.Store(context); var commit = Captured("original");
                        await store.CommitAsync(commit, Immediate); await store.CommitAsync(commit, Immediate);
                        Assert.Empty(context.ChangeTracker.Entries()); Assert.Null(context.Database.CurrentTransaction);
                    }
                    Assert.Equal(1, lost.Losses);
                    Assert.InRange(queries.Texts.Count(sql => sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                        sql.Contains("elsa_runtime_checkpoint_commit", StringComparison.OrdinalIgnoreCase)), 1, 2);
                    await database.AssertCaptured(1, "original");
                    break;
                case "stale-fence":
                case "marker-fence":
                    await using (var context = database.Open())
                    {
                        var lease = await database.Owner(context, "owner-a").AcquireAsync(RunId);
                        var commit = Captured("original") with { ExpectedFence = lease.ToFence() };
                        var store = database.Store(context);
                        if (scenario == "marker-fence") await store.CommitAsync(commit, Immediate);
                        var stale = commit with { ExpectedFence = new("wrong-lease", "wrong-owner", 1) };
                        if (scenario == "marker-fence") await store.CommitAsync(stale, Immediate);
                        else await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => store.CommitAsync(stale, Immediate).AsTask());
                    }
                    if (scenario == "marker-fence") await database.AssertCaptured(1, "original"); else await database.AssertAbsent();
                    break;
                case "first-head-heartbeat":
                case "first-head-takeover":
                    var cursors = new EfExecutionFenceCursors(database.Clock); var capture = new Commands();
                    await using (var context = database.Open(capture))
                    {
                        var lease = await database.Owner(context, "owner-a", cursors).AcquireAsync(RunId);
                        await using (var other = database.Open())
                        {
                            if (scenario == "first-head-heartbeat")
                            {
                                database.Clock.Advance(TimeSpan.FromSeconds(20));
                                Assert.True((await database.Owner(other, "owner-a").HeartbeatAsync(lease)).Succeeded);
                            }
                            else await database.Owner(other, "owner-b").AcquireAsync(RunId);
                        }
                        var store = database.Store(context, cursors);
                        var commit = Captured("original") with { ExpectedFence = lease.ToFence() };
                        if (scenario == "first-head-heartbeat") await store.CommitAsync(commit, Immediate);
                        else await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => store.CommitAsync(commit, Immediate).AsTask());
                        if (scenario == "first-head-heartbeat") Assert.False(context.ChangeTracker.HasChanges());
                        else Assert.Empty(context.ChangeTracker.Entries());
                        Assert.Null(context.Database.CurrentTransaction);
                    }
                    Assert.Contains(capture.Texts, sql => sql.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) &&
                        sql.Contains(RuntimeOperationalStateEfModule.ExecutionLivenessTableName, StringComparison.OrdinalIgnoreCase));
                    if (scenario == "first-head-heartbeat") await database.AssertCaptured(1, "original", database.Clock.GetUtcNow()); else await database.AssertAbsent();
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(scenario));
            }
            await database.AssertForeignUnchanged();
        }
        finally { await using var cleanup = database.Open(); await cleanup.Database.EnsureDeletedAsync(); }
    }

    private static WorkflowExecutionState Workflow(string scope, bool captured) => new(RunId, new("artifact", "definition", "version", "1", "hash"),
        WorkflowExecutionStatus.Running, null, At, At, At, null, null, null, scope, new Dictionary<string, string>())
        { HistoryCapturePolicy = captured ? Policy : null, HistoryCaptureEnabledAtAdmission = captured };
    private static RuntimeCheckpointCommit Plain(string id) => new(id, new("checkpoint-" + id, "control", RunId, At, [], new Dictionary<string, string>()),
        new(null, null, [], [], [], [], []), [], new Dictionary<string, string>());
    private static RuntimeCheckpointCommit WithBookmark(string id)
    {
        var bookmark = new BookmarkState("bookmark", RunId, "activity", "node", "resume", "signal", "owned-payload",
            JsonSerializer.SerializeToElement("owned-payload"), new Dictionary<string, string>(), At, At.AddHours(1));
        return Plain(id) with { StateChanges = new(null, null, [], [new("bookmark", RuntimeStateChangeOperation.Upsert, bookmark, new Dictionary<string, string>())], [], [], []) };
    }
    private static RuntimeCheckpointCommit Captured(string id)
    {
        var commit = WithBookmark(id); var bookmark = commit.StateChanges.Bookmarks.Single().State!;
        var start = new RuntimeHistoryCandidate(new("started", RunId, 0, RuntimeHistoryEventCategory.ActivityLifecycle, "ActivityStarted", At,
            "activity", checkpointId: commit.Checkpoint.CheckpointId)
            { Authority = RuntimeHistoryAuthority.CommittedTransition, OccurredAtKnown = false, ActivityAncestryKnown = true }, 0);
        var created = new RuntimeHistoryCandidate(new("created", RunId, 0, RuntimeHistoryEventCategory.BookmarkLifecycle, "BookmarkCreated", At,
            "activity", checkpointId: commit.Checkpoint.CheckpointId)
            { Authority = RuntimeHistoryAuthority.CommittedTransition, OccurredAtKnown = false, BookmarkId = "bookmark" }, 1,
            new(ActivityExecutionBookmarkSummary.From(bookmark), RuntimeHistoryBookmarkStatus.Active));
        return commit with { HistoryBatches = [new(Policy, commit.Checkpoint.CheckpointId, At, true, [start, created])] };
    }
    private static IEnumerable<Exception> Causes(Exception error)
    {
        yield return error;
        if (error is AggregateException group)
        { foreach (var inner in group.InnerExceptions) foreach (var cause in Causes(inner)) yield return cause; }
        else if (error.InnerException is { } inner) foreach (var cause in Causes(inner)) yield return cause;
    }

    private sealed class Database(string connection, Func<string, IInterceptor[], RuntimeDbContext> open)
    {
        internal FakeTimeProvider Clock { get; } = new(At.AddMinutes(1));
        private readonly Access access = new();
        private string foreignContent = "";
        internal RuntimeDbContext Open(params IInterceptor[] interceptors) => open(connection, interceptors);
        internal EfRuntimeCheckpointCommitStore Store(RuntimeDbContext context, EfExecutionFenceCursors? cursors = null) => new(context, access, Clock) { FenceCursors = cursors };
        internal RuntimeExecutionOwnershipService Owner(RuntimeDbContext context, string owner, EfExecutionFenceCursors? cursors = null) =>
            new(new EfExecutionLivenessStateStore(context, access, new Codec()) { FenceCursors = cursors }, Clock,
                new RuntimeExecutionOwnershipOptions { OwnerId = owner, LeaseDuration = TimeSpan.FromMinutes(10) });
        internal async Task Create(bool captured)
        {
            await using var context = Open(); Assert.False(context.Database.HasPendingModelChanges()); await context.Database.MigrateAsync();
            Assert.Equal(7, (await context.Database.GetAppliedMigrationsAsync()).Count()); Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            var native = EfWorkflowExecutionStateStore.ToEntity(Workflow(Scope, captured), Scope, EfWorkflowExecutionStateStore.CreateId(Scope, RunId), 1);
            var foreign = EfWorkflowExecutionStateStore.ToEntity(Workflow(ForeignScope, true), ForeignScope, EfWorkflowExecutionStateStore.CreateId(ForeignScope, RunId), 1);
            foreignContent = foreign.ContentJson; context.WorkflowExecutionStates.AddRange(native, foreign); await context.SaveChangesAsync();
        }
        internal async Task AssertForeignUnchanged()
        {
            await using var context = Open(); var id = EfWorkflowExecutionStateStore.CreateId(ForeignScope, RunId);
            var row = await context.WorkflowExecutionStates.AsNoTracking().SingleAsync(row => row.Id == id);
            Assert.Equal(foreignContent, row.ContentJson); Assert.Equal(1, row.Revision);
        }
        internal async Task AssertCaptured(int markers, string lastPhysical, DateTimeOffset? recordedAt = null)
        {
            await using var context = Open(); var mapping = new EfRuntimeHistoryProjectionMapping(RuntimeHistoryEfModule.Chain);
            var head = mapping.Read(await context.Set<RuntimeHistoryHeadEntity>().SingleAsync(), Scope, RunId);
            Assert.Equal(2, head.LastSequence); Assert.Equal(lastPhysical, head.LastPhysicalCommitId);
            var rows = await context.Set<RuntimeHistoryEventEntity>().OrderBy(row => row.Sequence).ToArrayAsync();
            Assert.Equal(new long[] { 1, 2 }, rows.Select(row => row.Sequence));
            foreach (var row in rows)
            {
                var item = mapping.Read(row, Scope, RunId, head);
                Assert.Equal("original", item.PhysicalCommitId); Assert.Equal(At, item.OccurredAt); Assert.False(item.OccurredAtKnown);
                Assert.Equal(recordedAt ?? At.AddMinutes(1), item.RecordedAt);
            }
            Assert.Single(await context.Bookmarks.ToArrayAsync()); Assert.Single(await context.Set<RuntimeHistoryBookmarkEntity>().ToArrayAsync());
            Assert.Single(await context.Set<RuntimeHistoryActivityEntity>().ToArrayAsync()); Assert.Single(await context.Set<RuntimeHistoryOccurrenceEntity>().ToArrayAsync());
            Assert.Equal(2, await context.Set<RuntimeHistoryOccurrenceMembershipEntity>().CountAsync());
            Assert.Equal(2, await context.Set<RuntimeHistoryOccurrenceCounterEntity>().CountAsync());
            Assert.Equal(2, await context.Set<RuntimeHistoryBookmarkCountEntity>().CountAsync());
            Assert.Equal(markers, await context.RuntimeCheckpointCommits.CountAsync());
        }
        internal async Task AssertAbsent(bool headExists = true, int nativeCount = 0)
        {
            await using var context = Open();
            if (headExists) Assert.Empty(await context.Set<RuntimeHistoryHeadEntity>().ToArrayAsync());
            Assert.Empty(await context.Set<RuntimeHistoryEventEntity>().ToArrayAsync()); Assert.Empty(await context.Set<RuntimeHistoryBookmarkEntity>().ToArrayAsync());
            Assert.Empty(await context.Set<RuntimeHistoryBookmarkCountEntity>().ToArrayAsync()); Assert.Empty(await context.Set<RuntimeHistoryActivityEntity>().ToArrayAsync());
            Assert.Empty(await context.Set<RuntimeHistoryOccurrenceEntity>().ToArrayAsync()); Assert.Empty(await context.Set<RuntimeHistoryOccurrenceMembershipEntity>().ToArrayAsync());
            Assert.Empty(await context.Set<RuntimeHistoryOccurrenceCounterEntity>().ToArrayAsync());
            Assert.Equal(nativeCount, await context.Bookmarks.CountAsync()); Assert.Equal(nativeCount, await context.RuntimeCheckpointCommits.CountAsync());
        }
        internal async Task CreateFailureTrigger(string engine, string table)
        {
            await using var context = Open();
            var sql = engine switch
            {
                "PostgreSql" => $"CREATE FUNCTION history_checkpoint_fail() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'controlled history save failure'; END; $$; CREATE TRIGGER history_checkpoint_fail BEFORE INSERT ON {table} FOR EACH ROW EXECUTE FUNCTION history_checkpoint_fail();",
                "SqlServer" => $"CREATE TRIGGER history_checkpoint_fail ON {table} INSTEAD OF INSERT AS BEGIN THROW 51000, 'controlled history save failure', 1; END;",
                _ => $"CREATE TRIGGER history_checkpoint_fail BEFORE INSERT ON {table} FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'controlled history save failure'"
            };
            await context.Database.ExecuteSqlRawAsync(sql);
        }
        internal async Task DropFailureTrigger(string engine, string table)
        {
            await using var context = Open();
            await context.Database.ExecuteSqlRawAsync(engine == "PostgreSql"
                ? $"DROP TRIGGER history_checkpoint_fail ON {table}; DROP FUNCTION history_checkpoint_fail();" : "DROP TRIGGER history_checkpoint_fail");
        }
    }
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new(Scope)); }
    private sealed class Codec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token);
    }
    private sealed class Commands : DbCommandInterceptor
    {
        internal List<string> Texts { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        { Texts.Add(command.CommandText); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken token = default)
        { Texts.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
    private sealed class LostAcknowledgement : DbTransactionInterceptor
    {
        private int armed;
        internal int Losses { get; private set; }
        internal void Arm() => Interlocked.Exchange(ref armed, 1);
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken token = default)
        {
            if (Interlocked.Exchange(ref armed, 0) == 1) { Losses++; throw new IOException("Controlled acknowledgement loss after the actual provider committed."); }
            return Task.CompletedTask;
        }
    }
}
