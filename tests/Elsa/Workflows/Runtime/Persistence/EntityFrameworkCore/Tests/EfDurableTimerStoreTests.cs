using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfDurableTimerStoreTests
{
    [Fact]
    public async Task Save_is_existing_wins_scoped_and_survives_restart()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var original = Timer("wf-1", "timer-1", DateTimeOffset.Parse("2030-01-02T03:04:05+02:00")) with
        {
            Input = JsonSerializer.SerializeToElement(new { value = "é" }),
            Metadata = new Dictionary<string, string> { ["key"] = "value" }
        };
        var replacement = original with { StimulusType = "Changed", DueTime = original.DueTime.AddHours(1) };

        AssertTimerEqual(original, await fixture.Store.SaveAsync(original));
        AssertTimerEqual(original, await fixture.Store.SaveAsync(replacement));
        AssertTimerEqual(original, await fixture.Store.FindAsync("wf-1", "timer-1"));

        await using var restarted = database.Open("tenant-a");
        AssertTimerEqual(original, await restarted.Store.FindAsync("wf-1", "timer-1"));

        await using var otherScope = database.Open("tenant-b");
        Assert.Null(await otherScope.Store.FindAsync("wf-1", "timer-1"));
    }

    [Fact]
    public async Task Due_query_is_bounded_inclusive_and_stably_ordered()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var asOf = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        foreach (var id in new[] { "c", "a", "b" })
            await fixture.Store.SaveAsync(Timer("wf-1", id, asOf));
        await fixture.Store.SaveAsync(Timer("wf-2", "a", asOf.AddTicks(1)));

        var due = await fixture.Store.ListDueAsync(asOf, 2);

        Assert.Equal(new[] { "a", "b" }, due.Select(x => x.TimerId));
    }

    [Fact]
    public async Task Workflow_pages_are_forward_bounded_and_restartable()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        foreach (var id in new[] { "c", "a", "b" })
            await fixture.Store.SaveAsync(Timer("wf-1", id, DateTimeOffset.UtcNow));

        var first = await fixture.Store.ListPageAsync(new DurableTimerPageQuery("wf-1", 2));
        Assert.Equal(new[] { "a", "b" }, first.Items.Select(x => x.TimerId));
        Assert.NotNull(first.NextContinuationToken);

        await using var restarted = database.Open("tenant-a");
        var second = await restarted.Store.ListPageAsync(new DurableTimerPageQuery("wf-1", 2, first.NextContinuationToken));
        Assert.Equal(new[] { "c" }, second.Items.Select(x => x.TimerId));
        Assert.Null(second.NextContinuationToken);
    }

    [Fact]
    public async Task Claims_are_fenced_and_recover_after_release()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var timer = Timer("wf-claim", "timer-1", now);
        await fixture.Store.SaveAsync(timer);
        var persisted = await fixture.Context.DurableTimers.AsNoTracking().SingleAsync();
        Assert.Equal(now.UtcTicks, persisted.DueTimeUtcTicks);
        Assert.Single(await fixture.Store.ListDueAsync(now, 10));
        Assert.Equal(1, await fixture.Context.DurableTimers.CountAsync(row =>
            row.DueTimeUtcTicks <= now.UtcTicks &&
            (row.VisibleAfterUtcTicks == null || row.VisibleAfterUtcTicks <= now.UtcTicks)));

        var claims = await fixture.Store.ClaimDueAsync(new RuntimeDurableTimerClaimRequest("worker-a", now, TimeSpan.FromMinutes(1), 10));
        var claim = Assert.Single(claims);
        Assert.Empty(await fixture.Store.ClaimDueAsync(new RuntimeDurableTimerClaimRequest("worker-b", now, TimeSpan.FromMinutes(1), 10)));

        var renewed = await fixture.Store.RenewClaimAsync(claim, now, TimeSpan.FromMinutes(2));
        Assert.Equal(RuntimeDurableTimerClaimTransitionStatus.Succeeded, renewed.Status);
        Assert.NotNull(renewed.Claim);
        var current = renewed.Claim!;
        Assert.Equal(RuntimeDurableTimerClaimTransitionStatus.Stale,
            (await fixture.Store.ReleaseClaimAsync(claim, now, CancellationToken.None)).Status);

        var visibleAt = current.VisibleAfter;
        Assert.Equal(RuntimeDurableTimerClaimTransitionStatus.Succeeded,
            (await fixture.Store.ReleaseClaimAsync(current, visibleAt)).Status);
        var recovered = Assert.Single(await fixture.Store.ClaimDueAsync(
            new RuntimeDurableTimerClaimRequest("worker-b", visibleAt, TimeSpan.FromMinutes(1), 10)));
        Assert.True(recovered.FencingToken > claim.FencingToken);
        Assert.Equal(RuntimeDurableTimerClaimTransitionStatus.Stale,
            (await fixture.Store.CompleteClaimAsync(current)).Status);
        Assert.Equal(RuntimeDurableTimerClaimTransitionStatus.Succeeded,
            (await fixture.Store.CompleteClaimAsync(recovered)).Status);
        Assert.Null(await fixture.Store.FindAsync(timer.WorkflowExecutionId, timer.TimerId));
    }

    [Fact]
    public async Task Save_participates_in_transaction_and_rollback_leaves_no_timer()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var fixture = database.Open("tenant-a"))
        {
            await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
            await fixture.Store.SaveAsync(Timer("wf-rollback", "timer-1", DateTimeOffset.UtcNow));
            await transaction.RollbackAsync();
        }

        await using var restarted = database.Open("tenant-a");
        Assert.Null(await restarted.Store.FindAsync("wf-rollback", "timer-1"));
    }

    [Fact]
    public async Task Cancellation_is_observed_before_database_work()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.SaveAsync(
            Timer("wf-cancel", "timer-1", DateTimeOffset.UtcNow), cancellation.Token).AsTask());
    }

    [Fact]
    public async Task Registration_replaces_only_the_runtime_default_and_is_load_order_safe()
    {
        foreach (var timerFirst in new[] { true, false })
        {
            var services = new ServiceCollection();
            services.AddSingleton<IDurableTimerStore, InMemoryDurableTimerStore>();
            services.AddSingleton<IPersistenceAccessContextAccessor>(new Accessor("tenant-a"));
            var timerOptions = new RuntimeDurableTimerEntityFrameworkCoreOptions
            {
                Provider = "Sqlite",
                ConnectionString = $"Data Source=file:ef-r24-registration-{Guid.NewGuid():N};Mode=Memory;Cache=Shared",
                RecoveryContinuationSigningKey = new string('k', 32)
            };
            var operationalOptions = new RuntimeOperationalStateEntityFrameworkCoreOptions
            {
                Provider = "Sqlite",
                ConnectionString = timerOptions.ConnectionString,
                RecoveryContinuationSigningKey = timerOptions.RecoveryContinuationSigningKey
            };
            if (timerFirst)
            {
                services.AddRuntimeDurableTimerEntityFrameworkCore(timerOptions);
                services.AddRuntimeOperationalStateEntityFrameworkCore(operationalOptions);
            }
            else
            {
                services.AddRuntimeOperationalStateEntityFrameworkCore(operationalOptions);
                services.AddRuntimeDurableTimerEntityFrameworkCore(timerOptions);
            }

            Assert.Equal(DurableTimerStoreBackend.EntityFramework, DurableTimerStoreBackend.Find(services)!.Name);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<BookmarkStateDbContext>();
            await context.Database.EnsureCreatedAsync();
            var store = scope.ServiceProvider.GetRequiredService<IDurableTimerStore>();
            Assert.IsType<EfDurableTimerStore>(store);
        }
    }

    private static DurableTimer Timer(string workflowExecutionId, string timerId, DateTimeOffset dueTime) =>
        new(timerId, workflowExecutionId, "Delay", $"stimulus-{timerId}", dueTime, dueTime.AddMinutes(-1));

    private static void AssertTimerEqual(DurableTimer expected, DurableTimer? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.TimerId, actual!.TimerId);
        Assert.Equal(expected.WorkflowExecutionId, actual.WorkflowExecutionId);
        Assert.Equal(expected.StimulusType, actual.StimulusType);
        Assert.Equal(expected.StimulusHash, actual.StimulusHash);
        Assert.Equal(expected.DueTime, actual.DueTime);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.Metadata, actual.Metadata);
        Assert.Equal(expected.Input?.GetRawText(), actual.Input?.GetRawText());
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection keeper;
        private readonly string connectionString;

        private TestDatabase(SqliteConnection keeper, string connectionString)
        {
            this.keeper = keeper;
            this.connectionString = connectionString;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=file:ef-r24-timers-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
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
        public EfDurableTimerStore Store { get; }

        public TestFixture(string connectionString, string scope)
        {
            connection = new SqliteConnection(connectionString);
            connection.Open();
            Context = new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            var accessor = new Accessor(scope);
            var codec = new HmacRuntimeRecoveryContinuationCodec(
                Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) }));
            Store = new EfDurableTimerStore(Context, accessor, codec);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class Accessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } =
            PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
