using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfSchedulerWorkQueueStoreTests
{
    [Fact]
    public async Task Enqueue_is_existing_wins_scoped_and_survives_restart()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var original = Work("wf-1", "work-1", 1);
        var replacement = Work("wf-1", "work-1", 2);

        Assert.Equal(original.WorkItemId, (await fixture.Store.EnqueueAsync(original)).WorkItemId);
        Assert.Equal(original.Sequence, (await fixture.Store.EnqueueAsync(replacement)).Sequence);
        Assert.Single((await fixture.Store.ListAsync(new RuntimeSchedulerWorkQuery("wf-1"))).Items);

        await using var restarted = database.Open("tenant-a");
        Assert.Equal(original.Sequence, (await restarted.Store.ListAsync(new RuntimeSchedulerWorkQuery("wf-1"))).Items.Single().Sequence);
        await using var otherScope = database.Open("tenant-b");
        Assert.Empty((await otherScope.Store.ListAsync(new RuntimeSchedulerWorkQuery("wf-1"))).Items);
    }

    [Fact]
    public async Task List_is_bounded_stable_and_restartable()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        foreach (var sequence in new[] { 3L, 1L, 2L })
            await fixture.Store.EnqueueAsync(Work("wf-page", $"work-{sequence}", sequence));

        var first = await fixture.Store.ListAsync(new RuntimeSchedulerWorkQuery("wf-page", 2));
        Assert.Equal(new long?[] { 1L, 2L }, first.Items.Select(item => item.Sequence));
        Assert.NotNull(first.NextContinuationToken);

        await using var restarted = database.Open("tenant-a");
        var second = await restarted.Store.ListAsync(new RuntimeSchedulerWorkQuery("wf-page", 2, first.NextContinuationToken));
        Assert.Equal(new long?[] { 3L }, second.Items.Select(item => item.Sequence));
        Assert.Null(second.NextContinuationToken);
    }

    [Fact]
    public async Task Pending_workflow_discovery_is_scoped_and_bounded()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Store.EnqueueAsync(Work("wf-b", "work-b", 1));
        await fixture.Store.EnqueueAsync(Work("wf-a", "work-a", 1));
        await using var otherScope = database.Open("tenant-b");
        await otherScope.Store.EnqueueAsync(Work("wf-z", "work-z", 1));

        Assert.Equal(new[] { "wf-a" }, (await fixture.Store.ListPendingWorkflowExecutionIdsAsync(1)).ToArray());
        Assert.Equal(new[] { "wf-a", "wf-b" }, (await fixture.Store.ListPendingWorkflowExecutionIdsAsync(10)).ToArray());
    }

    [Fact]
    public async Task Claims_are_fifo_fenced_renewable_and_recover_after_release()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        await fixture.Store.EnqueueAsync(Work("wf-claims", "work-1", 1));
        await fixture.Store.EnqueueAsync(Work("wf-claims", "work-2", 2));

        var first = await fixture.Store.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf-claims", "owner-a", now, TimeSpan.FromMinutes(1)));
        Assert.NotNull(first);
        Assert.Null(await fixture.Store.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf-claims", "owner-b", now, TimeSpan.FromMinutes(1))));
        var renewed = await fixture.Store.RenewClaimAsync(first!, now, TimeSpan.FromMinutes(2));
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Succeeded, renewed.Status);
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Stale, (await fixture.Store.ReleaseClaimAsync(first!, now)).Status);
        var current = renewed.Claim;
        Assert.NotNull(current);
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Succeeded, (await fixture.Store.ReleaseClaimAsync(current!, current!.VisibleAfter)).Status);

        var recovered = await fixture.Store.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf-claims", "owner-b", current!.VisibleAfter, TimeSpan.FromMinutes(1)));
        Assert.NotNull(recovered);
        Assert.True(recovered!.FencingToken > first!.FencingToken);
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Stale, (await fixture.Store.CompleteClaimAsync(current)).Status);
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Succeeded, (await fixture.Store.CompleteClaimAsync(recovered)).Status);
        Assert.Equal("work-2", (await fixture.Store.DequeueAsync("wf-claims"))!.WorkItemId);
    }

    [Fact]
    public async Task Consume_fences_on_owner_and_token_but_allows_renewal_replay()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var item = Work("wf-consume", "work-1", 1);
        await fixture.Store.EnqueueAsync(item);
        var claim = await fixture.Store.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf-consume", "owner-a", now, TimeSpan.FromMinutes(1)));
        Assert.NotNull(claim);
        var renewed = (await fixture.Store.RenewClaimAsync(claim!, now, TimeSpan.FromMinutes(2))).Claim;
        Assert.NotNull(renewed);

        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Succeeded,
            (await fixture.Store.ConsumeClaimedAsync(new ConsumedSchedulerWorkItem("wf-consume", "work-1", "owner-a", renewed!.FencingToken))).Status);
        Assert.Equal(RuntimeSchedulerWorkClaimTransitionStatus.Stale,
            (await fixture.Store.ConsumeClaimedAsync(new ConsumedSchedulerWorkItem("wf-consume", "work-1", "owner-a", renewed.FencingToken))).Status);
    }

    [Fact]
    public async Task Active_claim_inspection_and_transaction_rollback_are_durable()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        await fixture.Store.EnqueueAsync(Work("wf-active", "work-1", 1));
        var claim = await fixture.Store.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("wf-active", "owner-a", now, TimeSpan.FromMinutes(1)));
        Assert.NotNull(claim);
        Assert.Equal("owner-a", Assert.Single(await fixture.Store.ListActiveClaimsAsync("wf-active", now)).OwnerId);
        Assert.Empty(await fixture.Store.ListActiveClaimsAsync("wf-active", now.AddMinutes(2)));

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await fixture.Store.EnqueueAsync(Work("wf-rollback", "work-1", 1));
            await transaction.RollbackAsync();
        }
        await using var restarted = database.Open("tenant-a");
        Assert.Empty((await restarted.Store.ListAsync(new RuntimeSchedulerWorkQuery("wf-rollback"))).Items);
        Assert.NotNull(claim);
    }

    [Fact]
    public async Task Projection_drift_fails_closed_on_visible_row()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var item = Work("wf-drift", "work-1", 1);
        await fixture.Store.EnqueueAsync(item);
        var row = await fixture.Context.SchedulerWorkItems.SingleAsync();
        row.WorkOrderKey = "drift";
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.ListAsync(new RuntimeSchedulerWorkQuery("wf-drift")).AsTask());
    }

    [Fact]
    public async Task Registration_is_load_order_safe_and_opt_in()
    {
        foreach (var queueFirst in new[] { true, false })
        {
            var services = new ServiceCollection();
            services.AddSingleton<IWorkflowSchedulerWorkQueue, Elsa.Workflows.Runtime.Core.Services.InMemoryWorkflowSchedulerWorkQueue>();
            services.AddSingleton<IPersistenceAccessContextAccessor>(new FixedAccessor("tenant-a"));
            var connection = $"Data Source=file:ef-r22-registration-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var options = new RuntimeSchedulerWorkQueueEntityFrameworkCoreOptions
            {
                Provider = "Sqlite",
                ConnectionString = connection,
                RecoveryContinuationSigningKey = new string('k', 32)
            };
            if (queueFirst)
                services.AddRuntimeSchedulerWorkQueueEntityFrameworkCore(options);
            else
            {
                services.AddRuntimeSchedulerWorkQueueEntityFrameworkCore(options);
                services.AddRuntimeSchedulerWorkQueueEntityFrameworkCore(options);
            }

            Assert.Equal(SchedulerWorkQueueStoreBackend.EntityFramework, SchedulerWorkQueueStoreBackend.Find(services)!.Name);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<BookmarkStateDbContext>();
            await context.Database.EnsureCreatedAsync();
            Assert.IsType<EfSchedulerWorkQueueStore>(scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>());
            Assert.IsType<EfSchedulerWorkQueueStore>(scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkClaimInspection>());
        }
    }

    private static RuntimeSchedulerWorkItem Work(string workflowExecutionId, string workItemId, long sequence) =>
        new(workItemId, workflowExecutionId, $"command-{workItemId}", WorkflowExecutionCommandKind.ScheduleActivity,
            $"envelope-{workItemId}", $"idempotency-{workItemId}",
            new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.FromHours(1)),
            new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.FromHours(1)),
            sequence,
            JsonSerializer.SerializeToElement(new { sequence }),
            new Dictionary<string, string> { ["command"] = "metadata" },
            new Dictionary<string, string> { ["envelope"] = "metadata" });

    private sealed class TestDatabase(SqliteConnection keeper, string connectionString) : IAsyncDisposable
    {
        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=file:ef-r22-queue-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(keeper).Options);
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
            Context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            Store = new EfSchedulerWorkQueueStore(Context, new FixedAccessor(scope), new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) })));
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
