using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
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

public sealed class EfRuntimePostCommitOutboxStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Pending_replay_is_exactly_idempotent_and_conflicting_intent_is_refused()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var store = new EfRuntimePostCommitOutboxStore(context, new FixedAccessor("tenant-a"));
        var pending = Pending("replay", "workflow-a", kind: "publish");

        await store.SavePendingAsync(pending);
        await store.SavePendingAsync(pending);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SavePendingAsync(Pending("replay", "workflow-a", kind: "other")).AsTask());

        var persisted = await store.FindAsync("replay");
        Assert.NotNull(persisted);
        Assert.Equal("publish", persisted!.Intent.Kind);
        Assert.Single(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10)));
    }

    [Fact]
    public async Task Deterministic_due_queries_are_bounded_and_filter_workflow_and_intent()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var store = new EfRuntimePostCommitOutboxStore(context, new FixedAccessor("tenant-a"));
        await store.SavePendingAsync(PendingAt("z", Now, "workflow-a", "publish"));
        await store.SavePendingAsync(PendingAt("a", Now, "workflow-a", "publish"));
        await store.SavePendingAsync(PendingAt("b", Now, "workflow-a", "signal"));
        await store.SavePendingAsync(PendingAt("foreign", Now, "workflow-b", "publish"));

        var ordered = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 2));
        Assert.Equal(["a", "b"], ordered.Select(item => item.OutboxItemId));
        var filtered = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            Now,
            int.MaxValue,
            workflowExecutionId: "workflow-a",
            intentKind: "publish"));
        Assert.Equal(["a", "z"], filtered.Select(item => item.OutboxItemId));
        Assert.Throws<NotSupportedException>(() =>
            store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10, ownerId: "owner-a")).AsTask().GetAwaiter().GetResult());
    }

    [Fact]
    public async Task Equal_time_long_identity_order_is_bounded_deterministic_and_restart_stable()
    {
        await using var database = await TestDatabase.CreateAsync();
        var ids = Enumerable.Range(0, 20)
            .Select(index => new string('x', 451) + $"-{index:D2}")
            .Reverse()
            .ToArray();
        var expectedOrder = ids.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        string[] firstOrder;
        await using (var context = database.Open("tenant-a"))
        {
            var store = new EfRuntimePostCommitOutboxStore(context, new FixedAccessor("tenant-a"));
            foreach (var id in ids)
                await store.SavePendingAsync(PendingAt(id, Now, "workflow-a", "publish"));
            firstOrder = (await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 20)))
                .Select(item => item.OutboxItemId).ToArray();
            Assert.Equal(expectedOrder.Take(3), (await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 3)))
                .Select(item => item.OutboxItemId));
        }

        await using var restarted = database.Open("tenant-a");
        var secondOrder = (await new EfRuntimePostCommitOutboxStore(restarted, new FixedAccessor("tenant-a"))
                .GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 20)))
            .Select(item => item.OutboxItemId).ToArray();
        Assert.Equal(expectedOrder, firstOrder);
        Assert.Equal(firstOrder, secondOrder);
    }

    [Fact]
    public async Task Null_availability_is_immediately_eligible_and_exhausted_retry_is_not()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var store = new EfRuntimePostCommitOutboxStore(context, new FixedAccessor("tenant-a"));
        await store.SavePendingAsync(PendingWithoutAvailability("null-available", "workflow-a"));
        await store.SavePendingAsync(Pending(
            "exhausted",
            "workflow-a",
            retryPolicy: new RuntimePostCommitRetryPolicy(1, TimeSpan.FromSeconds(1))));

        await store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
            "exhausted",
            RuntimePostCommitOutboxStatus.FailedRetryable,
            Now,
            "final failure"));

        var deliverable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10));
        Assert.Equal("null-available", Assert.Single(deliverable).OutboxItemId);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, (await store.FindAsync("exhausted"))!.Status);
        var claim = Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "owner-a", Now, TimeSpan.FromMinutes(1), 10)));
        Assert.Equal("null-available", claim.OutboxItemId);
    }

    [Fact]
    public async Task Visible_projection_drift_fails_closed_when_candidate_is_selected()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var store = new EfRuntimePostCommitOutboxStore(context, new FixedAccessor("tenant-a"));
        await store.SavePendingAsync(Pending("drift", "workflow-a", kind: "publish"));

        var row = await context.RuntimePostCommitOutbox.SingleAsync();
        row.IntentKind = "tampered";
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetDeliverableAsync(
            new RuntimePostCommitOutboxQuery(Now, 10)).AsTask());
    }

    [Fact]
    public async Task Long_identity_claim_reclaim_and_completion_preserve_fencing_and_restart_durability()
    {
        await using var database = await TestDatabase.CreateAsync();
        var id = new string('x', 451);
        var item = Pending(id, "workflow-a");
        RuntimePostCommitOutboxClaim first;

        await using (var firstContext = database.Open("tenant-a"))
        {
            var store = new EfRuntimePostCommitOutboxStore(firstContext, new FixedAccessor("tenant-a"));
            await store.SavePendingAsync(item);
            Assert.Equal(id, (await store.FindAsync(id))!.OutboxItemId);
            Assert.Equal(id, Assert.Single(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10))).OutboxItemId);
            first = Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
                "owner-a", Now, TimeSpan.FromMinutes(1), 1)));
        }

        RuntimePostCommitOutboxClaim second;
        await using (var secondContext = database.Open("tenant-a"))
        {
            var store = new EfRuntimePostCommitOutboxStore(secondContext, new FixedAccessor("tenant-a"));
            second = Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
                "owner-b", Now.AddMinutes(2), TimeSpan.FromMinutes(1), 1)));
            Assert.Equal(first.FencingToken + 1, second.FencingToken);
            await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => store.RecordDeliveryResultAsync(
                first,
                new RuntimePostCommitOutboxDeliveryResult(id, RuntimePostCommitOutboxStatus.Delivered, Now.AddMinutes(2))).AsTask());
            await store.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
                second,
                new RuntimePostCommitOutboxDeliveryResult(id, RuntimePostCommitOutboxStatus.Delivered, Now.AddMinutes(2))));
        }

        await using var restarted = database.Open("tenant-a");
        var persisted = new EfRuntimePostCommitOutboxStore(restarted, new FixedAccessor("tenant-a"));
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await persisted.FindAsync(id))!.Status);
        Assert.Empty(await persisted.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now.AddHours(1), 10)));
    }

    [Fact]
    public async Task Scopes_and_restart_keep_same_logical_ids_isolated()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var tenantA = database.Open("tenant-a"))
        {
            await new EfRuntimePostCommitOutboxStore(tenantA, new FixedAccessor("tenant-a"))
                .SavePendingAsync(Pending("same-id", "workflow-a"));
        }
        await using (var tenantB = database.Open("tenant-b"))
        {
            await new EfRuntimePostCommitOutboxStore(tenantB, new FixedAccessor("tenant-b"))
                .SavePendingAsync(Pending("same-id", "workflow-b"));
        }

        await using var restartedA = database.Open("tenant-a");
        await using var restartedB = database.Open("tenant-b");
        Assert.Equal("workflow-a", (await new EfRuntimePostCommitOutboxStore(restartedA, new FixedAccessor("tenant-a")).FindAsync("same-id"))!.Intent.WorkflowExecutionId);
        Assert.Equal("workflow-b", (await new EfRuntimePostCommitOutboxStore(restartedB, new FixedAccessor("tenant-b")).FindAsync("same-id"))!.Intent.WorkflowExecutionId);
    }

    [Fact]
    public async Task Failed_completion_rolls_back_and_claim_remains_reusable()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new FailUpdateInterceptor();
        await using (var context = database.Open("tenant-a", interceptor))
        {
            var store = new EfRuntimePostCommitOutboxStore(context, new FixedAccessor("tenant-a"));
            await store.SavePendingAsync(Pending("rollback", "workflow-a"));
            var claim = Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
                "owner-a", Now, TimeSpan.FromMinutes(1), 1)));
            interceptor.Arm();
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => store.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
                claim,
                new RuntimePostCommitOutboxDeliveryResult("rollback", RuntimePostCommitOutboxStatus.Delivered, Now.AddSeconds(1)))).AsTask());
            Assert.IsType<InvalidOperationException>(failure.InnerException);
        }

        await using (var verification = database.Open("tenant-a"))
        {
            var store = new EfRuntimePostCommitOutboxStore(verification, new FixedAccessor("tenant-a"));
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, (await store.FindAsync("rollback"))!.Status);
            var reclaimed = Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
                "owner-b", Now.AddMinutes(2), TimeSpan.FromMinutes(1), 1)));
            await store.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
                reclaimed,
                new RuntimePostCommitOutboxDeliveryResult("rollback", RuntimePostCommitOutboxStatus.Delivered, Now.AddMinutes(2))));
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await store.FindAsync("rollback"))!.Status);
        }
    }

    [Fact]
    public async Task Long_alias_collision_fails_closed()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var store = new EfRuntimePostCommitOutboxStore(context, new FixedAccessor("tenant-a"));
        var longId = new string('x', 451);
        await store.SavePendingAsync(Pending(longId, "workflow-long"));
        var collision = RuntimePostCommitOutboxIdentity.CreateProjectionValue(longId);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SavePendingAsync(Pending(collision, "workflow-short")).AsTask());
        Assert.Contains("identity collision", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(longId, (await store.FindAsync(longId))!.OutboxItemId);
    }

    [Fact]
    public async Task Dispatch_redrive_returns_not_found_without_a_dispatch()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var store = new EfRuntimePostCommitOutboxStore(context, new FixedAccessor("tenant-a"));

        var result = await store.RedriveAsync(new WorkflowDispatchRedriveRequest("dispatch", "request", Now));
        Assert.Equal(WorkflowDispatchRedriveDisposition.NotFound, result.Disposition);
    }

    [Fact]
    public void Registration_replaces_all_public_outbox_contracts_after_dispatch_and_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });
        services.AddRuntimeWorkflowDispatchEntityFrameworkCore();
        services.AddRuntimePostCommitOutboxEntityFrameworkCore();
        services.AddRuntimePostCommitOutboxEntityFrameworkCore();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(EfRuntimePostCommitOutboxStore));
        Assert.Equal(RuntimePostCommitOutboxStoreBackend.EntityFramework, RuntimePostCommitOutboxStoreBackend.Find(services)!.Name);
        Assert.All(new[]
        {
            typeof(IRuntimePostCommitOutboxStore),
            typeof(IPostCommitOutboxLookupStore),
            typeof(IRuntimePostCommitOutboxClaimStore),
            typeof(IRuntimePostCommitOutboxClaimCompletionStore),
            typeof(IWorkflowDispatchRedriveStore)
        }, serviceType =>
        {
            var descriptors = services.Where(x => x.ServiceType == serviceType).ToArray();
            Assert.Single(descriptors);
            Assert.NotNull(descriptors[0].ImplementationFactory);
            Assert.True(RuntimePostCommitOutboxStoreBackend.Find(services)!.Owns(descriptors[0]));
        });
    }

    [Fact]
    public void Registration_rejects_outbox_without_an_EF_dispatch_backend()
    {
        var services = new ServiceCollection();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddRuntimePostCommitOutboxEntityFrameworkCore());
        Assert.Equal(before, services);
        Assert.Null(RuntimePostCommitOutboxStoreBackend.Find(services));
    }

    [Fact]
    public void Registration_rejects_an_explicit_outbox_contract_without_mutation()
    {
        var services = new ServiceCollection();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });
        services.AddRuntimeWorkflowDispatchEntityFrameworkCore();
        services.AddScoped<IRuntimePostCommitOutboxStore>(_ => throw new InvalidOperationException("foreign"));
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddRuntimePostCommitOutboxEntityFrameworkCore());
        Assert.Equal(before, services);
        Assert.Null(RuntimePostCommitOutboxStoreBackend.Find(services));
    }

    [Fact]
    public void Registration_rejects_a_foreign_outbox_factory_with_a_core_like_type_name()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new() { ConnectionString = "Data Source=:memory:" });
        services.AddRuntimeWorkflowDispatchEntityFrameworkCore();
        services.Remove(services.Single(descriptor => descriptor.ServiceType == typeof(IPostCommitOutboxLookupStore)));
        services.AddScoped<IPostCommitOutboxLookupStore>(ForeignRuntimeCoreServiceCollectionExtensions.ResolveOutboxLookup);
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddRuntimePostCommitOutboxEntityFrameworkCore());
        Assert.Equal(before, services);
    }

    private static class ForeignRuntimeCoreServiceCollectionExtensions
    {
        public static IPostCommitOutboxLookupStore ResolveOutboxLookup(IServiceProvider _) =>
            throw new InvalidOperationException("The foreign registration must not be resolved.");
    }

    [Fact]
    public async Task Public_dispatch_and_outbox_contracts_resolve_the_shared_sqlite_context()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-runtime-di-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = $"Data Source={databasePath}"
        });
        services.AddRuntimeWorkflowDispatchEntityFrameworkCore();
        services.AddRuntimePostCommitOutboxEntityFrameworkCore();

        try
        {
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
                PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            var context = scope.ServiceProvider.GetRequiredService<BookmarkStateDbContext>();
            await context.Database.EnsureCreatedAsync();

            var dispatch = scope.ServiceProvider.GetRequiredService<IWorkflowDispatchStore>();
            var outbox = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxStore>();
            Assert.IsType<EfWorkflowDispatchStore>(dispatch);
            Assert.IsType<EfRuntimePostCommitOutboxStore>(outbox);
            Assert.Same(dispatch, scope.ServiceProvider.GetRequiredService<EfWorkflowDispatchStore>());
            Assert.Same(outbox, scope.ServiceProvider.GetRequiredService<EfRuntimePostCommitOutboxStore>());

            var item = Pending("di-outbox", "workflow-di");
            await scope.ServiceProvider.GetRequiredService<EfRuntimePostCommitOutboxStore>().SavePendingAsync(item);
            Assert.Equal(item.OutboxItemId, (await scope.ServiceProvider
                .GetRequiredService<IPostCommitOutboxLookupStore>()
                .FindAsync(item.OutboxItemId))!.OutboxItemId);
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    private static RuntimePostCommitOutboxItem Pending(
        string id,
        string workflowExecutionId,
        string kind = "test.intent",
        RuntimePostCommitRetryPolicy? retryPolicy = null) =>
        new(
            id,
            new RuntimePostCommitIntent($"intent-{id}", workflowExecutionId, kind, Now, null, null, null),
            RuntimePostCommitOutboxStatus.Pending,
            Now,
            Now,
            retryPolicy);

    private static RuntimePostCommitOutboxItem PendingAt(
        string id,
        DateTimeOffset recordedAt,
        string workflowExecutionId,
        string kind) =>
        new(
            id,
            new RuntimePostCommitIntent($"intent-{id}", workflowExecutionId, kind, recordedAt, null, null, null),
            RuntimePostCommitOutboxStatus.Pending,
            recordedAt,
            recordedAt);

    private static RuntimePostCommitOutboxItem PendingWithoutAvailability(string id, string workflowExecutionId) =>
        new(
            id,
            new RuntimePostCommitIntent($"intent-{id}", workflowExecutionId, "test.intent", Now, null, null, null),
            RuntimePostCommitOutboxStatus.Pending,
            Now,
            null);

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class TestDatabase(SqliteConnection connection) : IAsyncDisposable
    {
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
            var builder = new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection);
            if (interceptors.Length > 0)
                builder.AddInterceptors(interceptors);
            return new BookmarkStateSqliteDbContext(builder.Options);
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class FailUpdateInterceptor : DbCommandInterceptor
    {
        private int armed;

        public void Arm() => Interlocked.Exchange(ref armed, 1);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfArmed(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfArmed(command);
            return ValueTask.FromResult(result);
        }

        private void ThrowIfArmed(DbCommand command)
        {
            if (command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains(RuntimePostCommitOutboxEfModule.TableName, StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref armed, 0) == 1)
                throw new InvalidOperationException("Simulated outbox completion failure.");
        }
    }
}
