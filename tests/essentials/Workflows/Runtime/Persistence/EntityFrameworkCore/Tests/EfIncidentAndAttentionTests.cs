using System.Security.Claims;
using Elsa.Attention.Core;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfIncidentAndAttentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Incident_store_is_composite_create_only_restartable_and_resolution_is_write_once()
    {
        await using var database = await TestDatabase.CreateAsync();
        var incident = Incident("incident", "workflow", IncidentStatus.Open, Now);
        await using (var fixture = database.Open("tenant-a"))
        {
            Assert.True(await fixture.Incidents.TryAddAsync(incident));
            Assert.False(await fixture.Incidents.TryAddAsync(incident));
            Assert.Equal(1, await fixture.Incidents.CountAsync("workflow"));
            Assert.Equal(incident.IncidentId, (await fixture.Incidents.FindAsync("workflow", "incident"))!.IncidentId);

            var outcome = new IncidentResolutionOutcome("resolve", Now.AddMinutes(1), null, "operator");
            var resolved = Incident("incident", "workflow", IncidentStatus.Resolved, Now, Now.AddMinutes(1), outcome);
            await fixture.Incidents.SaveAsync(resolved);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Incidents.SaveAsync(
                Incident("incident", "workflow", IncidentStatus.Resolved, Now, Now.AddMinutes(1),
                    new IncidentResolutionOutcome("other", Now.AddMinutes(1), null, "operator"))).AsTask());
        }

        await using var restarted = database.Open("tenant-a");
        var roundTrip = await restarted.Incidents.FindAsync("workflow", "incident");
        Assert.Equal(IncidentStatus.Resolved, roundTrip!.Status);
        Assert.Equal("resolve", roundTrip.ResolutionOutcome!.ActionKind);
    }

    [Fact]
    public async Task Incident_create_only_is_safe_under_concurrent_replay()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var left = database.Open("tenant-a");
        await using var right = database.Open("tenant-a");
        var state = Incident("incident", "workflow", IncidentStatus.Open, Now);

        var outcomes = await Task.WhenAll(
            left.Incidents.TryAddAsync(state).AsTask(),
            right.Incidents.TryAddAsync(state).AsTask());

        Assert.Equal([false, true], outcomes.Order());
        await using var verification = database.Open("tenant-a");
        Assert.Equal(1, await verification.Incidents.CountAsync("workflow"));
    }

    [Fact]
    public async Task Incident_reads_are_scope_isolated_and_attention_composes_faults_incidents_and_suppression()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var tenantA = database.Open("tenant-a");
        await tenantA.Executions.SaveAsync(Execution("fault", "definition-fault", WorkflowExecutionStatus.Faulted, Now.AddMinutes(-3), "tenant-a"));
        await tenantA.Executions.SaveAsync(Execution("blocked", "definition-blocked", WorkflowExecutionStatus.Running, Now.AddMinutes(-2), "tenant-a"));
        await tenantA.Executions.SaveAsync(Execution("open", "definition-open", WorkflowExecutionStatus.Faulted, Now.AddMinutes(-1), "tenant-a"));
        await tenantA.Incidents.TryAddAsync(Incident("incident-blocked", "blocked", IncidentStatus.Blocking, Now.AddMinutes(-2)));
        await tenantA.Incidents.TryAddAsync(Incident("incident-open", "open", IncidentStatus.Open, Now.AddMinutes(-1)));
        await tenantA.Incidents.TryAddAsync(Incident("incident-orphan", "missing", IncidentStatus.Blocking, Now));

        await using var tenantB = database.Open("tenant-b");
        await tenantB.Executions.SaveAsync(Execution("foreign", "definition-foreign", WorkflowExecutionStatus.Faulted, Now, "tenant-b"));
        await tenantB.Incidents.TryAddAsync(Incident("foreign-incident", "foreign", IncidentStatus.Blocking, Now));

        var result = await tenantA.Attention.QueryAsync(new(
            new AttentionQueryContext(new ClaimsPrincipal(), "tenant-a"),
            10));

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(
            [WorkflowRuntimeAttentionKind.BlockingIncident, WorkflowRuntimeAttentionKind.FaultedExecution, WorkflowRuntimeAttentionKind.OpenIncident],
            result.Records.Select(x => x.Kind));
        Assert.Equal("blocked", result.Records.First().WorkflowExecutionId);
        Assert.DoesNotContain(result.Records, x => x.WorkflowExecutionId is "foreign" or "missing");
        Assert.DoesNotContain(result.Records, x => x.WorkflowExecutionId == "open" && x.IncidentId is null);
        Assert.All(result.Records, x => Assert.Null(x.SanitizedSummary));
    }

    [Fact]
    public async Task Attention_counts_the_full_authorized_dataset_beyond_one_provider_page()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        for (var i = 0; i <= RuntimeStorePageRequest.MaximumLimit; i++)
        {
            var workflowId = $"workflow-{i:D3}";
            await fixture.Executions.SaveAsync(Execution(workflowId, "definition", WorkflowExecutionStatus.Faulted, Now, "tenant-a"));
        }

        var result = await fixture.Attention.QueryAsync(new(
            new AttentionQueryContext(new ClaimsPrincipal(), "tenant-a"),
            1));
        Assert.Equal(RuntimeStorePageRequest.MaximumLimit + 1, result.TotalCount);
        Assert.Equal("workflow-000", Assert.Single(result.Records).WorkflowExecutionId);
    }

    [Fact]
    public async Task Attention_requires_matching_tenant_scope_before_database_access()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var query = new EfWorkflowRuntimeAttentionQuery(
            fixture.Context,
            new FixedAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))),
            new FixedTimeProvider(Now));

        var unavailable = await query.QueryAsync(new(new(new ClaimsPrincipal(), null), 5));
        Assert.Equal("RUNTIME_ATTENTION_TENANT_REQUIRED", unavailable.ErrorCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => query.QueryAsync(new(
            new(new ClaimsPrincipal(), "tenant-b"), 5)).AsTask());
    }

    [Fact]
    public async Task Attention_fails_closed_when_status_projections_drift_from_content()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Executions.SaveAsync(Execution("fault", "definition", WorkflowExecutionStatus.Faulted, Now, "tenant-a"));
        await fixture.Incidents.TryAddAsync(Incident("incident", "fault", IncidentStatus.Open, Now));

        var executionRow = await fixture.Context.WorkflowExecutionStates.SingleAsync();
        executionRow.Status = (int)WorkflowExecutionStatus.Running;
        var incidentRow = await fixture.Context.IncidentStates.SingleAsync();
        incidentRow.Status = (int)IncidentStatus.Resolved;
        await fixture.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Attention.QueryAsync(new(
            new AttentionQueryContext(new ClaimsPrincipal(), "tenant-a"),
            5)).AsTask());

        incidentRow.Status = (int)IncidentStatus.Open;
        await fixture.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Attention.QueryAsync(new(
            new AttentionQueryContext(new ClaimsPrincipal(), "tenant-a"),
            5)).AsTask());
    }

    [Fact]
    public async Task Attention_rejects_an_active_incident_hidden_by_a_resolved_status_projection()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Executions.SaveAsync(Execution("fault", "definition", WorkflowExecutionStatus.Faulted, Now, "tenant-a"));
        await fixture.Incidents.TryAddAsync(Incident("incident", "fault", IncidentStatus.Open, Now));

        var row = await fixture.Context.IncidentStates.SingleAsync();
        row.Status = (int)IncidentStatus.Resolved;
        await fixture.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Attention.QueryAsync(new(
            new AttentionQueryContext(new ClaimsPrincipal(), "tenant-a"),
            5)).AsTask());
    }

    [Fact]
    public async Task Attention_fails_closed_when_an_authorized_execution_tenant_projection_is_missing()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Executions.SaveAsync(Execution("fault", "definition", WorkflowExecutionStatus.Faulted, Now, "tenant-a"));

        var row = await fixture.Context.WorkflowExecutionStates.SingleAsync();
        row.TenantId = null;
        row.TenantIdHash = null;
        await fixture.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Attention.QueryAsync(new(
            new AttentionQueryContext(new ClaimsPrincipal(), "tenant-a"),
            5)).AsTask());
    }

    [Fact]
    public void Operational_registration_owns_incidents_and_attention_and_is_order_independent()
    {
        var options = new RuntimeOperationalStateEntityFrameworkCoreOptions
        {
            ConnectionString = "Data Source=:memory:",
            RecoveryContinuationSigningKey = new string('k', 32)
        };
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        new WorkflowsRuntimeAttentionFeature().ConfigureServices(services);
        services.AddRuntimeOperationalStateEntityFrameworkCore(options);
        Assert.IsType<EfIncidentStateStore>(services.BuildServiceProvider().GetRequiredService<IIncidentStateStore>());
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowRuntimeAttentionQuery));
        Assert.Equal(RuntimeOperationalStateStoreBackend.EntityFramework, RuntimeOperationalStateStoreBackend.Find(services)!.Name);

        var reversed = new ServiceCollection();
        reversed.AddRuntimeOperationalStateEntityFrameworkCore(options);
        reversed.AddWorkflowRuntime();
        Assert.Contains(reversed, descriptor => descriptor.ServiceType == typeof(IIncidentStateStore));
        Assert.Contains(reversed, descriptor => descriptor.ServiceType == typeof(IWorkflowRuntimeAttentionQuery));
    }

    [Fact]
    public void Operational_registration_refuses_unowned_attention_adapter()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowRuntimeAttentionQuery, UnavailableWorkflowRuntimeAttentionQuery>();
        var snapshot = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeOperationalStateEntityFrameworkCore(new()));
        Assert.Equal(snapshot, services);
    }

    [Fact]
    public void Operational_attention_adapter_is_available_only_with_ef_workflow_execution_state()
    {
        var options = new RuntimeOperationalStateEntityFrameworkCoreOptions
        {
            ConnectionString = "Data Source=:memory:",
            RecoveryContinuationSigningKey = new string('k', 32)
        };
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        new WorkflowsRuntimeAttentionFeature().ConfigureServices(services);
        services.AddRuntimeWorkflowExecutionEntityFrameworkCore(new()
        {
            ConnectionString = options.ConnectionString,
            RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
        });
        services.AddRuntimeOperationalStateEntityFrameworkCore(options);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.IsType<EfWorkflowRuntimeAttentionQuery>(scope.ServiceProvider.GetRequiredService<IWorkflowRuntimeAttentionQuery>());
    }

    private static WorkflowExecutionState Execution(
        string id,
        string definitionId,
        WorkflowExecutionStatus status,
        DateTimeOffset timestamp,
        string tenantId) => new(
        id,
        new($"artifact-{id}", definitionId, "version-1", "1.0.0", "hash-1"),
        status,
        null,
        timestamp,
        timestamp,
        timestamp,
        status.IsTerminal() ? timestamp : null,
        null,
        null,
        tenantId,
        new Dictionary<string, string>());

    private static IncidentState Incident(
        string id,
        string workflowExecutionId,
        IncidentStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? resolvedAt = null,
        IncidentResolutionOutcome? outcome = null) => new(
        id,
        workflowExecutionId,
        null,
        null,
        IncidentSeverity.Critical,
        status,
        outcome,
        "TestFailure",
        "Sensitive failure detail",
        createdAt,
        resolvedAt);

    private sealed class TestDatabase(SqliteConnection connection, string connectionString) : IAsyncDisposable
    {
        public static async Task<TestDatabase> CreateAsync()
        {
            var connectionString = $"Data Source=file:elsa-ef-attention-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new(connection, connectionString);
        }

        public Fixture Open(string scope) => new(connectionString, scope);
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public readonly RuntimeSqliteDbContext Context;
        public readonly EfWorkflowExecutionStateStore Executions;
        public readonly EfIncidentStateStore Incidents;
        public readonly EfWorkflowRuntimeAttentionQuery Attention;

        public Fixture(string connectionString, string scope)
        {
            _connection = new SqliteConnection(connectionString);
            _connection.Open();
            Context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(_connection).Options);
            var accessor = new FixedAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(scope)));
            var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) }));
            Executions = new(Context, accessor, codec);
            Incidents = new(Context, accessor);
            Attention = new(Context, accessor, new FixedTimeProvider(Now));
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixedAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
