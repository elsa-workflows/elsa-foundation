using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.Sqlite;
using Groundwork.Store;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;

/// <summary>
/// Shared SQLite-backed public Groundwork v2 fixture for the legacy Identity test project.
/// Every consumer admits the same fresh 17-unit catalog and uses explicit scope-bound sessions.
/// </summary>
internal sealed class IdentityV2TestPersistence : IGroundworkStorageSessionSource, IDisposable
{
    private readonly Lock sessionGate = new();
    private readonly Dictionary<(string UnitId, StorageAccess Access), IStorageSession> sessions = [];
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"elsa-identity-v2-{Guid.NewGuid():N}.db");
    private readonly IReadOnlyDictionary<string, StorageUnit> units = IdentityV2StorageManifest.CreateUnits()
        .ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);

    public IdentityV2TestPersistence()
    {
        Connection = new SqliteProviderFactory().Create($"Data Source={databasePath}");
        foreach (var unit in units.Values)
            Connection.Schema.Apply(unit);
    }

    public IStorageProviderConnection Connection { get; }

    public GroundworkIdentityRowStore Rows(IPersistenceAccessContextAccessor access) => new(this, access);

    public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
    {
        var key = (unitId, access);
        lock (sessionGate)
        {
            if (sessions.TryGetValue(key, out var session))
                return session;

            session = Connection.OpenSession(Unit(unitId, targetName), access);
            sessions.Add(key, session);
            return session;
        }
    }

    public IUnitOfWork BeginUnitOfWork(
        StorageAccess access,
        BatchWriteOptions options,
        IReadOnlyList<string> unitIds,
        string? targetName = null) =>
        Connection.BeginUnitOfWork(access, options, unitIds.Select(id => Unit(id, targetName)).ToArray());

    public StorageUnit Unit(string unitId, string? targetName = null) => units[unitId];

    public void Dispose()
    {
        Connection.Dispose();
        if (File.Exists(databasePath))
            File.Delete(databasePath);
    }
}
