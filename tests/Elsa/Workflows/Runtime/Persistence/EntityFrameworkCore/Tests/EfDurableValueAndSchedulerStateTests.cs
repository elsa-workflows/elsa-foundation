using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfDurableValueAndSchedulerStateTests
{
    [Fact]
    public async Task Durable_values_are_scoped_stable_and_restartable()
    {
        await using var database = await TestDatabase.CreateAsync();
        var first = Value("z-value", "workflow-a");
        var second = Value("a-value", "workflow-a");
        await using (var tenantA = database.Open("tenant-a"))
        {
            await tenantA.Values.SaveAsync(first);
            await tenantA.Values.SaveAsync(second);
            var page = await tenantA.Values.ListPageAsync(new DurableValueStatePageQuery("workflow-a", 1));
            Assert.Equal("a-value", page.Items.Single().DurableValueId);
            Assert.NotNull(page.NextContinuationToken);
            var next = await tenantA.Values.ListPageAsync(new DurableValueStatePageQuery("workflow-a", 1, page.NextContinuationToken));
            Assert.Equal("z-value", next.Items.Single().DurableValueId);
            Assert.Null(await tenantA.Values.FindAsync("workflow-a", "not-there"));
        }

        await using (var tenantB = database.Open("tenant-b"))
        {
            await tenantB.Values.SaveAsync(Value("a-value", "workflow-a"));
            Assert.Null(await tenantB.Values.FindAsync("workflow-a", "z-value"));
        }

        await using var restarted = database.Open("tenant-a");
        Assert.Equal("z-value", (await restarted.Values.FindAsync("workflow-a", "z-value"))!.DurableValueId);
    }

    [Fact]
    public async Task Durable_value_projection_or_identity_corruption_fails_closed()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Values.SaveAsync(Value("value", "workflow-a"));
        var row = await fixture.Context.DurableValueStates.SingleAsync();
        row.ContentJson = row.ContentJson.Replace(EfRelationalIdentity.Encode("workflow-a"), EfRelationalIdentity.Encode("workflow-b"), StringComparison.Ordinal);
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Values.FindAsync("workflow-a", "value").AsTask());
    }

    [Fact]
    public async Task Durable_value_order_and_revision_projections_fail_closed()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Values.SaveAsync(Value("value", "workflow-a"));
        var row = await fixture.Context.DurableValueStates.SingleAsync();

        row.DurableValueIdOrderKey = "corrupt";
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Values.FindAsync("workflow-a", "value").AsTask());

        row.DurableValueIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey("value", RuntimeOperationalStateEfModule.IdentityMaximumLength));
        row.Revision = 0;
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Values.FindAsync("workflow-a", "value").AsTask());
    }

    [Fact]
    public async Task Durable_value_composite_identity_is_injective_for_separator_content()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var first = database.Open("tenant"))
            await first.Values.SaveAsync(Value("id", "workflow\u001fvalue"));
        await using (var second = database.Open("tenant\u001fworkflow"))
            await second.Values.SaveAsync(Value("id", "value"));

        await using var firstRestart = database.Open("tenant");
        await using var secondRestart = database.Open("tenant\u001fworkflow");
        Assert.NotNull(await firstRestart.Values.FindAsync("workflow\u001fvalue", "id"));
        Assert.NotNull(await secondRestart.Values.FindAsync("value", "id"));
    }

    [Fact]
    public async Task Runtime_operational_state_requires_one_ordinary_scoped_access_context()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) }));
        var global = new EfDurableValueStateStore(fixture.Context, new Accessor(PersistenceAccessContext.Global), codec);
        var privileged = new EfDurableValueStateStore(fixture.Context, new Accessor(PersistenceAccessContext.PrivilegedScoped(new PersistenceScope("tenant-a"), new PersistenceAccessPurpose("test"))), codec);
        var acrossScopes = new EfDurableValueStateStore(fixture.Context, new Accessor(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("test"))), codec);

        await Assert.ThrowsAsync<InvalidOperationException>(() => global.SaveAsync(Value("value", "workflow-a")).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => privileged.SaveAsync(Value("value", "workflow-a")).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => acrossScopes.SaveAsync(Value("value", "workflow-a")).AsTask());
    }

    [Fact]
    public async Task Scheduler_state_round_trips_across_scopes_and_lists_in_order()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var tenantA = database.Open("tenant-a");
        await tenantA.Scheduler.SaveAsync(new SchedulerState("workflow-z", 7));
        await tenantA.Scheduler.SaveAsync(new SchedulerState("workflow-a", 3));
        await using var tenantB = database.Open("tenant-b");
        await tenantB.Scheduler.SaveAsync(new SchedulerState("workflow-a", 99));

        var states = await tenantA.Scheduler.ListAsync();
        Assert.Equal(["workflow-a", "workflow-z"], states.Select(x => x.WorkflowExecutionId));
        Assert.Equal(7, (await tenantA.Scheduler.FindAsync("workflow-z"))!.Version);
        Assert.Equal(99, (await tenantB.Scheduler.FindAsync("workflow-a"))!.Version);
    }

    [Fact]
    public async Task Concurrent_scheduler_replacement_is_rejected_by_revision()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var seed = database.Open("tenant-a");
        await seed.Scheduler.SaveAsync(new SchedulerState("workflow-a", 1));
        await using var left = database.Open("tenant-a");
        await using var right = database.Open("tenant-a");
        var leftRow = await left.Context.SchedulerStates.SingleAsync();
        var rightRow = await right.Context.SchedulerStates.SingleAsync();
        leftRow.ContentJson = leftRow.ContentJson.Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal);
        leftRow.Revision++;
        await left.Context.SaveChangesAsync();
        rightRow.ContentJson = rightRow.ContentJson.Replace("\"version\":1", "\"version\":3", StringComparison.Ordinal);
        rightRow.Revision++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => right.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Scheduler_order_and_revision_projections_fail_closed()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Scheduler.SaveAsync(new SchedulerState("workflow-a", 1));
        var row = await fixture.Context.SchedulerStates.SingleAsync();

        row.WorkflowExecutionIdOrderKey = "corrupt";
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Scheduler.FindAsync("workflow-a").AsTask());

        row.WorkflowExecutionIdOrderKey = Convert.ToHexString(EfRelationalIdentity.CreateOrderKey("workflow-a", RuntimeOperationalStateEfModule.IdentityMaximumLength));
        row.Revision = 0;
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Scheduler.FindAsync("workflow-a").AsTask());
    }

    [Fact]
    public void Registration_is_opt_in_and_repeated_identical_registration_is_stable()
    {
        IServiceCollection services = new ServiceCollection();
        var options = new RuntimeOperationalStateEntityFrameworkCoreOptions { ConnectionString = "Data Source=:memory:" };
        services.AddRuntimeOperationalStateEntityFrameworkCore(options);
        var snapshot = services.ToArray();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new RuntimeOperationalStateEntityFrameworkCoreOptions { ConnectionString = options.ConnectionString });
        Assert.Equal(snapshot, services);
        Assert.Equal(RuntimeOperationalStateStoreBackend.EntityFramework, RuntimeOperationalStateStoreBackend.Find(services)!.Name);
        Assert.Contains(services, x => x.ServiceType == typeof(IDurableValueStateStore));
        Assert.Contains(services, x => x.ServiceType == typeof(ISchedulerStateStore));
    }

    [Fact]
    public void Registration_rejects_foreign_factory_without_mutating_services()
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(ServiceDescriptor.Scoped<IDurableValueStateStore>(_ => null!));
        var snapshot = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeOperationalStateEntityFrameworkCore(new()));
        Assert.Equal(snapshot, services);
    }

    [Fact]
    public void Operational_state_context_is_reused_by_scope_and_alteration_registrations()
    {
        var services = new ServiceCollection();
        const string connectionString = "Data Source=:memory:";
        const string signingKey = "shared-runtime-signing-key-32-bytes";
        services.AddRuntimeOperationalStateEntityFrameworkCore(new()
        {
            ConnectionString = connectionString,
            RecoveryContinuationSigningKey = signingKey
        });
        services.AddRuntimeWorkflowTestScopeEntityFrameworkCore(new()
        {
            ConnectionString = connectionString,
            RecoveryContinuationSigningKey = signingKey
        });
        services.AddRuntimeWorkflowAlterationEntityFrameworkCore(new()
        {
            ConnectionString = connectionString,
            RecoveryContinuationSigningKey = signingKey
        });

        Assert.Single(services, x => x.ServiceType == typeof(BookmarkStateSqliteDbContext));
        Assert.Single(services, x => x.ServiceType == typeof(BookmarkStateDbContext));
        Assert.Single(services, x => x.ServiceType == typeof(DbContextOptions<BookmarkStateSqliteDbContext>));
    }

    [Fact]
    public void Scope_registration_then_operational_state_reuses_the_same_context()
    {
        var services = new ServiceCollection();
        const string connectionString = "Data Source=:memory:";
        const string signingKey = "shared-runtime-signing-key-32-bytes";
        services.AddRuntimeWorkflowTestScopeEntityFrameworkCore(new()
        {
            ConnectionString = connectionString,
            RecoveryContinuationSigningKey = signingKey
        });
        services.AddRuntimeOperationalStateEntityFrameworkCore(new()
        {
            ConnectionString = connectionString,
            RecoveryContinuationSigningKey = signingKey
        });

        Assert.Single(services, x => x.ServiceType == typeof(BookmarkStateSqliteDbContext));
        Assert.Single(services, x => x.ServiceType == typeof(BookmarkStateDbContext));
        Assert.Single(services, x => x.ServiceType == typeof(DbContextOptions<BookmarkStateSqliteDbContext>));
    }

    private static DurableValueState Value(string durableValueId, string workflowExecutionId) =>
        new(durableValueId, workflowExecutionId, "value-id", new RuntimeValueTypeDescriptor("json", null, null), DurableValueLifecycle.Result, DurableValueStorage.Inline, JsonDocument.Parse("{\"answer\":42}").RootElement, null, null, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["source"] = "test" });

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _keeper;
        private readonly string _connectionString;

        private TestDatabase(SqliteConnection keeper, string connectionString)
        {
            _keeper = keeper;
            _connectionString = connectionString;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=file:runtime-operational-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            await using var context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(keeper).Options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(keeper, connectionString);
        }

        public TestFixture Open(string scope) => new(_connectionString, scope);
        public ValueTask DisposeAsync() => _keeper.DisposeAsync();
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public BookmarkStateSqliteDbContext Context { get; }
        public EfDurableValueStateStore Values { get; }
        public EfSchedulerStateStore Scheduler { get; }

        public TestFixture(string connectionString, string scope)
        {
            _connection = new SqliteConnection(connectionString);
            _connection.Open();
            Context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(_connection).Options);
            var accessor = new Accessor(scope);
            Values = new EfDurableValueStateStore(Context, accessor, new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) })));
            Scheduler = new EfSchedulerStateStore(Context, accessor);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class Accessor : IPersistenceAccessContextAccessor
    {
        public Accessor(string scope) : this(PersistenceAccessContext.Scoped(new PersistenceScope(scope)))
        {
        }

        public Accessor(PersistenceAccessContext current)
        {
            Current = current;
        }

        public PersistenceAccessContext Current { get; }
    }
}
