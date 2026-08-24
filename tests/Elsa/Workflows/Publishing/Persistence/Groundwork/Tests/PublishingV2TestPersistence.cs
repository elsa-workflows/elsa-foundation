using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.Sqlite;
using Groundwork.Store;
using Groundwork.Testing;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Tests;

internal sealed class PublishingV2TestPersistence : IAsyncDisposable
{
    private readonly string? databasePath;
    private IStorageProviderConnection connection;

    private PublishingV2TestPersistence(
        string provider,
        string? databasePath,
        IStorageProviderConnection connection)
    {
        Provider = provider;
        this.databasePath = databasePath;
        this.connection = connection;
        Sessions = new DirectSessionSource(connection);
    }

    public string Provider { get; }
    public DirectSessionSource Sessions { get; private set; }

    public static ValueTask<PublishingV2TestPersistence> CreateAsync(string provider)
    {
        var units = PublishingGroundworkStorageManifest.CreateUnits();
        if (provider == "memory")
        {
            var connection = new InMemoryProviderFactory().Create($"publishing-tests:{Guid.NewGuid():N}");
            ApplySchema(connection, units);
            return ValueTask.FromResult(new PublishingV2TestPersistence(provider, null, connection));
        }

        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-publishing-v2-{Guid.NewGuid():N}.db");
        var sqlite = new SqliteProviderFactory().Create($"Data Source={databasePath};Pooling=False");
        ApplySchema(sqlite, units);
        return ValueTask.FromResult(new PublishingV2TestPersistence(provider, databasePath, sqlite));
    }

    public void Restart()
    {
        if (databasePath is null)
            return;

        connection.Dispose();
        connection = new SqliteProviderFactory().Create($"Data Source={databasePath};Pooling=False");
        ApplySchema(connection, PublishingGroundworkStorageManifest.CreateUnits());
        Sessions = new DirectSessionSource(connection);
    }

    public IPersistenceAccessContextAccessor Access(string scope = "tenant-a") =>
        new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(scope)));

    public ValueTask DisposeAsync()
    {
        connection.Dispose();
        if (databasePath is not null)
        {
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
                if (File.Exists(path))
                    File.Delete(path);
        }

        return ValueTask.CompletedTask;
    }

    private static void ApplySchema(IStorageProviderConnection connection, IReadOnlyList<StorageUnit> units)
    {
        foreach (var unit in units)
            connection.Schema.Apply(unit);
    }

    internal sealed class DirectSessionSource(IStorageProviderConnection connection) : IGroundworkStorageSessionSource
    {
        private readonly IReadOnlyDictionary<string, StorageUnit> units =
            PublishingGroundworkStorageManifest.CreateUnits().ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            connection.OpenSession(Resolve(unitId), access);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            connection.BeginUnitOfWork(access, options, unitIds.Select(Resolve).ToArray());

        public StorageUnit Unit(string unitId, string? targetName = null) => Resolve(unitId);

        private StorageUnit Resolve(string unitId) => units[unitId];
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
