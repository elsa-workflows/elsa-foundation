using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Distributed;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests;

public sealed class GroundworkV2DistributedStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConcurrentPlacementClaimsHaveOneStorageCasWinner()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = new GroundworkExecutionPlacementStore(fixture.Sessions, fixture.Access());
        var second = new GroundworkExecutionPlacementStore(fixture.Sessions, fixture.Access());
        var claims = await Task.WhenAll(
            first.TryClaimAsync(new ExecutionPlacementClaim("execution-race", "node-a", Now, Now.AddMinutes(1)), Now).AsTask(),
            second.TryClaimAsync(new ExecutionPlacementClaim("execution-race", "node-b", Now, Now.AddMinutes(1)), Now).AsTask());

        Assert.Single(claims, claim => claim.Outcome == ExecutionPlacementClaimOutcome.Granted);
        Assert.Single(claims, claim => claim.Outcome == ExecutionPlacementClaimOutcome.Denied);
    }

    [Fact]
    public async Task ConcurrentExpiredPlacementTakeoversHaveOneStorageCasWinner()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Placement.TryClaimAsync(new ExecutionPlacementClaim("execution-race", "node-old", Now, Now.AddSeconds(1)), Now);
        var takeoverAt = Now.AddSeconds(2);
        var first = new GroundworkExecutionPlacementStore(fixture.Sessions, fixture.Access());
        var second = new GroundworkExecutionPlacementStore(fixture.Sessions, fixture.Access());

        var claims = await Task.WhenAll(
            first.TryClaimAsync(new ExecutionPlacementClaim("execution-race", "node-a", takeoverAt, takeoverAt.AddMinutes(1)), takeoverAt).AsTask(),
            second.TryClaimAsync(new ExecutionPlacementClaim("execution-race", "node-b", takeoverAt, takeoverAt.AddMinutes(1)), takeoverAt).AsTask());

        Assert.Single(claims, claim => claim.Outcome == ExecutionPlacementClaimOutcome.Granted);
        Assert.Single(claims, claim => claim.Outcome == ExecutionPlacementClaimOutcome.Denied);
    }

    [Fact]
    public async Task ScopedPlacementsWithTheSameIdentityRemainIsolated()
    {
        await using var fixture = await Fixture.CreateAsync();
        var tenantA = new GroundworkExecutionPlacementStore(fixture.Sessions, fixture.Access("tenant-a"));
        var tenantB = new GroundworkExecutionPlacementStore(fixture.Sessions, fixture.Access("tenant-b"));

        await tenantA.TryClaimAsync(new("execution-1", "node-a", Now, Now.AddMinutes(1)), Now);
        await tenantB.TryClaimAsync(new("execution-1", "node-b", Now, Now.AddMinutes(1)), Now);

        Assert.Equal("node-a", (await tenantA.FindAsync("execution-1"))!.OwnerId);
        Assert.Equal("node-b", (await tenantB.FindAsync("execution-1"))!.OwnerId);
    }

    [Fact]
    public async Task FailedTransportBatchRollsBackTheStreamHead()
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = fixture.Sessions.Open(
            DistributedGroundworkStorageManifest.CommandTransportUnitId,
            StorageAccess.Scoped(new StorageScope("tenant-a")));
        var duplicateId = "transport:execution-1:1";
        var duplicate = new StorageValues(new Dictionary<string, object?>
        {
            [DistributedGroundworkStorageManifest.TransportItemIdField] = duplicateId,
            [DistributedGroundworkStorageManifest.WorkflowExecutionIdField] = "execution-1",
            [DistributedGroundworkStorageManifest.SequenceField] = 1L,
            [DistributedGroundworkStorageManifest.EnqueuedAtField] = Now,
            [DistributedGroundworkStorageManifest.VisibleAtField] = Now,
            [DistributedGroundworkStorageManifest.LeaseOwnerIdField] = null,
            [DistributedGroundworkStorageManifest.LeaseTokenField] = 0L,
            [DistributedGroundworkStorageManifest.PayloadField] = "{}"
        });
        Assert.True(session.Insert(duplicate, WriteOptions.CreateOnly).Succeeded);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            fixture.Transport.SendAsync("execution-1", Envelope("execution-1", "envelope-1"), Now).AsTask());
        Assert.True(session.Delete(new StorageKey(new Dictionary<string, object?>
        {
            [DistributedGroundworkStorageManifest.TransportItemIdField] = duplicateId
        })).Succeeded);

        var written = await fixture.Transport.SendAsync("execution-1", Envelope("execution-1", "envelope-2"), Now);
        Assert.Equal(1, written.Sequence);
    }

    [Fact]
    public async Task SendRefusesAStreamHeadWhosePayloadBelongsToAnotherExecution()
    {
        await using var fixture = await Fixture.CreateAsync();
        var session = fixture.Sessions.Open(
            DistributedGroundworkStorageManifest.CommandStreamHeadUnitId,
            StorageAccess.Scoped(new StorageScope("tenant-a")));
        var values = new StorageValues(new Dictionary<string, object?>
        {
            [DistributedGroundworkStorageManifest.WorkflowExecutionIdField] = "execution-1",
            [DistributedGroundworkStorageManifest.LastSequenceField] = 3L,
            [DistributedGroundworkStorageManifest.PayloadField] = "{\"workflowExecutionId\":\"execution-other\",\"lastSequence\":3}"
        });
        Assert.True(session.Insert(values, WriteOptions.CreateOnly).Succeeded);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Transport.SendAsync("execution-1", Envelope("execution-1", "envelope-1"), Now).AsTask());

        Assert.Contains("belongs to workflow execution 'execution-other'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendRefusesACrossScopeEnvelopeBeforeOpeningProviderState()
    {
        await using var fixture = await Fixture.CreateAsync();
        var sessions = new CountingSessionSource(fixture.Sessions);
        var transport = new GroundworkExecutionCommandTransport(sessions, fixture.Access("tenant-a"));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            transport.SendAsync("execution-1", Envelope("execution-1", "envelope-1", "tenant-b"), Now).AsTask());

        Assert.Equal(0, sessions.OpenCount);
        Assert.Equal(0, sessions.BeginUnitOfWorkCount);
    }

    [Fact]
    public async Task ConcurrentCommandLeasesHaveOneStorageCasWinner()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Transport.SendAsync("execution-1", Envelope("execution-1", "envelope-1"), Now);
        var first = new GroundworkExecutionCommandTransport(fixture.Sessions, fixture.Access());
        var second = new GroundworkExecutionCommandTransport(fixture.Sessions, fixture.Access());

        var claims = await Task.WhenAll(
            first.LeaseAsync("execution-1", "node-a", Now, TimeSpan.FromMinutes(1), 1).AsTask(),
            second.LeaseAsync("execution-1", "node-b", Now, TimeSpan.FromMinutes(1), 1).AsTask());

        Assert.Equal(1, claims.Sum(items => items.Count));
    }

    [Fact]
    public void SchemaAndAssemblyUseFreshScopedV2UnitsOnly()
    {
        var units = DistributedGroundworkStorageManifest.CreateUnits();
        Assert.Equal(3, units.Count);
        Assert.All(units, unit =>
        {
            Assert.Equal(ScopePolicy.Scoped, unit.Scope);
            Assert.True(unit.Concurrency.IsOptimistic);
        });
        var transport = Assert.Single(units, unit => unit.Id.Value == DistributedGroundworkStorageManifest.CommandTransportUnitId);
        Assert.Equal(DistributedGroundworkStorageManifest.TransportItemIdMaximumLength, transport.Columns.Single(column => column.Name == DistributedGroundworkStorageManifest.TransportItemIdField).MaxLength);
        var pendingIndex = Assert.Single(transport.Indexes, index => index.Name == DistributedGroundworkStorageManifest.PendingCommandByExecutionSequenceIndex);
        Assert.Equal(
            [
                new IndexColumn(DistributedGroundworkStorageManifest.WorkflowExecutionIdField, SortDirection.Ascending),
                new IndexColumn(DistributedGroundworkStorageManifest.SequenceField, SortDirection.Descending)
            ],
            pendingIndex.Columns);
        var references = typeof(GroundworkExecutionCommandTransport).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name is "Groundwork.Core" or "Groundwork.Documents");
    }

    [Fact]
    public void RegistrationComposesOnlyV2SessionSourceAndScopedStores()
    {
        using var database = new TemporaryDatabase();
        using var connection = new SqliteProviderFactory().Create(database.ConnectionString);
        var services = new ServiceCollection()
            .AddGroundworkStorageProviderConnection(connection)
            .AddGroundworkDistributedRuntimeStores();
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IExecutionPlacementStore)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IExecutionCommandTransport)).Lifetime);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType.FullName?.Contains("GroundworkLane", StringComparison.Ordinal) == true);
        using var provider = services.BuildServiceProvider();
        Assert.True(provider.GetRequiredService<IWorkflowExecutionLeaseFencingCapability>().IsAvailable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RegistrationReplacesBothDistributedStoresInEitherFeatureOrder(bool distributedFeatureFirst)
    {
        using var database = new TemporaryDatabase();
        using var connection = new SqliteProviderFactory().Create(database.ConnectionString);
        var services = new ServiceCollection()
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IWorkflowExecutionCommandExecutor>(NoopWorkflowExecutionCommandExecutor.Instance)
            .AddGroundworkStorageProviderConnection(connection);
        var distributed = new WorkflowsRuntimeDistributedFeature();
        var persistence = new WorkflowsRuntimeDistributedGroundworkPersistenceFeature();

        if (distributedFeatureFirst)
        {
            distributed.ConfigureServices(services);
            persistence.ConfigureServices(services);
        }
        else
        {
            persistence.ConfigureServices(services);
            distributed.ConfigureServices(services);
        }

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        Assert.IsType<GroundworkExecutionPlacementStore>(scope.ServiceProvider.GetRequiredService<IExecutionPlacementStore>());
        Assert.IsType<GroundworkExecutionCommandTransport>(scope.ServiceProvider.GetRequiredService<IExecutionCommandTransport>());
        var evidence = scope.ServiceProvider.GetServices<IWorkflowDispatchDurabilityEvidence>()
            .Where(item => item.Component is WorkflowDispatchDurabilityComponents.Distribution or WorkflowDispatchDurabilityComponents.DistributionPersistence)
            .ToDictionary(item => item.Component, item => item.Level, StringComparer.Ordinal);
        Assert.Equal(WorkflowDispatchDurabilityLevel.ProcessLocal, evidence[WorkflowDispatchDurabilityComponents.Distribution]);
        Assert.Equal(WorkflowDispatchDurabilityLevel.Durable, evidence[WorkflowDispatchDurabilityComponents.DistributionPersistence]);
    }

    [Fact]
    public void RegistrationDoesNotClaimLeaseFencingWithoutTheProviderAtomicCommitDescriptor()
    {
        using var database = new TemporaryDatabase();
        using var inner = new SqliteProviderFactory().Create(database.ConnectionString);
        using var connection = new CapabilityFilteredConnection(inner);
        var services = new ServiceCollection()
            .AddGroundworkStorageProviderConnection(connection)
            .AddGroundworkDistributedRuntimeStores();

        using var provider = services.BuildServiceProvider();

        Assert.False(provider.GetRequiredService<IWorkflowExecutionLeaseFencingCapability>().IsAvailable);
    }

    private static WorkflowExecutionCommandEnvelope Envelope(string executionId, string envelopeId, string partition = "tenant-a") => new(
        envelopeId,
        executionId,
        new WorkflowExecutionCommand($"command-{envelopeId}", executionId, WorkflowExecutionCommandKind.Start, Now, null, new Dictionary<string, string>()),
        $"idempotency-{envelopeId}",
        WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
        Now,
        partition: new WorkflowExecutionPartition(partition));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TemporaryDatabase database;
        private readonly IStorageProviderConnection connection;
        public DirectSessionSource Sessions { get; }
        public IPersistenceAccessContextAccessor Access(string scope = "tenant-a") => new FixedAccessor(scope);
        public GroundworkExecutionPlacementStore Placement { get; }
        public GroundworkExecutionCommandTransport Transport { get; }

        private Fixture(TemporaryDatabase database, IStorageProviderConnection connection, DirectSessionSource sessions)
        {
            this.database = database;
            this.connection = connection;
            Sessions = sessions;
            Placement = new GroundworkExecutionPlacementStore(sessions, Access());
            Transport = new GroundworkExecutionCommandTransport(sessions, Access());
        }

        public static ValueTask<Fixture> CreateAsync()
        {
            var database = new TemporaryDatabase();
            var connection = new SqliteProviderFactory().Create(database.ConnectionString);
            var units = DistributedGroundworkStorageManifest.CreateUnits();
            foreach (var unit in units)
                connection.Schema.Apply(unit);
            return ValueTask.FromResult(new Fixture(database, connection, new DirectSessionSource(connection, units)));
        }

        public ValueTask DisposeAsync()
        {
            connection.Dispose();
            database.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DirectSessionSource(IStorageProviderConnection connection, IReadOnlyList<StorageUnit> units) : IGroundworkStorageSessionSource
    {
        private readonly Dictionary<string, StorageUnit> byId = units.ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) => connection.OpenSession(byId[unitId], access);
        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) => connection.BeginUnitOfWork(access, options, unitIds.Select(unitId => byId[unitId]).ToArray());
        public StorageUnit Unit(string unitId, string? targetName = null) => byId[unitId];
    }

    private sealed class CapabilityFilteredConnection(IStorageProviderConnection inner) : IStorageProviderConnection
    {
        public IProviderCatalog Catalog => inner.Catalog;
        public ISchemaCoordinator Schema => inner.Schema;
        public IReadOnlyList<CapabilityDescriptor> Capabilities => [];
        public IStorageSession OpenSession(StorageUnit unit, StorageAccess access) => inner.OpenSession(unit, access);
        public IUnitOfWork BeginUnitOfWork(StorageAccess access, params StorageUnit[] units) => inner.BeginUnitOfWork(access, units);
        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, params StorageUnit[] units) => inner.BeginUnitOfWork(access, options, units);
        public void Dispose() { }
    }

    private sealed class CountingSessionSource(IGroundworkStorageSessionSource inner) : IGroundworkStorageSessionSource
    {
        public int OpenCount { get; private set; }
        public int BeginUnitOfWorkCount { get; private set; }

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            OpenCount++;
            return inner.Open(unitId, access, targetName);
        }

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null)
        {
            BeginUnitOfWorkCount++;
            return inner.BeginUnitOfWork(access, options, unitIds, targetName);
        }

        public StorageUnit Unit(string unitId, string? targetName = null) => inner.Unit(unitId, targetName);
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string path = Path.Join(Path.GetTempPath(), $"elsa-distributed-v2-{Guid.NewGuid():N}.db");
        public string ConnectionString => $"Data Source={path}";
        public void Dispose()
        {
            foreach (var candidate in new[] { path, $"{path}-shm", $"{path}-wal" })
                if (File.Exists(candidate))
                    File.Delete(candidate);
        }
    }
}
