using System.Text.Json;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2DurableValueStateStoreTests
{
    [Fact]
    public void The_v2_store_implements_the_public_durable_value_contract()
    {
        Assert.Contains(
            typeof(IDurableValueStateStore),
            typeof(GroundworkV2DurableValueStateStore).GetInterfaces());
    }

    [Fact]
    public async Task Sqlite_round_trips_pages_isolates_scopes_and_preserves_same_ids_across_workflows()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.DurableValueStateDocumentKind);
        connection.Schema.Apply(unit);

        var source = new DirectSessionSource(connection, unit);
        var scopeA = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var scopeB = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b")));
        IDurableValueStateStore storeA = new GroundworkV2DurableValueStateStore(source, scopeA);
        IDurableValueStateStore storeB = new GroundworkV2DurableValueStateStore(source, scopeB);

        await storeA.SaveAsync(DurableValue("wf-1", "value-b", "one"));
        await storeA.SaveAsync(DurableValue("wf-1", "value-a", "two"));
        await storeA.SaveAsync(DurableValue("wf-2", "value-a", "three"));
        await storeB.SaveAsync(DurableValue("wf-1", "value-a", "isolated"));

        Assert.Equal("two", InlineValue(await storeA.FindAsync("wf-1", "value-a")));
        Assert.Equal("three", InlineValue(await storeA.FindAsync("wf-2", "value-a")));
        Assert.Equal("isolated", InlineValue(await storeB.FindAsync("wf-1", "value-a")));

        var first = await storeA.ListPageAsync(new DurableValueStatePageQuery("wf-1", limit: 1));
        var second = await storeA.ListPageAsync(
            new DurableValueStatePageQuery("wf-1", limit: 1, first.NextContinuationToken));
        Assert.Equal(["value-a"], first.Items.Select(value => value.DurableValueId));
        Assert.Equal(["value-b"], second.Items.Select(value => value.DurableValueId));
        Assert.Null(second.NextContinuationToken);

        Assert.True(await storeA.DeleteAsync("wf-1", "value-a"));
        Assert.Null(await storeA.FindAsync("wf-1", "value-a"));
        Assert.Equal("three", InlineValue(await storeA.FindAsync("wf-2", "value-a")));
    }

    [Fact]
    public async Task Save_writes_canonical_content_and_manifest_projections_at_the_identity_boundary()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.DurableValueStateDocumentKind);
        connection.Schema.Apply(unit);

        var workflowExecutionId = new string('w', ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength);
        var durableValueId = new string('v', ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength);
        var source = new DirectSessionSource(connection, unit);
        var accessor = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDurableValueStateStore store = new GroundworkV2DurableValueStateStore(source, accessor);
        var state = DurableValue(workflowExecutionId, durableValueId, "boundary");

        await store.SaveAsync(state);

        var physicalId = CompositeIdentity(workflowExecutionId, durableValueId);
        Assert.Equal(264, physicalId.Length);
        Assert.True(physicalId.Length <= ElsaRuntimeV2StorageManifest.IdMaximumLength);
        var entry = source.Open(
                unit.Id.Value,
                StorageAccess.Scoped(new StorageScope("tenant-a")))
            .Read(GroundworkRuntimeRowStore.Key(physicalId));
        Assert.NotNull(entry);
        Assert.Equal(
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            ReadString(entry!, ElsaRuntimeV2StorageManifest.SchemaVersionField));
        Assert.Equal(workflowExecutionId, ReadString(entry, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField));
        Assert.Equal(durableValueId, ReadString(entry, ElsaRuntimeV2StorageManifest.DurableValueIdField));
        var content = ReadJsonText(entry, ElsaRuntimeV2StorageManifest.ContentField);
        Assert.Equal("boundary", JsonDocument.Parse(content).RootElement.GetProperty("inlineValue").GetProperty("value").GetString());
        Assert.NotNull(await store.FindAsync(workflowExecutionId, durableValueId));
    }

    [Fact]
    public async Task Workflow_pages_use_keyset_order_and_exact_manifest_column_metadata()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.DurableValueStateDocumentKind);
        connection.Schema.Apply(unit);
        var requests = new List<QueryRequest>();
        var source = new RecordingSessionSource(connection, unit, requests);
        var accessor = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var store = new GroundworkV2DurableValueStateStore(source, accessor);

        var page = await store.ListPageAsync(new DurableValueStatePageQuery("wf-1", limit: 7));
        Assert.Empty(page.Items);
        var request = Assert.Single(requests);
        Assert.Equal(7, request.Paging.Limit);
        Assert.Equal([ElsaRuntimeV2StorageManifest.DurableValueIdField], request.Order.Select(term => term.Column.Name));
        var equality = Assert.IsType<Predicate.Equal>(request.Where);
        Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, equality.Column.Name);
        Assert.Equal(QueryType.String, equality.Value.Type);
        AssertColumnMatchesManifest(unit, equality.Column);
        AssertColumnMatchesManifest(unit, request.Order[0].Column);
    }

    [Fact]
    public async Task Global_and_across_scope_access_are_refused_before_opening_a_session()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.DurableValueStateDocumentKind);
        var session = new InterleavingSession(unit);
        var source = new InterleavingSessionSource(session, unit);
        var globalStore = new GroundworkV2DurableValueStateStore(
            source,
            new TestAccessContextAccessor(PersistenceAccessContext.Global));
        var acrossScopesStore = new GroundworkV2DurableValueStateStore(
            source,
            new TestAccessContextAccessor(
                PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("test"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            globalStore.FindAsync("wf-1", "value-a").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            acrossScopesStore.FindAsync("wf-1", "value-a").AsTask());
        Assert.False(session.ReadWasCalled);
    }

    [Fact]
    public async Task Concurrent_save_and_delete_use_create_only_and_if_version_without_unconditional_fallback()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.DurableValueStateDocumentKind);
        var createRaceSession = new InterleavingSession(unit) { FailInsert = true };
        var createRaceStore = NewInterleavingStore(createRaceSession, unit);
        var createException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            createRaceStore.SaveAsync(DurableValue("wf-create", "value-a", "create")).AsTask());
        Assert.Contains("lost a concurrent write; retry", createException.Message, StringComparison.Ordinal);
        Assert.Equal(WritePreconditionKind.CreateOnly, createRaceSession.LastInsertOptions!.Precondition.Kind);

        var session = new InterleavingSession(unit);
        var store = NewInterleavingStore(session, unit);
        var state = DurableValue("wf-1", "value-a", "one");
        await store.SaveAsync(state);
        session.FailConditionalUpsert = true;
        var saveException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(DurableValue("wf-1", "value-a", "two")).AsTask());
        Assert.Contains("lost a concurrent write; retry", saveException.Message, StringComparison.Ordinal);
        Assert.Equal(WritePreconditionKind.IfVersion, session.LastConditionalOptions!.Precondition.Kind);
        Assert.Equal(1, session.LastConditionalOptions.Precondition.Version);
        Assert.False(session.UnconditionalUpsertCalled);

        session.FailDelete = true;
        Assert.False(await store.DeleteAsync("wf-1", "value-a"));
        Assert.Equal(WritePreconditionKind.IfVersion, session.LastDeleteOptions!.Precondition.Kind);
        Assert.Equal(1, session.LastDeleteOptions.Precondition.Version);
        Assert.False(session.UnconditionalDeleteCalled);
        Assert.Equal("one", InlineValue(await store.FindAsync("wf-1", "value-a")));
    }

    [SkippableFact]
    [Trait("Category", "Sqlite")]
    public async Task Checkpoint_writer_and_direct_store_share_the_composite_identity()
    {
        var database = Path.Combine(Path.GetTempPath(), $"elsa-runtime-durable-value-checkpoint-{Guid.NewGuid():N}.db");
        try
        {
            using var connection = new SqliteProviderFactory().Create($"Data Source={database}");
            Skip.If(
                !connection.Capabilities.Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit)),
                "The installed SQLite Groundwork package does not evidence AtomicCommit; run with the preview.3 candidate for this vertical gate.");
            foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
                connection.Schema.Apply(unit);

            var source = new NativeSessionSource(connection);
            var accessor = new TestAccessContextAccessor(
                PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            IDurableValueStateStore store = new GroundworkV2DurableValueStateStore(source, accessor);
            var otherWorkflowState = DurableValue("workflow-2", "value-shared", "other");
            await store.SaveAsync(otherWorkflowState);

            var state = DurableValue("workflow-1", "value-shared", "writer");
            var writer = new GroundworkV2RuntimeCheckpointWriter(source, accessor);
            await writer.CommitAsync(
                NewCommit("durable-value-composite-upsert", [new RuntimeStateChange<DurableValueState>(
                    state.DurableValueId,
                    RuntimeStateChangeOperation.Upsert,
                    state,
                    new Dictionary<string, string>())]),
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

            var session = source.Open(
                ElsaRuntimeV2StorageManifest.DurableValueStateDocumentKind,
                StorageAccess.Scoped(new StorageScope("tenant-a")));
            Assert.NotNull(session.Read(GroundworkRuntimeRowStore.Key(CompositeIdentity(state.WorkflowExecutionId, state.DurableValueId))));
            Assert.Null(session.Read(GroundworkRuntimeRowStore.Key(state.DurableValueId)));
            Assert.Equal("writer", InlineValue(await store.FindAsync(state.WorkflowExecutionId, state.DurableValueId)));
            Assert.Equal("other", InlineValue(await store.FindAsync(otherWorkflowState.WorkflowExecutionId, otherWorkflowState.DurableValueId)));

            await writer.CommitAsync(
                NewCommit("durable-value-composite-delete", [new RuntimeStateChange<DurableValueState>(
                    state.DurableValueId,
                    RuntimeStateChangeOperation.Delete,
                    state,
                    new Dictionary<string, string>())]),
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

            Assert.Null(await store.FindAsync(state.WorkflowExecutionId, state.DurableValueId));
            Assert.Equal("other", InlineValue(await store.FindAsync(otherWorkflowState.WorkflowExecutionId, otherWorkflowState.DurableValueId)));
        }
        finally
        {
            foreach (var path in new[] { database, $"{database}-shm", $"{database}-wal" })
                if (File.Exists(path))
                    File.Delete(path);
        }
    }

    private static GroundworkV2DurableValueStateStore NewInterleavingStore(
        InterleavingSession session,
        StorageUnit unit) =>
        new(
            new InterleavingSessionSource(session, unit),
            new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));

    private static DurableValueState DurableValue(
        string workflowExecutionId,
        string durableValueId,
        string value) =>
        new(
            durableValueId,
            workflowExecutionId,
            $"value:{durableValueId}",
            new RuntimeValueTypeDescriptor("reference", "test.value", null),
            DurableValueLifecycle.Instance,
            DurableValueStorage.Inline,
            JsonSerializer.SerializeToElement(new { value }),
            null,
            "activity-1",
            new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero),
            new Dictionary<string, string> { ["tag"] = "v2" });

    private static string InlineValue(DurableValueState? state) =>
        state?.InlineValue?.GetProperty("value").GetString()
        ?? throw new InvalidOperationException("Durable value state did not contain an inline value.");

    private static string CompositeIdentity(string workflowExecutionId, string durableValueId) =>
        $"{workflowExecutionId.Length}:{workflowExecutionId}{durableValueId.Length}:{durableValueId}";

    private static string ReadString(StoredEntry entry, string field) =>
        entry.Values.Values[field] switch
        {
            string value => value,
            JsonElement { ValueKind: JsonValueKind.String } value => value.GetString()!,
            _ => throw new InvalidOperationException($"Field '{field}' was not a string.")
        };

    private static string ReadJsonText(StoredEntry entry, string field) =>
        entry.Values.Values[field] switch
        {
            string value => value,
            JsonElement value => value.GetRawText(),
            JsonDocument value => value.RootElement.GetRawText(),
            _ => throw new InvalidOperationException($"Field '{field}' was not JSON.")
        };

    private static void AssertColumnMatchesManifest(StorageUnit unit, ColumnRef column)
    {
        var definition = unit.Columns.Single(candidate => candidate.Name == column.Name);
        Assert.Equal(definition.IsNullable, column.IsNullable);
        Assert.Equal(definition.MaxLength, column.MaxLength);
    }

    private static RuntimeCheckpointCommit NewCommit(
        string commitId,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValues) =>
        new(
            commitId,
            new RuntimeCheckpoint(
                $"checkpoint-{commitId}",
                "runtime",
                "workflow-1",
                DateTimeOffset.UtcNow,
                [],
                new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(null, null, [], [], durableValues, [], []),
            [],
            new Dictionary<string, string>());

    private sealed class TestAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class DirectSessionSource(IStorageProviderConnection connection, StorageUnit unit) : IGroundworkStorageSessionSource
    {
        private readonly Dictionary<StorageAccess, IStorageSession> sessions = [];

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            if (sessions.TryGetValue(access, out var session))
                return session;
            session = connection.OpenSession(unit, access);
            sessions.Add(access, session);
            return session;
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            connection.BeginUnitOfWork(
                access,
                options,
                unitIds.Select(ElsaRuntimeV2StorageManifest.Require).ToArray());

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return unit;
        }
    }

    private sealed class RecordingSessionSource(
        IStorageProviderConnection connection,
        StorageUnit unit,
        ICollection<QueryRequest> requests) : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return new RecordingSession(connection.OpenSession(unit, access), requests);
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return unit;
        }
    }

    private sealed class RecordingSession(IStorageSession inner, ICollection<QueryRequest> requests) : SynchronousStorageSessionTestDouble, IStorageSession
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
    }

    private sealed class InterleavingSessionSource(InterleavingSession session, StorageUnit unit) : IGroundworkStorageSessionSource
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
            Assert.Equal(unit.Id.Value, unitId);
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
        public bool FailDelete { get; set; }
        public bool UnconditionalUpsertCalled { get; private set; }
        public bool UnconditionalDeleteCalled { get; private set; }
        public WriteOptions? LastInsertOptions { get; private set; }
        public WriteOptions? LastConditionalOptions { get; private set; }
        public WriteOptions? LastDeleteOptions { get; private set; }
        public bool ReadWasCalled { get; private set; }

        public StoredEntry? Read(StorageKey key)
        {
            ReadWasCalled = true;
            return entries.GetValueOrDefault(Id(key));
        }
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

        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null)
        {
            LastDeleteOptions = options;
            UnconditionalDeleteCalled = options?.Precondition.Kind is WritePreconditionKind.Unconditional;
            if (FailDelete)
                return new WriteOutcome(WriteOutcomeStatus.ConcurrencyConflict, 0);
            entries.Remove(Id(key));
            return new WriteOutcome(WriteOutcomeStatus.Deleted, 1);
        }

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

    private sealed class NativeSessionSource(IStorageProviderConnection connection) : IGroundworkStorageSessionSource, IGroundworkStorageCapabilitySource
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

        public StorageUnit Unit(string unitId, string? targetName = null) => ElsaRuntimeV2StorageManifest.Require(unitId);
    }

    private sealed class NativeProviderRuntime(string path) : IAsyncDisposable
    {
        public static NativeProviderRuntime Create() =>
            new(Path.Combine(Path.GetTempPath(), $"elsa-runtime-durable-value-{Guid.NewGuid():N}.db"));

        public IStorageProviderConnection OpenConnection() =>
            new SqliteProviderFactory().Create($"Data Source={path}");

        public ValueTask DisposeAsync()
        {
            foreach (var candidate in new[] { path, $"{path}-shm", $"{path}-wal" })
                if (File.Exists(candidate))
                    File.Delete(candidate);
            return ValueTask.CompletedTask;
        }
    }
}
