using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfRuntimeCheckpointSchedulerWorkParticipantTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task Claimed_work_is_consumed_with_sibling_and_marker_in_the_callers_transaction()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Store.EnqueueAsync(Work("workflow-success", "work-1"));
        var claim = (await fixture.Store.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
            "workflow-success", "owner-a", Now, TimeSpan.FromMinutes(1))))!;
        await fixture.Store.RenewClaimAsync(claim, Now, TimeSpan.FromMinutes(2));

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            fixture.Context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-success"));
            await StageConsumeAsync(fixture.Context, ConsumedSchedulerWorkItem.FromClaim(claim), "tenant-a");
            fixture.Context.RuntimeCheckpointCommits.Add(Marker("tenant-a", "commit-success", "workflow-success", "work-1"));
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verification = database.Open("tenant-a");
        Assert.Empty(await verification.Context.SchedulerWorkItems.ToArrayAsync());
        Assert.Single(await verification.Context.SchedulerStates.ToArrayAsync());
        Assert.Single(await verification.Context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Transaction_rollback_preserves_the_claimed_work_and_discards_siblings_and_marker()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Store.EnqueueAsync(Work("workflow-rollback", "work-1"));
        var claim = (await fixture.Store.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
            "workflow-rollback", "owner-a", Now, TimeSpan.FromMinutes(1))))!;

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            fixture.Context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-rollback"));
            await StageConsumeAsync(fixture.Context, ConsumedSchedulerWorkItem.FromClaim(claim), "tenant-a");
            fixture.Context.RuntimeCheckpointCommits.Add(Marker("tenant-a", "commit-rollback", "workflow-rollback", "work-1"));
            await fixture.Context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var verification = database.Open("tenant-a");
        var row = await verification.Context.SchedulerWorkItems.SingleAsync();
        Assert.Equal(EfRelationalIdentity.Encode("owner-a"), row.ClaimOwnerId);
        Assert.Equal(claim.FencingToken, row.ClaimToken);
        Assert.Empty(await verification.Context.SchedulerStates.ToArrayAsync());
        Assert.Empty(await verification.Context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Lost_claim_fails_with_exact_conflict_and_does_not_delete_successors_work()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Store.EnqueueAsync(Work("workflow-fence", "work-1"));
        var original = (await fixture.Store.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
            "workflow-fence", "owner-a", Now, TimeSpan.FromMinutes(1))))!;

        await using (var successorContext = database.Open("tenant-a"))
        {
            var successor = await successorContext.Store.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
                "workflow-fence", "owner-b", Now.AddMinutes(2), TimeSpan.FromMinutes(1)));
            Assert.NotNull(successor);
            Assert.True(successor!.FencingToken > original.FencingToken);
        }

        await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
        var exception = await Assert.ThrowsAsync<RuntimeSchedulerWorkConsumeConflictException>(() =>
            StageConsumeAsync(fixture.Context, ConsumedSchedulerWorkItem.FromClaim(original), "tenant-a"));
        Assert.Equal("workflow-fence", exception.WorkflowExecutionId);
        Assert.Equal("work-1", exception.WorkItemId);
        await transaction.RollbackAsync();

        await using var verification = database.Open("tenant-a");
        var row = await verification.Context.SchedulerWorkItems.SingleAsync();
        Assert.Equal(EfRelationalIdentity.Encode("owner-b"), row.ClaimOwnerId);
        Assert.True(row.ClaimToken > original.FencingToken);
    }

    private static async Task StageConsumeAsync(
        BookmarkStateDbContext context,
        ConsumedSchedulerWorkItem consumed,
        string scope)
    {
        var type = typeof(EfRuntimeCheckpointCommitStore).Assembly.GetType(
            "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeCheckpointParticipantStaging")!;
        var method = type.GetMethod(
            "StageConsumedSchedulerWorkAsync",
            BindingFlags.Public | BindingFlags.Static)!;

        try
        {
            var result = (ValueTask)method.Invoke(null, [context, consumed, scope, CancellationToken.None])!;
            await result.AsTask();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static RuntimeSchedulerWorkItem Work(string workflowExecutionId, string workItemId) =>
        new(workItemId, workflowExecutionId, "command", WorkflowExecutionCommandKind.ScheduleActivity,
            "envelope", "idempotency", Now, Now, 1);

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

    private static RuntimeCheckpointCommitEntity Marker(
        string scope,
        string commitId,
        string workflowExecutionId,
        string workItemId) => new()
    {
        Id = EfRelationalIdentity.Hash($"{scope.Length}:{scope}{commitId.Length}:{commitId}"),
        ScopeKey = EfRelationalIdentity.Encode(scope),
        ScopeKeyHash = EfRelationalIdentity.Hash(scope),
        CommitId = EfRelationalIdentity.Encode(commitId),
        CommitIdHash = EfRelationalIdentity.Hash(commitId),
        WorkflowExecutionId = EfRelationalIdentity.Encode(workflowExecutionId),
        WorkflowExecutionIdHash = EfRelationalIdentity.Hash(workflowExecutionId),
        WorkflowExecutionIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(workflowExecutionId, RuntimeOperationalStateEfModule.IdentityMaximumLength)),
        OccurredAtUtcTicks = Now.UtcTicks,
        Fingerprint = new string('a', 64),
        ContentJson = "{}",
        PendingPostCommitWorkIdsJson = "[]",
        ConsumedSchedulerWorkItemIdsJson = JsonSerializer.Serialize(new[] { workItemId }),
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
        Revision = 1
    };

    private sealed class TestDatabase(SqliteConnection keeper, string connectionString) : IAsyncDisposable
    {
        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=file:ef-r22-checkpoint-queue-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
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
        public EfSchedulerWorkQueueStore Store { get; }

        public TestFixture(string connectionString, string scope)
        {
            connection = new SqliteConnection(connectionString);
            connection.Open();
            Context = new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            Store = new EfSchedulerWorkQueueStore(
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
