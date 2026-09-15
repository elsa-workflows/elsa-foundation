using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfWorkflowSchedulerPoisonStoreTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task Record_find_list_replace_restart_and_tenant_isolation_are_durable()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var tenantA = database.Open("tenant-a"))
        await using (var tenantB = database.Open("tenant-b"))
        {
            await tenantA.Store.RecordAsync(Record(2));
            await tenantA.Store.RecordAsync(Record(1));
            await tenantB.Store.RecordAsync(Record(9));

            var replacement = Record(1, "replaced", failureCount: 3);
            await tenantA.Store.RecordAsync(replacement);
            var found = await tenantA.Store.FindAsync("workflow-1", "work-1");

            Assert.NotNull(found);
            Assert.Equal("replaced", found!.Fault.Message);
            Assert.Equal(3, found.FailureCount);
            Assert.Equal(["work-1", "work-2"], (await tenantA.Store.ListAsync("workflow-1")).Select(item => item.WorkItemId));
            Assert.Equal(["work-9"], (await tenantB.Store.ListAsync("workflow-1")).Select(item => item.WorkItemId));
        }

        await using var restarted = database.Open("tenant-a");
        var recovered = await restarted.Store.FindAsync("workflow-1", "work-1");
        Assert.NotNull(recovered);
        Assert.Equal("replaced", recovered!.Fault.Message);
        Assert.Equal(3, recovered.FailureCount);
    }

    [Fact]
    public async Task Current_json_preserves_retry_inner_fault_and_metadata()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var expected = Record(1, disposition: RuntimeSchedulerPoisonDisposition.RetryScheduled, nextRetryAt: Now.AddMinutes(5));

        await fixture.Store.RecordAsync(expected);
        var actual = await fixture.Store.FindAsync(expected.WorkflowExecutionId, expected.WorkItemId);

        Assert.NotNull(actual);
        Assert.Equal(expected.CommandKind, actual!.CommandKind);
        Assert.Equal(expected.HandlerName, actual.HandlerName);
        Assert.Equal(expected.Fault, actual.Fault);
        Assert.Equal(expected.InnerFault, actual.InnerFault);
        Assert.Equal(expected.FailureCount, actual.FailureCount);
        Assert.Equal(expected.Disposition, actual.Disposition);
        Assert.Equal(expected.FirstFailedAt, actual.FirstFailedAt);
        Assert.Equal(expected.LastFailedAt, actual.LastFailedAt);
        Assert.Equal(expected.NextRetryAt, actual.NextRetryAt);
        Assert.Equal("work-1", actual.Metadata["payload"]);
    }

    [Fact]
    public async Task List_is_bounded_stably_ordered_and_projection_drift_fails_closed()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        for (var index = 0; index <= RuntimeStorePageRequest.MaximumLimit; index++)
            await fixture.Store.RecordAsync(Record(index, workflowExecutionId: "workflow-many"));

        var listed = await fixture.Store.ListAsync("workflow-many");
        Assert.Equal(RuntimeStorePageRequest.MaximumLimit + 1, listed.Count);
        Assert.Equal(
            listed.OrderBy(item => item.FirstFailedAt).ThenBy(item => item.LastFailedAt).ThenBy(item => item.WorkItemId, StringComparer.Ordinal),
            listed);

        var row = await fixture.Context.WorkflowSchedulerPoisonRecords.SingleAsync(x => x.WorkItemId == EfRelationalIdentity.Encode("work-0"));
        row.WorkItemIdOrderKey = "corrupt";
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.FindAsync("workflow-many", "work-0").AsTask());

        row = await fixture.Context.WorkflowSchedulerPoisonRecords.SingleAsync(x => x.WorkItemId == EfRelationalIdentity.Encode("work-1"));
        row.ScopeKey = EfRelationalIdentity.Encode("tenant-b");
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.FindAsync("workflow-many", "work-1").AsTask());
    }

    [Fact]
    public async Task Record_uses_revision_compare_and_swap_and_transaction_rollback()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var first = Record(1);
        await fixture.Store.RecordAsync(first);

        await using var left = database.Open("tenant-a");
        await using var right = database.Open("tenant-a");
        var leftRow = await left.Context.WorkflowSchedulerPoisonRecords.AsNoTracking().SingleAsync();
        var rightRow = await right.Context.WorkflowSchedulerPoisonRecords.AsNoTracking().SingleAsync();
        leftRow.Revision++;
        left.Context.WorkflowSchedulerPoisonRecords.Attach(leftRow);
        left.Context.Entry(leftRow).Property(row => row.Revision).OriginalValue = 1;
        left.Context.Entry(leftRow).State = EntityState.Modified;
        await left.Context.SaveChangesAsync();
        rightRow.Revision++;
        right.Context.WorkflowSchedulerPoisonRecords.Attach(rightRow);
        right.Context.Entry(rightRow).Property(row => row.Revision).OriginalValue = 1;
        right.Context.Entry(rightRow).State = EntityState.Modified;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => right.Context.SaveChangesAsync());

        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            await fixture.Store.RecordAsync(Record(2, workflowExecutionId: "workflow-rollback"));
            await transaction.RollbackAsync();
        }

        await using var restarted = database.Open("tenant-a");
        Assert.Null(await restarted.Store.FindAsync("workflow-rollback", "work-2"));
    }

    [Fact]
    public async Task Generic_provider_failure_detaches_poison_entity_before_shared_context_sibling_save()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new PoisonWriteFailureInterceptor();
        await using var fixture = database.Open("tenant-a", interceptor);
        interceptor.Arm();

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Store.RecordAsync(Record(1)).AsTask());
        Assert.Empty(fixture.Context.ChangeTracker.Entries<WorkflowSchedulerPoisonEntity>());

        // A later participant using the same DbContext must not flush the failed poison insert implicitly.
        fixture.Context.SchedulerStates.Add(new SchedulerStateEntity
        {
            Id = "sibling-row",
            ScopeKey = EfRelationalIdentity.Encode("tenant-a"),
            ScopeKeyHash = EfRelationalIdentity.Hash("tenant-a"),
            WorkflowExecutionId = EfRelationalIdentity.Encode("workflow-sibling"),
            WorkflowExecutionIdHash = EfRelationalIdentity.Hash("workflow-sibling"),
            WorkflowExecutionIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey("workflow-sibling", RuntimeOperationalStateEfModule.IdentityMaximumLength)),
            Collection = "schedulerState",
            ContentJson = "{}",
            SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
            Revision = 1
        });
        await fixture.Context.SaveChangesAsync();

        Assert.Null(await fixture.Store.FindAsync("workflow-1", "work-1"));
        Assert.Single(await fixture.Context.SchedulerStates.ToArrayAsync());
    }

    [Fact]
    public async Task Registration_is_load_order_independent_and_keeps_poison_out_of_operational_backend()
    {
        foreach (var poisonFirst in new[] { true, false })
        {
            var connectionString = $"Data Source=file:ef-r23-registration-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var services = new ServiceCollection();
            services.AddSingleton<IWorkflowSchedulerPoisonStore, InMemoryWorkflowSchedulerPoisonStore>();
            services.AddSingleton<IPersistenceAccessContextAccessor>(new FixedAccessor("tenant-a"));
            var poisonOptions = new RuntimeSchedulerPoisonEntityFrameworkCoreOptions
            {
                Provider = "Sqlite",
                ConnectionString = connectionString
            };
            var operationalOptions = new RuntimeOperationalStateEntityFrameworkCoreOptions
            {
                Provider = "Sqlite",
                ConnectionString = connectionString
            };

            if (poisonFirst)
            {
                services.AddRuntimeSchedulerPoisonEntityFrameworkCore(poisonOptions);
                services.AddRuntimeOperationalStateEntityFrameworkCore(operationalOptions);
            }
            else
            {
                services.AddRuntimeOperationalStateEntityFrameworkCore(operationalOptions);
                services.AddRuntimeSchedulerPoisonEntityFrameworkCore(poisonOptions);
            }

            Assert.Equal(WorkflowSchedulerPoisonStoreBackend.EntityFramework, WorkflowSchedulerPoisonStoreBackend.Find(services)!.Name);
            Assert.Equal(RuntimeOperationalStateStoreBackend.EntityFramework, RuntimeOperationalStateStoreBackend.Find(services)!.Name);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<BookmarkStateDbContext>();
            await context.Database.EnsureCreatedAsync();
            Assert.IsType<EfWorkflowSchedulerPoisonStore>(scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>());
        }
    }

    [Fact]
    public void Registration_refuses_foreign_poison_ownership_without_partial_mutation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowSchedulerPoisonStore, ForeignPoisonStore>();
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeSchedulerPoisonEntityFrameworkCore(new()));
        Assert.Equal(before, services);
    }

    private static RuntimeSchedulerPoisonRecord Record(
        int index,
        string? message = null,
        string workflowExecutionId = "workflow-1",
        string? workItemId = null,
        int failureCount = 1,
        RuntimeSchedulerPoisonDisposition disposition = RuntimeSchedulerPoisonDisposition.Poisoned,
        DateTimeOffset? nextRetryAt = null) =>
        new(
            workflowExecutionId,
            workItemId ?? $"work-{index}",
            WorkflowExecutionCommandKind.RunSchedulerWork,
            $"handler-{index}",
            new RuntimeFaultInfo("System.InvalidOperationException", message ?? $"boom-{index}", "stack"),
            failureCount,
            disposition,
            Now.AddMilliseconds(index),
            Now.AddMilliseconds(index * 2.0),
            disposition == RuntimeSchedulerPoisonDisposition.RetryScheduled ? nextRetryAt ?? Now.AddMinutes(1) : null,
            new Dictionary<string, string> { ["source"] = "test", ["payload"] = $"work-{index}" },
            new RuntimeFaultInfo("System.ArgumentException", $"inner-{index}"));

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class ForeignPoisonStore : IWorkflowSchedulerPoisonStore
    {
        public ValueTask<RuntimeSchedulerPoisonRecord> RecordAsync(RuntimeSchedulerPoisonRecord record, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(record);

        public ValueTask<RuntimeSchedulerPoisonRecord?> FindAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RuntimeSchedulerPoisonRecord?>(null);

        public ValueTask<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>>([]);
    }

    private sealed class TestDatabase(SqliteConnection keeper, string connectionString) : IAsyncDisposable
    {
        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=file:ef-r23-poison-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            await using var context = new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(keeper).Options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(keeper, connectionString);
        }

        public Fixture Open(string scope, params IInterceptor[] interceptors) => new(connectionString, scope, interceptors);

        public async ValueTask DisposeAsync() => await keeper.DisposeAsync();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public readonly BookmarkStateSqliteDbContext Context;
        public readonly EfWorkflowSchedulerPoisonStore Store;

        public Fixture(string connectionString, string scope, params IInterceptor[] interceptors)
        {
            connection = new SqliteConnection(connectionString);
            connection.Open();
            var options = new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection);
            if (interceptors.Length > 0)
                options.AddInterceptors(interceptors);
            Context = new BookmarkStateSqliteDbContext(options.Options);
            Store = new EfWorkflowSchedulerPoisonStore(Context, new FixedAccessor(scope));
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class PoisonWriteFailureInterceptor : DbCommandInterceptor
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
            if (command.CommandText.Contains("elsa_runtime_scheduler_poison", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref armed, 0) == 1)
                throw new DbUpdateException("Simulated generic provider write failure.");
        }
    }
}
