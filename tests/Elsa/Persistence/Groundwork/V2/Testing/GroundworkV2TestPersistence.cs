using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.Sqlite;
using Groundwork.Store;
using Groundwork.Testing;

namespace Elsa.Persistence.Groundwork.V2.Testing;

/// <summary>
/// A provider-backed public-v2 catalog for tests: it applies the storage units it is given and hands back
/// the session source a lane's storage expects.
/// <para>
/// The units are the caller's, not a fixed lane's, because the operations worth proving span catalogs — a
/// publication writes design rows, runtime material and a receipt in one transaction, so its test needs
/// all three declared against one provider, exactly as a single-target host has them.
/// </para>
/// <para>
/// Prefer this to a hand-written store double. The doubles this replaced accepted writes the real
/// providers reject, so suites built on them agreed with each other and not with a database.
/// </para>
/// </summary>
public sealed class GroundworkV2TestPersistence : IAsyncDisposable
{
    private readonly IReadOnlyList<StorageUnit> units;
    private readonly string? databasePath;
    private IStorageProviderConnection connection;

    private GroundworkV2TestPersistence(
        string provider,
        IReadOnlyList<StorageUnit> units,
        string? databasePath,
        IStorageProviderConnection connection)
    {
        Provider = provider;
        this.units = units;
        this.databasePath = databasePath;
        this.connection = connection;
        Sessions = new UnitSessionSource(connection, units);
    }

    public string Provider { get; }

    public IGroundworkStorageSessionSource Sessions { get; private set; }

    /// <summary>Opens a catalog over <paramref name="units"/>. "memory" is in-process; anything else is SQLite on disk.</summary>
    public static GroundworkV2TestPersistence Create(string provider, params IReadOnlyList<StorageUnit>[] units)
    {
        var all = units.SelectMany(x => x).ToArray();
        Assert(all);

        if (provider == "memory")
        {
            var memory = new InMemoryProviderFactory().Create($"elsa-v2-tests:{Guid.NewGuid():N}");
            ApplySchema(memory, all);
            return new GroundworkV2TestPersistence(provider, all, null, memory);
        }

        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-v2-tests-{Guid.NewGuid():N}.db");
        var sqlite = new SqliteProviderFactory().Create($"Data Source={databasePath};Pooling=False");
        ApplySchema(sqlite, all);
        return new GroundworkV2TestPersistence(provider, all, databasePath, sqlite);
    }

    /// <summary>
    /// Reopens the database, as a process restart would. Only meaningful on a file-backed provider: an
    /// in-memory catalog has nothing to survive.
    /// </summary>
    public void Restart()
    {
        if (databasePath is null)
            return;

        connection.Dispose();
        connection = new SqliteProviderFactory().Create($"Data Source={databasePath};Pooling=False");
        ApplySchema(connection, units);
        Sessions = new UnitSessionSource(connection, units);
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

    /// <summary>
    /// Two catalogs declaring the same unit id with different shapes is the collision a single-target host
    /// hits at composition. Failing here names the pair instead of leaving a provider to reject one write.
    /// </summary>
    private static void Assert(IReadOnlyList<StorageUnit> units)
    {
        var duplicate = units
            .GroupBy(unit => unit.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Select(unit => unit.Name).Distinct(StringComparer.Ordinal).Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Storage unit '{duplicate.Key}' was declared more than once with different physical tables " +
                $"({string.Join(", ", duplicate.Select(unit => unit.Name).Distinct(StringComparer.Ordinal))}). " +
                "Catalogs sharing one target must not collide on a unit id.");
        }
    }

    private static void ApplySchema(IStorageProviderConnection connection, IReadOnlyList<StorageUnit> units)
    {
        foreach (var unit in units.DistinctBy(unit => unit.Id.Value, StringComparer.Ordinal))
            connection.Schema.Apply(unit);
    }

    private sealed class UnitSessionSource(
        IStorageProviderConnection connection,
        IReadOnlyList<StorageUnit> units) : IGroundworkStorageSessionSource, IGroundworkStorageCapabilitySource
    {
        private readonly IReadOnlyDictionary<string, StorageUnit> byId =
            units.DistinctBy(unit => unit.Id.Value, StringComparer.Ordinal)
                .ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            connection.OpenSession(Resolve(unitId), access);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            connection.BeginUnitOfWork(access, options, unitIds.Select(Resolve).ToArray());

        public StorageUnit Unit(string unitId, string? targetName = null) => Resolve(unitId);

        // The lanes refuse to stage without an evidenced atomic commit, so report what the provider offers.
        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => connection.Capabilities;

        private StorageUnit Resolve(string unitId) => byId.TryGetValue(unitId, out var unit)
            ? unit
            : throw new InvalidOperationException(
                $"Storage unit '{unitId}' is not declared in this catalog. Pass the lane's units to Create.");
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
