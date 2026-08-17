using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.Sqlite;
using Groundwork.Store;
using Elsa.Locking.Core;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

/// <summary>Small provider-backed fixture shared by the ported activity-design behavior suites.</summary>
internal sealed class ActivityDesignV2TestHarness : IDisposable
{
    private readonly string databasePath;
    private readonly IStorageProviderConnection connection;

    private ActivityDesignV2TestHarness(
        string databasePath,
        IStorageProviderConnection connection,
        MutableActivityDesignAccess access,
        GroundworkV2ActivityDesignStore store)
    {
        this.databasePath = databasePath;
        this.connection = connection;
        Access = access;
        Store = store;
    }

    public MutableActivityDesignAccess Access { get; }
    public GroundworkV2ActivityDesignStore Store { get; }

    public static ActivityDesignV2TestHarness Create(string scope = "tenant-a")
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-activity-design-v2-tests-{Guid.NewGuid():N}.db");
        var connection = new SqliteProviderFactory().Create($"Data Source={databasePath}");
        var units = ActivitiesDesignStorageManifest.CreateUnits()
            .ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);
        foreach (var unit in units.Values)
            connection.Schema.Apply(unit);

        var access = new MutableActivityDesignAccess(
            PersistenceAccessContext.Scoped(new PersistenceScope(scope)));
        var sessions = new DirectActivityDesignSessionSource(connection, units);
        return new(databasePath, connection, access, new GroundworkV2ActivityDesignStore(sessions, access));
    }

    public async Task SaveAsync<TEntity>(
        string documentKind,
        string collection,
        TEntity entity,
        JsonSerializerOptions jsonOptions,
        CancellationToken cancellationToken = default)
        where TEntity : Elsa.Primitives.Entities.Entity
    {
        var request = GroundworkV2ActivityDesignDocumentWriter.ToSaveRequest(
            documentKind,
            collection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            entity,
            jsonOptions);
        await Store.SaveAsync(request, cancellationToken);
    }

    public IReadOnlyList<ActivityDesignDocument> Rows(string documentKind, bool acrossScopes = false) =>
        Store.Query(new ActivityDesignQuery(
            documentKind,
            "test-rows",
            [],
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
            Take: 1000), acrossScopes).Documents;

    public void Dispose()
    {
        connection.Dispose();
        if (File.Exists(databasePath))
            File.Delete(databasePath);
    }
}

internal sealed class MutableActivityDesignAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
{
    public PersistenceAccessContext Current { get; set; } = current;
}

internal sealed class DirectActivityDesignSessionSource(
    IStorageProviderConnection connection,
    IReadOnlyDictionary<string, StorageUnit> units) : IGroundworkStorageSessionSource
{
    public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
        connection.OpenSession(Unit(unitId), access);

    public IUnitOfWork BeginUnitOfWork(
        StorageAccess access,
        BatchWriteOptions options,
        IReadOnlyList<string> unitIds,
        string? targetName = null) =>
        connection.BeginUnitOfWork(access, options, unitIds.Select(id => Unit(id)).ToArray());

    public StorageUnit Unit(string unitId, string? targetName = null) => units[unitId];
}

internal sealed class ImmediateDistributedLockProvider : IDistributedLockProvider
{
    public IDistributedSynchronizationHandle? TryAcquireLock(
        string name,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) => new Handle();

    public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(
        string name,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());

    public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(
        string name,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

    private sealed class Handle : IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => CancellationToken.None;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
