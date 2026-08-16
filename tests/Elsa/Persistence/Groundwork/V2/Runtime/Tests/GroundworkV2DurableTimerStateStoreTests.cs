using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;
using System.Text.Json;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2DurableTimerStateStoreTests
{
    [Fact]
    public void The_v2_store_implements_the_public_durable_timer_contract()
    {
        Assert.Contains(
            typeof(IDurableTimerStore),
            typeof(GroundworkV2DurableTimerStateStore).GetInterfaces());
    }

    [Fact]
    public async Task Sqlite_round_trips_existing_wins_scopes_due_order_and_workflow_pages()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind);
        connection.Schema.Apply(unit);
        var source = new DirectSessionSource(connection, unit);
        var tenantA = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var tenantB = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b")));
        IDurableTimerStore storeA = new GroundworkV2DurableTimerStateStore(source, tenantA);
        IDurableTimerStore storeB = new GroundworkV2DurableTimerStateStore(source, tenantB);
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

        var first = Timer("wf-1", "timer-b", now.AddMinutes(-2));
        var second = Timer("wf-1", "timer-a", now.AddMinutes(-1));
        await storeA.SaveAsync(first);
        await storeA.SaveAsync(second);
        await storeA.SaveAsync(first with { DueTime = now.AddHours(1), StimulusHash = "changed" });
        await storeA.SaveAsync(Timer("wf-2", "timer-a", now.AddMinutes(-3)));
        await storeB.SaveAsync(Timer("wf-1", "timer-a", now.AddMinutes(-4)));

        var foundFirst = await storeA.FindAsync("wf-1", "timer-b");
        var foundSecond = await storeA.FindAsync("wf-1", "timer-a");
        Assert.Equal(first.TimerId, foundFirst!.TimerId);
        Assert.Equal(first.DueTime, foundFirst.DueTime);
        Assert.Equal(second.TimerId, foundSecond!.TimerId);
        Assert.Equal(second.DueTime, foundSecond.DueTime);
        Assert.Equal("hash-timer-a", (await storeB.FindAsync("wf-1", "timer-a"))!.StimulusHash);

        var due = await storeA.ListDueAsync(now, limit: 10);
        Assert.Equal(["timer-a", "timer-b", "timer-a"], due.Select(timer => timer.TimerId));
        Assert.Equal(["wf-2", "wf-1", "wf-1"], due.Select(timer => timer.WorkflowExecutionId));

        var firstPage = await storeA.ListPageAsync(new DurableTimerPageQuery("wf-1", limit: 1));
        var secondPage = await storeA.ListPageAsync(
            new DurableTimerPageQuery("wf-1", limit: 1, firstPage.NextContinuationToken));
        Assert.Equal(["timer-a"], firstPage.Items.Select(timer => timer.TimerId));
        Assert.Equal(["timer-b"], secondPage.Items.Select(timer => timer.TimerId));
        Assert.Null(secondPage.NextContinuationToken);
    }

    [Fact]
    public async Task Claims_are_bounded_fenced_and_native_round_trip()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind);
        connection.Schema.Apply(unit);
        var source = new DirectSessionSource(connection, unit);
        var accessor = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDurableTimerStore store = new GroundworkV2DurableTimerStateStore(source, accessor);
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        await store.SaveAsync(Timer("wf-1", "timer-a", now.AddMinutes(-2)));
        await store.SaveAsync(Timer("wf-1", "timer-future", now.AddMinutes(10)));

        var request = new RuntimeDurableTimerClaimRequest("owner-a", now, TimeSpan.FromMinutes(1), limit: 1);
        var claims = await store.ClaimDueAsync(request);
        var claim = Assert.Single(claims);
        Assert.Equal(1, claim.FencingToken);
        Assert.Equal(2, claim.Revision);
        Assert.Empty(await store.ClaimDueAsync(new RuntimeDurableTimerClaimRequest("owner-b", now, TimeSpan.FromMinutes(1), 10)));

        var renewed = await store.RenewClaimAsync(claim, now.AddSeconds(5), TimeSpan.FromMinutes(2));
        Assert.Equal(RuntimeDurableTimerClaimTransitionStatus.Succeeded, renewed.Status);
        var current = Assert.IsType<RuntimeDurableTimerClaim>(renewed.Claim);
        Assert.Equal(3, current.Revision);
        Assert.Equal(RuntimeDurableTimerClaimTransitionStatus.Stale,
            (await store.ReleaseClaimAsync(claim, now.AddMinutes(3))).Status);
        Assert.Equal(RuntimeDurableTimerClaimTransitionStatus.Succeeded,
            (await store.ReleaseClaimAsync(current, now.AddMinutes(3))).Status);

        var reclaimed = Assert.Single(await store.ClaimDueAsync(
            new RuntimeDurableTimerClaimRequest("owner-b", now.AddMinutes(3), TimeSpan.FromMinutes(1), 10)));
        Assert.Equal(claim.Timer.TimerId, reclaimed.Timer.TimerId);
        Assert.Equal(2, reclaimed.FencingToken);
        Assert.Equal(RuntimeDurableTimerClaimTransitionStatus.Succeeded,
            (await store.CompleteClaimAsync(reclaimed)).Status);
        Assert.Equal(RuntimeDurableTimerClaimTransitionStatus.AlreadyApplied,
            (await store.CompleteClaimAsync(reclaimed)).Status);
        Assert.Null(await store.FindAsync("wf-1", claim.Timer.TimerId));
    }

    [Fact]
    public async Task Queries_use_bounded_pages_and_exact_manifest_column_metadata()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind);
        var requests = new List<QueryRequest>();
        var session = new RecordingSession(unit, requests);
        var source = new RecordingSessionSource(session, unit);
        var store = new GroundworkV2DurableTimerStateStore(
            source,
            new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));

        await store.ListDueAsync(DateTimeOffset.UtcNow, 7);
        await store.ListPageAsync(new DurableTimerPageQuery("wf-1", 5, "continuation"));
        Assert.Equal(2, requests.Count);
        Assert.Equal(7, requests[0].Paging.Limit);
        Assert.Equal(
            [ElsaRuntimeV2StorageManifest.DurableTimerDueTimeField, ElsaRuntimeV2StorageManifest.DurableTimerIdField],
            requests[0].Order.Select(term => term.Column.Name));
        Assert.Equal(5, requests[1].Paging.Limit);
        Assert.Equal(ElsaRuntimeV2StorageManifest.DurableTimerIdField, requests[1].Order.Single().Column.Name);
        Assert.Equal(ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField, Assert.IsType<Predicate.Equal>(requests[1].Where).Column.Name);
        foreach (var request in requests)
            foreach (var column in request.Order.Select(term => term.Column).Concat([
                request.Where switch
                {
                    Predicate.Equal equal => equal.Column,
                    Predicate.Range range => range.Column,
                    _ => throw new InvalidOperationException("Unexpected timer query predicate.")
                }
            ]))
            {
                var definition = unit.Columns.Single(candidate => candidate.Name == column.Name);
                Assert.Equal(definition.IsNullable, column.IsNullable);
                Assert.Equal(definition.MaxLength, column.MaxLength);
            }
    }

    [Fact]
    public async Task Global_and_across_scope_access_fail_closed()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind);
        var session = new InterleavingSession(unit);
        var source = new InterleavingSessionSource(session, unit);
        var global = new GroundworkV2DurableTimerStateStore(
            source,
            new TestAccessContextAccessor(PersistenceAccessContext.Global));
        var across = new GroundworkV2DurableTimerStateStore(
            source,
            new TestAccessContextAccessor(
                PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("test"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => global.FindAsync("wf-1", "timer-a").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => across.FindAsync("wf-1", "timer-a").AsTask());
        Assert.False(session.ReadWasCalled);
    }

    [Fact]
    public async Task Save_delete_and_claim_races_use_only_create_or_version_CAS()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind);
        var createRace = new InterleavingSession(unit) { FailInsert = true };
        var createStore = NewInterleavingStore(createRace, unit);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            createStore.SaveAsync(Timer("wf-create", "timer-a", DateTimeOffset.UtcNow)).AsTask());
        Assert.Equal(WritePreconditionKind.CreateOnly, createRace.LastInsertOptions!.Precondition.Kind);

        var session = new InterleavingSession(unit);
        var store = NewInterleavingStore(session, unit);
        var timer = Timer("wf-1", "timer-a", DateTimeOffset.UtcNow.AddMinutes(-1));
        await store.SaveAsync(timer);
        session.FailConditionalUpsert = true;
        var claim = await store.ClaimDueAsync(new RuntimeDurableTimerClaimRequest("owner", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), 1));
        Assert.Empty(claim);
        Assert.Equal(WritePreconditionKind.IfVersion, session.LastConditionalOptions!.Precondition.Kind);
        Assert.False(session.UnconditionalUpsertCalled);

        session.FailDelete = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteAsync("wf-1", "timer-a").AsTask());
        Assert.Equal(WritePreconditionKind.IfVersion, session.LastDeleteOptions!.Precondition.Kind);
        Assert.False(session.UnconditionalDeleteCalled);
    }

    [Fact]
    public async Task Fenced_transitions_refuse_a_row_whose_logical_identity_does_not_match_its_key()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind);
        var session = new InterleavingSession(unit);
        var store = NewInterleavingStore(session, unit);
        var now = DateTimeOffset.UtcNow;
        var storedTimer = Timer("wf-stored", "timer-stored", now.AddMinutes(-1));
        await store.SaveAsync(storedTimer);
        var storedClaim = Assert.Single(await store.ClaimDueAsync(
            new RuntimeDurableTimerClaimRequest("owner", now, TimeSpan.FromMinutes(1), 1)));
        var requestedTimer = Timer("wf-requested", "timer-requested", storedTimer.DueTime);
        session.MoveEntry(
            CompositeId(storedTimer.WorkflowExecutionId, storedTimer.TimerId),
            CompositeId(requestedTimer.WorkflowExecutionId, requestedTimer.TimerId));
        var forgedClaim = new RuntimeDurableTimerClaim(
            requestedTimer,
            storedClaim.OwnerId,
            storedClaim.FencingToken,
            storedClaim.Revision,
            storedClaim.ClaimedAt,
            storedClaim.VisibleAfter,
            storedClaim.FailureCount);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.CompleteClaimAsync(forgedClaim).AsTask());
        Assert.False(session.UnconditionalDeleteCalled);
    }

    private static string CompositeId(string workflowExecutionId, string timerId) =>
        $"{workflowExecutionId.Length}:{workflowExecutionId}{timerId.Length}:{timerId}";

    [Fact]
    public async Task Boundary_identity_and_projection_metadata_are_admitted_without_overflow()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind);
        connection.Schema.Apply(unit);
        var source = new DirectSessionSource(connection, unit);
        var accessor = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDurableTimerStore store = new GroundworkV2DurableTimerStateStore(source, accessor);
        var workflowExecutionId = new string('w', ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength);
        var timerId = new string('t', ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength);
        var timer = Timer(workflowExecutionId, timerId, DateTimeOffset.UtcNow);

        await store.SaveAsync(timer);
        var physicalId = $"{workflowExecutionId.Length}:{workflowExecutionId}{timerId.Length}:{timerId}";
        Assert.Equal(264, physicalId.Length);
        Assert.True(physicalId.Length <= ElsaRuntimeV2StorageManifest.IdMaximumLength);
        var entry = source.Open(unit.Id.Value, StorageAccess.Scoped(new StorageScope("tenant-a")))
            .Read(GroundworkRuntimeRowStore.Key(physicalId));
        Assert.NotNull(entry);
        Assert.Equal(timerId, ReadString(entry!, ElsaRuntimeV2StorageManifest.DurableTimerIdField));
        Assert.Equal(workflowExecutionId, ReadString(entry, ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField));
        Assert.Equal(timer.DueTime, ReadDateTime(entry, ElsaRuntimeV2StorageManifest.DurableTimerDueTimeField));
        Assert.Equal(84, ReadString(entry, ElsaRuntimeV2StorageManifest.DurableTimerClaimOrderKeyField).Length);
        Assert.NotNull(await store.FindAsync(workflowExecutionId, timerId));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.SaveAsync(Timer(new string('x', 129), timerId, timer.DueTime)).AsTask());
    }

    [Fact]
    public async Task Checkpoint_cleanup_uses_the_same_composite_timer_identity()
    {
        await using var runtime = NativeProviderRuntime.Create();
        using var connection = runtime.OpenConnection();
        foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
            connection.Schema.Apply(unit);
        var source = new NativeSessionSource(connection);
        var accessor = new TestAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDurableTimerStore store = new GroundworkV2DurableTimerStateStore(source, accessor);
        var timerA = Timer("wf-1", "timer-shared", DateTimeOffset.UtcNow.AddMinutes(-1));
        var timerB = Timer("wf-2", "timer-shared", DateTimeOffset.UtcNow.AddMinutes(-1));
        await store.SaveAsync(timerA);
        await store.SaveAsync(timerB);

        var writer = new GroundworkV2RuntimeCheckpointWriter(source, accessor);
        await writer.CommitAsync(
            CleanupCommit(
                "timer-cleanup",
                "wf-1",
                new ActivityScopeCleanupRequest("wf-1", "scope-1", ["scope-1"], [], ["timer-shared"], [])),
            new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.Null(await store.FindAsync("wf-1", "timer-shared"));
        Assert.NotNull(await store.FindAsync("wf-2", "timer-shared"));
        var session = source.Open(
            ElsaRuntimeV2StorageManifest.DurableTimerDocumentKind,
            StorageAccess.Scoped(new StorageScope("tenant-a")));
        Assert.Null(session.Read(GroundworkRuntimeRowStore.Key("timer-shared")));
    }

    private static GroundworkV2DurableTimerStateStore NewInterleavingStore(
        InterleavingSession session,
        StorageUnit unit) =>
        new(
            new InterleavingSessionSource(session, unit),
            new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));

    private static DurableTimer Timer(string workflowExecutionId, string timerId, DateTimeOffset dueTime) =>
        new(
            timerId,
            workflowExecutionId,
            "DurableTimer",
            $"hash-{timerId}",
            dueTime,
            new DateTimeOffset(2026, 8, 16, 11, 0, 0, TimeSpan.Zero),
            JsonSerializer.SerializeToElement(new { timerId }));

    private static RuntimeCheckpointCommit CleanupCommit(
        string commitId,
        string workflowExecutionId,
        ActivityScopeCleanupRequest cleanup) =>
        new(
            commitId,
            new RuntimeCheckpoint(
                $"checkpoint-{commitId}",
                "runtime",
                workflowExecutionId,
                DateTimeOffset.UtcNow,
                [],
                new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(
                null,
                null,
                [],
                [],
                [],
                [],
                [],
                activityScopeCleanups: [cleanup]),
            [],
            new Dictionary<string, string>());

    private static string ReadString(StoredEntry entry, string field) =>
        entry.Values.Values[field] switch
        {
            string value => value,
            JsonElement { ValueKind: JsonValueKind.String } value => value.GetString()!,
            _ => throw new InvalidOperationException($"Field '{field}' was not a string.")
        };

    private static DateTimeOffset ReadDateTime(StoredEntry entry, string field) =>
        entry.Values.Values[field] switch
        {
            DateTimeOffset value => value,
            string value => DateTimeOffset.Parse(value),
            JsonElement value => value.GetDateTimeOffset(),
            _ => throw new InvalidOperationException($"Field '{field}' was not a timestamp.")
        };

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

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) =>
            connection.BeginUnitOfWork(access, options, unitIds.Select(ElsaRuntimeV2StorageManifest.Require).ToArray());

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return unit;
        }
    }

    private sealed class RecordingSessionSource(RecordingSession session, StorageUnit unit) : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return session;
        }

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) => throw new NotSupportedException();
        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return unit;
        }
    }

    private sealed class RecordingSession(StorageUnit unit, ICollection<QueryRequest> requests) : IStorageSession
    {
        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access => StorageAccess.Scoped(new StorageScope("tenant-a"));
        public StoredEntry? Read(StorageKey key) => null;
        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
        {
            requests.Add(request);
            return new QueryMaterializedResult([], null, null);
        }
        public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw new NotSupportedException();
    }

    private sealed class InterleavingSessionSource(InterleavingSession session, StorageUnit unit) : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return session;
        }

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) => throw new NotSupportedException();
        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return unit;
        }
    }

    private sealed class InterleavingSession(StorageUnit unit) : IStorageSession, IConcurrencyStorageSession
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
        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) =>
            new(entries.Values.Select(entry => entry.Values.Values).ToArray(), null, null);
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
            return new WriteOutcome(WriteOutcomeStatus.Updated, current.Version.GetValueOrDefault() + 1);
        }
        public void MoveEntry(string sourceId, string targetId)
        {
            entries[targetId] = entries[sourceId];
            entries.Remove(sourceId);
        }
        private static string Id(StorageKey key) => (string)key.Values[ElsaRuntimeV2StorageManifest.IdField]!;
        private static string Id(StorageValues values) => (string)values.Values[ElsaRuntimeV2StorageManifest.IdField]!;
    }

    private sealed class NativeSessionSource(IStorageProviderConnection connection) :
        IGroundworkStorageSessionSource,
        IGroundworkStorageCapabilitySource
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
            new(Path.Combine(Path.GetTempPath(), $"elsa-runtime-durable-timer-{Guid.NewGuid():N}.db"));
        public IStorageProviderConnection OpenConnection() => new SqliteProviderFactory().Create($"Data Source={path}");
        public ValueTask DisposeAsync()
        {
            foreach (var candidate in new[] { path, $"{path}-shm", $"{path}-wal" })
                if (File.Exists(candidate))
                    File.Delete(candidate);
            return ValueTask.CompletedTask;
        }
    }
}
