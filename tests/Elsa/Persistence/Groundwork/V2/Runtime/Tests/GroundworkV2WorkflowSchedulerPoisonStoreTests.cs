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

public sealed class GroundworkV2WorkflowSchedulerPoisonStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sqlite_record_find_list_replace_restart_and_tenant_isolation_use_public_api()
    {
        string database;
        {
            await using var runtime = NativeProviderRuntime.Create("sqlite", null);
            database = runtime.SqlitePath!;
            var tenantA = runtime.Store("tenant-a");
            var tenantB = runtime.Store("tenant-b");

            await tenantA.RecordAsync(Record(2));
            await tenantA.RecordAsync(Record(1));
            await tenantB.RecordAsync(Record(9));

            var replacement = Record(1, message: "replaced", failureCount: 3);
            await tenantA.RecordAsync(replacement);
            var found = await tenantA.FindAsync("workflow-1", "work-1");
            Assert.NotNull(found);
            Assert.Equal("replaced", found!.Fault.Message);
            Assert.Equal(3, found.FailureCount);
            Assert.Equal(["work-1", "work-2"], (await tenantA.ListAsync("workflow-1")).Select(item => item.WorkItemId));
            Assert.Equal(["work-9"], (await tenantB.ListAsync("workflow-1")).Select(item => item.WorkItemId));
        }

        await using var reopened = NativeProviderRuntime.Create("sqlite", database);
        var reopenedExpected = Record(1, message: "replaced", failureCount: 3);
        var recovered = await reopened.Store("tenant-a").FindAsync("workflow-1", "work-1");
        Assert.NotNull(recovered);
        Assert.Equal(reopenedExpected.Fault.Message, recovered!.Fault.Message);
        Assert.Equal(reopenedExpected.FailureCount, recovered.FailureCount);
    }

    [Fact]
    public async Task Sqlite_current_json_round_trip_preserves_retry_inner_fault_and_metadata()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store("tenant-a");
        var record = Record(
            1,
            disposition: RuntimeSchedulerPoisonDisposition.RetryScheduled,
            nextRetryAt: Now.AddMinutes(5));

        await store.RecordAsync(record);
        var actual = await store.FindAsync(record.WorkflowExecutionId, record.WorkItemId);

        Assert.NotNull(actual);
        Assert.Equal(record.WorkflowExecutionId, actual!.WorkflowExecutionId);
        Assert.Equal(record.WorkItemId, actual.WorkItemId);
        Assert.Equal(record.CommandKind, actual.CommandKind);
        Assert.Equal(record.HandlerName, actual.HandlerName);
        Assert.Equal(record.Fault, actual.Fault);
        Assert.Equal(record.FailureCount, actual.FailureCount);
        Assert.Equal(record.Disposition, actual.Disposition);
        Assert.Equal(record.FirstFailedAt, actual.FirstFailedAt);
        Assert.Equal(record.LastFailedAt, actual.LastFailedAt);
        Assert.Equal(record.NextRetryAt, actual.NextRetryAt);
        Assert.Equal(record.Metadata, actual.Metadata);
        Assert.Equal(record.InnerFault, actual!.InnerFault);
        Assert.Equal("work-1", actual.Metadata["payload"]);
    }

    [Fact]
    public async Task Sqlite_separator_and_long_identities_are_injective_and_collision_fails_closed()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store("tenant-a");
        var first = Record(1, workflowExecutionId: "a:b", workItemId: "c");
        var second = Record(2, workflowExecutionId: "a", workItemId: "b:c");
        var longWorkflow = new string('w', ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength);
        var longWorkItem = new string('i', ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength);
        var longRecord = Record(3, workflowExecutionId: longWorkflow, workItemId: longWorkItem);

        await store.RecordAsync(first);
        await store.RecordAsync(second);
        await store.RecordAsync(longRecord);

        Assert.Single(await store.ListAsync(first.WorkflowExecutionId));
        Assert.Single(await store.ListAsync(second.WorkflowExecutionId));
        var longActual = await store.FindAsync(longWorkflow, longWorkItem);
        Assert.NotNull(longActual);
        Assert.Equal(longRecord.WorkflowExecutionId, longActual!.WorkflowExecutionId);
        Assert.Equal(longRecord.WorkItemId, longActual.WorkItemId);

        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerPoisonDocumentKind);
        var session = runtime.Open(unit, "tenant-a");
        var physicalId = GroundworkV2WorkflowSchedulerPoisonStorageConventions.PhysicalId(
            longWorkflow,
            longWorkItem);
        var forged = Record(4, workflowExecutionId: "foreign", workItemId: "foreign-work");
        var values = GroundworkV2WorkflowSchedulerPoisonStorageConventions.Values(forged).Values
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        values[ElsaRuntimeV2StorageManifest.IdField] = physicalId;
        Assert.Equal(WriteOutcomeStatus.Upserted, session.Upsert(new StorageValues(values), WriteOptions.Unconditional).Status);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.FindAsync(longWorkflow, longWorkItem).AsTask());
        Assert.Contains("projection", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sqlite_list_is_bounded_ordered_and_rejects_any_continuation_cycle()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store("tenant-a");
        for (var index = 0; index <= RuntimeStorePageRequest.MaximumLimit; index++)
            await store.RecordAsync(Record(index, workflowExecutionId: "workflow-many"));

        var listed = await store.ListAsync("workflow-many");
        Assert.Equal(RuntimeStorePageRequest.MaximumLimit + 1, listed.Count);
        Assert.Equal(2, runtime.Requests.Count);
        Assert.All(runtime.Requests, request => Assert.Equal(RuntimeStorePageRequest.MaximumLimit, request.Paging.Limit));
        Assert.Equal(
            listed.OrderBy(item => item.FirstFailedAt).ThenBy(item => item.LastFailedAt).ThenBy(item => item.WorkItemId, StringComparer.Ordinal),
            listed);

        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerPoisonDocumentKind);
        var cycleRow = GroundworkV2WorkflowSchedulerPoisonStorageConventions.Values(Record(99));
        var cycling = new CyclingSession(unit, cycleRow, ["a", "b", "a"]);
        var cycleStore = new GroundworkV2WorkflowSchedulerPoisonStore(
            new FakeSessionSource(cycling, unit),
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => cycleStore.ListAsync("workflow-1").AsTask());
        Assert.Contains("continuation", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, cycling.QueryCount);
    }

    [Fact]
    public async Task Scope_global_and_across_refusal_happens_before_provider_io()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerPoisonDocumentKind);
        var source = new RecordingSource(unit);
        var record = Record(1);

        foreach (var context in new[]
                 {
                     PersistenceAccessContext.Global,
                     PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("test"))
                 })
        {
            var store = new GroundworkV2WorkflowSchedulerPoisonStore(
                source,
                new FixedAccessContextAccessor(context));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordAsync(record).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindAsync(record.WorkflowExecutionId, record.WorkItemId).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ListAsync(record.WorkflowExecutionId).AsTask());
        }

        var scoped = new GroundworkV2WorkflowSchedulerPoisonStore(
            source,
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            scoped.ListAsync(new string('w', ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength + 1)).AsTask());

        Assert.Equal(0, source.OpenCount);
    }

    [Fact]
    public async Task Schema_content_and_projection_drift_is_rejected()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var record = Record(1);

        var schema = Values(record);
        schema[ElsaRuntimeV2StorageManifest.SchemaVersionField] = "0.9.0";
        runtime.InsertRaw(new StorageValues(schema), "tenant-a");
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.Store("tenant-a").FindAsync(record.WorkflowExecutionId, record.WorkItemId).AsTask());

        var invalidContentRecord = Record(2);
        var content = Values(invalidContentRecord);
        content[ElsaRuntimeV2StorageManifest.ContentField] = "{}";
        runtime.InsertRaw(new StorageValues(content), "tenant-a");
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.Store("tenant-a").FindAsync(invalidContentRecord.WorkflowExecutionId, invalidContentRecord.WorkItemId).AsTask());

        var projectionRecord = Record(3);
        var projection = Values(projectionRecord);
        projection[ElsaRuntimeV2StorageManifest.SchedulerPoisonWorkItemIdField] = "wrong";
        runtime.InsertRaw(new StorageValues(projection), "tenant-a");
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.Store("tenant-a").FindAsync(projectionRecord.WorkflowExecutionId, projectionRecord.WorkItemId).AsTask());
    }

    [Fact]
    public async Task Concurrent_record_uses_create_only_and_if_version_without_unconditional_fallback()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerPoisonDocumentKind);
        var createSession = new InterleavingSession(unit) { FailInsert = true };
        var createStore = NewInterleavingStore(createSession, unit);
        var createException = await Assert.ThrowsAsync<InvalidOperationException>(() => createStore.RecordAsync(Record(1)).AsTask());
        Assert.Contains("did not settle", createException.Message, StringComparison.Ordinal);
        Assert.Equal(WritePreconditionKind.CreateOnly, createSession.LastInsertOptions!.Precondition.Kind);
        Assert.False(createSession.UnconditionalUpsertCalled);

        var session = new InterleavingSession(unit);
        var store = NewInterleavingStore(session, unit);
        await store.RecordAsync(Record(1));
        session.FailConditionalUpsert = true;
        var updateException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RecordAsync(Record(1, message: "raced")).AsTask());
        Assert.Contains("did not settle", updateException.Message, StringComparison.Ordinal);
        Assert.Equal(WritePreconditionKind.IfVersion, session.LastConditionalOptions!.Precondition.Kind);
        Assert.False(session.UnconditionalUpsertCalled);
    }

    [SkippableTheory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("mongodb")]
    public async Task Configured_native_provider_preserves_poison_contract(string providerName)
    {
        var connectionString = Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        Skip.If(string.IsNullOrWhiteSpace(connectionString), $"Set {EnvironmentVariable(providerName)} to run the {providerName} poison-store gate.");

        await using var runtime = NativeProviderRuntime.Create(providerName, connectionString);
        var store = runtime.Store("tenant-native");
        var record = Record(1, workflowExecutionId: "workflow-native");
        Assert.Equal(record, await store.RecordAsync(record));
        var found = await store.FindAsync(record.WorkflowExecutionId, record.WorkItemId);
        Assert.NotNull(found);
        Assert.Equal(record.WorkflowExecutionId, found!.WorkflowExecutionId);
        Assert.Equal(record.WorkItemId, found.WorkItemId);
        Assert.Equal(record.Fault, found.Fault);
        Assert.Equal(record.Metadata, found.Metadata);
        Assert.Equal([record.WorkItemId], (await store.ListAsync(record.WorkflowExecutionId)).Select(item => item.WorkItemId));
    }

    private static RuntimeSchedulerPoisonRecord Record(
        int index,
        string workflowExecutionId = "workflow-1",
        string? workItemId = null,
        string? message = null,
        int failureCount = 1,
        RuntimeSchedulerPoisonDisposition disposition = RuntimeSchedulerPoisonDisposition.Poisoned,
        DateTimeOffset? nextRetryAt = null) =>
        new(
            workflowExecutionId,
            workItemId ?? $"work-{index}",
            WorkflowExecutionCommandKind.RunSchedulerWork,
            $"handler-{index}",
            new RuntimeFaultInfo("System.InvalidOperationException", message ?? $"boom-{index}", "stack"),
            failureCount,
            disposition,
            Now.AddMilliseconds(index),
            Now.AddMilliseconds(index * 2),
            disposition == RuntimeSchedulerPoisonDisposition.RetryScheduled
                ? nextRetryAt ?? Now.AddMinutes(1)
                : null,
            new Dictionary<string, string>
            {
                ["source"] = "test",
                ["payload"] = $"work-{index}"
            },
            new RuntimeFaultInfo("System.ArgumentException", $"inner-{index}"));

    private static Dictionary<string, object?> Values(RuntimeSchedulerPoisonRecord record) =>
        GroundworkV2WorkflowSchedulerPoisonStorageConventions.Values(record).Values
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static string EnvironmentVariable(string providerName) =>
        $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING";

    private static IStorageProviderConnection CreateConnection(string providerName, string connectionString) => providerName switch
    {
        "sqlite" => new SqliteProviderFactory().Create(connectionString),
        "postgresql" => new PostgreSqlProviderFactory().Create(connectionString),
        "sqlserver" => new SqlServerProviderFactory().Create(connectionString),
        "mongodb" => new MongoProviderFactory().Create(connectionString),
        _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
    };

    private static GroundworkV2WorkflowSchedulerPoisonStore NewInterleavingStore(
        InterleavingSession session,
        StorageUnit unit) =>
        new(
            new FakeSessionSource(session, unit),
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));

    private sealed class NativeProviderRuntime : IAsyncDisposable
    {
        private readonly IStorageProviderConnection connection;
        private readonly DirectSessionSource source;
        private readonly StorageUnit unit;

        private NativeProviderRuntime(IStorageProviderConnection connection, StorageUnit unit, string? sqlitePath)
        {
            this.connection = connection;
            this.unit = unit;
            SqlitePath = sqlitePath;
            connection.Schema.Apply(unit);
            source = new DirectSessionSource(connection, unit);
        }

        public string? SqlitePath { get; }
        public IReadOnlyList<QueryRequest> Requests => source.Requests;

        public static NativeProviderRuntime Create(string providerName, string? connectionString)
        {
            string? sqlitePath = null;
            if (providerName == "sqlite")
            {
                sqlitePath = connectionString is null
                    ? Path.Combine(Path.GetTempPath(), $"elsa-poison-v2-{Guid.NewGuid():N}.db")
                    : connectionString.Replace("Data Source=", string.Empty, StringComparison.Ordinal);
                connectionString = $"Data Source={sqlitePath}";
            }

            var connection = CreateConnection(providerName, connectionString!);
            var declaredUnit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.SchedulerPoisonDocumentKind);
            var unit = providerName == "sqlite"
                ? declaredUnit
                : declaredUnit with
                {
                    Id = new StorageUnitId($"{declaredUnit.Id.Value}-{Guid.NewGuid():N}"),
                    Name = $"{declaredUnit.Name}_{Guid.NewGuid():N}"
                };
            return new NativeProviderRuntime(connection, unit, sqlitePath);
        }

        public GroundworkV2WorkflowSchedulerPoisonStore Store(string scope) =>
            new(source, new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(scope))));

        public IStorageSession Open(StorageUnit selectedUnit, string scope) =>
            connection.OpenSession(selectedUnit, StorageAccess.Scoped(new StorageScope(scope)));

        public void InsertRaw(StorageValues values, string scope)
        {
            var outcome = connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope(scope)))
                .Insert(values, WriteOptions.CreateOnly);
            Assert.Equal(WriteOutcomeStatus.Inserted, outcome.Status);
        }

        public ValueTask DisposeAsync()
        {
            connection.Dispose();
            if (SqlitePath is not null)
            {
                foreach (var path in new[] { SqlitePath, $"{SqlitePath}-shm", $"{SqlitePath}-wal", $"{SqlitePath}-journal", $"{SqlitePath}.schema.lock" })
                    if (File.Exists(path))
                        File.Delete(path);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class FakeSessionSource(IStorageSession session, StorageUnit unit) : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) => session;
        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) => throw new NotSupportedException();
        public StorageUnit Unit(string unitId, string? targetName = null) => unit;
    }

    private sealed class RecordingSource(StorageUnit unit) : IGroundworkStorageSessionSource
    {
        public int OpenCount { get; private set; }
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            OpenCount++;
            throw new InvalidOperationException("provider open should not occur");
        }

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) => throw new NotSupportedException();
        public StorageUnit Unit(string unitId, string? targetName = null) => unit;
    }

    private sealed class DirectSessionSource(IStorageProviderConnection connection, StorageUnit unit) : IGroundworkStorageSessionSource
    {
        public List<QueryRequest> Requests { get; } = [];
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            new RecordingSession(connection.OpenSession(unit, access), Requests);
        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) => throw new NotSupportedException();
        public StorageUnit Unit(string unitId, string? targetName = null) => unit;

        private sealed class RecordingSession(IStorageSession inner, ICollection<QueryRequest> requests) : IStorageSession, IConcurrencyStorageSession
        {
            public StorageUnit Unit => inner.Unit;
            public StorageAccess Access => inner.Access;
            public StoredEntry? Read(StorageKey key) => inner.Read(key);
            public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) { requests.Add(request); return inner.Query(request, options); }
            public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
            public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
            public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
            public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
            public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
            public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);
            public WriteOutcome ConditionalUpsert(StorageValues values, WriteOptions? options = null) =>
                ((IConcurrencyStorageSession)inner).ConditionalUpsert(values, options);
        }
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
            UnconditionalUpsertCalled |= options?.Precondition.Kind is WritePreconditionKind.Unconditional;
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
            return new WriteOutcome(WriteOutcomeStatus.Updated, entries[id].Version);
        }
        private static string Id(StorageKey key) => (string)key.Values[ElsaRuntimeV2StorageManifest.IdField]!;
        private static string Id(StorageValues values) => (string)values.Values[ElsaRuntimeV2StorageManifest.IdField]!;
    }

    private sealed class CyclingSession(StorageUnit unit, StorageValues row, IReadOnlyList<string> tokens) : IStorageSession
    {
        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access { get; } = StorageAccess.Scoped(new StorageScope("tenant-a"));
        public int QueryCount { get; private set; }
        public StoredEntry? Read(StorageKey key) => null;
        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) =>
            new([row.Values], null, tokens[Math.Min(QueryCount++, tokens.Count - 1)]);
        public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw new NotSupportedException();
    }
}
