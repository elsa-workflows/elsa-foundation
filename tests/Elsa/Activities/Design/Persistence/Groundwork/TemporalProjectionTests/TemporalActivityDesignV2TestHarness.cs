using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Groundwork.Kernel;
using Groundwork.Sqlite;
using Groundwork.Store;

namespace Elsa.Activities.Design.Persistence.Groundwork.TemporalProjectionTests;

/// <summary>Provider-backed public-v2 fixture for activity-management projection behavior.</summary>
internal sealed class TemporalActivityDesignV2TestHarness : IDisposable
{
    private readonly string databasePath;
    private readonly IStorageProviderConnection connection;

    private TemporalActivityDesignV2TestHarness(
        string databasePath,
        IStorageProviderConnection connection,
        TemporalActivityDesignAccess access,
        GroundworkV2ActivityDesignStore store,
        GroundworkPrivilegedQueryAuditSink auditSink)
    {
        this.databasePath = databasePath;
        this.connection = connection;
        Access = access;
        Store = store;
        AuditSink = auditSink;
    }

    public TemporalActivityDesignAccess Access { get; }

    public GroundworkV2ActivityDesignStore Store { get; }

    public GroundworkPrivilegedQueryAuditSink AuditSink { get; }

    public GroundworkActivityDefinitionManagementProjectionStore Reader =>
        new(Store, Store, Access);

    public GroundworkActivityManagementProjectionWriter Writer { get; private set; } = null!;

    public static TemporalActivityDesignV2TestHarness Create(string scope = "tenant-a")
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-activity-temporal-v2-tests-{Guid.NewGuid():N}.db");
        var connection = new SqliteProviderFactory().Create($"Data Source={databasePath}");
        var units = ActivitiesDesignStorageManifest.CreateUnits()
            .ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);
        foreach (var unit in units.Values)
            connection.Schema.Apply(unit);

        var access = new TemporalActivityDesignAccess(
            PersistenceAccessContext.Scoped(new PersistenceScope(scope)));
        var sessions = new TemporalActivityDesignSessionSource(connection, units);
        var auditSink = new GroundworkPrivilegedQueryAuditSink();
        var auditExecutor = new GroundworkPrivilegedQueryAuditExecutor(sessions, access, auditSink);
        var store = new GroundworkV2ActivityDesignStore(
            sessions,
            access,
            privilegedQueryAuditExecutor: auditExecutor);
        var harness = new TemporalActivityDesignV2TestHarness(databasePath, connection, access, store, auditSink);
        harness.Writer = new GroundworkActivityManagementProjectionWriter(
            store,
            new ImmediateDistributedLockProvider(),
            store);
        return harness;
    }

    public void Dispose()
    {
        connection.Dispose();
        if (File.Exists(databasePath))
            File.Delete(databasePath);
    }

    private sealed class TemporalActivityDesignSessionSource(
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
}

internal sealed class TemporalActivityDesignAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
{
    public PersistenceAccessContext Current { get; set; } = current;
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
