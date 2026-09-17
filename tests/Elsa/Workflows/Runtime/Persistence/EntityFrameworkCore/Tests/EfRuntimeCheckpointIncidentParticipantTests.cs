using System.Reflection;
using System.Runtime.ExceptionServices;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfRuntimeCheckpointIncidentParticipantTests
{
    private static readonly DateTimeOffset CapturedAt = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task Upsert_is_staged_with_a_sibling_in_the_callers_transaction()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Store.TryAddAsync(Incident("incident-a", "workflow-a", "before"));

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            fixture.Context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Upsert, Incident("incident-a", "workflow-a", "after")), "tenant-a", "workflow-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verification = database.Open("tenant-a");
        Assert.Equal("after", (await verification.Store.FindAsync("workflow-a", "incident-a"))!.Message);
        Assert.Equal(2, await verification.Context.IncidentStates.Select(row => row.Revision).SingleAsync());
        Assert.Single(await verification.Context.SchedulerStates.ToArrayAsync());
    }

    [Fact]
    public async Task Append_is_create_only_and_conflicting_replay_fails_closed()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var incident = Incident("incident-a", "workflow-a", "first");

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Append, incident), "tenant-a", "workflow-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var replayTransaction = await fixture.Context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<RuntimeCheckpointCommitValidationException>(() =>
            StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Append, incident), "tenant-a", "workflow-a"));
        await replayTransaction.RollbackAsync();
        Assert.Equal("first", (await fixture.Store.FindAsync("workflow-a", "incident-a"))!.Message);
    }

    [Fact]
    public async Task Delete_is_staged_and_missing_delete_is_idempotent()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var incident = Incident("incident-a", "workflow-a", "before");
        await fixture.Store.TryAddAsync(incident);

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Delete, incident), "tenant-a", "workflow-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Delete, incident), "tenant-a", "workflow-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        Assert.Null(await fixture.Store.FindAsync("workflow-a", "incident-a"));
    }

    [Fact]
    public async Task Rollback_discards_the_incident_and_sibling_and_retry_replays_it()
    {
        await using var database = await TestDatabase.CreateAsync();
        var incident = Incident("incident-a", "workflow-a", "retry");
        await using (var fixture = database.Open("tenant-a"))
        {
            await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
            fixture.Context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Append, incident), "tenant-a", "workflow-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using (var verification = database.Open("tenant-a"))
        {
            Assert.Null(await verification.Store.FindAsync("workflow-a", "incident-a"));
            Assert.Empty(await verification.Context.SchedulerStates.ToArrayAsync());
        }

        await using (var fixture = database.Open("tenant-a"))
        {
            await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
            await StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Append, incident), "tenant-a", "workflow-a");
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var replay = database.Open("tenant-a");
        Assert.Equal("retry", (await replay.Store.FindAsync("workflow-a", "incident-a"))!.Message);
    }

    [Fact]
    public async Task Staging_uses_revision_concurrency_and_rejects_projection_drift()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = Incident("incident-a", "workflow-a", "before");
        await using (var seed = database.Open("tenant-a"))
            await seed.Store.TryAddAsync(original);

        await using var stale = database.Open("tenant-a");
        _ = await stale.Context.IncidentStates.SingleAsync();
        await using (var winner = database.Open("tenant-a"))
            await winner.Store.SaveAsync(Incident("incident-a", "workflow-a", "winner"));

        await using (var transaction = await stale.Context.Database.BeginTransactionAsync())
        {
            await StageAsync(stale.Context, Change(RuntimeStateChangeOperation.Upsert, Incident("incident-a", "workflow-a", "stale")), "tenant-a", "workflow-a");
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.Context.SaveChangesAsync());
            await transaction.RollbackAsync();
        }

        await using var tampered = database.Open("tenant-a");
        var row = await tampered.Context.IncidentStates.SingleAsync();
        row.Status = (int)IncidentStatus.Resolved;
        await tampered.Context.SaveChangesAsync();
        await using var transaction2 = await tampered.Context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            StageAsync(tampered.Context, Change(RuntimeStateChangeOperation.Upsert, Incident("incident-a", "workflow-a", "ignored")), "tenant-a", "workflow-a"));
        await transaction2.RollbackAsync();
    }

    [Fact]
    public async Task Staging_requires_a_caller_owned_transaction()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var incident = Incident("incident-a", "workflow-a", "before");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StageAsync(fixture.Context, Change(RuntimeStateChangeOperation.Append, incident), "tenant-a", "workflow-a"));
        Assert.Empty(await fixture.Context.IncidentStates.ToArrayAsync());
    }

    [Fact]
    public async Task Staging_keeps_identical_incident_ids_isolated_by_scope()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = Incident("incident-a", "workflow-a", "before");
        await using (var tenantA = database.Open("tenant-a"))
            await tenantA.Store.TryAddAsync(original);
        await using (var tenantB = database.Open("tenant-b"))
            await tenantB.Store.TryAddAsync(original);

        await using (var tenantA = database.Open("tenant-a"))
        {
            await using var transaction = await tenantA.Context.Database.BeginTransactionAsync();
            await StageAsync(tenantA.Context, Change(RuntimeStateChangeOperation.Upsert, Incident("incident-a", "workflow-a", "tenant-a")), "tenant-a", "workflow-a");
            await tenantA.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verificationA = database.Open("tenant-a");
        await using var verificationB = database.Open("tenant-b");
        Assert.Equal("tenant-a", (await verificationA.Store.FindAsync("workflow-a", "incident-a"))!.Message);
        Assert.Equal("before", (await verificationB.Store.FindAsync("workflow-a", "incident-a"))!.Message);
    }

    private static RuntimeStateChange<IncidentState> Change(RuntimeStateChangeOperation operation, IncidentState state) =>
        new(state.IncidentId, operation, state, new Dictionary<string, string>());

    private static IncidentState Incident(string id, string workflowExecutionId, string message) => new(
        id,
        workflowExecutionId,
        null,
        null,
        IncidentSeverity.Error,
        IncidentStatus.Open,
        null,
        "TestFailure",
        message,
        CapturedAt,
        null,
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
        RuntimeStateChange<IncidentState> change,
        string scope,
        string workflowExecutionId)
    {
        var type = typeof(EfRuntimeCheckpointCommitStore).Assembly.GetType(
            "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeCheckpointIncidentParticipantStaging")!;
        var method = type.GetMethod("StageIncidentAsync", BindingFlags.Public | BindingFlags.Static)!;
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
            var connectionString = $"Data Source=file:ef-r19-incident-participant-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
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
        public EfIncidentStateStore Store { get; }

        public TestFixture(string connectionString, string scope)
        {
            connection = new SqliteConnection(connectionString);
            connection.Open();
            Context = new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            Store = new(Context, new FixedAccessor(scope));
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
