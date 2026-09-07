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

public sealed class GroundworkV2SchedulerStateStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sqlite_save_find_replace_and_scope_refusal_use_the_public_store()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var scoped = runtime.Store(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var otherScope = runtime.Store(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b")));
        var global = runtime.Store(PersistenceAccessContext.Global);
        var state = State("workflow-1", 1);

        var opensBeforeRefusal = runtime.OpenCount;
        await Assert.ThrowsAsync<InvalidOperationException>(() => global.SaveAsync(state).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => global.FindAsync(state.WorkflowExecutionId).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => global.ListAsync().AsTask());
        Assert.Equal(opensBeforeRefusal, runtime.OpenCount);

        Assert.Equal(state, await scoped.SaveAsync(state));
        var replacement = State(state.WorkflowExecutionId, 2);
        Assert.Equal(replacement, await scoped.SaveAsync(replacement));
        var conditionalWrite = Assert.Single(runtime.ConditionalWrites);
        Assert.NotNull(conditionalWrite);
        Assert.Equal(WritePreconditionKind.IfVersion, conditionalWrite!.Precondition.Kind);
        Assert.Equal(1, conditionalWrite.Precondition.Version);
        Assert.Equal(replacement, await scoped.FindAsync(state.WorkflowExecutionId));
        Assert.Null(await otherScope.FindAsync(state.WorkflowExecutionId));
    }

    [Fact]
    public async Task Sqlite_round_trips_each_scheduler_lane_through_current_json()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var generatedEvent = new GeneratedEvent(
            "event-1",
            "workflow-rich",
            "generator-activity-1",
            "branch-1",
            "event.name",
            1,
            now,
            GeneratedEventDurability.Durable);
        var state = new SchedulerState(
            "workflow-rich",
            7,
            pendingWork:
            [
                new ScheduledActivityWorkItem(
                    "work-1",
                    "workflow-rich",
                    "node-1",
                    "activity-1",
                    null,
                    "branch-1",
                    null,
                    now,
                    "test")
            ],
            pendingContinuations:
            [
                new SchedulerContinuationWorkItem(
                    "continuation-1",
                    "workflow-rich",
                    "activity-1",
                    "branch-1",
                    SchedulerContinuationKind.InternalContinuation,
                    null,
                    now,
                    "test")
            ],
            volatileWaits:
            [
                new VolatileWaitRegistration(
                    "wait-1",
                    "workflow-rich",
                    "activity-1",
                    "branch-1",
                    now,
                    now.AddHours(1),
                    "timer",
                    VolatileWaitStatus.Registered,
                    VolatileWaitHostShutdownBehavior.DrainInFlight,
                    VolatileWaitCancellationBehavior.CompleteWithCancellationResult)
            ],
            pendingCompletionWork:
            [
                new SchedulerCompletionWorkItem(
                    "completion-1",
                    "workflow-rich",
                    "activity-1",
                    null,
                    null,
                    SchedulerCompletionKind.ActivityCompleted,
                    1,
                    now,
                    "test")
            ],
            activeGenerators:
            [
                new GeneratorRegistration(
                    "generator-1",
                    "workflow-rich",
                    "generator-activity-1",
                    null,
                    "branch-1",
                    now,
                    GeneratorStatus.Paused,
                    GeneratorStopPolicy.ScopeEnd,
                    GeneratorBackpressurePolicy.Throttle)
            ],
            pendingGeneratedEvents:
            [
                new SchedulerGeneratedEventWorkItem("generated-work-1", generatedEvent, now, "test")
            ]);

        await store.SaveAsync(state);
        var actual = await store.FindAsync(state.WorkflowExecutionId);

        Assert.NotNull(actual);
        Assert.Equal(state.WorkflowExecutionId, actual!.WorkflowExecutionId);
        Assert.Equal(state.Version, actual.Version);
        Assert.Equal(state.PendingWork, actual.PendingWork);
        Assert.Equal(state.PendingContinuations.Single().WorkItemId, actual.PendingContinuations.Single().WorkItemId);
        Assert.Equal(state.VolatileWaits.Single().WaitId, actual.VolatileWaits.Single().WaitId);
        Assert.Equal(state.PendingCompletionWork.Single().WorkItemId, actual.PendingCompletionWork.Single().WorkItemId);
        Assert.Equal(state.ActiveGenerators.Single().GeneratorId, actual.ActiveGenerators.Single().GeneratorId);
        Assert.Equal(state.PendingGeneratedEvents.Single().WorkItemId, actual.PendingGeneratedEvents.Single().WorkItemId);
        Assert.Equal(generatedEvent.GeneratedEventId, actual.PendingGeneratedEvents.Single().GeneratedEvent.GeneratedEventId);
    }

    [Fact]
    public async Task Concurrent_save_uses_create_only_and_if_version_without_unconditional_fallback()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind);
        var createRaceSession = new InterleavingSession(unit) { FailInsert = true };
        var createRaceStore = NewInterleavingStore(createRaceSession, unit);
        var createException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            createRaceStore.SaveAsync(State("workflow-create", 1)).AsTask());
        Assert.Contains("lost a concurrent write; retry", createException.Message, StringComparison.Ordinal);
        Assert.Equal(WritePreconditionKind.CreateOnly, createRaceSession.LastInsertOptions!.Precondition.Kind);

        var session = new InterleavingSession(unit);
        var store = NewInterleavingStore(session, unit);
        await store.SaveAsync(State("workflow-race", 1));
        session.FailConditionalUpsert = true;
        var saveException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(State("workflow-race", 2)).AsTask());
        Assert.Contains("lost a concurrent write; retry", saveException.Message, StringComparison.Ordinal);
        Assert.Equal(WritePreconditionKind.IfVersion, session.LastConditionalOptions!.Precondition.Kind);
        Assert.Equal(1, session.LastConditionalOptions.Precondition.Version);
        Assert.False(session.UnconditionalUpsertCalled);
    }

    [Fact]
    public async Task Sqlite_checkpoint_and_direct_store_share_scheduler_identity_and_projections()
    {
        var database = Path.Combine(
            Path.GetTempPath(),
            $"elsa-runtime-scheduler-checkpoint-{Guid.NewGuid():N}.db");
        try
        {
            using var connection = new SqliteProviderFactory().Create($"Data Source={database}");
            foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
                connection.Schema.Apply(unit);

            var source = new CheckpointSessionSource(connection);
            var state = State("workflow-checkpoint", 1);
            var changes = new RuntimeCheckpointStateChangeSet(
                null,
                new RuntimeStateChange<SchedulerState>(
                    state.WorkflowExecutionId,
                    RuntimeStateChangeOperation.Upsert,
                    state,
                    new Dictionary<string, string>()),
                [], [], [], [], [],
                workflowDispatches: null,
                activityExecutionInspections: null,
                postCommitOutbox: null,
                activityScopeCleanups: null);
            var commit = new RuntimeCheckpointCommit(
                "scheduler-checkpoint-commit",
                new RuntimeCheckpoint(
                    "checkpoint-scheduler",
                    "runtime",
                    state.WorkflowExecutionId,
                    Now,
                    [state.WorkflowExecutionId],
                    new Dictionary<string, string>()),
                changes,
                [],
                new Dictionary<string, string>());

            await new GroundworkV2RuntimeCheckpointWriter(
                    source,
                    new FixedAccessContextAccessor(
                        PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))))
                .CommitAsync(
                    commit,
                    new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

            var schedulerUnit = ElsaRuntimeV2StorageManifest.Require(
                ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind);
            var entry = connection.OpenSession(
                    schedulerUnit,
                    StorageAccess.Scoped(new StorageScope("tenant-a")))
                .Read(GroundworkRuntimeRowStore.Key(state.WorkflowExecutionId));
            Assert.NotNull(entry);
            foreach (var (field, expected) in GroundworkV2SchedulerStateStorageConventions.Projections(state))
                Assert.Equal(expected, entry!.Values.Values[field]);

            var direct = new GroundworkV2SchedulerStateStore(
                source,
                new FixedAccessContextAccessor(
                    PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
            Assert.Equal(state.WorkflowExecutionId, (await direct.FindAsync(state.WorkflowExecutionId))!.WorkflowExecutionId);
        }
        finally
        {
            foreach (var path in new[]
                     {
                         database,
                         $"{database}-shm",
                         $"{database}-wal",
                         $"{database}-journal",
                         $"{database}.schema.lock"
                     })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Sqlite_list_uses_bounded_ordered_provider_pages()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));

        for (var index = 0; index <= RuntimeStorePageRequest.MaximumLimit; index++)
            await store.SaveAsync(State($"workflow-{index:D4}", index));

        var states = await store.ListAsync();

        Assert.Equal(RuntimeStorePageRequest.MaximumLimit + 1, states.Count);
        Assert.Equal(2, runtime.Requests.Count);
        Assert.All(runtime.Requests, request => Assert.Equal(RuntimeStorePageRequest.MaximumLimit, request.Paging.Limit));
        Assert.All(runtime.Requests, request => Assert.Equal(
            ElsaRuntimeV2StorageManifest.IdField,
            Assert.Single(request.Order).Column.Name));
        Assert.Equal("workflow-0000", states.First().WorkflowExecutionId);
        Assert.Equal("workflow-0500", states.Last().WorkflowExecutionId);
    }

    [Fact]
    public async Task Sqlite_reads_refuse_schema_content_and_projection_drift()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var state = State("workflow-corrupt", 1);

        var schemaValues = Values(state);
        schemaValues[ElsaRuntimeV2StorageManifest.SchemaVersionField] = "0.9.0";
        runtime.InsertRaw(new StorageValues(schemaValues), "tenant-a");
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.Store("tenant-a").FindAsync(state.WorkflowExecutionId).AsTask());

        var contentValues = Values(state with { WorkflowExecutionId = "workflow-content-corrupt" });
        contentValues[ElsaRuntimeV2StorageManifest.ContentField] = "{}";
        runtime.InsertRaw(new StorageValues(contentValues), "tenant-a");
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.Store("tenant-a").FindAsync("workflow-content-corrupt").AsTask());

        var projectionValues = Values(state with { WorkflowExecutionId = "workflow-projection-corrupt" });
        projectionValues[ElsaRuntimeV2StorageManifest.CollectionField] = "wrong-collection";
        runtime.InsertRaw(new StorageValues(projectionValues), "tenant-a");
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.Store("tenant-a").FindAsync("workflow-projection-corrupt").AsTask());
    }

    [SkippableTheory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("mongodb")]
    public async Task Configured_native_provider_preserves_scheduler_state_contract(string providerName)
    {
        var connectionString = Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        Skip.If(
            string.IsNullOrWhiteSpace(connectionString),
            $"Set {EnvironmentVariable(providerName)} to run the {providerName} scheduler-state gate.");

        await using var runtime = NativeProviderRuntime.Create(providerName, connectionString);
        var store = runtime.Store(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var state = State("workflow-native", 1);

        Assert.Equal(state, await store.SaveAsync(state));
        Assert.Equal(state, await store.FindAsync(state.WorkflowExecutionId));
        Assert.Equal([state.WorkflowExecutionId], (await store.ListAsync()).Select(item => item.WorkflowExecutionId));
    }

    private static SchedulerState State(string workflowExecutionId, long version) =>
        new(workflowExecutionId, version, pendingWork: []);

    private static Dictionary<string, object?> Values(SchedulerState state) =>
        GroundworkV2SchedulerStateStorageConventions.Values(state).Values
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static string EnvironmentVariable(string providerName) =>
        $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING";

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
                sqlitePath = Path.Combine(Path.GetTempPath(), $"elsa-scheduler-v2-{Guid.NewGuid():N}.db");
                connectionString = $"Data Source={sqlitePath}";
            }

            var connection = CreateConnection(providerName, connectionString!);
            var declaredUnit = ElsaRuntimeV2StorageManifest.Require(
                ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind);
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

        public IReadOnlyList<QueryRequest> Requests => source.Requests;

        public int OpenCount => source.OpenCount;

        public IReadOnlyList<WriteOptions?> ConditionalWrites => source.ConditionalWrites;

        public GroundworkV2SchedulerStateStore Store(PersistenceAccessContext context) =>
            new(source, new FixedAccessContextAccessor(context));

        public GroundworkV2SchedulerStateStore Store(string scope) =>
            Store(PersistenceAccessContext.Scoped(new PersistenceScope(scope)));

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
                foreach (var path in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal" })
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private static GroundworkV2SchedulerStateStore NewInterleavingStore(
        InterleavingSession session,
        StorageUnit unit) =>
        new(
            new InterleavingSessionSource(session, unit),
            new FixedAccessContextAccessor(
                PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));

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
            Assert.True(
                StringComparer.Ordinal.Equals(ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind, unitId)
                || StringComparer.Ordinal.Equals(unit.Id.Value, unitId));
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

        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
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

    private sealed class CheckpointSessionSource(IStorageProviderConnection connection)
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

    private sealed class DirectSessionSource(IStorageProviderConnection connection, StorageUnit unit)
        : IGroundworkStorageSessionSource
    {
        public List<QueryRequest> Requests { get; } = [];

        public List<WriteOptions?> ConditionalWrites { get; } = [];

        public int OpenCount { get; private set; }

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.True(
                StringComparer.Ordinal.Equals(ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind, unitId)
                || StringComparer.Ordinal.Equals(unit.Id.Value, unitId));
            OpenCount++;
            return new RecordingSession(connection.OpenSession(unit, access), Requests, ConditionalWrites);
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.True(
                StringComparer.Ordinal.Equals(ElsaRuntimeV2StorageManifest.SchedulerStateDocumentKind, unitId)
                || StringComparer.Ordinal.Equals(unit.Id.Value, unitId));
            return unit;
        }

        private sealed class RecordingSession(
            IStorageSession inner,
            ICollection<QueryRequest> requests,
            ICollection<WriteOptions?> conditionalWrites)
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
            public WriteOutcome ConditionalUpsert(StorageValues values, WriteOptions? options = null)
            {
                conditionalWrites.Add(options);
                return inner is IConcurrencyStorageSession concurrency
                    ? concurrency.ConditionalUpsert(values, options)
                    : throw new NotSupportedException();
            }
        }
    }
}
