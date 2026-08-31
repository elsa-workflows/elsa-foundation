using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
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

public sealed class GroundworkV2ActivityExecutionStateStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);

    [SkippableTheory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("mongodb")]
    public async Task Configured_native_provider_preserves_save_replace_count_and_page_contract(
        string providerName)
    {
        var connectionString = Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        Skip.If(
            string.IsNullOrWhiteSpace(connectionString),
            $"Set {EnvironmentVariable(providerName)} to run the {providerName} activity-execution gate.");

        using var connection = CreateConnection(providerName, connectionString);
        var declaredUnit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var unit = declaredUnit with
        {
            Id = new StorageUnitId($"{declaredUnit.Id.Value}-{suffix}"),
            Name = $"{declaredUnit.Name}_{suffix}"
        };
        connection.Schema.Apply(unit);
        var source = new DirectSessionSource(connection, unit);
        var store = new GroundworkV2ActivityExecutionStateStore(source, Access("tenant-a"));
        var state = State("wf-native", "activity-native");

        await store.SaveAsync(state);
        await store.SaveAsync(state with { Status = ActivityExecutionStatus.Completed, CompletedAt = Now.AddMinutes(1) });

        Assert.Equal(ActivityExecutionStatus.Completed,
            (await store.FindAsync("wf-native", "activity-native"))!.Status);
        Assert.Equal(1, await store.CountAsync("wf-native"));
        var page = await store.ListPageAsync(new ActivityExecutionStatePageQuery("wf-native", limit: 1));
        Assert.Equal("activity-native", Assert.Single(page.Items).Execution.ActivityExecutionId);
    }

    [Fact]
    public async Task Sqlite_save_find_replace_count_and_bounded_parent_pages_are_scoped()
    {
        await using var runtime = new SqliteRuntime();
        var tenantA = runtime.Store("tenant-a");
        var tenantB = runtime.Store("tenant-b");

        var child = State("wf-a", "activity-a", parentId: "parent-a");
        var replacement = child with { Status = ActivityExecutionStatus.Completed, CompletedAt = Now.AddMinutes(1) };
        await tenantA.SaveAsync(child);
        await tenantA.SaveAsync(replacement);
        await tenantA.SaveAsync(State("wf-a", "activity-b", parentId: "parent-a"));
        await tenantA.SaveAsync(State("wf-a", "activity-c", parentId: "parent-b"));
        await tenantA.SaveAsync(State("wf-b", "activity-a", parentId: "parent-other-workflow"));

        var found = await tenantA.FindAsync("wf-a", "activity-a");
        Assert.NotNull(found);
        Assert.Equal(replacement.Execution, found!.Execution);
        Assert.Equal(replacement.Status, found.Status);
        Assert.Equal(replacement.CompletedAt, found.CompletedAt);
        Assert.Equal(replacement.ParentActivityExecutionId, found.ParentActivityExecutionId);
        Assert.Equal(
            "wf-b",
            (await tenantA.FindAsync("wf-b", "activity-a"))!.Execution.WorkflowExecutionId);
        Assert.Null(await tenantB.FindAsync("wf-a", "activity-a"));
        runtime.ClearRequests();
        Assert.Equal(3, await tenantA.CountAsync("wf-a"));
        var countRequest = Assert.Single(runtime.Requests);
        Assert.False(countRequest.Projection.AllColumns);
        Assert.Equal(
            [ElsaRuntimeV2StorageManifest.ActivityExecutionIdField],
            countRequest.Projection.Columns.Select(column => column.Name));
        Assert.Equal(1, countRequest.Paging.Limit);
        Assert.Same(ResultShape.TotalCount.Instance, countRequest.Result);

        var allFirst = await tenantA.ListPageAsync(new ActivityExecutionStatePageQuery("wf-a", limit: 2));
        var allSecond = await tenantA.ListPageAsync(new ActivityExecutionStatePageQuery(
            "wf-a", limit: 2, continuationToken: allFirst.NextContinuationToken));
        Assert.Equal(["activity-a", "activity-b"], allFirst.Items.Select(item => item.Execution.ActivityExecutionId));
        Assert.Equal(["activity-c"], allSecond.Items.Select(item => item.Execution.ActivityExecutionId));

        runtime.ClearRequests();
        var first = await tenantA.ListByParentPageAsync(new ActivityExecutionStateParentPageQuery(
            "wf-a", "parent-a", limit: 1));
        var second = await tenantA.ListByParentPageAsync(new ActivityExecutionStateParentPageQuery(
            "wf-a", "parent-a", limit: 1, continuationToken: first.NextContinuationToken));

        Assert.Equal(["activity-a"], first.Items.Select(item => item.Execution.ActivityExecutionId));
        Assert.Equal(["activity-b"], second.Items.Select(item => item.Execution.ActivityExecutionId));
        Assert.Null(second.NextContinuationToken);
        var parentWhere = Assert.IsType<Predicate.And>(runtime.Requests[0].Where);
        Assert.Equal(
            new[]
            {
                ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField,
                ElsaRuntimeV2StorageManifest.ParentActivityExecutionIdField
            }.Order(StringComparer.Ordinal),
            parentWhere.Terms.Cast<Predicate.Equal>().Select(term => term.Column.Name).Order(StringComparer.Ordinal));

        var continuationException = await Assert.ThrowsAsync<ArgumentException>(() => tenantA.ListByParentPageAsync(
            new ActivityExecutionStateParentPageQuery("wf-a", "parent-b", limit: 1,
                continuationToken: first.NextContinuationToken)).AsTask());
        Assert.Equal("continuationToken", continuationException.ParamName);
    }

    [Fact]
    public async Task Concurrent_save_uses_create_only_and_if_version_without_unconditional_fallback()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind);
        var createRaceSession = new InterleavingSession(unit) { FailInsert = true };
        var createRaceStore = NewInterleavingStore(createRaceSession, unit);
        var createException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            createRaceStore.SaveAsync(State("wf-create", "activity-a")).AsTask());
        Assert.Contains("lost a concurrent write; retry", createException.Message, StringComparison.Ordinal);
        Assert.Equal(WritePreconditionKind.CreateOnly, createRaceSession.LastInsertOptions!.Precondition.Kind);
        Assert.False(createRaceSession.UnconditionalUpsertCalled);

        var session = new InterleavingSession(unit);
        var store = NewInterleavingStore(session, unit);
        await store.SaveAsync(State("wf-a", "activity-a"));
        session.FailConditionalUpsert = true;
        var saveException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(State("wf-a", "activity-a", status: ActivityExecutionStatus.Completed)).AsTask());
        Assert.Contains("lost a concurrent write; retry", saveException.Message, StringComparison.Ordinal);
        Assert.Equal(WritePreconditionKind.IfVersion, session.LastConditionalOptions!.Precondition.Kind);
        Assert.Equal(1, session.LastConditionalOptions.Precondition.Version);
        Assert.False(session.UnconditionalUpsertCalled);
    }

    [Fact]
    public async Task Sqlite_checkpoint_and_direct_store_share_activity_identity_and_projections()
    {
        var database = Path.Combine(
            Path.GetTempPath(),
            $"elsa-runtime-activity-checkpoint-{Guid.NewGuid():N}.db");
        try
        {
            using var connection = new SqliteProviderFactory().Create($"Data Source={database}");
            foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
                connection.Schema.Apply(unit);

            var source = new NativeSessionSource(connection);
            var state = State("wf-checkpoint", "activity-checkpoint", parentId: "parent-checkpoint");
            var changes = new RuntimeCheckpointStateChangeSet(
                null,
                null,
                [new RuntimeStateChange<ActivityExecutionState>(
                    state.Execution.ActivityExecutionId,
                    RuntimeStateChangeOperation.Upsert,
                    state,
                    new Dictionary<string, string>())],
                [], [], [], [],
                workflowDispatches: null,
                activityExecutionInspections: null,
                postCommitOutbox: null,
                activityScopeCleanups: null);
            var commit = new RuntimeCheckpointCommit(
                "activity-checkpoint-commit",
                new RuntimeCheckpoint(
                    "checkpoint-activity",
                    "runtime",
                    state.Execution.WorkflowExecutionId,
                    Now,
                    [state.Execution.ActivityExecutionId],
                    new Dictionary<string, string>()),
                changes,
                [],
                new Dictionary<string, string>());

            await new GroundworkV2RuntimeCheckpointWriter(
                    source,
                    new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))))
                .CommitAsync(commit, new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

            var activityUnit = ElsaRuntimeV2StorageManifest.Require(
                ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind);
            var entry = connection.OpenSession(
                    activityUnit,
                    StorageAccess.Scoped(new StorageScope("tenant-a")))
                .Read(GroundworkRuntimeRowStore.Key(
                    GroundworkV2ActivityExecutionStorageConventions.PhysicalId(
                        state.Execution.WorkflowExecutionId,
                        state.Execution.ActivityExecutionId)));
            Assert.NotNull(entry);
            foreach (var (field, expected) in GroundworkV2ActivityExecutionStorageConventions.Projections(state))
                Assert.Equal(expected, entry!.Values.Values[field]);

            var direct = new GroundworkV2ActivityExecutionStateStore(source, Access("tenant-a"));
            Assert.Equal(
                state.Execution.ActivityExecutionId,
                (await direct.FindAsync(state.Execution.WorkflowExecutionId, state.Execution.ActivityExecutionId))!
                .Execution.ActivityExecutionId);
        }
        finally
        {
            foreach (var path in new[] { database, $"{database}-shm", $"{database}-wal", $"{database}-journal", $"{database}.schema.lock" })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Sqlite_reads_refuse_projection_drift_and_global_scope_before_io()
    {
        await using var runtime = new SqliteRuntime();
        var state = State("wf-drift", "activity-drift");
        var values = GroundworkV2ActivityExecutionStorageConventions.Values(state).Values
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        values[ElsaRuntimeV2StorageManifest.StatusField] = ActivityExecutionStatus.Completed.ToString();
        runtime.InsertRaw(new StorageValues(values), "tenant-a");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            runtime.Store("tenant-a").FindAsync("wf-drift", "activity-drift").AsTask());

        var opens = runtime.OpenCount;
        var global = runtime.Store(PersistenceAccessContext.Global);
        await Assert.ThrowsAsync<InvalidOperationException>(() => global.FindAsync("wf-drift", "activity-drift").AsTask());
        Assert.Equal(opens, runtime.OpenCount);
    }

    private static ActivityExecutionState State(
        string workflowExecutionId,
        string activityExecutionId,
        string? parentId = null,
        ActivityExecutionStatus status = ActivityExecutionStatus.Running) =>
        new(
            new ActivityExecution(
                activityExecutionId,
                workflowExecutionId,
                "node-1",
                "authored-1",
                "Test.Activity",
                "1.0"),
            status,
            null,
            Now,
            Now,
            null,
            null,
            parentId,
            null,
            null,
            null,
            [],
            [],
            0,
            0,
            new Dictionary<string, string>());

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
        public int OpenCount { get; private set; }

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            OpenCount++;
            return new RecordingSession(connection.OpenSession(unit, access), Requests);
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.Equal(ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind, unitId);
            return unit;
        }
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
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);
        public WriteOutcome ConditionalUpsert(StorageValues values, WriteOptions? options = null) =>
            inner is IConcurrencyStorageSession concurrency
                ? concurrency.ConditionalUpsert(values, options)
                : throw new NotSupportedException();
    }

    private static IActivityExecutionStateStore NewInterleavingStore(
        InterleavingSession session,
        StorageUnit unit) =>
        new GroundworkV2ActivityExecutionStateStore(
            new InterleavingSessionSource(session, unit),
            Access("tenant-a"));

    private sealed class InterleavingSessionSource(InterleavingSession session, StorageUnit unit)
        : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return session;
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.Equal(ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind, unitId);
            return unit;
        }
    }

    private sealed class InterleavingSession(StorageUnit unit) : SynchronousStorageSessionTestDouble, IStorageSession, IConcurrencyStorageSession
    {
        private readonly Dictionary<string, StoredEntry> entries = new(StringComparer.Ordinal);

        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access { get; } = StorageAccess.Scoped(new StorageScope("tenant-a"));
        public bool FailInsert { get; set; }
        public bool FailConditionalUpsert { get; set; }
        public bool UnconditionalUpsertCalled { get; private set; }
        public WriteOptions? LastInsertOptions { get; private set; }
        public WriteOptions? LastConditionalOptions { get; private set; }

        public StoredEntry? Read(StorageKey key) => entries.GetValueOrDefault(Id(key));
        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) => throw new NotSupportedException();
        public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();

        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null)
        {
            LastInsertOptions = options;
            var id = Id(values);
            if (FailInsert || entries.ContainsKey(id))
                return new WriteOutcome(WriteOutcomeStatus.ConcurrencyConflict, 0);
            entries[id] = new StoredEntry(values, 1);
            return new WriteOutcome(WriteOutcomeStatus.Inserted, 1);
        }

        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null)
        {
            UnconditionalUpsertCalled = options?.Precondition.Kind is WritePreconditionKind.Unconditional;
            return new WriteOutcome(WriteOutcomeStatus.Upserted, 1);
        }
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw new NotSupportedException();
        public WriteOutcome ConditionalUpsert(StorageValues values, WriteOptions? options = null)
        {
            LastConditionalOptions = options;
            if (FailConditionalUpsert)
                return new WriteOutcome(WriteOutcomeStatus.ConcurrencyConflict, 0);
            var id = Id(values);
            var current = entries[id];
            entries[id] = new StoredEntry(values, current.Version.GetValueOrDefault() + 1);
            return new WriteOutcome(WriteOutcomeStatus.Updated, 1);
        }

        private static string Id(StorageKey key) => (string)key.Values[ElsaRuntimeV2StorageManifest.IdField]!;
        private static string Id(StorageValues values) => (string)values.Values[ElsaRuntimeV2StorageManifest.IdField]!;
    }

    private sealed class NativeSessionSource(IStorageProviderConnection connection)
        : IGroundworkStorageSessionSource, IGroundworkStorageCapabilitySource
    {
        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => connection.Capabilities;

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            connection.OpenSession(ElsaRuntimeV2StorageManifest.Require(unitId), access);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            connection.BeginUnitOfWork(
                access,
                options,
                unitIds.Select(ElsaRuntimeV2StorageManifest.Require).ToArray());

        public StorageUnit Unit(string unitId, string? targetName = null) =>
            ElsaRuntimeV2StorageManifest.Require(unitId);
    }

    private sealed class SqliteRuntime : IAsyncDisposable
    {
        private readonly string database = Path.Combine(
            Path.GetTempPath(),
            $"elsa-runtime-activity-{Guid.NewGuid():N}.db");
        private readonly IStorageProviderConnection connection;
        private readonly DirectSessionSource source;
        private readonly StorageUnit unit;

        public SqliteRuntime()
        {
            connection = new SqliteProviderFactory().Create($"Data Source={database}");
            unit = ElsaRuntimeV2StorageManifest.Require(
                ElsaRuntimeV2StorageManifest.ActivityExecutionStateDocumentKind);
            connection.Schema.Apply(unit);
            source = new DirectSessionSource(connection, unit);
        }

        public IActivityExecutionStateStore Store(string scope) =>
            new GroundworkV2ActivityExecutionStateStore(source, Access(scope));

        public IActivityExecutionStateStore Store(PersistenceAccessContext context) =>
            new GroundworkV2ActivityExecutionStateStore(source, new TestAccessContextAccessor(context));

        public int OpenCount => source.OpenCount;
        public IReadOnlyList<QueryRequest> Requests => source.Requests;

        public void ClearRequests() => source.Requests.Clear();

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
