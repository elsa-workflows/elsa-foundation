using System.Security.Claims;
using Elsa.Attention.Core;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Store;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

[Collection(GroundworkV2NativeProviderMatrixCollection.Name)]
public sealed class GroundworkV2WorkflowRuntimeAttentionQueryTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);
    private readonly string database = Path.Combine(
        Path.GetTempPath(),
        $"elsa-v2-runtime-attention-{Guid.NewGuid():N}.db");
    private readonly IStorageProviderConnection connection;
    private readonly DirectSessionSource source;

    public GroundworkV2WorkflowRuntimeAttentionQueryTests()
    {
        connection = new SqliteProviderFactory().Create($"Data Source={database}");
        foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
            connection.Schema.Apply(unit);
        source = new(connection);
    }

    [Fact]
    public async Task Sqlite_query_returns_active_incidents_and_uncovered_faults_in_contract_order()
    {
        var tenant = Store("tenant-a");
        await tenant.SaveAsync(Execution("faulted", "definition-faulted", WorkflowExecutionStatus.Faulted, Now.AddMinutes(-3), "tenant-a"));
        await tenant.SaveAsync(Execution("blocked", "definition-blocked", WorkflowExecutionStatus.Running, Now.AddMinutes(-2), "tenant-a"));
        await tenant.SaveAsync(Execution("open", "definition-open", WorkflowExecutionStatus.Faulted, Now.AddMinutes(-1), "tenant-a"));
        await Store("tenant-b").SaveAsync(Execution("foreign", "definition-foreign", WorkflowExecutionStatus.Faulted, Now, "tenant-b"));
        await tenant.SaveAsync(Execution("healthy", "definition-healthy", WorkflowExecutionStatus.Completed, Now, "tenant-a"));

        var incidents = IncidentStore("tenant-a");
        await incidents.TryAddAsync(Incident("incident-blocked", "blocked", IncidentStatus.Blocking, Now.AddMinutes(-2)));
        await incidents.TryAddAsync(Incident("incident-open", "open", IncidentStatus.Open, Now.AddMinutes(-1)));
        await incidents.TryAddAsync(Incident("incident-resolved", "blocked", IncidentStatus.Resolved, Now));
        await incidents.TryAddAsync(Incident("incident-orphan", "missing", IncidentStatus.Blocking, Now));

        var result = await Query("tenant-a").QueryAsync(Request(10));

        Assert.True(result.IsAvailable);
        Assert.Equal(3, result.TotalCount);
        Assert.Collection(
            result.Records,
            record =>
            {
                Assert.Equal("blocked", record.WorkflowExecutionId);
                Assert.Equal("incident-blocked", record.IncidentId);
                Assert.Equal(WorkflowRuntimeAttentionKind.BlockingIncident, record.Kind);
            },
            record =>
            {
                Assert.Equal("faulted", record.WorkflowExecutionId);
                Assert.Null(record.IncidentId);
                Assert.Equal(WorkflowRuntimeAttentionKind.FaultedExecution, record.Kind);
            },
            record =>
            {
                Assert.Equal("open", record.WorkflowExecutionId);
                Assert.Equal("incident-open", record.IncidentId);
                Assert.Equal(WorkflowRuntimeAttentionKind.OpenIncident, record.Kind);
            });
        Assert.All(result.Records, record => Assert.Null(record.SanitizedSummary));
    }

    [Fact]
    public async Task Sqlite_query_is_bounded_deterministic_and_scope_isolated()
    {
        var tenantA = Store("tenant-a");
        var tenantB = Store("tenant-b");
        foreach (var id in new[] { "fault-b", "fault-a", "fault-c" })
            await tenantA.SaveAsync(Execution(id, "definition", WorkflowExecutionStatus.Faulted, Now, "tenant-a"));
        await tenantB.SaveAsync(Execution("foreign", "definition", WorkflowExecutionStatus.Faulted, Now, "tenant-b"));

        var first = await Query("tenant-a").QueryAsync(Request(2));
        var second = await Query("tenant-a").QueryAsync(Request(2));

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(["fault-a", "fault-b"], first.Records.Select(record => record.WorkflowExecutionId));
        Assert.Equal(first.Records, second.Records);
        Assert.DoesNotContain(first.Records, record => record.WorkflowExecutionId == "foreign");
    }

    [Fact]
    public async Task Sqlite_query_traverses_multiple_provider_pages_and_keeps_exact_total()
    {
        var tenant = Store("tenant-a");
        for (var index = 0; index <= RuntimeStorePageRequest.MaximumLimit; index++)
        {
            await tenant.SaveAsync(Execution(
                $"fault-{index:D4}",
                "definition-fault",
                WorkflowExecutionStatus.Faulted,
                Now,
                "tenant-a"));
        }

        var result = await Query("tenant-a").QueryAsync(Request(3));

        Assert.Equal(RuntimeStorePageRequest.MaximumLimit + 1, result.TotalCount);
        Assert.Equal(
            ["fault-0000", "fault-0001", "fault-0002"],
            result.Records.Select(record => record.WorkflowExecutionId));
    }

    [Fact]
    public async Task Query_refuses_missing_global_across_scope_or_mismatched_tenant_before_provider_io()
    {
        var before = source.OpenCount;
        var missingTenant = new GroundworkV2WorkflowRuntimeAttentionQuery(
            source,
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))),
            new FixedTimeProvider(Now));
        var unavailable = await missingTenant.QueryAsync(
            new(new AttentionQueryContext(new ClaimsPrincipal(), null), 5));
        Assert.False(unavailable.IsAvailable);
        Assert.Equal("RUNTIME_ATTENTION_TENANT_REQUIRED", unavailable.ErrorCode);
        Assert.Equal(before, source.OpenCount);

        var global = Query(PersistenceAccessContext.Global);
        await Assert.ThrowsAsync<InvalidOperationException>(() => global.QueryAsync(Request(5)).AsTask());

        var acrossScopes = Query(PersistenceAccessContext.PrivilegedAcrossScopes(
            new PersistenceAccessPurpose("runtime-attention-audit")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => acrossScopes.QueryAsync(Request(5)).AsTask());

        var wrongTenant = Query(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => wrongTenant.QueryAsync(Request(5)).AsTask());
        Assert.Equal(before, source.OpenCount);
    }

    [SkippableTheory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("mongodb")]
    public async Task Configured_native_provider_preserves_attention_scope_and_order(string providerName)
    {
        var variable = $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING";
        var connectionString = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (Environment.GetEnvironmentVariable("GROUNDWORK_V2_REQUIRE_NATIVE_PROVIDER_MATRIX") == "1")
                throw new XunitException($"{variable} must be set when the native provider matrix is required.");
            Skip.If(true, $"Set {variable} to run the {providerName} attention-query gate.");
        }

        using var nativeConnection = CreateConnection(providerName, connectionString!);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var units = ElsaRuntimeV2StorageManifest.CreateUnits()
            .Select(unit => unit with
            {
                Id = new StorageUnitId($"{unit.Id.Value}-{suffix}"),
                Name = $"{unit.Name}_{suffix}"
            })
            .ToDictionary(
                unit => unit.Id.Value[..unit.Id.Value.LastIndexOf('-')],
                StringComparer.Ordinal);
        foreach (var unit in units.Values)
            nativeConnection.Schema.Apply(unit);

        var nativeSource = new DirectSessionSource(nativeConnection, units);
        var store = new GroundworkV2WorkflowExecutionStateStore(nativeSource, Access("tenant-a"));
        var incidents = new GroundworkV2IncidentStateStore(nativeSource, Access("tenant-a"));
        await store.SaveAsync(Execution("faulted", "definition-faulted", WorkflowExecutionStatus.Faulted, Now.AddMinutes(-2), "tenant-a"));
        await store.SaveAsync(Execution("blocked", "definition-blocked", WorkflowExecutionStatus.Running, Now, "tenant-a"));
        await incidents.TryAddAsync(Incident("incident-blocked", "blocked", IncidentStatus.Blocking, Now));
        await new GroundworkV2WorkflowExecutionStateStore(nativeSource, Access("tenant-b")).SaveAsync(
            Execution("foreign", "definition-foreign", WorkflowExecutionStatus.Faulted, Now, "tenant-b"));

        var result = await new GroundworkV2WorkflowRuntimeAttentionQuery(
            nativeSource,
            Access("tenant-a"),
            new FixedTimeProvider(Now)).QueryAsync(Request(5));

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(
            [WorkflowRuntimeAttentionKind.BlockingIncident, WorkflowRuntimeAttentionKind.FaultedExecution],
            result.Records.Select(record => record.Kind));
        Assert.DoesNotContain(result.Records, record => record.WorkflowExecutionId == "foreign");
    }

    [Fact]
    public void Explicit_v2_registration_declares_runtime_attention_units_and_replaces_fallback()
    {
        var services = new ServiceCollection();
        services.AddScoped<IWorkflowRuntimeAttentionQuery, UnavailableWorkflowRuntimeAttentionQuery>();

        services.AddGroundworkV2WorkflowRuntimeAttention("runtime");

        var registry = Assert.IsType<GroundworkStorageUnitRegistry>(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);
        Assert.Equal(
            [
                ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind,
                ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind
            ],
            registry.Registrations.Select(registration => registration.Unit.Id.Value));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IWorkflowRuntimeAttentionQuery) &&
                          descriptor.Lifetime == ServiceLifetime.Scoped &&
                          descriptor.ImplementationFactory is not null);
    }

    private GroundworkV2WorkflowRuntimeAttentionQuery Query(string tenant) => Query(
        PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));

    private GroundworkV2WorkflowRuntimeAttentionQuery Query(PersistenceAccessContext context) =>
        new(source, new FixedAccessContextAccessor(context), new FixedTimeProvider(Now));

    private GroundworkV2WorkflowExecutionStateStore Store(string tenant) =>
        new(source, new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(tenant))));

    private GroundworkV2IncidentStateStore IncidentStore(string tenant) =>
        new(source, new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(tenant))));

    private WorkflowRuntimeAttentionQuery Request(int maximumItems) =>
        new(new AttentionQueryContext(new ClaimsPrincipal(), "tenant-a"), maximumItems);

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
        DateTimeOffset createdAt) => new(
        id,
        workflowExecutionId,
        null,
        null,
        IncidentSeverity.Critical,
        status,
        status == IncidentStatus.Resolved
            ? new IncidentResolutionOutcome("test.resolve", createdAt, null, "test")
            : null,
        "TestFailure",
        "Sensitive failure detail",
        createdAt,
        status == IncidentStatus.Resolved ? createdAt : null);

    private static IPersistenceAccessContextAccessor Access(string tenant) =>
        new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));

    private static IStorageProviderConnection CreateConnection(string providerName, string connectionString) =>
        providerName switch
        {
            "postgresql" => new PostgreSqlProviderFactory().Create(connectionString),
            "sqlserver" => new SqlServerProviderFactory().Create(connectionString),
            "mongodb" => new MongoProviderFactory().Create(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
        };

    public ValueTask DisposeAsync()
    {
        connection.Dispose();
        foreach (var path in new[] { database, $"{database}-shm", $"{database}-wal", $"{database}-journal", $"{database}.schema.lock" })
            if (File.Exists(path))
                File.Delete(path);
        return ValueTask.CompletedTask;
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext context) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = context;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class DirectSessionSource(
        IStorageProviderConnection connection,
        IReadOnlyDictionary<string, StorageUnit>? units = null) : IGroundworkStorageSessionSource
    {
        public int OpenCount { get; private set; }

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            OpenCount++;
            return connection.OpenSession(Unit(unitId, targetName), access);
        }

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            if (units is not null)
            {
                if (units.TryGetValue(unitId, out var logicalUnit))
                    return logicalUnit;
                var physicalUnit = units.Values.SingleOrDefault(candidate =>
                    StringComparer.Ordinal.Equals(candidate.Id.Value, unitId));
                if (physicalUnit is not null)
                    return physicalUnit;
            }

            return ElsaRuntimeV2StorageManifest.Require(unitId);
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();
    }
}
