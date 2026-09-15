using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfRuntimeCheckpointCommitStoreTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Marker_is_create_only_replay_safe_scope_isolated_and_restartable()
    {
        await using var database = await TestDatabase.CreateAsync();
        var commit = Commit("commit-1");

        await using (var tenantA = database.Open("tenant-a"))
        {
            var store = new EfRuntimeCheckpointCommitStore(tenantA, new FixedAccessor("tenant-a"));
            var first = await store.CommitAsync(commit, Decision());
            var replay = await store.CommitAsync(commit, Decision());

            Assert.Empty(first.PendingPostCommitWorkIds);
            Assert.Empty(replay.PendingPostCommitWorkIds);
            await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() =>
                store.CommitAsync(commit with { Checkpoint = commit.Checkpoint with { Name = "different" } }, Decision()).AsTask());
        }

        await using (var tenantB = database.Open("tenant-b"))
        {
            var store = new EfRuntimeCheckpointCommitStore(tenantB, new FixedAccessor("tenant-b"));
            await store.CommitAsync(commit, Decision());
            Assert.Single(await tenantB.RuntimeCheckpointCommits.Where(row => row.ScopeKey == EfRelationalIdentity.Encode("tenant-b")).ToArrayAsync());
        }

        await using var restarted = database.Open("tenant-a");
        Assert.Single(await restarted.RuntimeCheckpointCommits.Where(row => row.ScopeKey == EfRelationalIdentity.Encode("tenant-a")).ToArrayAsync());
        var row = await restarted.RuntimeCheckpointCommits.SingleAsync(row => row.ScopeKey == EfRelationalIdentity.Encode("tenant-a"));
        Assert.Equal(EfRelationalIdentity.Encode("tenant-a"), row.ScopeKey);
        Assert.Equal(EfRelationalIdentity.Encode(commit.CommitId), row.CommitId);
    }

    [Fact]
    public async Task Marker_commit_flushes_a_pre_staged_sibling_in_the_same_transaction()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new ParticipantOrderAndFailureInterceptor();
        await using var context = database.Open("tenant-a", interceptor);
        context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));

        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
        await store.CommitAsync(Commit("commit-sibling"), Decision());

        await using var reopened = database.Open("tenant-a");
        Assert.Single(await reopened.SchedulerStates.ToArrayAsync());
        Assert.Single(await reopened.RuntimeCheckpointCommits.ToArrayAsync());
        var schedulerIndex = interceptor.Tables.IndexOf("elsa_runtime_scheduler_state");
        var markerIndex = interceptor.Tables.IndexOf("elsa_runtime_checkpoint_commit");
        Assert.True(schedulerIndex >= 0 && markerIndex > schedulerIndex, "The immutable checkpoint marker must be inserted after staged participant rows.");
    }

    [Fact]
    public async Task Marker_failure_rolls_back_a_pre_staged_sibling_and_leaves_marker_reusable()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new ParticipantOrderAndFailureInterceptor();
        await using (var context = database.Open("tenant-a", interceptor))
        {
            context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));
            interceptor.Arm();
            var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));

            await Assert.ThrowsAsync<DbUpdateException>(() =>
                store.CommitAsync(Commit("commit-rollback"), Decision()).AsTask());
            Assert.Contains("elsa_runtime_scheduler_state", interceptor.Tables);
            Assert.DoesNotContain("elsa_runtime_checkpoint_commit", interceptor.Tables);
        }

        await using (var verification = database.Open("tenant-a"))
        {
            Assert.Empty(await verification.SchedulerStates.ToArrayAsync());
            Assert.Empty(await verification.RuntimeCheckpointCommits.ToArrayAsync());
        }

        await using var retryContext = database.Open("tenant-a");
        var retry = new EfRuntimeCheckpointCommitStore(retryContext, new FixedAccessor("tenant-a"));
        await retry.CommitAsync(Commit("commit-rollback"), Decision());
        Assert.Single(await retryContext.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Ambiguous_transaction_acknowledgement_reconciles_through_the_marker()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new CommitAcknowledgementLossInterceptor();
        await using (var context = database.Open("tenant-a", interceptor))
        {
            var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
            interceptor.Arm();
            var result = await store.CommitAsync(Commit("commit-ambiguous"), Decision());
            Assert.Empty(result.PendingPostCommitWorkIds);
        }

        await using var reopened = database.Open("tenant-a");
        Assert.Single(await reopened.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public void Registration_exposes_only_the_preview_marker_adapter_after_an_owned_runtime_context_exists()
    {
        var services = new ServiceCollection();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });
        services.AddRuntimeCheckpointCommitEntityFrameworkCore();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(EfRuntimeCheckpointCommitStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore));
    }

    [Fact]
    public async Task Thin_marker_slice_rejects_nonempty_state_and_fence_without_writing_a_marker()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));

        var schedulerState = new RuntimeStateChange<SchedulerState>(
            "workflow-a",
            RuntimeStateChangeOperation.Upsert,
            new SchedulerState("workflow-a", 1),
            new Dictionary<string, string>());
        var nonempty = Commit("commit-nonempty") with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(
                null,
                schedulerState,
                [],
                [],
                [],
                [],
                [])
        };

        await Assert.ThrowsAsync<NotSupportedException>(() => store.CommitAsync(nonempty, Decision()).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            store.CommitAsync(Commit("commit-fenced") with { ExpectedFence = new RuntimeExecutionFence("lease", "owner", 1) }, Decision()).AsTask());
        Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync());

        var replayable = Commit("commit-replay-fence");
        await store.CommitAsync(replayable, Decision());
        var replay = await store.CommitAsync(replayable with { ExpectedFence = new RuntimeExecutionFence("lease", "owner", 1) }, Decision());
        Assert.Empty(replay.PendingPostCommitWorkIds);
    }

    private static RuntimeCheckpointCommit Commit(string commitId) => new(
        commitId,
        new RuntimeCheckpoint(
            $"checkpoint-{commitId}",
            "EmptyCheckpoint",
            "workflow-a",
            OccurredAt,
            [],
            new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
        [],
        new Dictionary<string, string>());

    private static RuntimeCheckpointPersistenceDecision Decision() => new(RuntimeCheckpointPersistenceMode.Immediate);

    private static SchedulerStateEntity SchedulerRow(string scope, string workflowExecutionId) => new()
    {
        Id = "pre-staged-scheduler-row",
        ScopeKey = EfRelationalIdentity.Encode(scope),
        ScopeKeyHash = EfRelationalIdentity.Hash(scope),
        WorkflowExecutionId = EfRelationalIdentity.Encode(workflowExecutionId),
        WorkflowExecutionIdHash = EfRelationalIdentity.Hash(workflowExecutionId),
        WorkflowExecutionIdOrderKey = Order(workflowExecutionId),
        Collection = "schedulerState",
        ContentJson = $$"""{"workflowExecutionId":"{{workflowExecutionId}}","version":1,"pendingWork":[],"pendingContinuations":[],"volatileWaits":[],"pendingCompletionWork":[],"activeGenerators":[],"pendingGeneratedEvents":[]}""",
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
        Revision = 1
    };

    private static string Order(string value) => Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value, RuntimeOperationalStateEfModule.IdentityMaximumLength));

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class TestDatabase(SqliteConnection connection) : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = connection;

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var context = new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection);
        }

        public BookmarkStateSqliteDbContext Open(string scope, params IInterceptor[] interceptors)
        {
            var builder = new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(Connection);
            if (interceptors.Length > 0)
                builder.AddInterceptors(interceptors);
            return new BookmarkStateSqliteDbContext(builder.Options);
        }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }

    private sealed class ParticipantOrderAndFailureInterceptor : DbCommandInterceptor
    {
        private int armed;

        public List<string> Tables { get; } = [];

        public void Arm() => Interlocked.Exchange(ref armed, 1);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ObserveAndFail(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ObserveAndFail(command);
            return ValueTask.FromResult(result);
        }

        private void ObserveAndFail(DbCommand command)
        {
            var isInsert = command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase);
            if (isInsert && command.CommandText.Contains("elsa_runtime_checkpoint_commit", StringComparison.OrdinalIgnoreCase))
                Tables.Add("elsa_runtime_checkpoint_commit");
            if (isInsert && command.CommandText.Contains("elsa_runtime_scheduler_state", StringComparison.OrdinalIgnoreCase))
                Tables.Add("elsa_runtime_scheduler_state");
            if (command.CommandText.Contains("elsa_runtime_scheduler_state", StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref armed, 0) == 1)
                throw new InvalidOperationException("Simulated later sibling participant failure.");
        }
    }

    private sealed class CommitAcknowledgementLossInterceptor : DbTransactionInterceptor
    {
        private int armed;

        public void Arm() => Interlocked.Exchange(ref armed, 1);

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref armed, 0) == 1)
                throw new InvalidOperationException("Simulated acknowledgement loss after commit.");
            return Task.CompletedTask;
        }
    }
}
