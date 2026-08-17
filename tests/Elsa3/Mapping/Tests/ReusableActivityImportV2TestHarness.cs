using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa3.Activities.Design.Import.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;
using Groundwork.Sqlite;

namespace Elsa3.Mapping.Tests;

/// <summary>Provider-backed v2 fixture for the reusable-import tests using the real design manifests.</summary>
internal sealed class ReusableActivityImportV2TestHarness : IDisposable
{
    private readonly string databasePath;
    private readonly IStorageProviderConnection connection;
    private readonly IReadOnlyDictionary<string, StorageUnit> units;

    private ReusableActivityImportV2TestHarness(
        string databasePath,
        IStorageProviderConnection connection,
        IReadOnlyDictionary<string, StorageUnit> units,
        MutableImportAccess access,
        GroundworkV2ActivityDesignStore store,
        GroundworkDesignStorage workflowStorage,
        ImportSessionSource sessions)
    {
        this.databasePath = databasePath;
        this.connection = connection;
        this.units = units;
        Access = access;
        Store = store;
        WorkflowStorage = workflowStorage;
        Sessions = sessions;
    }

    public MutableImportAccess Access { get; }
    public GroundworkV2ActivityDesignStore Store { get; }
    public GroundworkDesignStorage WorkflowStorage { get; }
    public ImportSessionSource Sessions { get; }
    public int SaveCount { get; private set; }

    public static ReusableActivityImportV2TestHarness Create(string scope = "tenant-a")
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-reusable-import-v2-tests-{Guid.NewGuid():N}.db");
        var connection = new SqliteProviderFactory().Create($"Data Source={databasePath}");
        var registry = new GroundworkStorageUnitRegistry();
        // The existing activity and workflow features currently publish different
        // physical declarations for the shared designOperation identity. The
        // reusable-import write set never stages that ledger unit, so keep this
        // proof limited to the real units that the command reads/writes until the
        // two upstream manifests converge on one public declaration.
        foreach (var unit in ActivitiesDesignStorageManifest.CreateUnits()
            .Where(unit => !StringComparer.Ordinal.Equals(
                unit.Id.Value,
                ActivitiesDesignStorageManifest.DesignOperationDocumentKind))
            .Concat(Elsa3ImportStorageManifest.CreateUnits())
            .Concat(WorkflowsDesignStorageManifest.CreateUnits().Where(unit => !StringComparer.Ordinal.Equals(
                unit.Id.Value,
                WorkflowsDesignStorageManifest.DesignOperationDocumentKind))))
            registry.Declare(unit);
        var units = registry.Registrations
            .Select(registration => registration.Unit)
            .ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);
        foreach (var unit in units.Values)
            connection.Schema.Apply(unit);

        var access = new MutableImportAccess(PersistenceAccessContext.Scoped(new PersistenceScope(scope)));
        var sessions = new ImportSessionSource(connection, units);
        var store = new GroundworkV2ActivityDesignStore(sessions, access);
        var workflowStorage = new GroundworkDesignStorage(sessions, access);
        return new(databasePath, connection, units, access, store, workflowStorage, sessions);
    }

    public IReadOnlyList<StorageValues> Snapshot(string documentKind)
    {
        if (WorkflowsDesignStorageManifest.CreateUnits().Any(unit => StringComparer.Ordinal.Equals(unit.Id.Value, documentKind)))
        {
            var unit = units[documentKind];
            var access = GroundworkStorageAccessMapper.Map(Access.Current, unit.Scope, "elsa-reusable-import-tests");
            var table = new TableId(unit.Name);
            var id = new ColumnRef(table, WorkflowsDesignStorageManifest.IdField, QueryType.String, false, 450);
            return connection.OpenSession(unit, access)
                .Query(new QueryRequest(
                    table,
                    new Predicate.AlwaysTrue(),
                    [new OrderTerm(id, OrderDirection.Ascending, NullOrder.Last)],
                    Projection.All,
                    Paging.Keyset(1024)))
                .Rows.Select(row => new StorageValues(row)).ToArray();
        }

        return Store.Query(new ActivityDesignQuery(
            documentKind,
            ActivitiesDesignStorageManifest.ListAllDocumentsQuery,
            [],
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
            Take: ActivityDesignQueryPager.PageSize)).Documents
            .Select(document => new StorageValues(new Dictionary<string, object?>
            {
                [ActivitiesDesignStorageManifest.IdField] = document.Id,
                [ActivitiesDesignStorageManifest.SchemaVersionField] = document.SchemaVersion,
                [ActivitiesDesignStorageManifest.ContentField] = document.ContentJson,
                [ActivitiesDesignStorageManifest.RevisionField] = document.Version,
                [ActivitiesDesignStorageManifest.UpdatedAtField] = document.UpdatedAt
            }))
            .ToArray();
    }

    /// <summary>Raw row insertion is used only to retain malformed-document coverage.</summary>
    public void InsertRaw(string documentKind, string id, string contentJson, string tenantId = "tenant-a")
    {
        var unit = units[documentKind];
        var session = connection.OpenSession(unit, GroundworkStorageAccessMapper.Map(
            PersistenceAccessContext.Scoped(new PersistenceScope(tenantId)), unit.Scope, "elsa-reusable-import-tests"));
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ActivitiesDesignStorageManifest.IdField] = id,
            [ActivitiesDesignStorageManifest.SchemaVersionField] = Elsa3ImportStorageManifest.SchemaVersion,
            [ActivitiesDesignStorageManifest.ContentField] = contentJson,
            [ActivitiesDesignStorageManifest.RevisionField] = 1L,
            [ActivitiesDesignStorageManifest.UpdatedAtField] = DateTimeOffset.UtcNow,
            [ActivitiesDesignStorageManifest.ScopeField] = tenantId,
            [ActivitiesDesignStorageManifest.TenantIdField] = tenantId,
            [ActivitiesDesignStorageManifest.ManagementSearchField] = string.Empty
        };
        session.Insert(new StorageValues(values), WriteOptions.CreateOnly);
    }

    public void Dispose()
    {
        connection.Dispose();
        if (File.Exists(databasePath))
            File.Delete(databasePath);
    }

}

internal sealed class MutableImportAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
{
    public PersistenceAccessContext Current { get; set; } = current;
}

internal sealed class ImportSessionSource(
    IStorageProviderConnection connection,
    IReadOnlyDictionary<string, StorageUnit> units) : IGroundworkStorageSessionSource
{
    public bool ThrowOnRead { get; set; }
    public bool ThrowOnCommit { get; set; }
    public string? FailOnUnit { get; set; }
    public List<IReadOnlyList<string>> BegunUnitIds { get; } = [];

    public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
        new ImportStorageSession(connection.OpenSession(Unit(unitId), access), () => ThrowOnRead);

    public IUnitOfWork BeginUnitOfWork(
        StorageAccess access,
        BatchWriteOptions options,
        IReadOnlyList<string> unitIds,
        string? targetName = null)
    {
        BegunUnitIds.Add(unitIds.ToArray());
        return new ImportUnitOfWork(
            connection.BeginUnitOfWork(access, options, unitIds.Select(id => Unit(id)).ToArray()),
            unitIds,
            () => ThrowOnCommit,
            () => FailOnUnit);
    }

    public StorageUnit Unit(string unitId, string? targetName = null) => units[unitId];
}

internal sealed class ImportStorageSession(IStorageSession inner, Func<bool> throwOnRead) : IStorageSession
{
    public StorageUnit Unit => inner.Unit;
    public StorageAccess Access => inner.Access;
    public StoredEntry? Read(StorageKey key) => throwOnRead() ? throw new IOException("read failed") : inner.Read(key);
    public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) => inner.Query(request, options);
    public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
    public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
    public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
    public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
    public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
    public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);
}

internal sealed class ImportUnitOfWork(
    IUnitOfWork inner,
    IReadOnlyList<string> unitIds,
    Func<bool> throwOnCommit,
    Func<string?> failOnUnit) : IUnitOfWork
{
    public IStorageSession OpenSession(StorageUnit unit) => inner.OpenSession(unit);
    public void Stage(RowWrite write) => inner.Stage(write);
    public ValueTask<BatchWriteReport> CommitWithOutcomesAsync(CancellationToken cancellationToken = default)
    {
        if (throwOnCommit() || unitIds.Any(x => StringComparer.Ordinal.Equals(x, failOnUnit())))
            return ValueTask.FromException<BatchWriteReport>(new IOException("atomic projection failpoint"));
        return inner.CommitWithOutcomesAsync(cancellationToken);
    }
    public BatchWriteReport CommitWithOutcomes() =>
        throwOnCommit() || unitIds.Any(x => StringComparer.Ordinal.Equals(x, failOnUnit()))
            ? throw new IOException("atomic projection failpoint")
            : inner.CommitWithOutcomes();
    public BatchWriteSummary Commit()
    {
        if (throwOnCommit() || unitIds.Any(x => StringComparer.Ordinal.Equals(x, failOnUnit())))
            throw new IOException("atomic projection failpoint");
        return inner.Commit();
    }
    public ValueTask<BatchWriteSummary> CommitAsync(CancellationToken cancellationToken = default)
    {
        if (throwOnCommit() || unitIds.Any(x => StringComparer.Ordinal.Equals(x, failOnUnit())))
            return ValueTask.FromException<BatchWriteSummary>(new IOException("atomic projection failpoint"));
        return inner.CommitAsync(cancellationToken);
    }
    public void Rollback() => inner.Rollback();
    public void Dispose() => inner.Dispose();
}

internal static class ReusableActivityImportV2StoreExtensions
{
    public static IReadOnlyList<ActivityDesignDocument> Snapshot(
        this GroundworkV2ActivityDesignStore store,
        string documentKind) =>
        store.Query(new ActivityDesignQuery(
            documentKind,
            ActivitiesDesignStorageManifest.ListAllDocumentsQuery,
            [],
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
            Take: ActivityDesignQueryPager.PageSize)).Documents;
}
