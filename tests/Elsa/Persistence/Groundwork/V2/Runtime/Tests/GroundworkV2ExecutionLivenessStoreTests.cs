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
using QueryPredicate = Groundwork.Query.Model.Predicate;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2ExecutionLivenessStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sqlite_round_trips_scoped_rows_pages_and_compare_and_swap()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        await AssertStoreBehaviorAsync(runtime);
    }

    [SkippableTheory]
    [InlineData("postgresql")]
    [InlineData("sqlserver")]
    [InlineData("mongodb")]
    public async Task Configured_native_provider_preserves_liveness_contract(string providerName)
    {
        var connectionString = Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        Skip.If(string.IsNullOrWhiteSpace(connectionString), $"Set {EnvironmentVariable(providerName)} to run the {providerName} liveness gate.");

        await using var runtime = NativeProviderRuntime.Create(providerName, connectionString);
        await AssertStoreBehaviorAsync(runtime);
    }

    [Fact]
    public async Task Sqlite_recovery_routes_are_exactly_bounded_and_provider_ordered()
    {
        await using var runtime = NativeProviderRuntime.Create("sqlite", null);
        using var connection = runtime.OpenConnection();
        var unit = UniqueLivenessUnit();
        connection.Schema.Apply(unit);

        var source = new DirectSessionSource(connection, unit);
        var scope = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var scanner = new GroundworkV2RuntimeRecoveryScanner(source, scope);
        var request = new RuntimeRecoveryScanRequest(Now, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), 10);

        Assert.Empty(await scanner.ScanAsync(request));
        AssertUnfilteredRecoveryRoutes(source, request.Limit);

        source.QueryRequests.Clear();
        Assert.Empty(await scanner.ScanAsync(new RuntimeRecoveryScanRequest(request.Now, request.LeaseTimeout, request.HeartbeatTimeout, request.Limit, "worker-a")));
        AssertOwnerRecoveryRoutes(source, request.Limit);
    }

    private static async Task AssertStoreBehaviorAsync(NativeProviderRuntime runtime)
    {
        using var connection = runtime.OpenConnection();
        var unit = UniqueLivenessUnit();
        connection.Schema.Apply(unit);

        var source = new DirectSessionSource(connection, unit);
        var scopeA = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var scopeB = new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b")));
        IExecutionLivenessStateStore storeA = new GroundworkV2ExecutionLivenessStateStore(source, scopeA);
        IExecutionLivenessStateStore storeB = new GroundworkV2ExecutionLivenessStateStore(source, scopeB);

        await storeA.SaveAsync(State("wf-1", "op-2", owner: "worker-a"));
        await storeA.SaveAsync(State("wf-1", "op-1", owner: "worker-a"));
        await storeA.SaveAsync(State("wf-2", "op-1", owner: "worker-a"));
        Assert.Null(await storeB.FindAsync("wf-1", "op-1"));

        var found = await storeA.FindVersionedAsync("wf-1", "op-1");
        Assert.NotNull(found);
        Assert.Equal(1, found!.Revision);

        var page = await storeA.ListPageAsync(new ExecutionLivenessStatePageQuery("wf-1", 1));
        Assert.Equal(["op-1"], page.Items.Select(state => state.OperationalStateId));
        Assert.NotNull(page.NextContinuationToken);
        var next = await storeA.ListPageAsync(new ExecutionLivenessStatePageQuery("wf-1", 1, page.NextContinuationToken));
        Assert.Equal(["op-2"], next.Items.Select(state => state.OperationalStateId));

        var all = await storeA.ListAllPageAsync(new RuntimeStorePageRequest(10));
        Assert.Equal(["wf-1/op-1", "wf-1/op-2", "wf-2/op-1"], all.Items.Select(state => $"{state.WorkflowExecutionId}/{state.OperationalStateId}"));

        var replacement = State("wf-1", "op-1", owner: "worker-b", metadata: new Dictionary<string, string> { ["value"] = "replacement" });
        var saved = await storeA.TrySaveAsync(replacement, found.Revision);
        Assert.Equal(ExecutionLivenessStateWriteStatus.Saved, saved.Status);
        Assert.True(saved.Succeeded);
        Assert.Equal(2, saved.Revision);
        Assert.Equal("replacement", (await storeA.FindAsync("wf-1", "op-1"))!.Metadata["value"]);

        var stale = await storeA.TrySaveAsync(State("wf-1", "op-1", owner: "stale"), found.Revision);
        Assert.Equal(ExecutionLivenessStateWriteStatus.RevisionConflict, stale.Status);
        var createConflict = await storeA.TrySaveAsync(State("wf-1", "op-1", owner: "create"), expectedRevision: 0);
        Assert.Equal(ExecutionLivenessStateWriteStatus.RevisionConflict, createConflict.Status);
        var missing = await storeA.TrySaveAsync(State("wf-1", "missing", owner: "missing"), expectedRevision: 1);
        Assert.Equal(ExecutionLivenessStateWriteStatus.NotFound, missing.Status);

        await storeA.SaveAsync(State(
            "wf-recovery",
            "op-detected",
            owner: null,
            interrupted: new InterruptedExecutionState(
                "interrupt-1",
                "wf-recovery",
                leaseId: null,
                lastCheckpointId: "checkpoint-1",
                RuntimeInterruptionReason.HostStopped,
                RuntimeInterruptionStatus.Detected,
                Now.AddMinutes(-3))));
        await storeA.SaveAsync(State("wf-recovery", "op-lease", "worker-a", leaseExpiresAt: Now.AddMinutes(-1)));
        await storeA.SaveAsync(State("wf-recovery", "op-heartbeat", "worker-a", leaseExpiresAt: Now.AddMinutes(5), heartbeatRecordedAt: Now.AddMinutes(-2)));

        source.QueryRequests.Clear();
        var scanner = new GroundworkV2RuntimeRecoveryScanner(source, scopeA);
        var candidates = await scanner.ScanAsync(new RuntimeRecoveryScanRequest(Now, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), 10));
        Assert.Equal(
            ["op-detected", "op-lease", "op-heartbeat"],
            candidates.Select(candidate => candidate.OperationalStateId));
        Assert.Equal(RuntimeInterruptionReason.HostStopped, candidates.First().Reason);

        AssertUnfilteredRecoveryRoutes(source, 10);

        source.QueryRequests.Clear();
        var ownerCandidates = await scanner.ScanAsync(new RuntimeRecoveryScanRequest(Now, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), 10, "worker-a"));
        Assert.Equal(["op-detected", "op-lease", "op-heartbeat"], ownerCandidates.Select(candidate => candidate.OperationalStateId));

        AssertOwnerRecoveryRoutes(source, 10);
    }

    private static StorageUnit UniqueLivenessUnit()
    {
        var declaredUnit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind);
        var suffix = Guid.NewGuid().ToString("N");
        return declaredUnit with
        {
            Id = new StorageUnitId($"{declaredUnit.Id.Value}-{suffix}"),
            Name = $"{declaredUnit.Name}_{suffix}"
        };
    }

    private static void AssertUnfilteredRecoveryRoutes(DirectSessionSource source, int limit)
    {
        Assert.Equal(4, source.QueryRequests.Count);
        Assert.Equal(
            [
                ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField,
                ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField,
                ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField,
                ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField
            ],
            source.QueryRequests.Select(request => Assert.Single(request.Order).Column.Name));
        Assert.All(source.QueryRequests, request => Assert.Equal(limit, request.Paging.Limit));
        Assert.Equal(
            [
                new[] { ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField },
                new[] { ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField },
                new[] { ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField },
                new[] { ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField }
            ],
            source.QueryRequests.Select(request => PredicateColumns(request.Where).OrderBy(column => column, StringComparer.Ordinal).ToArray()));
        var detectedPredicate = Assert.IsType<QueryPredicate.Equal>(source.QueryRequests[0].Where);
        Assert.Equal(QueryType.Int32, detectedPredicate.Value.Type);
        Assert.IsType<int>(detectedPredicate.Value.Value);
    }

    private static void AssertOwnerRecoveryRoutes(DirectSessionSource source, int limit)
    {
        Assert.Equal(6, source.QueryRequests.Count);
        Assert.Equal(
            [
                ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField,
                ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField,
                ElsaRuntimeV2StorageManifest.RecoveryInterruptedAtField,
                ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField,
                ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField,
                ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField
            ],
            source.QueryRequests.Select(request => Assert.Single(request.Order).Column.Name));
        Assert.All(source.QueryRequests, request => Assert.Equal(limit, request.Paging.Limit));
        Assert.Equal(
            [
                new[] { ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField, ElsaRuntimeV2StorageManifest.RecoveryLeaseOwnerIdField }.OrderBy(column => column, StringComparer.Ordinal).ToArray(),
                new[] { ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField, ElsaRuntimeV2StorageManifest.RecoveryHeartbeatOwnerIdField }.OrderBy(column => column, StringComparer.Ordinal).ToArray(),
                new[] { ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField, ElsaRuntimeV2StorageManifest.RecoveryHasOperationalOwnerField }.OrderBy(column => column, StringComparer.Ordinal).ToArray(),
                new[] { ElsaRuntimeV2StorageManifest.RecoveryLeaseOwnerIdField, ElsaRuntimeV2StorageManifest.RecoveryLeaseExpiresAtField }.OrderBy(column => column, StringComparer.Ordinal).ToArray(),
                new[] { ElsaRuntimeV2StorageManifest.RecoveryLeaseOwnerIdField, ElsaRuntimeV2StorageManifest.RecoveryLeaseAcquiredAtField }.OrderBy(column => column, StringComparer.Ordinal).ToArray(),
                new[] { ElsaRuntimeV2StorageManifest.RecoveryHeartbeatOwnerIdField, ElsaRuntimeV2StorageManifest.RecoveryHeartbeatRecordedAtField }.OrderBy(column => column, StringComparer.Ordinal).ToArray()
            ],
            source.QueryRequests.Select(request => PredicateColumns(request.Where).OrderBy(column => column, StringComparer.Ordinal).ToArray()));
        Assert.All(
            source.QueryRequests.Take(3),
            request =>
            {
                var detected = EqualityFor(request.Where, ElsaRuntimeV2StorageManifest.RecoveryInterruptedStatusField);
                Assert.Equal(QueryType.Int32, detected.Value.Type);
                Assert.IsType<int>(detected.Value.Value);
            });
    }

    private static IReadOnlyCollection<string> PredicateColumns(QueryPredicate predicate) => predicate switch
    {
        QueryPredicate.Equal equal => [equal.Column.Name],
        QueryPredicate.Range range => [range.Column.Name],
        QueryPredicate.And and => and.Terms.SelectMany(PredicateColumns).ToArray(),
        _ => []
    };

    private static QueryPredicate.Equal EqualityFor(QueryPredicate predicate, string field)
    {
        if (TryEqualityFor(predicate, field, out var equal))
            return equal!;

        throw new InvalidOperationException($"Predicate did not contain equality for '{field}'.");
    }

    private static bool TryEqualityFor(QueryPredicate predicate, string field, out QueryPredicate.Equal? equality)
    {
        if (predicate is QueryPredicate.Equal equal && StringComparer.Ordinal.Equals(equal.Column.Name, field))
        {
            equality = equal;
            return true;
        }

        if (predicate is QueryPredicate.And and)
            foreach (var term in and.Terms)
                if (TryEqualityFor(term, field, out equality))
                    return true;

        equality = null;
        return false;
    }

    private static ExecutionLivenessState State(
        string workflowExecutionId,
        string operationalStateId,
        string? owner,
        DateTimeOffset? leaseExpiresAt = null,
        DateTimeOffset? heartbeatRecordedAt = null,
        InterruptedExecutionState? interrupted = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            operationalStateId,
            workflowExecutionId,
            owner is null
                ? null
                : new RuntimeExecutionLease(
                    $"lease-{operationalStateId}",
                    workflowExecutionId,
                    owner,
                    Now.AddMinutes(-1),
                    leaseExpiresAt ?? Now.AddMinutes(5),
                    fencingToken: 1),
            owner is null
                ? null
                : new RuntimeHeartbeat(
                    $"heartbeat-{operationalStateId}",
                    workflowExecutionId,
                    owner,
                    $"lease-{operationalStateId}",
                    heartbeatRecordedAt ?? Now,
                    metadata),
            drain: null,
            interrupted,
            metadata: metadata);

    private static string EnvironmentVariable(string providerName) =>
        $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING";

    private sealed class TestAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class DirectSessionSource(IStorageProviderConnection connection, StorageUnit unit) : IGroundworkStorageSessionSource
    {
        private readonly Dictionary<StorageAccess, IStorageSession> sessions = [];

        public List<QueryRequest> QueryRequests { get; } = [];

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            if (sessions.TryGetValue(access, out var session))
                return session;

            session = new RecordingSession(connection.OpenSession(unit, access), QueryRequests);
            sessions.Add(access, session);
            return session;
        }

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) =>
            throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null)
        {
            Assert.Equal(ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind, unitId);
            return unit;
        }
    }

    private sealed class RecordingSession(IStorageSession inner, ICollection<QueryRequest> requests) : IStorageSession, IConcurrencyStorageSession
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

    private sealed class NativeProviderRuntime(string providerName, string connectionString, string? sqlitePath) : IAsyncDisposable
    {
        public static NativeProviderRuntime Create(string providerName, string? configuredConnection)
        {
            if (!string.IsNullOrWhiteSpace(configuredConnection))
                return new(providerName, configuredConnection, null);

            var path = Path.Combine(Path.GetTempPath(), $"elsa-runtime-liveness-{Guid.NewGuid():N}.db");
            return new(providerName, $"Data Source={path}", path);
        }

        public IStorageProviderConnection OpenConnection() => providerName switch
        {
            "sqlite" => new SqliteProviderFactory().Create(connectionString),
            "postgresql" => new PostgreSqlProviderFactory().Create(connectionString),
            "sqlserver" => new SqlServerProviderFactory().Create(connectionString),
            "mongodb" => new MongoProviderFactory().Create(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
        };

        public ValueTask DisposeAsync()
        {
            if (sqlitePath is not null)
            {
                foreach (var path in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal" })
                    if (File.Exists(path))
                        File.Delete(path);
            }

            return ValueTask.CompletedTask;
        }
    }
}
