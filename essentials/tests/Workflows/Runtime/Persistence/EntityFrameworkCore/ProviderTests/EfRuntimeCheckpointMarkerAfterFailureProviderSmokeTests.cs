using System.Collections.Concurrent;
using System.Data.Common;
using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeCheckpointMarkerAfterFailurePostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    private static RuntimeDbContext Create(string connection, IInterceptor capture) => new RuntimePostgreSqlDbContext(
        new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).AddInterceptors(capture).Options);

    [SkippableFact]
    public Task PostgreSql_plain_commit_reads_its_marker_only_after_a_failure_and_a_replay_gets_the_stored_result() =>
        RuntimeCheckpointMarkerAfterFailureProviderSmoke.RunAsync(fixture, Create, RuntimePostgreSqlDbContext.ExpectedProviderName);

    [SkippableTheory]
    [InlineData("transaction-scope")]
    [InlineData("enlisted-transaction")]
    public Task PostgreSql_replay_inside_a_transaction_the_store_does_not_own_reads_its_marker_first(string transaction) =>
        RuntimeCheckpointMarkerAfterFailureProviderSmoke.ReadsMarkerFirstInsideAsync(fixture, Create, transaction);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeCheckpointMarkerAfterFailureSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    private static RuntimeDbContext Create(string connection, IInterceptor capture) => new RuntimeSqlServerDbContext(
        new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).AddInterceptors(capture).Options);

    [SkippableFact]
    public Task SqlServer_plain_commit_reads_its_marker_only_after_a_failure_and_a_replay_gets_the_stored_result() =>
        RuntimeCheckpointMarkerAfterFailureProviderSmoke.RunAsync(fixture, Create, RuntimeSqlServerDbContext.ExpectedProviderName);

    [SkippableTheory]
    [InlineData("transaction-scope")]
    [InlineData("enlisted-transaction")]
    public Task SqlServer_replay_inside_a_transaction_the_store_does_not_own_reads_its_marker_first(string transaction) =>
        RuntimeCheckpointMarkerAfterFailureProviderSmoke.ReadsMarkerFirstInsideAsync(fixture, Create, transaction);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeCheckpointMarkerAfterFailureMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_plain_commit_reads_its_marker_only_after_a_failure_and_a_replay_gets_the_stored_result() =>
        RuntimeCheckpointMarkerAfterFailureProviderSmoke.RunAsync(
            fixture,
            (connection, capture) => new RuntimeMySqlDbContext(
                new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).AddInterceptors(capture).Options),
            RuntimeMySqlDbContext.ExpectedProviderName);
}

/// <summary>
/// #2564 on a native provider. A plain commit does not read its marker before it runs. A replay of a commit that is
/// durable runs its attempt, which the provider refuses at the marker's key, rolls back, and gets the stored result
/// from the marker read after that failure. A commit id that is durable with other content is refused as a replay
/// conflict the same way. Neither leaves anything behind, and the context commits again afterwards. Inside a
/// transaction the store does not own, a replay reads its marker first.
/// </summary>
internal static class RuntimeCheckpointMarkerAfterFailureProviderSmoke
{
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly RuntimeCheckpointPersistenceDecision Immediate = new(RuntimeCheckpointPersistenceMode.Immediate);
    private const string Run = "workflow-marker-after-failure";

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, IInterceptor, RuntimeDbContext> createContext,
        string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-marker-after-failure-{Guid.NewGuid():N}";
        var capture = new CommandCapture();
        await using var context = createContext(fixture.ConnectionString, capture);
        Assert.Equal(expectedProvider, context.Database.ProviderName);
        await context.Database.EnsureCreatedAsync();
        await using var secondConnection = createContext(fixture.ConnectionString, new CommandCapture());
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor(scope), new FakeTimeProvider(Now));

        // A commit that is not durable yet: no marker read, and its marker is written.
        var before = capture.Count;
        var first = await store.CommitAsync(SchedulerCommit("commit-1", 1), Immediate);
        Assert.DoesNotContain(capture.Since(before), IsMarkerRead);
        Assert.Equal(1, await RevisionAsync(secondConnection, scope));

        // The same commit again. Its attempt fails on the marker's key and rolls back. The marker read that follows
        // is the last command, and it answers with the stored result.
        before = capture.Count;
        var replay = await store.CommitAsync(SchedulerCommit("commit-1", 1), Immediate);
        Assert.Equal(first.PendingPostCommitWorkIds, replay.PendingPostCommitWorkIds);
        Assert.Equal(first.ConsumedSchedulerWorkItemIds, replay.ConsumedSchedulerWorkItemIds);
        var replayCommands = capture.Since(before);
        Assert.Single(replayCommands, IsMarkerRead);
        Assert.True(IsMarkerRead(replayCommands[^1]));
        Assert.True(replayCommands.Length > 1);
        Assert.Equal(1, await RevisionAsync(secondConnection, scope));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);

        // The commit id with other content is refused as a replay conflict, and nothing of its attempt is kept.
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() => store.CommitAsync(SchedulerCommit("commit-1", 2), Immediate).AsTask());
        Assert.Equal(1, await RevisionAsync(secondConnection, scope));

        // The context and its connection commit again after those failed attempts.
        before = capture.Count;
        await store.CommitAsync(SchedulerCommit("commit-2", 2), Immediate);
        Assert.DoesNotContain(capture.Since(before), IsMarkerRead);
        Assert.Equal(2, await RevisionAsync(secondConnection, scope));
        var markers = await secondConnection.RuntimeCheckpointCommits.AsNoTracking()
            .Where(row => row.ScopeKey == EfRelationalIdentity.Encode(scope)).Select(row => row.CommitId).ToArrayAsync();
        Assert.Equal(2, markers.Length);
    }

    /// <summary>
    /// The commit writes a workflow row, so an attempt would ask the root write lease manager before it tries to
    /// begin its transaction. Read first, the replay is answered by one command and the manager is not asked.
    /// SQLite cannot run this: it has no ambient or enlisted transactions.
    /// </summary>
    public static async Task ReadsMarkerFirstInsideAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<string, IInterceptor, RuntimeDbContext> createContext,
        string transaction)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-marker-after-failure-{Guid.NewGuid():N}";
        var clock = new FakeTimeProvider(Now);
        var commit = RootCommit("commit-root", scope);
        await using (var seed = createContext(fixture.ConnectionString, new CommandCapture()))
        {
            await seed.Database.EnsureCreatedAsync();
            await new EfRuntimeCheckpointCommitStore(seed, new FixedAccessor(scope), clock, new RecordingRootWriteLeaseManager()).CommitAsync(commit, Immediate);
        }
        var capture = new CommandCapture();
        var manager = new RecordingRootWriteLeaseManager();
        await using var context = createContext(fixture.ConnectionString, capture);
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor(scope), clock, manager);

        if (transaction == "transaction-scope")
        {
            using (new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
                await store.CommitAsync(commit, Immediate);
        }
        else
        {
            // A connection enlists only while it is open.
            await context.Database.OpenConnectionAsync();
            using var enlisted = new CommittableTransaction();
            context.Database.EnlistTransaction(enlisted);
            Assert.NotNull(context.Database.GetEnlistedTransaction());
            Assert.Null(Transaction.Current);
            await store.CommitAsync(commit, Immediate);
            enlisted.Rollback();
            await context.Database.CloseConnectionAsync();
        }

        Assert.Equal(0, manager.Asked);
        Assert.True(IsMarkerRead(Assert.Single(capture.Since(0))));
    }

    private static bool IsMarkerRead(string command) =>
        command.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
        command.Contains(RuntimeOperationalStateEfModule.CheckpointCommitTableName, StringComparison.OrdinalIgnoreCase);

    private static async Task<long> RevisionAsync(RuntimeDbContext context, string scope) =>
        (await context.SchedulerStates.AsNoTracking().SingleAsync(row => row.ScopeKey == EfRelationalIdentity.Encode(scope))).Revision;

    private static RuntimeCheckpointCommit SchedulerCommit(string commitId, long version) => new(
        commitId,
        new RuntimeCheckpoint($"checkpoint-{commitId}", "MarkerAfterFailureCheckpoint", Run, Now, [], new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(
            null,
            new RuntimeStateChange<SchedulerState>(Run, RuntimeStateChangeOperation.Upsert, new SchedulerState(Run, version), new Dictionary<string, string>()),
            [], [], [], [], []),
        [],
        new Dictionary<string, string>());

    private static RuntimeCheckpointCommit RootCommit(string commitId, string scope) => new(
        commitId,
        new RuntimeCheckpoint($"checkpoint-{commitId}", "MarkerAfterFailureCheckpoint", Run, Now, [], new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>(Run, RuntimeStateChangeOperation.Upsert,
                new WorkflowExecutionState(Run, new("artifact", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Running,
                    null, Now, Now, Now, null, null, null, scope, new Dictionary<string, string>()),
                new Dictionary<string, string>()),
            null, [], [], [], [], []),
        [],
        new Dictionary<string, string>());

    /// <summary>Counts how often the store asks for a root write lease, and runs the write.</summary>
    private sealed class RecordingRootWriteLeaseManager : IWorkflowExecutableRootWriteLeaseManager
    {
        public int Asked { get; private set; }

        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default)
        {
            Asked++;
            return write(cancellationToken);
        }
    }

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

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
