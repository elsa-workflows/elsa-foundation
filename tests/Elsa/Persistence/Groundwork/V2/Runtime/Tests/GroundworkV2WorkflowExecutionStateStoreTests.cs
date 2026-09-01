using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2WorkflowExecutionStateStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sqlite_save_find_replace_delete_and_scope_isolation_use_the_public_store()
    {
        await using var runtime = new SqliteRuntime();
        var tenantA = runtime.Store("tenant-a");
        var tenantB = runtime.Store("tenant-b");

        var opensBeforeRefusal = runtime.OpenCount;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tenantA.SaveAsync(State(
                "wf-wrong-scope",
                "tenant-b",
                WorkflowExecutionStatus.Pending,
                Now)).AsTask());
        Assert.Equal(opensBeforeRefusal, runtime.OpenCount);

        var pending = State("wf-1", "tenant-a", WorkflowExecutionStatus.Pending, Now);
        var running = pending with { Status = WorkflowExecutionStatus.Running, UpdatedAt = Now.AddMinutes(1) };
        Assert.Equal(pending, await tenantA.SaveAsync(pending));
        Assert.Equal(running, await tenantA.SaveAsync(running));
        var found = await tenantA.FindAsync("wf-1");
        Assert.NotNull(found);
        Assert.Equal(running.WorkflowExecutionId, found!.WorkflowExecutionId);
        Assert.Equal(running.Status, found.Status);
        Assert.Equal(running.UpdatedAt, found.UpdatedAt);
        Assert.Equal(running.PinnedExecutable, found.PinnedExecutable);
        Assert.Null(await tenantB.FindAsync("wf-1"));
        Assert.False(await tenantA.DeleteAsync("missing"));
        Assert.True(await tenantA.DeleteAsync("wf-1"));
        Assert.False(await tenantA.DeleteAsync("wf-1"));
    }

    [SkippableTheory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("mongodb")]
    public async Task Configured_native_provider_preserves_history_capture_and_retention_contract(
        string providerName)
    {
        var connectionString = Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        Skip.If(
            string.IsNullOrWhiteSpace(connectionString),
            $"Set {EnvironmentVariable(providerName)} to run the {providerName} workflow-execution gate.");

        using var connection = CreateConnection(providerName, connectionString);
        var declaredUnit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var unit = declaredUnit with
        {
            Id = new StorageUnitId($"{declaredUnit.Id.Value}-{suffix}"),
            Name = $"{declaredUnit.Name}_{suffix}"
        };
        connection.Schema.Apply(unit);
        var source = new DirectSessionSource(connection, unit);
        var store = new GroundworkV2WorkflowExecutionStateStore(source, Access("tenant-a"));
        var authority = new WorkflowExecutionAuthoritySnapshot("system-a", "root-a");
        await store.SaveAsync(State(
            "wf-b",
            "tenant-a",
            WorkflowExecutionStatus.Faulted,
            Now,
            artifactId: "artifact-z",
            authority: authority));
        await store.SaveAsync(State(
            "wf-a",
            "tenant-a",
            WorkflowExecutionStatus.Faulted,
            Now.AddMinutes(1),
            artifactId: "artifact-a",
            authority: authority));

        var history = await store.QueryPageAsync(new WorkflowExecutionStatePageQuery(
            PageSize: 1,
            TenantId: "tenant-a",
            Status: WorkflowExecutionStatus.Faulted));
        Assert.Equal("wf-a", Assert.Single(history.Items).WorkflowExecutionId);
        Assert.Equal(2, history.TotalCount);
        Assert.NotNull(history.NextCursor);
        var capture = await store.QueryAlterationCapturePageAsync(
            new WorkflowExecutionAlterationCaptureQuery(
                "tenant-a",
                "system-a",
                "root-a",
                null,
                new WorkflowAlterationQuerySelector(matchAllAuthorized: true),
                pageSize: 10));
        Assert.Equal(["wf-a", "wf-b"], capture.Items.Select(state => state.WorkflowExecutionId));
        Assert.Equal(
            ["artifact-a", "artifact-z"],
            await store.ListPinnedExecutableArtifactIdsAsync());
    }

    [Fact]
    public async Task Sqlite_history_filters_count_and_keyset_continuation_follow_the_public_contract()
    {
        await using var runtime = new SqliteRuntime();
        var tenantA = runtime.Store("tenant-a");
        var tenantB = runtime.Store("tenant-b");
        await tenantA.SaveAsync(State(
            "wf-a",
            "tenant-a",
            WorkflowExecutionStatus.Faulted,
            Now.AddMinutes(2),
            correlationId: "correlation-target",
            runKind: WorkflowRunKind.TestRun));
        await tenantA.SaveAsync(State(
            "wf-b",
            "tenant-a",
            WorkflowExecutionStatus.Faulted,
            Now.AddMinutes(1),
            correlationId: "correlation-target",
            runKind: WorkflowRunKind.TestRun));
        await tenantA.SaveAsync(State(
            "wf-other",
            "tenant-a",
            WorkflowExecutionStatus.Completed,
            Now.AddMinutes(3),
            definitionId: "definition-other",
            artifactId: "artifact-other",
            correlationId: "correlation-other"));
        await tenantB.SaveAsync(State(
            "wf-foreign",
            "tenant-b",
            WorkflowExecutionStatus.Faulted,
            Now.AddMinutes(4),
            correlationId: "correlation-target",
            runKind: WorkflowRunKind.TestRun));

        var query = new WorkflowExecutionStatePageQuery(
            PageSize: 1,
            TenantId: "tenant-a",
            DefinitionId: "definition-1",
            Status: WorkflowExecutionStatus.Faulted,
            RunKind: WorkflowRunKind.TestRun,
            From: Now,
            To: Now.AddMinutes(3),
            CorrelationId: "correlation-target",
            ArtifactId: "artifact-1");
        var first = await tenantA.QueryPageAsync(query);
        var second = await tenantA.QueryPageAsync(query with { Cursor = first.NextCursor });

        Assert.Equal(["wf-a"], first.Items.Select(item => item.WorkflowExecutionId));
        Assert.Equal(["wf-b"], second.Items.Select(item => item.WorkflowExecutionId));
        Assert.True(first.HasNext);
        Assert.False(second.HasNext);
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(2, second.TotalCount);
        Assert.Null(second.NextCursor);

        var exact = await tenantA.QueryPageAsync(query with
        {
            PageSize = 10,
            WorkflowExecutionId = "wf-b"
        });
        Assert.Equal("wf-b", Assert.Single(exact.Items).WorkflowExecutionId);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => tenantA.QueryPageAsync(query with
        {
            CorrelationId = "different",
            Cursor = first.NextCursor
        }).AsTask());
        Assert.Equal("cursor", exception.ParamName);
    }

    [Fact]
    public async Task Sqlite_alteration_capture_is_authority_scoped_and_orders_by_immutable_execution_id()
    {
        await using var runtime = new SqliteRuntime();
        var tenantA = runtime.Store("tenant-a");
        var authority = new WorkflowExecutionAuthoritySnapshot(
            "system-a",
            "root-a",
            new Dictionary<string, string> { ["region"] = "west" });
        await tenantA.SaveAsync(State(
            "wf-b",
            "tenant-a",
            WorkflowExecutionStatus.Faulted,
            Now,
            authority: authority));
        await tenantA.SaveAsync(State(
            "wf-a",
            "tenant-a",
            WorkflowExecutionStatus.Faulted,
            Now.AddMinutes(1),
            authority: authority));
        await tenantA.SaveAsync(State(
            "wf-wrong-authority",
            "tenant-a",
            WorkflowExecutionStatus.Faulted,
            Now.AddMinutes(2),
            authority: new WorkflowExecutionAuthoritySnapshot("system-b", "root-a")));

        var query = new WorkflowExecutionAlterationCaptureQuery(
            "tenant-a",
            "system-a",
            "root-a",
            new Dictionary<string, string> { ["region"] = "west" },
            new WorkflowAlterationQuerySelector(
                status: WorkflowExecutionStatus.Faulted,
                matchAllAuthorized: false),
            pageSize: 1);
        var first = await tenantA.QueryAlterationCapturePageAsync(query);
        var second = await tenantA.QueryAlterationCapturePageAsync(
            new WorkflowExecutionAlterationCaptureQuery(
                query.TenantPartition,
                query.SystemIdentity,
                query.RootInitiator,
                query.AuthorityMetadata,
                query.Selector,
                query.PageSize,
                first.NextCursor));

        Assert.Equal(["wf-a"], first.Items.Select(item => item.WorkflowExecutionId));
        Assert.Equal(["wf-b"], second.Items.Select(item => item.WorkflowExecutionId));
        Assert.True(first.HasNext);
        Assert.False(second.HasNext);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            tenantA.QueryAlterationCapturePageAsync(new WorkflowExecutionAlterationCaptureQuery(
                "tenant-a",
                "system-a",
                "different-root",
                new Dictionary<string, string> { ["region"] = "west" },
                query.Selector,
                pageSize: 1,
                cursor: first.NextCursor)).AsTask());
        Assert.Equal("cursor", exception.ParamName);
    }

    [Fact]
    public async Task Sqlite_retention_roots_are_distinct_and_ordinally_sorted()
    {
        await using var runtime = new SqliteRuntime();
        var store = runtime.Store("tenant-a");
        await store.SaveAsync(State("wf-c", "tenant-a", WorkflowExecutionStatus.Completed, Now, artifactId: "artifact-z"));
        await store.SaveAsync(State("wf-b", "tenant-a", WorkflowExecutionStatus.Faulted, Now, artifactId: "artifact-a"));
        await store.SaveAsync(State("wf-a", "tenant-a", WorkflowExecutionStatus.Running, Now, artifactId: "artifact-z"));

        Assert.Equal(
            ["artifact-a", "artifact-z"],
            await store.ListPinnedExecutableArtifactIdsAsync());
        var request = Assert.Single(runtime.Requests);
        Assert.False(request.Projection.AllColumns);
        Assert.Equal(
            [ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryArtifactIdField],
            request.Projection.Columns.Select(column => column.Name));
        Assert.Equal(
            ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryArtifactIdField,
            request.LatestPerKey?.Key.Name);
        Assert.Equal(
            ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryArtifactTimestampField,
            request.LatestPerKey?.Timestamp.Name);
    }

    [Fact]
    public async Task Sqlite_list_traverses_bounded_provider_pages()
    {
        await using var runtime = new SqliteRuntime();
        var store = runtime.Store("tenant-a");
        for (var index = 0; index <= RuntimeStorePageRequest.MaximumLimit; index++)
        {
            await store.SaveAsync(State(
                $"wf-{index:D4}",
                "tenant-a",
                WorkflowExecutionStatus.Completed,
                Now.AddTicks(index)));
        }

        var states = await store.ListAsync();

        Assert.Equal(RuntimeStorePageRequest.MaximumLimit + 1, states.Count);
        Assert.Equal(2, runtime.Requests.Count);
        Assert.All(runtime.Requests, request =>
            Assert.Equal(RuntimeStorePageRequest.MaximumLimit, request.Paging.Limit));
    }

    [Fact]
    public async Task Sqlite_read_refuses_content_projection_drift()
    {
        await using var runtime = new SqliteRuntime();
        var state = State("wf-corrupt", "tenant-a", WorkflowExecutionStatus.Running, Now);
        var values = GroundworkV2WorkflowExecutionStorageConventions.Values(state).Values
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        values[ElsaRuntimeV2StorageManifest.WorkflowExecutionHistoryStatusField] =
            (int)WorkflowExecutionStatus.Completed;
        runtime.InsertRaw(new StorageValues(values), "tenant-a");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            runtime.Store("tenant-a").FindAsync(state.WorkflowExecutionId).AsTask());
    }

    private static WorkflowExecutionState State(
        string id,
        string tenantId,
        WorkflowExecutionStatus status,
        DateTimeOffset timestamp,
        string definitionId = "definition-1",
        string artifactId = "artifact-1",
        string correlationId = "correlation-1",
        WorkflowRunKind runKind = WorkflowRunKind.PublishedRun,
        WorkflowExecutionAuthoritySnapshot? authority = null) => new(
        id,
        new WorkflowExecutableIdentity(artifactId, definitionId, "version-1", "1.0.0", "hash-1"),
        status,
        null,
        timestamp,
        timestamp,
        timestamp,
        null,
        correlationId,
        null,
        tenantId,
        new Dictionary<string, string>())
        {
            RunKind = runKind,
            Authority = authority
        };

    private static TestAccessContextAccessor Access(string scope) =>
        new(PersistenceAccessContext.Scoped(new PersistenceScope(scope)));

    private static string EnvironmentVariable(string providerName) =>
        $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING";

    private static IStorageProviderConnection CreateConnection(
        string providerName,
        string connectionString) => providerName switch
        {
            "postgresql" => new PostgreSqlProviderFactory().Create(connectionString),
            "sqlserver" => new SqlServerProviderFactory().Create(connectionString),
            "mongodb" => new MongoProviderFactory().Create(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
        };

    private sealed class TestAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class DirectSessionSource(IStorageProviderConnection connection, StorageUnit unit)
        : IGroundworkStorageSessionSource
    {
        public List<QueryRequest> Requests { get; } = [];

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            OpenCount++;
            return new RecordingSession(connection.OpenSession(unit, access), Requests);
        }

        public int OpenCount { get; private set; }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind, unitId);
            return unit;
        }

        private sealed class RecordingSession(IStorageSession inner, ICollection<QueryRequest> requests)
            : SynchronousStorageSessionTestDouble, IStorageSession, IConcurrencyStorageSession
        {
            public StorageUnit Unit => inner.Unit;
            public StorageAccess Access => inner.Access;
            public StoredEntry? Read(StorageKey key) => inner.Read(key);

            public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
            {
                requests.Add(request);
                return inner.Query(request, options);
            }

            public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
            public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
            public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
            public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
            public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
            public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) =>
                inner.Append(operationId, values);

            public WriteOutcome ConditionalUpsert(StorageValues values, WriteOptions? options = null) =>
                inner is IConcurrencyStorageSession concurrency
                    ? concurrency.ConditionalUpsert(values, options)
                    : throw new NotSupportedException();
        }
    }

    private sealed class SqliteRuntime : IAsyncDisposable
    {
        private readonly string database = Path.Combine(
            Path.GetTempPath(),
            $"elsa-runtime-history-{Guid.NewGuid():N}.db");
        private readonly IStorageProviderConnection connection;
        private readonly DirectSessionSource source;
        private readonly StorageUnit unit;

        public SqliteRuntime()
        {
            connection = new SqliteProviderFactory().Create($"Data Source={database}");
            unit = ElsaRuntimeV2StorageManifest.Require(
                ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind);
            connection.Schema.Apply(unit);
            source = new DirectSessionSource(connection, unit);
        }

        public IWorkflowExecutionStateStore Store(string scope) =>
            new GroundworkV2WorkflowExecutionStateStore(source, Access(scope));

        public IReadOnlyList<QueryRequest> Requests => source.Requests;

        public int OpenCount => source.OpenCount;

        public void InsertRaw(StorageValues values, string scope)
        {
            var outcome = connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope(scope)))
                .Insert(values, WriteOptions.CreateOnly);
            Assert.Equal(WriteOutcomeStatus.Inserted, outcome.Status);
        }

        public ValueTask DisposeAsync()
        {
            connection.Dispose();
            foreach (var path in new[] { database, $"{database}-shm", $"{database}-wal" })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }

            return ValueTask.CompletedTask;
        }
    }
}
