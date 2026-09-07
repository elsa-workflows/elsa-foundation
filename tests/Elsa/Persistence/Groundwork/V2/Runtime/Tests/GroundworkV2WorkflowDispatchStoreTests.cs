using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2WorkflowDispatchStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sqlite_round_trips_replay_and_provider_recreation_with_run_kind()
    {
        var database = Path.Combine(Path.GetTempPath(), $"elsa-runtime-dispatch-{Guid.NewGuid():N}.db");
        try
        {
            var record = Pending("parent-recreated", "activity-1", WorkflowRunKind.TestRun);
            using (var connection = new SqliteProviderFactory().Create($"Data Source={database}"))
            {
                var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind);
                connection.Schema.Apply(unit);
                var store = new GroundworkV2WorkflowDispatchStore(
                    new DirectSessionSource(connection, unit),
                    Access("tenant-a"));
                Assert.True(WorkflowDispatchLifecycle.RecordsEqual(record, await store.SaveAsync(record)));
                Assert.True(WorkflowDispatchLifecycle.RecordsEqual(record, await store.SaveAsync(record)));
            }

            using var reopened = new SqliteProviderFactory().Create($"Data Source={database}");
            var recreatedUnit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind);
            var recreated = new GroundworkV2WorkflowDispatchStore(
                new DirectSessionSource(reopened, recreatedUnit),
                Access("tenant-a"));
            var found = await recreated.FindAsync(record.DispatchId);
            Assert.NotNull(found);
            Assert.Equal(WorkflowRunKind.TestRun, found!.RunKind);
        }
        finally
        {
            foreach (var path in new[] { database, $"{database}-shm", $"{database}-wal" })
                if (File.Exists(path))
                    File.Delete(path);
        }
    }

    [Fact]
    public async Task Sqlite_preserves_ordered_parent_continuation_and_bounded_take()
    {
        await using var runtime = new NativeProviderRuntime();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind);
        connection.Schema.Apply(unit);
        var source = new DirectSessionSource(connection, unit);
        var store = new GroundworkV2WorkflowDispatchStore(source, Access("tenant-a"));
        var records = Enumerable.Range(0, 5)
            .Select(index => Pending("parent-query", $"activity-{index:D2}"))
            .ToArray();
        foreach (var record in records)
            await store.SaveAsync(record);

        var ordered = records.OrderBy(record => record.CreatedAt)
            .ThenBy(record => record.DispatchId, StringComparer.Ordinal)
            .ToArray();
        var first = await store.QueryAsync(new WorkflowDispatchQuery(
            parentWorkflowExecutionId: "parent-query", take: 2));
        Assert.Equal(ordered.Take(2).Select(record => record.DispatchId), first.Select(record => record.DispatchId));
        var second = await store.QueryAsync(new WorkflowDispatchQuery(
            parentWorkflowExecutionId: "parent-query",
            take: 2,
            afterCreatedAt: first.Last().CreatedAt,
            afterDispatchId: first.Last().DispatchId));
        Assert.Equal(ordered.Skip(2).Take(2).Select(record => record.DispatchId), second.Select(record => record.DispatchId));

        var child = (await store.QueryAsync(
            new WorkflowDispatchQuery(childWorkflowExecutionId: records[0].ChildWorkflowExecutionId))).Single();
        Assert.Equal(records[0].DispatchId, child.DispatchId);
        Assert.Empty(await store.QueryAsync(new WorkflowDispatchQuery(
            childWorkflowExecutionId: child.ChildWorkflowExecutionId,
            afterCreatedAt: child.CreatedAt,
            afterDispatchId: child.DispatchId)));
    }

    [Fact]
    public async Task Sqlite_filters_status_and_test_scope_without_cross_scope_rows()
    {
        await using var runtime = new NativeProviderRuntime();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind);
        connection.Schema.Apply(unit);
        var store = new GroundworkV2WorkflowDispatchStore(
            new DirectSessionSource(connection, unit),
            Access("tenant-a"));
        var scope = new WorkflowTestScope(
            "scope-query",
            Now.AddHours(1),
            "tenant-a",
            new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue));
        var scoped = Pending("parent-scope", "activity-scoped", WorkflowRunKind.TestRun, testScope: scope);
        var unscoped = Pending("parent-scope", "activity-unscoped");
        await store.SaveAsync(scoped);
        await store.SaveAsync(unscoped);
        var started = unscoped.TransitionTo(WorkflowDispatchStatus.Started, Now.AddSeconds(1));
        await store.SaveAsync(started);

        var pending = await store.QueryAsync(new WorkflowDispatchQuery(
            parentWorkflowExecutionId: "parent-scope",
            status: WorkflowDispatchStatus.Pending,
            testScopeId: scope.ScopeId));

        var result = Assert.Single(pending);
        Assert.Equal(scoped.DispatchId, result.DispatchId);
        Assert.Equal(WorkflowDispatchStatus.Pending, result.Status);
        Assert.Equal(scope.ScopeId, result.TestScope!.ScopeId);

        var startedOnly = await store.QueryAsync(new WorkflowDispatchQuery(status: WorkflowDispatchStatus.Started));
        Assert.Equal(started.DispatchId, Assert.Single(startedOnly).DispatchId);
    }

    [Fact]
    public async Task Sqlite_query_work_is_bounded_by_take_and_continuation_reads()
    {
        await using var runtime = new NativeProviderRuntime();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind);
        connection.Schema.Apply(unit);
        var source = new CountingSessionSource(connection, unit);
        var store = new GroundworkV2WorkflowDispatchStore(source, Access("tenant-a"));
        foreach (var index in Enumerable.Range(0, 201))
            await store.SaveAsync(Pending("parent-bounded", $"activity-{index:D3}"));

        source.QueryRequests.Clear();
        var first = await store.QueryAsync(new WorkflowDispatchQuery(parentWorkflowExecutionId: "parent-bounded", take: 2));
        Assert.Equal(2, first.Count);
        var firstQuery = Assert.Single(source.QueryRequests);
        Assert.Equal(2, firstQuery.Paging.Limit);

        source.QueryRequests.Clear();
        var continuation = await store.QueryAsync(new WorkflowDispatchQuery(
            parentWorkflowExecutionId: "parent-bounded",
            take: 2,
            afterCreatedAt: first.Last().CreatedAt,
            afterDispatchId: first.Last().DispatchId));
        Assert.Equal(2, continuation.Count);
        Assert.Equal(2, source.QueryRequests.Count);
        Assert.All(source.QueryRequests, request => Assert.Equal(2, request.Paging.Limit));
    }

    [Fact]
    public async Task List_by_parent_reads_all_bounded_continuation_pages()
    {
        await using var runtime = new NativeProviderRuntime();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind);
        connection.Schema.Apply(unit);
        var store = new GroundworkV2WorkflowDispatchStore(
            new DirectSessionSource(connection, unit),
            Access("tenant-a"));
        foreach (var index in Enumerable.Range(0, WorkflowDispatchQuery.MaximumTake + 1))
            await store.SaveAsync(Pending("parent-all", $"activity-{index:D3}"));

        var records = await store.ListAsync("parent-all");
        Assert.Equal(WorkflowDispatchQuery.MaximumTake + 1, records.Count);
        Assert.Equal(
            records.OrderBy(record => record.CreatedAt).ThenBy(record => record.DispatchId, StringComparer.Ordinal).Select(record => record.DispatchId),
            records.Select(record => record.DispatchId));
    }

    [Fact]
    public async Task Legal_transition_regression_conflict_roots_and_conditional_delete_are_enforced()
    {
        await using var runtime = new NativeProviderRuntime();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind);
        connection.Schema.Apply(unit);
        var store = new GroundworkV2WorkflowDispatchStore(
            new DirectSessionSource(connection, unit),
            Access("tenant-a"));
        var pending = Pending("parent-lifecycle", "activity-1");
        var started = pending.TransitionTo(WorkflowDispatchStatus.Started, Now.AddSeconds(1));
        var completed = started.TransitionTo(WorkflowDispatchStatus.Completed, Now.AddSeconds(2));
        await store.SaveAsync(pending);
        await store.SaveAsync(started);
        await store.SaveAsync(completed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(started).AsTask());
        var immutableConflict = Pending("parent-lifecycle", "activity-1", correlationId: "different-correlation");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(immutableConflict).AsTask());
        Assert.False(await store.TryDeleteAsync(started));
        Assert.True(await store.TryDeleteAsync(completed));
        Assert.True(await store.TryDeleteAsync(completed));
        Assert.Null(await store.FindAsync(completed.DispatchId));

        var active = Pending("parent-roots", "activity-active");
        var startedActive = Pending("parent-roots", "activity-started");
        var terminal = Pending("parent-roots", "activity-terminal");
        await store.SaveAsync(active);
        await store.SaveAsync(startedActive);
        startedActive = startedActive.TransitionTo(WorkflowDispatchStatus.Started, Now.AddSeconds(1));
        await store.SaveAsync(startedActive);
        await store.SaveAsync(terminal);
        terminal = terminal
            .TransitionTo(WorkflowDispatchStatus.Started, Now.AddSeconds(1))
            .TransitionTo(WorkflowDispatchStatus.Completed, Now.AddSeconds(2));
        await store.SaveAsync(terminal);
        Assert.Equal(
            new[] { active.ChildExecutable.ArtifactId, startedActive.ChildExecutable.ArtifactId }
                .Order(StringComparer.Ordinal),
            await store.ListPinnedExecutableArtifactIdsAsync());
    }

    [Fact]
    public async Task Explicit_tenant_mismatch_is_rejected_before_opening_a_session()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind);
        var session = new RecordingSession(unit);
        var source = new RecordingSource(session, unit);
        var store = new GroundworkV2WorkflowDispatchStore(source, Access("tenant-a"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(Pending("parent-tenant", "activity-1", tenantId: "tenant-b")).AsTask());
        Assert.False(source.Opened);
        Assert.False(session.ReadCalled);
    }

    [Fact]
    public async Task Foreign_tenant_rows_fail_closed_on_find_and_query()
    {
        await using var runtime = new NativeProviderRuntime();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind);
        connection.Schema.Apply(unit);
        var source = new DirectSessionSource(connection, unit);
        var record = Pending("parent-foreign", "activity-1", tenantId: "tenant-a");
        var tenantA = Access("tenant-a");
        await new GroundworkV2WorkflowDispatchStore(source, tenantA).SaveAsync(record);
        // Deliberately corrupt only the serialized tenant context while retaining the tenant-a physical scope.
        var session = source.Open(unit.Id.Value, StorageAccess.Scoped(new StorageScope("tenant-a")));
        var entry = session.Read(GroundworkRuntimeRowStore.Key(record.DispatchId))!;
        var foreignRecord = Pending("parent-foreign", "activity-1", tenantId: "tenant-b");
        var json = System.Text.Json.JsonSerializer.Serialize(foreignRecord, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });
        var corrupted = entry.Values.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        corrupted[ElsaRuntimeV2StorageManifest.ContentField] = json;
        session.Upsert(new StorageValues(corrupted), WriteOptions.IfVersion(entry.Version!.Value));

        var store = new GroundworkV2WorkflowDispatchStore(source, tenantA);
        var find = await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindAsync(record.DispatchId).AsTask());
        var query = await Assert.ThrowsAsync<InvalidOperationException>(() => store.QueryAsync(
            new WorkflowDispatchQuery(parentWorkflowExecutionId: record.ParentWorkflowExecutionId)).AsTask());
        Assert.DoesNotContain("tenant-a", find.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tenant-a", query.Message, StringComparison.Ordinal);
    }

    private static TestAccessContextAccessor Access(string tenant) =>
        new(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));

    private static WorkflowDispatchRecord Pending(
        string parentWorkflowExecutionId,
        string parentActivityExecutionId,
        WorkflowRunKind runKind = WorkflowRunKind.PublishedRun,
        string? tenantId = "tenant-a",
        WorkflowTestScope? testScope = null,
        string? artifactId = null,
        string? correlationId = null)
    {
        var identity = new WorkflowDispatchIdentity(parentWorkflowExecutionId, parentActivityExecutionId);
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parentWorkflowExecutionId,
            parentActivityExecutionId,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity(
                artifactId ?? $"artifact-{parentActivityExecutionId}", "definition-child", "version-child", "1", $"hash-{artifactId ?? parentActivityExecutionId}"),
            new WorkflowExecutableSourceProvenance(
                $"source-{parentActivityExecutionId}", "WorkflowDefinitionVersion", "version-child", "1",
                "definition-child", "version-child", "1", "publication-child", "slot-child"),
            WorkflowDispatchMode.FireAndForget,
            WorkflowDispatchStatus.Pending,
            correlationId,
            tenantId,
            new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue),
            runKind,
            new WorkflowExecutionAuthoritySnapshot(parentWorkflowExecutionId, "initiator-1"),
            [new WorkflowDispatchInputDescriptor("orderId", "string")],
            Now,
            Now,
            new Dictionary<string, string> { ["safe-code"] = "dispatch" },
            testScope);
    }

    private sealed class TestAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class DirectSessionSource(IStorageProviderConnection connection, StorageUnit unit) : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return connection.OpenSession(unit, access);
        }

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) =>
            connection.BeginUnitOfWork(access, options, unitIds.Select(ElsaRuntimeV2StorageManifest.Require).ToArray());

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return unit;
        }
    }

    private sealed class CountingSessionSource(IStorageProviderConnection connection, StorageUnit unit) : IGroundworkStorageSessionSource
    {
        public List<QueryRequest> QueryRequests { get; } = [];

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return new CountingSession(connection.OpenSession(unit, access), QueryRequests);
        }

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) =>
            connection.BeginUnitOfWork(access, options, unitIds.Select(ElsaRuntimeV2StorageManifest.Require).ToArray());

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return unit;
        }
    }

    private sealed class CountingSession(IStorageSession inner, ICollection<QueryRequest> requests) : SynchronousStorageSessionTestDouble, IStorageSession, IConcurrencyStorageSession
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
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);
        public WriteOutcome ConditionalUpsert(StorageValues values, WriteOptions? options = null) =>
            ((IConcurrencyStorageSession)inner).ConditionalUpsert(values, options);
    }

    private sealed class NativeProviderRuntime : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"elsa-runtime-dispatch-{Guid.NewGuid():N}.db");

        public IStorageProviderConnection OpenConnection() => new SqliteProviderFactory().Create($"Data Source={path}");

        public ValueTask DisposeAsync()
        {
            foreach (var candidate in new[] { path, $"{path}-shm", $"{path}-wal" })
                if (File.Exists(candidate))
                    File.Delete(candidate);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingSource(RecordingSession session, StorageUnit unit) : IGroundworkStorageSessionSource
    {
        public bool Opened { get; private set; }

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Opened = true;
            return session;
        }

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null) => unit;
    }

    private sealed class RecordingSession(StorageUnit unit) : SynchronousStorageSessionTestDouble, IStorageSession
    {
        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access => StorageAccess.Scoped(new StorageScope("tenant-a"));
        public bool ReadCalled { get; private set; }
        public StoredEntry? Read(StorageKey key) { ReadCalled = true; return null; }
        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) => throw new NotSupportedException();
        public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw new NotSupportedException();
    }
}
