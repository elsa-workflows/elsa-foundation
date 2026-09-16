using System.Reflection;
using System.Runtime.ExceptionServices;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfRuntimeCheckpointActivityExecutionParticipantTests
{
    private static readonly DateTimeOffset CapturedAt = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task Upsert_is_staged_with_a_sibling_in_the_callers_transaction_and_preserves_content()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Store.SaveAsync(State("workflow-a", "activity-a", 1));
        var firstRevision = await fixture.Context.ActivityExecutionStates.Select(row => row.Revision).SingleAsync();

        var replacement = State("workflow-a", "activity-a", 2);
        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            fixture.Context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Upsert, replacement), "tenant-a", "workflow-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verification = database.Open("tenant-a");
        var saved = await verification.Store.FindAsync("workflow-a", "activity-a");
        Assert.NotNull(saved);
        Assert.Equal(2, saved!.ExecutionSequence);
        Assert.Equal(ActivityExecutionStatus.Completed, saved.Status);
        Assert.Equal(firstRevision + 1, await verification.Context.ActivityExecutionStates.Select(row => row.Revision).SingleAsync());
        Assert.Single(await verification.Context.SchedulerStates.ToArrayAsync());
    }

    [Fact]
    public async Task Append_is_create_only_and_conflicting_replay_fails_closed()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var state = State("workflow-a", "activity-a", 1);

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Append, state), "tenant-a", "workflow-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var replayTransaction = await fixture.Context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Append, state), "tenant-a", "workflow-a"));
        await replayTransaction.RollbackAsync();

        Assert.Equal(1, (await fixture.Store.FindAsync("workflow-a", "activity-a"))!.ExecutionSequence);
    }

    [Fact]
    public async Task Delete_is_staged_and_missing_delete_is_idempotent()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var state = State("workflow-a", "activity-a", 1);
        await fixture.Store.SaveAsync(state);

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Delete, state), "tenant-a", "workflow-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Delete, state), "tenant-a", "workflow-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        Assert.Null(await fixture.Store.FindAsync("workflow-a", "activity-a"));
    }

    [Fact]
    public async Task Rollback_discards_activity_execution_and_sibling_and_retry_replays_it()
    {
        await using var database = await TestDatabase.CreateAsync();
        var state = State("workflow-a", "activity-a", 1);
        await using (var fixture = database.Open("tenant-a"))
        {
            await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
            fixture.Context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Append, state), "tenant-a", "workflow-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using (var verification = database.Open("tenant-a"))
        {
            Assert.Null(await verification.Store.FindAsync("workflow-a", "activity-a"));
            Assert.Empty(await verification.Context.SchedulerStates.ToArrayAsync());
        }

        await using (var fixture = database.Open("tenant-a"))
        {
            await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Append, state), "tenant-a", "workflow-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var replay = database.Open("tenant-a");
        Assert.NotNull(await replay.Store.FindAsync("workflow-a", "activity-a"));
    }

    [Fact]
    public async Task Staging_uses_revision_concurrency_and_rejects_projection_drift()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = State("workflow-a", "activity-a", 1);
        await using (var seed = database.Open("tenant-a"))
            await seed.Store.SaveAsync(original);

        await using var stale = database.Open("tenant-a");
        _ = await stale.Context.ActivityExecutionStates.SingleAsync();
        await using (var winner = database.Open("tenant-a"))
            await winner.Store.SaveAsync(State("workflow-a", "activity-a", 2));

        await using (var transaction = await stale.Context.Database.BeginTransactionAsync())
        {
            await StageAsync(stale.Context, Change(RuntimeStateChangeOperation.Upsert, State("workflow-a", "activity-a", 3)), "tenant-a", "workflow-a");
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.Context.SaveChangesAsync());
            await transaction.RollbackAsync();
        }

        await using var tampered = database.Open("tenant-a");
        var row = await tampered.Context.ActivityExecutionStates.SingleAsync();
        row.Status = nameof(ActivityExecutionStatus.Running);
        await tampered.Context.SaveChangesAsync();
        await using var transaction2 = await tampered.Context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            StageAsync(tampered.Context, Change(RuntimeStateChangeOperation.Upsert, State("workflow-a", "activity-a", 4)), "tenant-a", "workflow-a"));
        await transaction2.RollbackAsync();
    }

    [Fact]
    public async Task Staging_requires_a_caller_owned_transaction()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var state = State("workflow-a", "activity-a", 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Append, state), "tenant-a", "workflow-a"));
        Assert.Empty(await fixture.Context.ActivityExecutionStates.ToArrayAsync());
    }

    [Fact]
    public async Task Staging_keeps_identical_activity_ids_isolated_by_scope()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = State("workflow-a", "activity-a", 1);
        await using (var tenantA = database.Open("tenant-a"))
            await tenantA.Store.SaveAsync(original);
        await using (var tenantB = database.Open("tenant-b"))
            await tenantB.Store.SaveAsync(original);

        await using (var tenantA = database.Open("tenant-a"))
        {
            await using var transaction = await tenantA.Context.Database.BeginTransactionAsync();
            await StageAsync(tenantA.Context, Change(RuntimeStateChangeOperation.Upsert, State("workflow-a", "activity-a", 2)), "tenant-a", "workflow-a");
            await tenantA.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verificationA = database.Open("tenant-a");
        await using var verificationB = database.Open("tenant-b");
        Assert.Equal(2, (await verificationA.Store.FindAsync("workflow-a", "activity-a"))!.ExecutionSequence);
        Assert.Equal(1, (await verificationB.Store.FindAsync("workflow-a", "activity-a"))!.ExecutionSequence);
    }

    private static RuntimeStateChange<ActivityExecutionState> Change(RuntimeStateChangeOperation operation, ActivityExecutionState state) =>
        new(state.Execution.ActivityExecutionId, operation, state, new Dictionary<string, string>());

    private static ActivityExecutionState State(string workflowExecutionId, string activityExecutionId, long sequence) => new(
        new ActivityExecution(
            activityExecutionId,
            workflowExecutionId,
            $"node-{activityExecutionId}",
            $"authored-{activityExecutionId}",
            "Test.Activity",
            "1"),
        ActivityExecutionStatus.Completed,
        null,
        sequence,
        CapturedAt.AddSeconds(sequence),
        CapturedAt.AddSeconds(sequence),
        CapturedAt.AddSeconds(sequence),
        null,
        null,
        null,
        null,
        ActivitySchedulingProvenance.From(workflowExecutionId, null, null, null, null, null, null, "test"),
        null,
        [],
        [],
        0,
        0,
        new Dictionary<string, string>());

    private static SchedulerStateEntity SchedulerRow(string scope, string workflowExecutionId) => new()
    {
        Id = EfRelationalIdentity.Hash($"{scope.Length}:{scope}{workflowExecutionId.Length}:{workflowExecutionId}"),
        ScopeKey = EfRelationalIdentity.Encode(scope),
        ScopeKeyHash = EfRelationalIdentity.Hash(scope),
        WorkflowExecutionId = EfRelationalIdentity.Encode(workflowExecutionId),
        WorkflowExecutionIdHash = EfRelationalIdentity.Hash(workflowExecutionId),
        WorkflowExecutionIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(workflowExecutionId, RuntimeOperationalStateEfModule.IdentityMaximumLength)),
        Collection = "schedulerState",
        ContentJson = "{}",
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
        Revision = 1
    };

    private static async Task StageAsync(
        BookmarkStateDbContext context,
        RuntimeStateChange<ActivityExecutionState> change,
        string scope,
        string workflowExecutionId)
    {
        var type = typeof(EfRuntimeCheckpointCommitStore).Assembly.GetType(
            "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeCheckpointActivityExecutionParticipantStaging")!;
        var method = type.GetMethod("StageActivityExecutionAsync", BindingFlags.Public | BindingFlags.Static)!;
        try
        {
            var result = (ValueTask)method.Invoke(null, [context, change, scope, workflowExecutionId, CancellationToken.None])!;
            await result.AsTask();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private sealed class TestDatabase(SqliteConnection keeper, string connectionString) : IAsyncDisposable
    {
        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=file:ef-r19-activity-participant-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            await using var context = new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(keeper).Options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(keeper, connectionString);
        }

        public TestFixture Open(string scope) => new(connectionString, scope);
        public ValueTask DisposeAsync() => keeper.DisposeAsync();
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public BookmarkStateSqliteDbContext Context { get; }
        public EfActivityExecutionStateStore Store { get; }

        public TestFixture(string connectionString, string scope)
        {
            connection = new SqliteConnection(connectionString);
            connection.Open();
            Context = new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            Store = new(Context, new FixedAccessor(scope), new HmacRuntimeRecoveryContinuationCodec(
                Microsoft.Extensions.Options.Options.Create(new RuntimeRecoveryContinuationOptions
                {
                    SigningKey = "ef-r19-activity-participant-signing-key-32-bytes"
                })));
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
