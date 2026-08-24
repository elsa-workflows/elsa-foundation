using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Distributed.Services;
using Groundwork.Kernel;
using Groundwork.Sqlite;
using Groundwork.Store;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests;

internal sealed class DistributedStoreHarness : IAsyncDisposable
{
    public const string InMemory = "in-memory";
    public const string GroundworkSqlite = "groundwork-sqlite-v2";

    private readonly TemporarySqliteDatabase? database;
    private readonly List<IStorageProviderConnection> connections = [];
    private readonly string scope;
    private readonly DirectSessionSource? sessionSource;

    private DistributedStoreHarness(
        IExecutionPlacementStore placementStore,
        IExecutionCommandTransport transport,
        string scope,
        TemporarySqliteDatabase? database = null,
        DirectSessionSource? sessionSource = null)
    {
        PlacementStore = placementStore;
        Transport = transport;
        this.scope = scope;
        this.database = database;
        this.sessionSource = sessionSource;
    }

    public IExecutionPlacementStore PlacementStore { get; }
    public IExecutionCommandTransport Transport { get; }

    public static ValueTask<DistributedStoreHarness> CreateAsync(string provider, string scope = PersistenceScope.DefaultValue)
    {
        if (provider == InMemory)
        {
            return ValueTask.FromResult(new DistributedStoreHarness(
                new InMemoryExecutionPlacementStore(),
                new InMemoryExecutionCommandTransport(),
                scope));
        }

        if (provider != GroundworkSqlite)
            throw new ArgumentOutOfRangeException(nameof(provider), provider, null);

        var database = new TemporarySqliteDatabase();
        var connection = new SqliteProviderFactory().Create(database.ConnectionString);
        var source = CreateSource(connection);
        var harness = new DistributedStoreHarness(
            new GroundworkExecutionPlacementStore(source, Access(scope)),
            new GroundworkExecutionCommandTransport(source, Access(scope)),
            scope,
            database,
            source);
        harness.connections.Add(connection);
        return ValueTask.FromResult(harness);
    }

    public ValueTask<IExecutionCommandTransport> ReopenTransportAsync()
    {
        if (database is null)
            throw new NotSupportedException("The process-local in-memory transport has no durable restart boundary.");

        return ValueTask.FromResult<IExecutionCommandTransport>(
            new GroundworkExecutionCommandTransport(sessionSource!, Access(scope)));
    }

    public static WorkflowExecutionCommandEnvelope Envelope(
        string executionId,
        string envelopeId,
        DateTimeOffset now,
        string partition = PersistenceScope.DefaultValue) =>
        new(
            envelopeId,
            executionId,
            new WorkflowExecutionCommand(
                $"cmd-{envelopeId}",
                executionId,
                WorkflowExecutionCommandKind.RunSchedulerWork,
                now,
                null,
                new Dictionary<string, string>()),
            $"idem-{envelopeId}",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            now,
            partition: new WorkflowExecutionPartition(partition));

    public ValueTask DisposeAsync()
    {
        foreach (var connection in connections)
            connection.Dispose();
        database?.Dispose();
        return ValueTask.CompletedTask;
    }

    private static DirectSessionSource CreateSource(IStorageProviderConnection connection)
    {
        var units = DistributedGroundworkStorageManifest.CreateUnits();
        foreach (var unit in units)
            connection.Schema.Apply(unit);
        return new DirectSessionSource(connection, units);
    }

    internal static IPersistenceAccessContextAccessor Access(string scope) => new FixedAccessor(scope);

    internal sealed class DirectSessionSource(
        IStorageProviderConnection connection,
        IReadOnlyList<StorageUnit> units) : IGroundworkStorageSessionSource
    {
        private readonly Dictionary<string, StorageUnit> unitsById =
            units.ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            connection.OpenSession(unitsById[unitId], access);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            connection.BeginUnitOfWork(access, options, unitIds.Select(unitId => unitsById[unitId]).ToArray());

        public StorageUnit Unit(string unitId, string? targetName = null) => unitsById[unitId];
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } =
            PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class TemporarySqliteDatabase : IDisposable
    {
        private readonly string path = Path.Join(Path.GetTempPath(), $"elsa-distributed-v2-{Guid.NewGuid():N}.db");
        public string ConnectionString => $"Data Source={path}";

        public void Dispose()
        {
            foreach (var candidate in new[] { path, $"{path}-shm", $"{path}-wal" })
            {
                if (File.Exists(candidate))
                    File.Delete(candidate);
            }
        }
    }
}
