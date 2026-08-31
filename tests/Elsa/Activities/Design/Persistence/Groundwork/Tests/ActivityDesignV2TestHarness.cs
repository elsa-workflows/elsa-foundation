using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.Sqlite;
using Groundwork.Store;
using Elsa.Locking.Core;
using Groundwork.Query.Model;

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
        GroundworkV2ActivityDesignStore store,
        GroundworkPrivilegedQueryAuditSink auditSink,
        List<QueryRequest> queryRequests)
    {
        this.databasePath = databasePath;
        this.connection = connection;
        Access = access;
        Store = store;
        AuditSink = auditSink;
        QueryRequests = queryRequests;
    }

    public MutableActivityDesignAccess Access { get; }
    public GroundworkV2ActivityDesignStore Store { get; }
    public GroundworkPrivilegedQueryAuditSink AuditSink { get; }
    public List<QueryRequest> QueryRequests { get; }
    public IStorageProviderConnection Connection => connection;

    public static ActivityDesignV2TestHarness Create(string scope = "tenant-a", List<QueryRequest>? queryRequests = null)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-activity-design-v2-tests-{Guid.NewGuid():N}.db");
        var connection = new SqliteProviderFactory().Create($"Data Source={databasePath}");
        var units = ActivitiesDesignStorageManifest.CreateUnits()
            .ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);
        foreach (var unit in units.Values)
            connection.Schema.Apply(unit);

        var access = new MutableActivityDesignAccess(
            PersistenceAccessContext.Scoped(new PersistenceScope(scope)));
        var recordedQueries = queryRequests ?? [];
        var sessions = new DirectActivityDesignSessionSource(connection, units, recordedQueries);
        var auditSink = new GroundworkPrivilegedQueryAuditSink();
        var auditExecutor = new GroundworkPrivilegedQueryAuditExecutor(sessions, access, auditSink);
        var store = new GroundworkV2ActivityDesignStore(
            sessions,
            access,
            privilegedQueryAuditExecutor: auditExecutor);
        return new(databasePath, connection, access, store, auditSink, recordedQueries);
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
            ActivitiesDesignStorageManifest.ListAllDocumentsQuery,
            [],
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
            Take: ActivityDesignQueryPager.PageSize), acrossScopes).Documents;

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
    IReadOnlyDictionary<string, StorageUnit> units,
    ICollection<QueryRequest>? queryRequests = null) : IGroundworkStorageSessionSource
{
    public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
    {
        var session = connection.OpenSession(Unit(unitId), access);
        return queryRequests is null ? session : new RecordingActivityDesignSession(session, queryRequests);
    }

    public IUnitOfWork BeginUnitOfWork(
        StorageAccess access,
        BatchWriteOptions options,
        IReadOnlyList<string> unitIds,
        string? targetName = null) =>
        connection.BeginUnitOfWork(access, options, unitIds.Select(id => Unit(id)).ToArray());

    public StorageUnit Unit(string unitId, string? targetName = null) => units[unitId];
}

internal sealed class RecordingActivityDesignSession(
    IStorageSession inner,
    ICollection<QueryRequest> requests) : SynchronousStorageSessionTestDouble, IStorageSession, IPrivilegedCrossScopeQuerySession
{
    public StorageUnit Unit => inner.Unit;
    public StorageAccess Access => inner.Access;
    public StoredEntry? Read(StorageKey key) => inner.Read(key);
    public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
    {
        requests.Add(request);
        return inner.Query(request, options);
    }

    public CrossScopeQueryResult QueryAcrossScopes(QueryRequest request, QueryRenderOptions? options = null)
    {
        requests.Add(request);
        return ((IPrivilegedCrossScopeQuerySession)inner).QueryAcrossScopes(request, options);
    }

    public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
    public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
    public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
    public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
    public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
    public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);
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
