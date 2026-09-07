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

public sealed class GroundworkV2IncidentStateStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sqlite_incident_lifecycle_is_scoped_composite_and_provider_counted()
    {
        var incidentUnit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind);
        Assert.Contains(incidentUnit.Indexes, index =>
            StringComparer.Ordinal.Equals(index.Name, "by_workflow_execution_and_incident_id"));
        Assert.Contains(incidentUnit.Indexes, index =>
            StringComparer.Ordinal.Equals(index.Name, "by_workflow_execution_and_status_and_incident_id"));

        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var scoped = runtime.Store(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var otherScope = runtime.Store(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b")));
        var global = runtime.Store(PersistenceAccessContext.Global);
        var acrossScopes = runtime.Store(PersistenceAccessContext.PrivilegedAcrossScopes(
            new PersistenceAccessPurpose("test.incident-across-scopes-refusal")));
        var first = Incident("shared-id", "workflow-a", IncidentStatus.Open);
        var sameIdOtherWorkflow = Incident("shared-id", "workflow-b", IncidentStatus.Blocking);

        var opensBeforeRefusal = runtime.OpenCount;
        await Assert.ThrowsAsync<InvalidOperationException>(() => global.TryAddAsync(first).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => global.FindAsync(first.WorkflowExecutionId, first.IncidentId).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => global.ListAsync(first.WorkflowExecutionId).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => acrossScopes.CountAsync(first.WorkflowExecutionId).AsTask());
        Assert.Equal(opensBeforeRefusal, runtime.OpenCount);

        Assert.True(await scoped.TryAddAsync(first));
        Assert.False(await scoped.TryAddAsync(Incident("shared-id", "workflow-a", IncidentStatus.Open)));
        Assert.True(await scoped.TryAddAsync(sameIdOtherWorkflow));
        AssertIncident(first, await scoped.FindAsync(first.WorkflowExecutionId, first.IncidentId));
        AssertIncident(sameIdOtherWorkflow, await scoped.FindAsync(sameIdOtherWorkflow.WorkflowExecutionId, sameIdOtherWorkflow.IncidentId));
        Assert.Equal(1, await scoped.CountAsync(first.WorkflowExecutionId));
        Assert.Equal(1, await scoped.CountAsync(sameIdOtherWorkflow.WorkflowExecutionId));
        Assert.Equal([first.IncidentId], (await scoped.ListAsync(first.WorkflowExecutionId)).Select(item => item.IncidentId));
        Assert.Equal([sameIdOtherWorkflow.IncidentId], (await scoped.ListBlockingAsync(sameIdOtherWorkflow.WorkflowExecutionId)).Select(item => item.IncidentId));
        Assert.Null(await otherScope.FindAsync(first.WorkflowExecutionId, first.IncidentId));

        var replacement = Incident(first.IncidentId, first.WorkflowExecutionId, IncidentStatus.Blocking);
        AssertIncident(replacement, await scoped.SaveAsync(replacement));
        AssertIncident(replacement, await scoped.FindAsync(first.WorkflowExecutionId, first.IncidentId));
    }

    [Fact]
    public async Task Sqlite_resolution_outcome_is_write_once_and_save_never_falls_back_to_unconditional()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store("tenant-a");
        var open = Incident("incident-resolution", "workflow-resolution", IncidentStatus.Open);
        await store.SaveAsync(open);

        var resolvedAt = Now.AddMinutes(1);
        var outcome = new IncidentResolutionOutcome("test.resolve", resolvedAt, null, "test");
        var resolved = new IncidentState(
            open.IncidentId,
            open.WorkflowExecutionId,
            open.ActivityExecutionId,
            open.ExecutableNodeId,
            open.Severity,
            IncidentStatus.Resolved,
            outcome,
            open.FailureType,
            open.Message,
            open.CreatedAt,
            resolvedAt,
            open.Metadata);
        AssertIncident(resolved, await store.SaveAsync(resolved));
        AssertIncident(resolved, await store.FindAsync(resolved.WorkflowExecutionId, resolved.IncidentId));

        var changedOutcome = new IncidentState(
            resolved.IncidentId,
            resolved.WorkflowExecutionId,
            resolved.ActivityExecutionId,
            resolved.ExecutableNodeId,
            resolved.Severity,
            resolved.Status,
            new IncidentResolutionOutcome("test.other", resolvedAt, null, "test"),
            resolved.FailureType,
            resolved.Message,
            resolved.CreatedAt,
            resolved.ResolvedAt,
            resolved.Metadata);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(changedOutcome).AsTask());
        Assert.Contains(runtime.ConditionalWrites, options =>
            options?.Precondition.Kind == WritePreconditionKind.IfVersion);
        Assert.DoesNotContain(runtime.ConditionalWrites, options =>
            options?.Precondition.Kind == WritePreconditionKind.Unconditional);
    }

    [Fact]
    public async Task Sqlite_list_pages_are_bounded_ordered_and_blocking_is_provider_filtered()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var store = runtime.Store("tenant-a");
        for (var index = 0; index <= RuntimeStorePageRequest.MaximumLimit; index++)
        {
            await store.SaveAsync(Incident(
                $"incident-{index:D4}",
                "workflow-page",
                index % 2 == 0 ? IncidentStatus.Blocking : IncidentStatus.Open));
        }

        var all = await store.ListAsync("workflow-page");
        var blocking = await store.ListBlockingAsync("workflow-page");

        Assert.Equal(RuntimeStorePageRequest.MaximumLimit + 1, all.Count);
        Assert.Equal((RuntimeStorePageRequest.MaximumLimit / 2) + 1, blocking.Count);
        Assert.Equal("incident-0000", all.First().IncidentId);
        Assert.Equal("incident-0500", all.Last().IncidentId);
        Assert.All(blocking, incident => Assert.Equal(IncidentStatus.Blocking, incident.Status));
        Assert.Equal(3, runtime.Requests.Count);
        Assert.All(runtime.Requests, request =>
        {
            Assert.Equal(RuntimeStorePageRequest.MaximumLimit, request.Paging.Limit);
            Assert.Equal(ElsaRuntimeV2StorageManifest.IncidentIdField, Assert.Single(request.Order).Column.Name);
        });
    }

    [Fact]
    public async Task Non_adjacent_continuation_cycles_are_rejected_before_unbounded_enumeration()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind);
        var state = Incident("incident-cycle", "workflow-cycle", IncidentStatus.Open);
        var session = new CyclingSession(
            unit,
            GroundworkV2IncidentStateStorageConventions.Values(state),
            ["cycle-a", "cycle-b", "cycle-a"]);
        var store = new GroundworkV2IncidentStateStore(
            new FakeSessionSource(session, unit),
            new FixedAccessContextAccessor(
                PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.ListAsync(state.WorkflowExecutionId).AsTask());
        Assert.Equal(3, session.QueryCount);
    }

    [Fact]
    public async Task Sqlite_reads_refuse_schema_content_and_projection_drift()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        var state = Incident("incident-corrupt", "workflow-corrupt", IncidentStatus.Open);

        var schemaValues = Values(state);
        schemaValues[ElsaRuntimeV2StorageManifest.SchemaVersionField] = "0.9.0";
        runtime.InsertRaw(new StorageValues(schemaValues), "tenant-a");
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.Store("tenant-a")
            .FindAsync(state.WorkflowExecutionId, state.IncidentId).AsTask());

        var contentState = Incident("incident-content-corrupt", "workflow-corrupt", IncidentStatus.Open);
        var contentValues = Values(contentState);
        contentValues[ElsaRuntimeV2StorageManifest.ContentField] = "{}";
        runtime.InsertRaw(new StorageValues(contentValues), "tenant-a");
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.Store("tenant-a")
            .FindAsync(contentState.WorkflowExecutionId, contentState.IncidentId).AsTask());

        var projectionState = Incident("incident-projection-corrupt", "workflow-corrupt", IncidentStatus.Open);
        var projectionValues = Values(projectionState);
        projectionValues[ElsaRuntimeV2StorageManifest.StatusField] = IncidentStatus.Blocking.ToString();
        runtime.InsertRaw(new StorageValues(projectionValues), "tenant-a");
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.Store("tenant-a")
            .FindAsync(projectionState.WorkflowExecutionId, projectionState.IncidentId).AsTask());
    }

    [Fact]
    public async Task Concurrent_try_add_and_save_use_create_only_and_if_version_without_unconditional_fallback()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(
            ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind);
        var createRaceSession = new InterleavingSession(unit)
        {
            FailInsert = true,
            ConflictWinner = Incident("incident-create-race", "workflow-create-race", IncidentStatus.Blocking)
        };
        var createRaceStore = NewInterleavingStore(createRaceSession, unit);
        Assert.False(await createRaceStore.TryAddAsync(
            Incident("incident-create-race", "workflow-create-race", IncidentStatus.Open)));
        Assert.Equal(WritePreconditionKind.CreateOnly, createRaceSession.LastInsertOptions!.Precondition.Kind);

        var session = new InterleavingSession(unit);
        var store = NewInterleavingStore(session, unit);
        var state = Incident("incident-save-race", "workflow-save-race", IncidentStatus.Open);
        await store.SaveAsync(state);
        session.FailConditionalUpsert = true;
        var saveException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(Incident("incident-save-race", "workflow-save-race", IncidentStatus.Blocking)).AsTask());
        Assert.Contains("lost a concurrent write; retry", saveException.Message, StringComparison.Ordinal);
        Assert.Equal(WritePreconditionKind.IfVersion, session.LastConditionalOptions!.Precondition.Kind);
        Assert.Equal(1, session.LastConditionalOptions.Precondition.Version);
        Assert.False(session.UnconditionalUpsertCalled);
    }

    [Fact]
    public async Task Sqlite_checkpoint_and_direct_store_converge_on_composite_incident_identity()
    {
        var database = Path.Combine(
            Path.GetTempPath(),
            $"elsa-runtime-incident-checkpoint-{Guid.NewGuid():N}.db");
        try
        {
            using var connection = new SqliteProviderFactory().Create($"Data Source={database}");
            foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
                connection.Schema.Apply(unit);

            var access = StorageAccess.Scoped(new StorageScope("tenant-a"));
            var workflow = NewExecution("workflow-incident-checkpoint");
            connection.OpenSession(
                    ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind),
                    access)
                .Insert(GroundworkV2WorkflowExecutionStorageConventions.Values(workflow), WriteOptions.CreateOnly);
            connection.OpenSession(
                    ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind),
                    access)
                .Insert(
                    GroundworkV2WorkflowRunHealthStorageConventions.Values(
                        workflow.WorkflowExecutionId,
                        workflow.PinnedExecutable.DefinitionId,
                        workflow.RunKind,
                        workflow.StartedAt,
                        workflow.Status,
                        0,
                        0),
                    WriteOptions.CreateOnly);

            var incident = Incident(
                "incident-shared",
                workflow.WorkflowExecutionId,
                IncidentStatus.Blocking);
            var source = new CheckpointSessionSource(connection);
            var changes = new RuntimeCheckpointStateChangeSet(
                null,
                null,
                [],
                [],
                [],
                [new RuntimeStateChange<IncidentState>(
                    incident.IncidentId,
                    RuntimeStateChangeOperation.Append,
                    incident,
                    new Dictionary<string, string>())],
                []);
            var commit = new RuntimeCheckpointCommit(
                "incident-checkpoint-commit",
                new RuntimeCheckpoint(
                    "checkpoint-incident",
                    "runtime",
                    workflow.WorkflowExecutionId,
                    Now,
                    [incident.IncidentId],
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

            var physicalId = $"{workflow.WorkflowExecutionId.Length}:{workflow.WorkflowExecutionId}{incident.IncidentId.Length}:{incident.IncidentId}";
            var entry = connection.OpenSession(
                    ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind),
                    access)
                .Read(GroundworkRuntimeRowStore.Key(physicalId));
            Assert.NotNull(entry);
            Assert.Equal(physicalId, entry!.Values.Values[ElsaRuntimeV2StorageManifest.IdField]);
            Assert.Equal(workflow.WorkflowExecutionId, entry.Values.Values[ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField]);
            Assert.Equal(incident.IncidentId, entry.Values.Values[ElsaRuntimeV2StorageManifest.IncidentIdField]);

            var direct = new GroundworkV2IncidentStateStore(
                source,
                new FixedAccessContextAccessor(
                    PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
            AssertIncident(incident, await direct.FindAsync(workflow.WorkflowExecutionId, incident.IncidentId));
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
    public async Task Sqlite_checkpoints_keep_identical_incident_ids_isolated_between_workflows()
    {
        var database = Path.Combine(
            Path.GetTempPath(),
            $"elsa-runtime-incident-isolation-{Guid.NewGuid():N}.db");
        try
        {
            using var connection = new SqliteProviderFactory().Create($"Data Source={database}");
            foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
                connection.Schema.Apply(unit);

            var source = new CheckpointSessionSource(connection);
            var firstWorkflow = NewExecution("workflow-incident-a");
            var secondWorkflow = NewExecution("workflow-incident-b");
            var firstIncident = Incident("incident-same", firstWorkflow.WorkflowExecutionId, IncidentStatus.Open);
            var secondIncident = Incident("incident-same", secondWorkflow.WorkflowExecutionId, IncidentStatus.Blocking);
            foreach (var pair in new[]
                     {
                         (Workflow: firstWorkflow, Incident: firstIncident),
                         (Workflow: secondWorkflow, Incident: secondIncident)
                     })
            {
                var access = StorageAccess.Scoped(new StorageScope("tenant-a"));
                connection.OpenSession(
                        ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind),
                        access)
                    .Insert(GroundworkV2WorkflowExecutionStorageConventions.Values(pair.Workflow), WriteOptions.CreateOnly);
                connection.OpenSession(
                        ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind),
                        access)
                    .Insert(
                        GroundworkV2WorkflowRunHealthStorageConventions.Values(
                            pair.Workflow.WorkflowExecutionId,
                            pair.Workflow.PinnedExecutable.DefinitionId,
                            pair.Workflow.RunKind,
                            pair.Workflow.StartedAt,
                            pair.Workflow.Status,
                            0,
                            0),
                        WriteOptions.CreateOnly);

                var changes = new RuntimeCheckpointStateChangeSet(
                    null,
                    null,
                    [],
                    [],
                    [],
                    [new RuntimeStateChange<IncidentState>(
                        pair.Incident.IncidentId,
                        RuntimeStateChangeOperation.Append,
                        pair.Incident,
                        new Dictionary<string, string>())],
                    []);
                var commit = new RuntimeCheckpointCommit(
                    $"incident-isolation-{pair.Workflow.WorkflowExecutionId}",
                    new RuntimeCheckpoint(
                        $"checkpoint-{pair.Workflow.WorkflowExecutionId}",
                        "runtime",
                        pair.Workflow.WorkflowExecutionId,
                        Now,
                        [pair.Incident.IncidentId],
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
            }

            var direct = new GroundworkV2IncidentStateStore(
                source,
                new FixedAccessContextAccessor(
                    PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
            AssertIncident(firstIncident, await direct.FindAsync(firstWorkflow.WorkflowExecutionId, firstIncident.IncidentId));
            AssertIncident(secondIncident, await direct.FindAsync(secondWorkflow.WorkflowExecutionId, secondIncident.IncidentId));
            Assert.NotEqual(
                GroundworkV2IncidentStateStorageConventions.PhysicalId(firstWorkflow.WorkflowExecutionId, firstIncident.IncidentId),
                GroundworkV2IncidentStateStorageConventions.PhysicalId(secondWorkflow.WorkflowExecutionId, secondIncident.IncidentId));
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

    [SkippableTheory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("mongodb")]
    public async Task Configured_native_provider_preserves_incident_state_contract(string providerName)
    {
        var connectionString = Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        Skip.If(
            string.IsNullOrWhiteSpace(connectionString),
            $"Set {EnvironmentVariable(providerName)} to run the {providerName} incident-state gate.");

        await using var runtime = NativeProviderRuntime.Create(providerName, connectionString);
        var store = runtime.Store(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var state = Incident("incident-native", "workflow-native", IncidentStatus.Blocking);

        Assert.True(await store.TryAddAsync(state));
        AssertIncident(state, await store.FindAsync(state.WorkflowExecutionId, state.IncidentId));
        Assert.Equal(1, await store.CountAsync(state.WorkflowExecutionId));
        Assert.Equal([state.IncidentId], (await store.ListAsync(state.WorkflowExecutionId)).Select(item => item.IncidentId));
        Assert.Equal([state.IncidentId], (await store.ListBlockingAsync(state.WorkflowExecutionId)).Select(item => item.IncidentId));
    }

    private static IncidentState Incident(
        string incidentId,
        string workflowExecutionId,
        IncidentStatus status) =>
        new(
            incidentId,
            workflowExecutionId,
            "activity-1",
            "node-1",
            IncidentSeverity.Error,
            status,
            null,
            "TestFailure",
            $"message-{incidentId}",
            Now,
            null,
            new Dictionary<string, string> { ["source"] = "test" });

    private static void AssertIncident(IncidentState expected, IncidentState? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.IncidentId, actual!.IncidentId);
        Assert.Equal(expected.WorkflowExecutionId, actual.WorkflowExecutionId);
        Assert.Equal(expected.ActivityExecutionId, actual.ActivityExecutionId);
        Assert.Equal(expected.ExecutableNodeId, actual.ExecutableNodeId);
        Assert.Equal(expected.Severity, actual.Severity);
        Assert.Equal(expected.Status, actual.Status);
        if (expected.ResolutionOutcome is null)
        {
            Assert.Null(actual.ResolutionOutcome);
        }
        else
        {
            Assert.NotNull(actual.ResolutionOutcome);
            Assert.Equal(expected.ResolutionOutcome.ActionKind, actual.ResolutionOutcome!.ActionKind);
            Assert.Equal(expected.ResolutionOutcome.AppliedAt, actual.ResolutionOutcome.AppliedAt);
            Assert.Equal(expected.ResolutionOutcome.SystemSource, actual.ResolutionOutcome.SystemSource);
            Assert.Equal(expected.ResolutionOutcome.Strategy, actual.ResolutionOutcome.Strategy);
            Assert.Equal(expected.ResolutionOutcome.Metadata, actual.ResolutionOutcome.Metadata);
        }
        Assert.Equal(expected.FailureType, actual.FailureType);
        Assert.Equal(expected.Message, actual.Message);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.ResolvedAt, actual.ResolvedAt);
        Assert.Equal(expected.Metadata, actual.Metadata);
    }

    private static Dictionary<string, object?> Values(IncidentState state) =>
        GroundworkV2IncidentStateStorageConventions.Values(state).Values
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static string EnvironmentVariable(string providerName) =>
        $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING";

    private static GroundworkV2IncidentStateStore NewInterleavingStore(
        InterleavingSession session,
        StorageUnit unit) =>
        new(
            new InterleavingSessionSource(session, unit),
            new FixedAccessContextAccessor(
                PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));

    private static WorkflowExecutionState NewExecution(string workflowExecutionId) =>
        new(
            workflowExecutionId,
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Running,
            null,
            Now,
            null,
            Now,
            null,
            null,
            null,
            "tenant-a",
            new Dictionary<string, string>());

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

        public StorageUnit Unit(string unitId, string? targetName = null) => unit;
    }

    private sealed class InterleavingSession(StorageUnit unit)
        : SynchronousStorageSessionTestDouble, IStorageSession, IConcurrencyStorageSession
    {
        private readonly Dictionary<string, StoredEntry> entries = new(StringComparer.Ordinal);

        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access { get; } = StorageAccess.Scoped(new StorageScope("tenant-a"));
        public bool FailInsert { get; set; }
        public IncidentState? ConflictWinner { get; set; }
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
            if (FailInsert)
            {
                if (ConflictWinner is not null)
                    entries[id] = new StoredEntry(
                        GroundworkV2IncidentStateStorageConventions.Values(ConflictWinner),
                        1);
                return new WriteOutcome(WriteOutcomeStatus.ConcurrencyConflict, 0);
            }
            if (entries.ContainsKey(id))
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
                sqlitePath = Path.Combine(Path.GetTempPath(), $"elsa-incident-v2-{Guid.NewGuid():N}.db");
                connectionString = $"Data Source={sqlitePath}";
            }

            var connection = CreateConnection(providerName, connectionString!);
            var declaredUnit = ElsaRuntimeV2StorageManifest.Require(
                ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind);
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

        public GroundworkV2IncidentStateStore Store(PersistenceAccessContext context) =>
            new(source, new FixedAccessContextAccessor(context));

        public GroundworkV2IncidentStateStore Store(string scope) =>
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
                foreach (var path in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal", $"{sqlitePath}-journal" })
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

    private sealed class CyclingSession(
        StorageUnit unit,
        StorageValues row,
        IReadOnlyList<string> continuationTokens)
        : SynchronousStorageSessionTestDouble, IStorageSession
    {
        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access { get; } = StorageAccess.Scoped(new StorageScope("tenant-a"));
        public int QueryCount { get; private set; }
        public StoredEntry? Read(StorageKey key) => null;

        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
        {
            var token = continuationTokens[Math.Min(QueryCount, continuationTokens.Count - 1)];
            QueryCount++;
            return new QueryMaterializedResult([row.Values], null, token);
        }

        public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw new NotSupportedException();
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
                StringComparer.Ordinal.Equals(ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind, unitId) ||
                StringComparer.Ordinal.Equals(unit.Id.Value, unitId));
            OpenCount++;
            return new RecordingSession(connection.OpenSession(unit, access), Requests, ConditionalWrites);
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.True(
                StringComparer.Ordinal.Equals(ElsaRuntimeV2StorageManifest.IncidentStateDocumentKind, unitId) ||
                StringComparer.Ordinal.Equals(unit.Id.Value, unitId));
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
