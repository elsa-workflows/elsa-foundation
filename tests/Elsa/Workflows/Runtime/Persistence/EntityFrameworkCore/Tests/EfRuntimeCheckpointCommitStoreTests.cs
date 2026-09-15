using System.Data.Common;
using System.Text.Json;
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
    public async Task Nonempty_execution_scheduler_and_fence_commit_as_one_replayable_unit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = OccurredAt;
        var scope = "tenant-a";
        await using (var context = database.Open(scope))
        {
            var accessor = new FixedAccessor(scope);
            var liveness = new EfExecutionLivenessStateStore(context, accessor, new NoopContinuationCodec());
            var lease = new RuntimeExecutionLease("lease-a", "workflow-a", "owner-a", now, now.AddMinutes(5), 1);
            await liveness.SaveAsync(new ExecutionLivenessState("ownership:workflow-a", "workflow-a", lease, null, null, null));

            var commit = Commit("commit-nonempty") with
            {
                ExpectedFence = lease.ToFence(),
                StateChanges = new RuntimeCheckpointStateChangeSet(
                    new RuntimeStateChange<WorkflowExecutionState>("workflow-a", RuntimeStateChangeOperation.Upsert, Execution("workflow-a", scope), new Dictionary<string, string>()),
                    new RuntimeStateChange<SchedulerState>("workflow-a", RuntimeStateChangeOperation.Upsert, new SchedulerState("workflow-a", 7), new Dictionary<string, string>()),
                    [], [], [], [], [])
            };

            var store = new EfRuntimeCheckpointCommitStore(context, accessor, new FixedTimeProvider(now));
            var first = await store.CommitAsync(commit, Decision());
            var replay = await store.CommitAsync(commit, Decision());

            Assert.Empty(first.PendingPostCommitWorkIds);
            Assert.Empty(replay.PendingPostCommitWorkIds);
            Assert.Equal(1, (await context.WorkflowExecutionStates.SingleAsync()).Revision);
            Assert.Equal(1, (await context.SchedulerStates.SingleAsync()).Revision);
            Assert.Equal(2, (await context.ExecutionLivenessStates.SingleAsync()).Revision);
            Assert.Single(await context.RuntimeCheckpointCommits.ToArrayAsync());
        }
    }

    [Fact]
    public async Task Staged_sibling_concurrency_failure_rolls_back_execution_and_marker_and_clears_retryable_state()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var scheduler = SchedulerRow("tenant-a", "workflow-a");
        context.SchedulerStates.Add(scheduler);
        await context.SaveChangesAsync();

        await using (var competing = database.Open("tenant-a"))
        {
            var competingRow = await competing.SchedulerStates.SingleAsync();
            competingRow.Revision++;
            await competing.SaveChangesAsync();
        }

        // This is the sibling mutation already staged by an R15 caller. The checkpoint must not clear it before
        // the atomic save, but a failed unit must clear the stale tracker after rollback so a later SaveChanges
        // cannot silently retry the sibling mutation.
        scheduler.ContentJson = scheduler.ContentJson.Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal);
        scheduler.Revision++;
        var commit = Commit("commit-sibling-cas") with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>("workflow-a", RuntimeStateChangeOperation.Upsert, Execution("workflow-a", "tenant-a"), new Dictionary<string, string>()),
                null,
                [], [], [], [], [])
        };

        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => store.CommitAsync(commit, Decision()).AsTask());
        Assert.Empty(context.ChangeTracker.Entries());

        await using var verification = database.Open("tenant-a");
        Assert.Null(await verification.WorkflowExecutionStates.SingleOrDefaultAsync());
        Assert.Empty(await verification.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Equal(2, (await verification.SchedulerStates.SingleAsync()).Revision);
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
    public async Task Checkpoint_slice_rejects_unsupported_state_without_writing_a_marker()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));

        var durableValue = new RuntimeStateChange<DurableValueState>(
            "value-a",
            RuntimeStateChangeOperation.Upsert,
            new DurableValueState("value-a", "workflow-a", "value-a", new RuntimeValueTypeDescriptor("json", null, null), DurableValueLifecycle.Result, DurableValueStorage.Inline, JsonDocument.Parse("42").RootElement, null, null, OccurredAt, new Dictionary<string, string>()),
            new Dictionary<string, string>());
        var nonempty = Commit("commit-nonempty") with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(
                null,
                null,
                [],
                [],
                [durableValue],
                [],
                [])
        };

        await Assert.ThrowsAsync<NotSupportedException>(() => store.CommitAsync(nonempty, Decision()).AsTask());
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

    private static WorkflowExecutionState Execution(string id, string tenantId) => new(
        id,
        new WorkflowExecutableIdentity($"artifact-{id}", $"definition-{id}", "version-1", "1.0.0", "hash-1"),
        WorkflowExecutionStatus.Running,
        null,
        OccurredAt,
        OccurredAt,
        OccurredAt,
        null,
        null,
        null,
        tenantId,
        new Dictionary<string, string>());

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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoopContinuationCodec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token);
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
            if ((isInsert || command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)) &&
                command.CommandText.Contains("elsa_runtime_scheduler_state", StringComparison.OrdinalIgnoreCase) &&
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
