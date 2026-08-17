using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
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

public sealed class GroundworkV2WorkflowHoldStateStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sqlite_save_find_replace_lists_global_records_and_isolates_scopes()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var scoped = runtime.Store(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var otherScope = runtime.Store(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b")));
        var global = runtime.Store(PersistenceAccessContext.Global);
        var acrossScopes = runtime.Store(PersistenceAccessContext.PrivilegedAcrossScopes(
            new PersistenceAccessPurpose("test.workflow-hold-across-scopes-refusal")));
        var workflowState = State("hold-1", "workflow-1");
        var globalState = State("hold-global", null);

        var opensBeforeRefusal = runtime.OpenCount;
        await Assert.ThrowsAsync<InvalidOperationException>(() => global.SaveAsync(workflowState).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => global.FindAsync(workflowState.ControlPlaneStateId).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => global.ListAllAsync().AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => acrossScopes.ListAllAsync().AsTask());
        Assert.Equal(opensBeforeRefusal, runtime.OpenCount);

        Assert.Same(workflowState, await scoped.SaveAsync(workflowState));
        Assert.Same(globalState, await scoped.SaveAsync(globalState));
        var replacement = State(workflowState.ControlPlaneStateId, workflowState.WorkflowExecutionId, released: true);
        Assert.Same(replacement, await scoped.SaveAsync(replacement));

        AssertState(replacement, await scoped.FindAsync(workflowState.ControlPlaneStateId));
        Assert.Null(await otherScope.FindAsync(workflowState.ControlPlaneStateId));
        Assert.Equal([replacement.ControlPlaneStateId],
            (await scoped.ListForWorkflowExecutionAsync("workflow-1")).Select(state => state.ControlPlaneStateId));
        var all = await scoped.ListAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, state => state.WorkflowExecutionId is null);
        Assert.Contains(all, state => state.ControlPlaneStateId == replacement.ControlPlaneStateId);
    }

    [Fact]
    public async Task Sqlite_lists_are_bounded_keyset_ordered_and_workflow_query_excludes_global_state()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store("tenant-a");
        for (var index = 0; index <= RuntimeStorePageRequest.MaximumLimit; index++)
            await store.SaveAsync(State($"hold-{index:D4}", "workflow-page"));
        await store.SaveAsync(State("hold-global-a", null));
        await store.SaveAsync(State("hold-global-b", null));

        var workflow = await store.ListForWorkflowExecutionAsync("workflow-page");
        var all = await store.ListAllAsync();

        Assert.Equal(RuntimeStorePageRequest.MaximumLimit + 1, workflow.Count);
        Assert.Equal(RuntimeStorePageRequest.MaximumLimit + 3, all.Count);
        Assert.Equal("hold-0000", workflow.First().ControlPlaneStateId);
        Assert.Equal($"hold-{RuntimeStorePageRequest.MaximumLimit:D4}", workflow.Last().ControlPlaneStateId);
        Assert.DoesNotContain(workflow, state => state.WorkflowExecutionId is null);
        Assert.Contains(all, state => state.ControlPlaneStateId == "hold-global-a");
        Assert.Contains(all, state => state.ControlPlaneStateId == "hold-global-b");
    }

    [Fact]
    public async Task Sqlite_reads_refuse_schema_content_and_projection_drift()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);

        var schemaState = State("hold-schema", "workflow-corrupt");
        var schemaValues = Values(schemaState);
        schemaValues[ElsaRuntimeV2StorageManifest.SchemaVersionField] = "0.9.0";
        runtime.InsertRaw(new StorageValues(schemaValues), "tenant-a");
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.Store("tenant-a")
            .FindAsync(schemaState.ControlPlaneStateId).AsTask());

        var contentState = State("hold-content", "workflow-corrupt");
        var contentValues = Values(contentState);
        contentValues[ElsaRuntimeV2StorageManifest.ContentField] = "{}";
        runtime.InsertRaw(new StorageValues(contentValues), "tenant-a");
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.Store("tenant-a")
            .FindAsync(contentState.ControlPlaneStateId).AsTask());

        var projectionState = State("hold-projection", "workflow-corrupt");
        var projectionValues = Values(projectionState);
        projectionValues[ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = "workflow-other";
        runtime.InsertRaw(new StorageValues(projectionValues), "tenant-a");
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.Store("tenant-a")
            .FindAsync(projectionState.ControlPlaneStateId).AsTask());

        var collectionState = State("hold-collection", "workflow-corrupt");
        var collectionValues = Values(collectionState);
        collectionValues[ElsaRuntimeV2StorageManifest.CollectionField] = "wrong-collection";
        runtime.InsertRaw(new StorageValues(collectionValues), "tenant-a");
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.Store("tenant-a")
            .FindAsync(collectionState.ControlPlaneStateId).AsTask());
    }

    [Fact]
    public async Task Concurrent_save_uses_create_only_and_if_version_without_unconditional_fallback()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.WorkflowHoldStateDocumentKind);
        var createSession = new InterleavingSession(unit) { FailInsert = true };
        var createStore = NewInterleavingStore(createSession, unit);
        var createException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            createStore.SaveAsync(State("hold-create-race", "workflow-race")).AsTask());
        Assert.Contains("lost a concurrent write; retry", createException.Message, StringComparison.Ordinal);
        Assert.Equal(WritePreconditionKind.CreateOnly, createSession.LastInsertOptions!.Precondition.Kind);

        var updateSession = new InterleavingSession(unit);
        var updateStore = NewInterleavingStore(updateSession, unit);
        await updateStore.SaveAsync(State("hold-update-race", "workflow-race"));
        updateSession.FailConditionalUpsert = true;
        var updateException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            updateStore.SaveAsync(State("hold-update-race", "workflow-race", released: true)).AsTask());
        Assert.Contains("lost a concurrent write; retry", updateException.Message, StringComparison.Ordinal);
        Assert.Equal(WritePreconditionKind.IfVersion, updateSession.LastConditionalOptions!.Precondition.Kind);
        Assert.Equal(1, updateSession.LastConditionalOptions.Precondition.Version);
        Assert.False(updateSession.UnconditionalUpsertCalled);
    }

    [Fact]
    public async Task Sqlite_state_survives_provider_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-workflow-hold-v2-{Guid.NewGuid():N}.db");
        try
        {
            await using (var first = NativeProviderRuntime.Create("sqlite", path))
                await first.Store("tenant-a").SaveAsync(State("hold-restart", "workflow-restart"));

            await using var restarted = NativeProviderRuntime.Create("sqlite", path);
            AssertState(
                State("hold-restart", "workflow-restart"),
                await restarted.Store("tenant-a").FindAsync("hold-restart"));
        }
        finally
        {
            foreach (var file in new[] { path, $"{path}-shm", $"{path}-wal", $"{path}-journal", $"{path}.schema.lock" })
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
        }
    }

    [Fact]
    public async Task Continuation_cycles_are_rejected_before_unbounded_enumeration()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.WorkflowHoldStateDocumentKind);
        var state = State("hold-cycle", "workflow-cycle");
        var session = new CyclingSession(unit, GroundworkV2WorkflowHoldStateStorageConventions.Values(state));
        var store = new GroundworkV2WorkflowHoldStateStore(
            new FakeSessionSource(session, unit),
            new FixedAccessContextAccessor(
                PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.ListForWorkflowExecutionAsync("workflow-cycle").AsTask());
        Assert.Equal(2, session.QueryCount);
    }

    [SkippableTheory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("mongodb")]
    public async Task Configured_native_provider_preserves_workflow_hold_contract(string providerName)
    {
        var connectionString = Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        Skip.If(
            string.IsNullOrWhiteSpace(connectionString),
            $"Set {EnvironmentVariable(providerName)} to run the {providerName} workflow-hold gate.");

        await using var runtime = NativeProviderRuntime.Create(providerName, connectionString);
        var store = runtime.Store("tenant-a");
        var workflowState = State("hold-native", "workflow-native");
        var globalState = State("hold-native-global", null);

        Assert.Same(workflowState, await store.SaveAsync(workflowState));
        Assert.Same(globalState, await store.SaveAsync(globalState));
        AssertState(workflowState, await store.FindAsync(workflowState.ControlPlaneStateId));
        Assert.Single(await store.ListForWorkflowExecutionAsync(workflowState.WorkflowExecutionId!));
        Assert.Equal(2, (await store.ListAllAsync()).Count);
    }

    private static WorkflowHoldState State(
        string controlPlaneStateId,
        string? workflowExecutionId,
        bool released = false)
    {
        var hold = workflowExecutionId is not null
            ? released
                ? new WorkflowHold(
                    $"{controlPlaneStateId}-hold",
                    WorkflowHoldScope.WorkflowExecution,
                    WorkflowHoldStatus.Released,
                    Now,
                    "operator",
                    "maintenance",
                    workflowExecutionId: workflowExecutionId,
                    releasedAt: Now.AddMinutes(1),
                    releasedBy: "operator",
                    metadata: new Dictionary<string, string> { ["source"] = "test" })
                : WorkflowHold.ForWorkflowExecution(
                    $"{controlPlaneStateId}-hold",
                    workflowExecutionId,
                    Now,
                    "operator",
                    "maintenance",
                    new Dictionary<string, string> { ["source"] = "test" })
            : WorkflowHold.ForHostDrain(
                $"{controlPlaneStateId}-hold",
                "host-1",
                Now,
                "operator",
                "maintenance",
                new Dictionary<string, string> { ["source"] = "test" });

        return new WorkflowHoldState(
            controlPlaneStateId,
            workflowExecutionId,
            activeHolds: released ? [] : [hold],
            releasedHolds: released ? [hold] : [],
            metadata: new Dictionary<string, string> { ["state"] = "current" });
    }

    private static void AssertState(WorkflowHoldState expected, WorkflowHoldState? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.ControlPlaneStateId, actual!.ControlPlaneStateId);
        Assert.Equal(expected.WorkflowExecutionId, actual.WorkflowExecutionId);
        Assert.Equal(expected.ActiveHolds.Count, actual.ActiveHolds.Count);
        Assert.Equal(expected.ReleasedHolds.Count, actual.ReleasedHolds.Count);
        Assert.Equal(expected.Metadata, actual.Metadata);
        var expectedHolds = expected.ActiveHolds.Concat(expected.ReleasedHolds).ToArray();
        var actualHolds = actual.ActiveHolds.Concat(actual.ReleasedHolds).ToArray();
        Assert.Equal(expectedHolds.Select(hold => hold.HoldId), actualHolds.Select(hold => hold.HoldId));
        Assert.Equal(expectedHolds.Select(hold => hold.Status), actualHolds.Select(hold => hold.Status));
        Assert.Equal(expectedHolds.Select(hold => hold.WorkflowExecutionId), actualHolds.Select(hold => hold.WorkflowExecutionId));
    }

    private static Dictionary<string, object?> Values(WorkflowHoldState state) =>
        GroundworkV2WorkflowHoldStateStorageConventions.Values(state).Values
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static string EnvironmentVariable(string providerName) =>
        $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING";

    private static GroundworkV2WorkflowHoldStateStore NewInterleavingStore(
        InterleavingSession session,
        StorageUnit unit) =>
        new(
            new FakeSessionSource(session, unit),
            new FixedAccessContextAccessor(
                PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));

    private static IStorageProviderConnection CreateConnection(
        string providerName,
        string connectionString) => providerName switch
        {
            "sqlite" => new SqliteProviderFactory().Create(connectionString),
            "postgresql" => new PostgreSqlProviderFactory().Create(connectionString),
            "sqlserver" => new SqlServerProviderFactory().Create(connectionString),
            "mongodb" => new MongoProviderFactory().Create(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
        };

    private sealed class NativeProviderRuntime : IAsyncDisposable
    {
        private readonly IStorageProviderConnection connection;
        private readonly DirectSessionSource source;
        private readonly StorageUnit unit;
        private readonly string? sqlitePath;

        private NativeProviderRuntime(
            IStorageProviderConnection connection,
            StorageUnit unit,
            string? sqlitePath)
        {
            this.connection = connection;
            this.unit = unit;
            this.sqlitePath = sqlitePath;
            connection.Schema.Apply(unit);
            source = new DirectSessionSource(connection, unit);
        }

        public static NativeProviderRuntime Create(string providerName, string? connectionString)
        {
            string? sqlitePath = null;
            if (providerName == "sqlite")
            {
                sqlitePath = connectionString ??
                             Path.Combine(Path.GetTempPath(), $"elsa-workflow-hold-v2-{Guid.NewGuid():N}.db");
                connectionString = $"Data Source={sqlitePath}";
            }

            var connection = CreateConnection(providerName, connectionString!);
            var declaredUnit = ElsaRuntimeV2StorageManifest.Require(
                ElsaRuntimeV2StorageManifest.WorkflowHoldStateDocumentKind);
            var suffix = Guid.NewGuid().ToString("N")[..12];
            var unit = providerName == "sqlite"
                ? declaredUnit
                : declaredUnit with
                {
                    Id = new StorageUnitId($"{declaredUnit.Id.Value}-{suffix}"),
                    Name = $"{declaredUnit.Name}_{suffix}"
                };
            return new NativeProviderRuntime(connection, unit, sqlitePath);
        }

        public int OpenCount => source.OpenCount;

        public GroundworkV2WorkflowHoldStateStore Store(string scope) =>
            new(
                source,
                new FixedAccessContextAccessor(
                    PersistenceAccessContext.Scoped(new PersistenceScope(scope))));

        public GroundworkV2WorkflowHoldStateStore Store(PersistenceAccessContext context) =>
            new(source, new FixedAccessContextAccessor(context));

        public void InsertRaw(StorageValues values, string scope)
        {
            var outcome = connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope(scope)))
                .Insert(values, WriteOptions.CreateOnly);
            Assert.Equal(WriteOutcomeStatus.Inserted, outcome.Status);
        }

        public ValueTask DisposeAsync()
        {
            connection.Dispose();
            if (sqlitePath is not null)
            {
                foreach (var file in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal", $"{sqlitePath}-journal", $"{sqlitePath}.schema.lock" })
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class DirectSessionSource(IStorageProviderConnection connection, StorageUnit unit)
        : IGroundworkStorageSessionSource
    {
        public int OpenCount { get; private set; }

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.True(
                StringComparer.Ordinal.Equals(unitId, unit.Id.Value) ||
                StringComparer.Ordinal.Equals(unitId, ElsaRuntimeV2StorageManifest.WorkflowHoldStateDocumentKind));
            OpenCount++;
            return connection.OpenSession(unit, access);
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.True(
                StringComparer.Ordinal.Equals(unitId, unit.Id.Value) ||
                StringComparer.Ordinal.Equals(unitId, ElsaRuntimeV2StorageManifest.WorkflowHoldStateDocumentKind));
            return unit;
        }
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class FakeSessionSource(IStorageSession session, StorageUnit unit)
        : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) => session;

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null) => unit;
    }

    private sealed class InterleavingSession(StorageUnit unit) : IStorageSession, IConcurrencyStorageSession
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
        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) =>
            throw new NotSupportedException();
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

        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) =>
            throw new NotSupportedException();

        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null)
        {
            UnconditionalUpsertCalled = options?.Precondition.Kind is WritePreconditionKind.Unconditional;
            return new WriteOutcome(WriteOutcomeStatus.Upserted, 1);
        }

        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) =>
            throw new NotSupportedException();
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) =>
            throw new NotSupportedException();

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

    private sealed class CyclingSession(StorageUnit unit, StorageValues row)
        : IStorageSession
    {
        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access { get; } = StorageAccess.Scoped(new StorageScope("tenant-a"));
        public int QueryCount { get; private set; }
        public StoredEntry? Read(StorageKey key) => null;

        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
        {
            QueryCount++;
            return new QueryMaterializedResult([row.Values], null, "cycle");
        }

        public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw new NotSupportedException();
    }
}
