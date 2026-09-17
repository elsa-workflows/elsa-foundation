using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfRuntimeCheckpointDurableValueParticipantTests
{
    private static readonly DateTimeOffset CapturedAt = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task Upsert_is_staged_with_sibling_and_marker_in_the_callers_transaction()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Store.SaveAsync(Value("value-a", "workflow-a", "before"));

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            fixture.Context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Upsert, Value("value-a", "workflow-a", "after")), "tenant-a");
            fixture.Context.RuntimeCheckpointCommits.Add(Marker("tenant-a", "commit-upsert", "workflow-a"));
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verification = database.Open("tenant-a");
        var value = await verification.Store.FindAsync("workflow-a", "value-a");
        Assert.Equal("after", value!.InlineValue!.Value.GetString());
        Assert.Equal(2, await verification.Context.DurableValueStates.Select(row => row.Revision).SingleAsync());
        Assert.Single(await verification.Context.SchedulerStates.ToArrayAsync());
        Assert.Single(await verification.Context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Rollback_discards_staged_upsert_and_sibling_marker_without_independent_save()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var original = Value("value-a", "workflow-a", "before");
        await fixture.Store.SaveAsync(original);

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            fixture.Context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Upsert, Value("value-a", "workflow-a", "after")), "tenant-a");
            fixture.Context.RuntimeCheckpointCommits.Add(Marker("tenant-a", "commit-rollback", "workflow-a"));
            await fixture.Context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var verification = database.Open("tenant-a");
        var value = await verification.Store.FindAsync("workflow-a", "value-a");
        Assert.Equal(original.DurableValueId, value!.DurableValueId);
        Assert.Equal(original.WorkflowExecutionId, value.WorkflowExecutionId);
        Assert.Equal(original.InlineValue!.Value.GetString(), value.InlineValue!.Value.GetString());
        Assert.Equal(1, await verification.Context.DurableValueStates.Select(row => row.Revision).SingleAsync());
        Assert.Empty(await verification.Context.SchedulerStates.ToArrayAsync());
        Assert.Empty(await verification.Context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Delete_is_staged_and_missing_delete_is_idempotent()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var value = Value("value-a", "workflow-a", "before");
        await fixture.Store.SaveAsync(value);

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Delete, value), "tenant-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Delete, value), "tenant-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verification = database.Open("tenant-a");
        Assert.Null(await verification.Store.FindAsync("workflow-a", "value-a"));
    }

    [Fact]
    public async Task Staging_requires_a_caller_owned_transaction()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var value = Value("value-a", "workflow-a", "before");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Upsert, value), "tenant-a"));
        Assert.Empty(await fixture.Context.DurableValueStates.ToArrayAsync());
    }

    [Fact]
    public async Task Staged_upsert_uses_revision_concurrency_and_preserves_the_winner()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var seed = database.Open("tenant-a");
        await seed.Store.SaveAsync(Value("value-a", "workflow-a", "before"));

        await using var stale = database.Open("tenant-a");
        _ = await stale.Context.DurableValueStates.SingleAsync();
        await using (var winner = database.Open("tenant-a"))
            await winner.Store.SaveAsync(Value("value-a", "workflow-a", "winner"));

        await using var transaction = await stale.Context.Database.BeginTransactionAsync();
        await StageAsync(stale.Context, Change(RuntimeStateChangeOperation.Upsert, Value("value-a", "workflow-a", "stale")), "tenant-a");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.Context.SaveChangesAsync());
        await transaction.RollbackAsync();

        await using var verification = database.Open("tenant-a");
        Assert.Equal("winner", (await verification.Store.FindAsync("workflow-a", "value-a"))!.InlineValue!.Value.GetString());
        Assert.Equal(2, await verification.Context.DurableValueStates.Select(row => row.Revision).SingleAsync());
    }

    [Fact]
    public async Task Staging_is_scope_isolated_and_retry_after_rollback_replays_one_value()
    {
        await using var database = await TestDatabase.CreateAsync();
        var value = Value("value-a", "workflow-a", "before");
        await using (var tenantA = database.Open("tenant-a"))
            await tenantA.Store.SaveAsync(value);
        await using (var tenantB = database.Open("tenant-b"))
            await tenantB.Store.SaveAsync(value);

        await using (var tenantA = database.Open("tenant-a"))
        {
            await using var transaction = await tenantA.Context.Database.BeginTransactionAsync();
            await StageAsync(tenantA.Context, Change(RuntimeStateChangeOperation.Upsert, Value("value-a", "workflow-a", "retry")), "tenant-a");
            await tenantA.Context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using (var retry = database.Open("tenant-a"))
        {
            await using var transaction = await retry.Context.Database.BeginTransactionAsync();
            await StageAsync(retry.Context, Change(RuntimeStateChangeOperation.Upsert, Value("value-a", "workflow-a", "retry")), "tenant-a");
            await retry.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verificationA = database.Open("tenant-a");
        await using var verificationB = database.Open("tenant-b");
        Assert.Equal("retry", (await verificationA.Store.FindAsync("workflow-a", "value-a"))!.InlineValue!.Value.GetString());
        Assert.Equal("before", (await verificationB.Store.FindAsync("workflow-a", "value-a"))!.InlineValue!.Value.GetString());
    }

    private static RuntimeStateChange<DurableValueState> Change(RuntimeStateChangeOperation operation, DurableValueState state) =>
        new(state.DurableValueId, operation, state, new Dictionary<string, string>());

    private static DurableValueState Value(string durableValueId, string workflowExecutionId, string value) =>
        new(
            durableValueId,
            workflowExecutionId,
            durableValueId,
            new RuntimeValueTypeDescriptor("json", null, null),
            DurableValueLifecycle.Result,
            DurableValueStorage.Inline,
            JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement,
            null,
            null,
            CapturedAt,
            new Dictionary<string, string>());

    private static async Task StageAsync(
        BookmarkStateDbContext context,
        RuntimeStateChange<DurableValueState> change,
        string scope)
    {
        var type = typeof(EfRuntimeCheckpointCommitStore).Assembly.GetType(
            "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeCheckpointParticipantStaging")!;
        var method = type.GetMethod("StageDurableValueAsync", BindingFlags.Public | BindingFlags.Static)!;
        try
        {
            var result = (ValueTask)method.Invoke(null, [context, change, scope, CancellationToken.None])!;
            await result.AsTask();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

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

    private static RuntimeCheckpointCommitEntity Marker(string scope, string commitId, string workflowExecutionId) => new()
    {
        Id = EfRelationalIdentity.Hash($"{scope.Length}:{scope}{commitId.Length}:{commitId}"),
        ScopeKey = EfRelationalIdentity.Encode(scope),
        ScopeKeyHash = EfRelationalIdentity.Hash(scope),
        CommitId = EfRelationalIdentity.Encode(commitId),
        CommitIdHash = EfRelationalIdentity.Hash(commitId),
        WorkflowExecutionId = EfRelationalIdentity.Encode(workflowExecutionId),
        WorkflowExecutionIdHash = EfRelationalIdentity.Hash(workflowExecutionId),
        WorkflowExecutionIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(workflowExecutionId, RuntimeOperationalStateEfModule.IdentityMaximumLength)),
        OccurredAtUtcTicks = CapturedAt.UtcTicks,
        Fingerprint = new string('a', 64),
        ContentJson = "{}",
        PendingPostCommitWorkIdsJson = "[]",
        ConsumedSchedulerWorkItemIdsJson = "[]",
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
        Revision = 1
    };

    private sealed class TestDatabase(SqliteConnection keeper, string connectionString) : IAsyncDisposable
    {
        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=file:ef-r19-durable-value-participant-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
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
        public EfDurableValueStateStore Store { get; }

        public TestFixture(string connectionString, string scope)
        {
            connection = new SqliteConnection(connectionString);
            connection.Open();
            Context = new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            Store = new EfDurableValueStateStore(
                Context,
                new FixedAccessor(scope),
                new HmacRuntimeRecoveryContinuationCodec(
                    Microsoft.Extensions.Options.Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) })));
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
