using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Recovery;
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
    public async Task Execution_liveness_supports_create_only_cas_versioned_reads_and_bounded_pages_after_restart()
    {
        await using var database = await TestDatabase.CreateAsync();
        var stateA = Liveness("workflow-a", "state-z");
        var stateB = Liveness("workflow-a", "state-a");
        await using (var tenantA = database.Open("tenant-a"))
        {
            var created = await tenantA.Liveness.TrySaveAsync(stateA, expectedRevision: 0);
            Assert.Equal(ExecutionLivenessStateWriteStatus.Saved, created.Status);
            Assert.Equal(1, created.Revision);
            Assert.Equal(1, (await tenantA.Liveness.FindVersionedAsync("workflow-a", "state-z"))!.Revision);

            var createConflict = await tenantA.Liveness.TrySaveAsync(stateA, expectedRevision: 0);
            Assert.Equal(ExecutionLivenessStateWriteStatus.RevisionConflict, createConflict.Status);
            Assert.Equal(1, createConflict.Revision);

            var replaced = await tenantA.Liveness.TrySaveAsync(stateA, expectedRevision: 1);
            Assert.Equal(ExecutionLivenessStateWriteStatus.Saved, replaced.Status);
            Assert.Equal(2, replaced.Revision);
            var stale = await tenantA.Liveness.TrySaveAsync(stateA, expectedRevision: 1);
            Assert.Equal(ExecutionLivenessStateWriteStatus.RevisionConflict, stale.Status);
            Assert.Equal(2, stale.Revision);

            await tenantA.Liveness.SaveAsync(stateB);
            var first = await tenantA.Liveness.ListPageAsync(new ExecutionLivenessStatePageQuery("workflow-a", 1));
            Assert.Equal("state-a", first.Items.Single().OperationalStateId);
            Assert.NotNull(first.NextContinuationToken);
            var second = await tenantA.Liveness.ListPageAsync(new ExecutionLivenessStatePageQuery("workflow-a", 1, first.NextContinuationToken));
            Assert.Equal("state-z", second.Items.Single().OperationalStateId);

            var all = await tenantA.Liveness.ListAllPageAsync(new RuntimeStorePageRequest(1));
            Assert.Equal("workflow-a", all.Items.Single().WorkflowExecutionId);
            Assert.NotNull(all.NextContinuationToken);
        }

        await using var restarted = database.Open("tenant-a");
        Assert.Equal(2, (await restarted.Liveness.FindVersionedAsync("workflow-a", "state-z"))!.Revision);
        await using var tenantB = database.Open("tenant-b");
        Assert.Null(await tenantB.Liveness.FindAsync("workflow-a", "state-z"));
    }

    [Fact]
    public async Task Execution_liveness_recovery_pages_are_bounded_due_ordered_and_fail_closed_on_corruption()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = DateTimeOffset.UtcNow;
        await fixture.Liveness.SaveAsync(Liveness("workflow-a", "state-detected", interruptedAt: now.AddMinutes(-5)));
        await fixture.Liveness.SaveAsync(Liveness("workflow-a", "state-lease", leaseAcquiredAt: now.AddMinutes(-3), leaseExpiresAt: now.AddMinutes(1)));
        await fixture.Liveness.SaveAsync(Liveness("workflow-a", "state-heartbeat", heartbeatRecordedAt: now.AddMinutes(-2.5)));
        await fixture.Liveness.SaveAsync(Liveness("workflow-a", "state-live", leaseAcquiredAt: now, leaseExpiresAt: now.AddHours(1), heartbeatRecordedAt: now));

        var request = new RuntimeRecoveryScanRequest(now, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2), 2);
        var first = await fixture.Recovery.ScanPageAsync(request);
        Assert.Equal(["state-detected", "state-lease"], first.Items.Select(x => x.OperationalStateId));
        Assert.NotNull(first.NextContinuationToken);
        var second = await fixture.Recovery.ScanPageAsync(new RuntimeRecoveryScanRequest(now, request.LeaseTimeout, request.HeartbeatTimeout, 2, continuationToken: first.NextContinuationToken));
        Assert.Equal(["state-heartbeat"], second.Items.Select(x => x.OperationalStateId));
        Assert.Null(second.NextContinuationToken);

        var row = await fixture.Context.ExecutionLivenessStates.SingleAsync(x => x.OperationalStateId == EfRelationalIdentity.Encode("state-live"));
        row.Revision = 0;
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Liveness.FindAsync("workflow-a", "state-live").AsTask());
    }

    [Fact]
    public async Task Execution_liveness_persists_lease_heartbeat_and_fencing_across_restart()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);
        RuntimeExecutionLease first;
        await using (var fixture = database.Open("tenant-a"))
        {
            var ownership = new RuntimeExecutionOwnershipService(
                fixture.Liveness,
                clock,
                new RuntimeExecutionOwnershipOptions { OwnerId = "owner-a", LeaseDuration = TimeSpan.FromMinutes(1) });
            first = await ownership.AcquireAsync("workflow-a");
            Assert.Equal(1, first.FencingToken);
            Assert.Equal(RuntimeExecutionOwnershipTransitionStatus.Applied, (await ownership.HeartbeatAsync(first)).Status);
        }

        await using (var restarted = database.Open("tenant-a"))
        {
            var ownership = new RuntimeExecutionOwnershipService(
                restarted.Liveness,
                clock,
                new RuntimeExecutionOwnershipOptions { OwnerId = "owner-a", LeaseDuration = TimeSpan.FromMinutes(1) });
            var second = await ownership.AcquireAsync("workflow-a");
            Assert.Equal(2, second.FencingToken);
            await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => ownership.EnsureCurrentAsync("workflow-a", first.FencingToken).AsTask());
            Assert.Equal(RuntimeExecutionOwnershipTransitionStatus.Applied, (await ownership.ReleaseAsync(second)).Status);
        }
    }

    [Fact]
    public async Task Execution_liveness_recovery_owner_filter_fences_route_candidates()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = DateTimeOffset.UtcNow;
        await fixture.Liveness.SaveAsync(Liveness("workflow-a", "state-owner-a", ownerId: "owner-a", leaseAcquiredAt: now.AddMinutes(-3), leaseExpiresAt: now.AddMinutes(1)));
        await fixture.Liveness.SaveAsync(Liveness("workflow-a", "state-owner-b", ownerId: "owner-b", leaseAcquiredAt: now.AddMinutes(-3), leaseExpiresAt: now.AddMinutes(1)));

        var request = new RuntimeRecoveryScanRequest(now, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2), 10, ownerId: "owner-a");
        var page = await fixture.Recovery.ScanPageAsync(request);
        Assert.Equal(["state-owner-a"], page.Items.Select(x => x.OperationalStateId));
        Assert.Null(page.NextContinuationToken);
    }

    [Fact]
    public async Task Execution_liveness_recovery_continuation_distinguishes_an_owner_named_all_from_no_owner_filter()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = DateTimeOffset.UtcNow;
        await fixture.Liveness.SaveAsync(Liveness("workflow-a", "state-a", interruptedAt: now.AddMinutes(-2)));
        await fixture.Liveness.SaveAsync(Liveness("workflow-a", "state-b", interruptedAt: now.AddMinutes(-1)));

        var allRequest = new RuntimeRecoveryScanRequest(now, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2), 1);
        var all = await fixture.Liveness.ListRecoveryPageAsync(allRequest, new RuntimeStorePageRequest(1));
        Assert.NotNull(all.NextContinuationToken);
        var ownerRequest = new RuntimeRecoveryScanRequest(now, allRequest.LeaseTimeout, allRequest.HeartbeatTimeout, 1, ownerId: "<all>");
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Liveness.ListRecoveryPageAsync(
            ownerRequest, new RuntimeStorePageRequest(1, all.NextContinuationToken)).AsTask());

        var owned = await fixture.Liveness.ListRecoveryPageAsync(ownerRequest, new RuntimeStorePageRequest(1));
        Assert.NotNull(owned.NextContinuationToken);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Liveness.ListRecoveryPageAsync(
            allRequest, new RuntimeStorePageRequest(1, owned.NextContinuationToken)).AsTask());
    }

    [Fact]
    public async Task Workflow_holds_are_keyed_by_control_plane_id_and_global_embedded_holds_are_workflow_visible()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var fixture = database.Open("tenant-a"))
        {
            await fixture.Holds.SaveAsync(new WorkflowHoldState("workflow-state", "workflow-a", [WorkflowHold.ForWorkflowExecution("direct", "workflow-a", DateTimeOffset.UtcNow, "test", "direct")]));
            await fixture.Holds.SaveAsync(new WorkflowHoldState("global-state", activeHolds: [WorkflowHold.ForWorkflowExecution("embedded", "workflow-b", DateTimeOffset.UtcNow, "test", "embedded")]));
            await fixture.Holds.SaveAsync(new WorkflowHoldState("other-state", "workflow-c", [WorkflowHold.ForWorkflowExecution("other", "workflow-c", DateTimeOffset.UtcNow, "test", "other")]));

            Assert.Equal("workflow-state", (await fixture.Holds.FindAsync("workflow-state"))!.ControlPlaneStateId);
            Assert.Equal(["workflow-state"], (await fixture.Holds.ListForWorkflowExecutionAsync("workflow-a")).Select(x => x.ControlPlaneStateId));
            Assert.Equal(["global-state"], (await fixture.Holds.ListForWorkflowExecutionAsync("workflow-b")).Select(x => x.ControlPlaneStateId));
            Assert.Collection(await fixture.Holds.ListAllAsync(),
                state => Assert.Equal("global-state", state.ControlPlaneStateId),
                state => Assert.Equal("other-state", state.ControlPlaneStateId),
                state => Assert.Equal("workflow-state", state.ControlPlaneStateId));
        }

        await using var restarted = database.Open("tenant-a");
        Assert.Single(await restarted.Holds.ListForWorkflowExecutionAsync("workflow-a"));
        var row = await restarted.Context.WorkflowHoldStates.SingleAsync(x => x.ControlPlaneStateId == EfRelationalIdentity.Encode("global-state"));
        row.ContentJson = "not-json";
        await restarted.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => restarted.Holds.FindAsync("global-state").AsTask());
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
    public async Task Registration_replaces_core_defaults_with_the_complete_ef_operational_family()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new RuntimeOperationalStateEntityFrameworkCoreOptions
        {
            ConnectionString = "Data Source=:memory:",
            RecoveryContinuationSigningKey = "shared-runtime-signing-key-32-bytes"
        });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BookmarkStateDbContext>();
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        Assert.IsType<EfExecutionLivenessStateStore>(scope.ServiceProvider.GetRequiredService<IExecutionLivenessStateStore>());
        Assert.IsType<EfWorkflowHoldStateStore>(scope.ServiceProvider.GetRequiredService<IWorkflowHoldStateStore>());
        Assert.True(scope.ServiceProvider.GetRequiredService<IRuntimeRecoveryScanner>() is IRuntimeRecoveryPagedScanner { SupportsPaging: true });
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

    private static ExecutionLivenessState Liveness(
        string workflowExecutionId,
        string operationalStateId,
        string ownerId = "owner",
        DateTimeOffset? interruptedAt = null,
        DateTimeOffset? leaseAcquiredAt = null,
        DateTimeOffset? leaseExpiresAt = null,
        DateTimeOffset? heartbeatRecordedAt = null)
    {
        var lease = leaseAcquiredAt is { } acquired
            ? new RuntimeExecutionLease("lease-" + operationalStateId, workflowExecutionId, ownerId, acquired, leaseExpiresAt ?? acquired.AddHours(1), 1)
            : null;
        var heartbeat = heartbeatRecordedAt is { } recorded
            ? new RuntimeHeartbeat("heartbeat-" + operationalStateId, workflowExecutionId, ownerId, lease?.LeaseId, recorded)
            : null;
        var interrupted = interruptedAt is { } interruptedTime
            ? new InterruptedExecutionState("interrupt-" + operationalStateId, workflowExecutionId, lease?.LeaseId, "checkpoint", RuntimeInterruptionReason.HostStopped, RuntimeInterruptionStatus.Detected, interruptedTime)
            : null;
        return new ExecutionLivenessState(operationalStateId, workflowExecutionId, lease, heartbeat, null, interrupted);
    }

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
        public EfExecutionLivenessStateStore Liveness { get; }
        public EfWorkflowHoldStateStore Holds { get; }
        public InMemoryRuntimeRecoveryScanner Recovery { get; }

        public TestFixture(string connectionString, string scope)
        {
            _connection = new SqliteConnection(connectionString);
            _connection.Open();
            Context = new BookmarkStateSqliteDbContext(new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(_connection).Options);
            var accessor = new Accessor(scope);
            Values = new EfDurableValueStateStore(Context, accessor, new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) })));
            Scheduler = new EfSchedulerStateStore(Context, accessor);
            var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) }));
            Liveness = new EfExecutionLivenessStateStore(Context, accessor, codec);
            Holds = new EfWorkflowHoldStateStore(Context, accessor);
            Recovery = new InMemoryRuntimeRecoveryScanner(Liveness, codec);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
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
