using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;
using System.Text.Json;
using Testcontainers.MongoDb;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Elsa.Activities.Design.Persistence.Groundwork.V2.ProviderMatrix.Tests;

/// <summary>
/// Provider acceptance for the activity-design public-v2 seam. SQLite always runs locally; the other
/// providers use explicit connection strings when configured and disposable containers in CI. The
/// MongoDB container is a replica set so the exact unit-of-work and atomic-marker checks exercise a
/// transactional deployment.
/// </summary>
public sealed class GroundworkActivitiesDesignV2ProviderMatrixTests
{
    public static TheoryData<string> Providers => new() { "sqlite", "postgresql", "sqlserver", "mongodb" };

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    [Trait("Category", "GroundworkProviderMatrix")]
    public async Task Public_v2_activity_design_preserves_schema_scope_cas_keyset_and_atomic_restart_contract(string providerName)
    {
        var configuredConnection = Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        Skip.If(
            providerName != "sqlite" && string.IsNullOrWhiteSpace(configuredConnection) && !IsContinuousIntegration(),
            $"Set {EnvironmentVariable(providerName)} locally, or run the matrix in CI.");

        await using var runtime = await ProviderRuntime.CreateAsync(providerName, configuredConnection);
        var tenantA = $"activity-design-a-{Guid.NewGuid():N}";
        var tenantB = $"activity-design-b-{Guid.NewGuid():N}";
        using (var persistence = ActivityDesignPersistence.Create(runtime.OpenConnection(), tenantA))
        {
            persistence.ApplySchema();
            await ExercisePublicContractAsync(persistence, tenantA, tenantB);
        }

        using var reopened = ActivityDesignPersistence.Create(runtime.OpenConnection(), tenantA);
        reopened.ApplySchema();
        var durable = await reopened.Store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "matrix-activity");
        Assert.NotNull(durable);
        Assert.Equal(2, durable!.Version);
        Assert.Contains("Acme.Matrix.Updated", durable.ContentJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Matrix_uses_only_the_public_v2_groundwork_surface()
    {
        var references = typeof(GroundworkV2ActivityDesignStore).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name is "Groundwork.Core" or "Groundwork.Documents");
        Assert.Contains(references, reference => reference.Name == "Groundwork.Kernel");
        Assert.Contains(references, reference => reference.Name == "Groundwork.Query.Model");
        Assert.Contains(references, reference => reference.Name == "Groundwork.Store");
    }

    private static async Task ExercisePublicContractAsync(
        ActivityDesignPersistence persistence,
        string tenantA,
        string tenantB)
    {
        var rows = Enumerable.Range(0, 3)
            .Select(index => new ActivityDesignSaveRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                $"matrix-activity-{index}",
                ActivitiesDesignStorageManifest.SchemaVersion,
                Content(tenantA, $"Acme.Matrix.{index}")))
            .Append(new ActivityDesignSaveRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                "matrix-activity",
                ActivitiesDesignStorageManifest.SchemaVersion,
                Content(tenantA, "Acme.Matrix")))
            .ToArray();
        await persistence.Store.SaveAllAsync(
            ActivityDesignCommitScope.Of(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind),
            rows);

        var page = await persistence.Store.QueryAsync(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByTypeKeyQuery,
            [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.In(
                ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField,
                ["Acme.Matrix", "Acme.Matrix.0", "Acme.Matrix.1", "Acme.Matrix.2"]))],
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
            Take: 2));
        Assert.Equal(["matrix-activity", "matrix-activity-0"], page.Documents.Select(document => document.Id));
        Assert.NotNull(page.NextContinuationToken);

        var secondPage = await persistence.Store.QueryAsync(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByTypeKeyQuery,
            [ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.In(
                ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField,
                ["Acme.Matrix", "Acme.Matrix.0", "Acme.Matrix.1", "Acme.Matrix.2"]))],
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
            Take: 2,
            ContinuationToken: page.NextContinuationToken));
        Assert.Equal(["matrix-activity-1", "matrix-activity-2"], secondPage.Documents.Select(document => document.Id));

        var current = await persistence.Store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "matrix-activity");
        Assert.NotNull(current);
        await persistence.Store.SaveAsync(new ActivityDesignSaveRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "matrix-activity",
            ActivitiesDesignStorageManifest.SchemaVersion,
            Content(tenantA, "Acme.Matrix.Updated"),
            current!.Version));
        await Assert.ThrowsAsync<ActivityDesignWriteConflictException>(() => persistence.Store.SaveAsync(new(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "matrix-activity",
            ActivitiesDesignStorageManifest.SchemaVersion,
            Content(tenantA, "Acme.Matrix.Stale"),
            current.Version)));

        using (var unitOfWork = persistence.Store.Begin(ActivityDesignCommitScope.Of(
                   ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind)))
        {
            unitOfWork.StageSave(new ActivityDesignSaveRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                "matrix-staged",
                ActivitiesDesignStorageManifest.SchemaVersion,
                Content(tenantA, "Acme.Matrix.Staged")));
            Assert.Equal(1, unitOfWork.Load(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                "matrix-staged")!.Version);
            await unitOfWork.CommitAsync(CancellationToken.None);
        }

        var atomic = new GroundworkDesignAtomicWrite(persistence.Store);
        var request = new GroundworkDesignAtomicWriteRequest(
            new GroundworkDesignOperationIdentity("activity-design-matrix", "operation-1"),
            "fingerprint-1",
            [ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind]);
        var result = await atomic.ExecuteAsync(
            request,
            async (context, cancellationToken) =>
            {
                await context.SaveAsync(new ActivityDesignSaveRequest(
                    ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                    "matrix-atomic",
                    ActivitiesDesignStorageManifest.SchemaVersion,
                    Content(tenantA, "Acme.Matrix.Atomic")), cancellationToken);
                return GroundworkDesignAtomicWriteStageResult.Accepted("result-1", "{\"ok\":true}");
            });
        Assert.Equal(GroundworkDesignAtomicWriteStatus.Committed, result.Status);
        var replay = await atomic.ExecuteAsync(request, (_, _) =>
            Task.FromResult(GroundworkDesignAtomicWriteStageResult.Accepted("unused", "{\"unused\":true}")));
        Assert.Equal(GroundworkDesignAtomicWriteStatus.Replayed, replay.Status);

        persistence.Access.Current = PersistenceAccessContext.Scoped(new PersistenceScope(tenantB));
        Assert.Null(await persistence.Store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "matrix-activity"));

        // Provider scope, rather than the caller-controlled JSON tenant field, is
        // authoritative for privileged reads.  The same id is intentionally
        // present in both scopes so a cross-scope point read must refuse ambiguity.
        await persistence.Store.SaveAsync(new ActivityDesignSaveRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "matrix-activity",
            ActivitiesDesignStorageManifest.SchemaVersion,
            Content(tenantB, "Acme.Matrix.TenantB")));
        var scopedRows = await persistence.Store.QueryAsync(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ListAllDocumentsQuery,
            [],
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
            Take: 100));
        Assert.Contains(scopedRows.Documents, document => document.Id == "matrix-activity");

        persistence.Access.Current = PersistenceAccessContext.PrivilegedAcrossScopes(
            new PersistenceAccessPurpose("activity-design-provider-matrix-read"));
        Assert.Throws<InvalidOperationException>(() => persistence.Store.Load(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "matrix-activity",
            acrossScopes: true));
        var acrossScopes = persistence.Store.Query(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ListAllDocumentsQuery,
            [],
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
            Take: 100), acrossScopes: true);
        Assert.True(acrossScopes.Documents.Count(document => document.Id == "matrix-activity") >= 2);

        var search = persistence.Store.Query(new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.SearchActivityDefinitionsQuery,
            [ActivityDesignQueryClause.AnyOf(
                ActivityDesignQueryComparison.Contains(ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField, "TenantB"),
                ActivityDesignQueryComparison.Contains(ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField, "Matrix"))],
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)],
            Take: 100), acrossScopes: true);
        Assert.Contains(search.Documents, document => document.ContentJson.Contains("Acme.Matrix.TenantB", StringComparison.Ordinal));
        var accepted = persistence.QueryRequests.Where(request => request.AcceptedScan?.Allowed == true).ToArray();
        Assert.Equal(2, accepted.Length);
        Assert.Equal(ActivitiesDesignStorageManifest.MaximumActivityDefinitionSearchCatalogRows + 1, accepted[0].Paging.Limit);
        Assert.Equal(100, accepted[1].Paging.Limit);

        var auditRecords = persistence.AuditSink.Snapshot();
        Assert.Equal(3, auditRecords.Count(record =>
            record.EventKind == GroundworkPrivilegedQueryAuditEventKind.Acquisition));
        Assert.Equal(3, auditRecords.Count(record =>
            record.EventKind == GroundworkPrivilegedQueryAuditEventKind.Outcome));
        Assert.All(auditRecords, record =>
        {
            Assert.Equal(StorageAccessKind.PrivilegedAcrossScopes, record.AccessKind);
            Assert.NotEqual(Guid.Empty, record.AcquisitionId);
        });
    }

    private static string Content(string tenantId, string activityTypeKey) =>
        JsonSerializer.Serialize(new { tenantId, activityTypeKey, category = "Matrix" });

    private static string EnvironmentVariable(string providerName) =>
        $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING";

    private static bool IsContinuousIntegration() =>
        Environment.GetEnvironmentVariable("CI") is "1" or "true";

    private sealed class ActivityDesignPersistence(
        IStorageProviderConnection connection,
        string tenantId) : IDisposable
    {
        private readonly IReadOnlyDictionary<string, StorageUnit> units = ActivitiesDesignStorageManifest.CreateUnits()
            .ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);
        private readonly List<QueryRequest> queryRequests = [];
        private SessionSource sessions = null!;
        private GroundworkPrivilegedQueryAuditSink auditSink = null!;

        public MutableAccess Access { get; } = new(PersistenceAccessContext.Scoped(new PersistenceScope(tenantId)));
        public GroundworkV2ActivityDesignStore Store { get; private set; } = null!;
        public GroundworkPrivilegedQueryAuditSink AuditSink => auditSink;
        public IReadOnlyList<QueryRequest> QueryRequests => queryRequests;

        private ActivityDesignPersistence Initialize()
        {
            sessions = new SessionSource(connection, queryRequests);
            auditSink = new GroundworkPrivilegedQueryAuditSink();
            var auditExecutor = new GroundworkPrivilegedQueryAuditExecutor(sessions, Access, auditSink);
            Store = new GroundworkV2ActivityDesignStore(
                sessions,
                Access,
                privilegedQueryAuditExecutor: auditExecutor);
            return this;
        }

        public static ActivityDesignPersistence Create(IStorageProviderConnection connection, string tenantId) =>
            new ActivityDesignPersistence(connection, tenantId).Initialize();

        public void ApplySchema()
        {
            foreach (var unit in units.Values)
                connection.Schema.Apply(unit);
        }

        public void Dispose() => connection.Dispose();

        private sealed class SessionSource(
            IStorageProviderConnection connection,
            ICollection<QueryRequest> queryRequests) : IGroundworkStorageSessionSource
        {
            public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
                new RecordingSession(connection.OpenSession(Unit(unitId, targetName), access), queryRequests);

            public IUnitOfWork BeginUnitOfWork(
                StorageAccess access,
                BatchWriteOptions options,
                IReadOnlyList<string> unitIds,
                string? targetName = null) =>
                connection.BeginUnitOfWork(access, options, unitIds.Select(id => Unit(id, targetName)).ToArray());

            public StorageUnit Unit(string unitId, string? targetName = null) =>
                units[unitId];

            private readonly IReadOnlyDictionary<string, StorageUnit> units =
                ActivitiesDesignStorageManifest.CreateUnits().ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);
        }

        private sealed class RecordingSession(
            IStorageSession inner,
            ICollection<QueryRequest> queryRequests) : IStorageSession, IPrivilegedCrossScopeQuerySession
        {
            public StorageUnit Unit => inner.Unit;
            public StorageAccess Access => inner.Access;
            public StoredEntry? Read(StorageKey key) => inner.Read(key);
            public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
            {
                queryRequests.Add(request);
                return inner.Query(request, options);
            }

            public CrossScopeQueryResult QueryAcrossScopes(QueryRequest request, QueryRenderOptions? options = null)
            {
                queryRequests.Add(request);
                return ((IPrivilegedCrossScopeQuerySession)inner).QueryAcrossScopes(request, options);
            }

            public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
            public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
            public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
            public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
            public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
            public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);
        }
    }

    private sealed class MutableAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; set; } = current;
    }

    private sealed class ProviderRuntime(
        string providerName,
        string connectionString,
        IAsyncDisposable? container,
        string? sqlitePath) : IAsyncDisposable
    {
        public static async Task<ProviderRuntime> CreateAsync(string providerName, string? configuredConnection)
        {
            if (!string.IsNullOrWhiteSpace(configuredConnection))
                return new(providerName, configuredConnection, null, null);
            return providerName switch
            {
                "sqlite" => CreateSqliteRuntime(),
                "postgresql" => await CreatePostgreSqlRuntimeAsync(),
                "sqlserver" => await CreateSqlServerRuntimeAsync(),
                "mongodb" => await CreateMongoRuntimeAsync(),
                _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
            };
        }

        public IStorageProviderConnection OpenConnection() => providerName switch
        {
            "sqlite" => new SqliteProviderFactory().Create(connectionString),
            "postgresql" => new PostgreSqlProviderFactory().Create(connectionString),
            "sqlserver" => new SqlServerProviderFactory().Create(connectionString),
            "mongodb" => new MongoProviderFactory().Create(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
        };

        public async ValueTask DisposeAsync()
        {
            if (container is not null)
                await container.DisposeAsync();
            if (sqlitePath is null)
                return;
            foreach (var path in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal" })
                if (File.Exists(path))
                    File.Delete(path);
        }

        private static ProviderRuntime CreateSqliteRuntime()
        {
            var path = Path.Combine(Path.GetTempPath(), $"elsa-activity-design-v2-matrix-{Guid.NewGuid():N}.db");
            return new("sqlite", $"Data Source={path}", null, path);
        }

        private static async Task<ProviderRuntime> CreatePostgreSqlRuntimeAsync()
        {
            var container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("elsa")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await container.StartAsync();
            return new("postgresql", container.GetConnectionString(), container, null);
        }

        private static async Task<ProviderRuntime> CreateSqlServerRuntimeAsync()
        {
            var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();
            await container.StartAsync();
            return new("sqlserver", container.GetConnectionString(), container, null);
        }

        private static async Task<ProviderRuntime> CreateMongoRuntimeAsync()
        {
            var container = new MongoDbBuilder("mongo:7.0.37").WithReplicaSet("rs0").Build();
            await container.StartAsync();
            var connection = container.GetConnectionString();
            var queryStart = connection.IndexOf('?', StringComparison.Ordinal);
            var server = (queryStart < 0 ? connection : connection[..queryStart]).TrimEnd('/');
            return new("mongodb", $"{server}/elsa_activity_design_v2?replicaSet=rs0&authSource=admin&directConnection=true", container, null);
        }
    }
}
