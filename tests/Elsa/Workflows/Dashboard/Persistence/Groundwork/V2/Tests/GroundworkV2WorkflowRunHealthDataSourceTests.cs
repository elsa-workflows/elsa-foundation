using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Dashboard.Persistence.Groundwork.V2;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Dashboard.Persistence.Groundwork.V2.Tests;

public sealed class GroundworkV2WorkflowRunHealthDataSourceTests : IAsyncDisposable
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        $"elsa-v2-run-health-{Guid.NewGuid():N}.db");
    private readonly IStorageProviderConnection connection;
    private readonly NativeSessionSource source;

    public GroundworkV2WorkflowRunHealthDataSourceTests()
    {
        connection = new SqliteProviderFactory().Create($"Data Source={databasePath}");
        foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
            connection.Schema.Apply(unit);
        source = new(connection);
    }

    [Fact]
    public void Explicit_v2_registration_replaces_the_dashboard_run_health_source()
    {
        var services = new ServiceCollection();
        services.AddScoped<IWorkflowRunHealthDataSource, UnavailableWorkflowRunHealthDataSource>();
        services.AddSingleton<IGroundworkStorageSessionSource, StubSessionSource>();
        services.AddSingleton<IPersistenceAccessContextAccessor>(
            new FixedAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));

        services.AddGroundworkV2WorkflowRunHealth("runtime");

        using var provider = services.BuildServiceProvider();
        Assert.IsType<GroundworkV2WorkflowRunHealthDataSource>(
            provider.GetRequiredService<IWorkflowRunHealthDataSource>());
        var registry = provider.GetRequiredService<GroundworkStorageUnitRegistry>();
        Assert.Contains(
            registry.Registrations,
            registration => registration.Unit.Id.Value == ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind && registration.TargetName == "runtime");
    }

    [Fact]
    public async Task Native_aggregation_returns_zero_filled_buckets_and_all_statuses()
    {
        var tenant = new FixedAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var dataSource = new GroundworkV2WorkflowRunHealthDataSource(source, tenant);
        var from = DateTimeOffset.UnixEpoch;
        var buckets = new WorkflowRunHealthBucketRange[]
        {
            new(0, from, from.AddHours(1)),
            new(1, from.AddHours(1), from.AddHours(2)),
            new(2, from.AddHours(2), from.AddHours(3)),
            new(3, from.AddHours(3), from.AddHours(4))
        };

        Put("run-completed", "definition-b", WorkflowExecutionStatus.Completed, from.AddMinutes(10), 0, 0);
        Put("run-boundary", "definition-b", WorkflowExecutionStatus.Completed, from.AddHours(1), 0, 0);
        Put("run-faulted", "definition-a", WorkflowExecutionStatus.Faulted, from.AddHours(1).AddMinutes(10), 2, 1);
        Put("run-pending", "definition-c", WorkflowExecutionStatus.Pending, from.AddHours(2).AddMinutes(10), 0, 0);
        Put("run-running", "definition-a", WorkflowExecutionStatus.Running, from.AddDays(4), 0, 0);

        var aggregate = await dataSource.QueryAsync(new(
            new WorkflowRunHealthQuery(from, from.AddHours(4), "Etc/UTC", WorkflowRunHealthBucketSize.Hour, "tenant-a"),
            buckets));

        Assert.Equal(4, aggregate.StartedCount);
        Assert.Equal(2, aggregate.SucceededCount);
        Assert.Equal(1, aggregate.FailedCount);
        Assert.Equal(1, aggregate.IncompleteCount);
        Assert.Equal(1, aggregate.IncidentBearingRunCount);
        Assert.Equal(2, aggregate.IncidentCount);
        Assert.Equal(1, aggregate.RunningCount);
        Assert.Equal(
            [1, 2, 1, 0],
            aggregate.Buckets.Select(bucket => bucket.StartedCount));
        Assert.Equal(1, aggregate.Buckets.ElementAt(1).IncidentBearingRunCount);
        Assert.Equal(0, aggregate.Buckets.ElementAt(2).IncidentCount);
        Assert.Equal(
            new WorkflowRunHealthBucket(from.AddHours(3), from.AddHours(4), 0, 0, 0, 0, 0, 0, 0),
            aggregate.Buckets.ElementAt(3));
    }

    [Fact]
    public async Task Top_failures_are_tied_by_definition_id_and_test_runs_are_excluded()
    {
        var tenant = new FixedAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var dataSource = new GroundworkV2WorkflowRunHealthDataSource(source, tenant);
        var from = DateTimeOffset.UnixEpoch;
        for (var index = 0; index < 6; index++)
        {
            var definition = $"definition-{(char)('a' + index)}";
            Put($"run-{index}", definition, WorkflowExecutionStatus.Faulted, from.AddMinutes(index), 0, 0);
        }
        Put("test-run", "definition-z", WorkflowExecutionStatus.Faulted, from.AddMinutes(20), 0, 0, WorkflowRunKind.TestRun);

        var query = new WorkflowRunHealthQuery(from, from.AddHours(1), "Etc/UTC", WorkflowRunHealthBucketSize.Hour, "tenant-a");
        var aggregate = await dataSource.QueryAsync(new(query, [new(0, from, from.AddHours(1))]));

        Assert.Equal(
            ["definition-a", "definition-b", "definition-c", "definition-d", "definition-e"],
            aggregate.HighestFailureDefinitions.Select(definition => definition.DefinitionId));
        Assert.Equal(6, aggregate.FailedCount);

        var included = await dataSource.QueryAsync(new(query with { IncludeTestRuns = true }, [new(0, from, from.AddHours(1))]));
        Assert.Equal(7, included.FailedCount);
    }

    [Fact]
    public async Task Native_aggregation_handles_more_than_one_hundred_runs_without_scope_leakage()
    {
        var tenantA = new FixedAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var dataSource = new GroundworkV2WorkflowRunHealthDataSource(source, tenantA);
        var from = DateTimeOffset.UnixEpoch;

        for (var index = 0; index < 125; index++)
        {
            Put(
                $"run-a-{index}",
                "definition-a",
                WorkflowExecutionStatus.Completed,
                from.AddMinutes(index),
                0,
                0);
        }

        PutInScope(
            "colliding-execution-id",
            "definition-a",
            WorkflowExecutionStatus.Completed,
            from.AddMinutes(130),
            0,
            0,
            "tenant-a");
        PutInScope(
            "colliding-execution-id",
            "definition-b",
            WorkflowExecutionStatus.Faulted,
            from.AddMinutes(130),
            1,
            1,
            "tenant-b");

        var query = new WorkflowRunHealthQuery(
            from,
            from.AddHours(3),
            "Etc/UTC",
            WorkflowRunHealthBucketSize.Hour,
            "tenant-a");
        var aggregate = await dataSource.QueryAsync(new(query, [new(0, from, query.To)]));

        Assert.Equal(126, aggregate.StartedCount);
        Assert.Equal(126, aggregate.SucceededCount);
        Assert.Equal(0, aggregate.FailedCount);
        Assert.Equal(0, aggregate.IncidentCount);
    }

    [Fact]
    public async Task Native_aggregation_can_read_the_other_scope_only_when_ambient_scope_matches()
    {
        var from = DateTimeOffset.UnixEpoch;
        PutInScope(
            "colliding-execution-id",
            "definition-a",
            WorkflowExecutionStatus.Completed,
            from.AddMinutes(1),
            0,
            0,
            "tenant-a");
        PutInScope(
            "colliding-execution-id",
            "definition-b",
            WorkflowExecutionStatus.Faulted,
            from.AddMinutes(1),
            2,
            1,
            "tenant-b");

        var query = new WorkflowRunHealthQuery(
            from,
            from.AddHours(1),
            "Etc/UTC",
            WorkflowRunHealthBucketSize.Hour,
            "tenant-b");
        var tenantB = new GroundworkV2WorkflowRunHealthDataSource(
            source,
            new FixedAccessContextAccessor(
                PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"))));

        var aggregate = await tenantB.QueryAsync(new(query, [new(0, from, query.To)]));

        Assert.Equal(1, aggregate.StartedCount);
        Assert.Equal(1, aggregate.FailedCount);
        Assert.Equal(2, aggregate.IncidentCount);
        Assert.Equal(1, aggregate.IncidentBearingRunCount);
    }

    [Fact]
    public async Task Scope_and_tenant_mismatch_are_rejected_before_provider_io()
    {
        var accessor = new FixedAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var recording = new RecordingSessionSource(source);
        var dataSource = new GroundworkV2WorkflowRunHealthDataSource(recording, accessor);
        var query = new WorkflowRunHealthQuery(
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddHours(1),
            "Etc/UTC",
            WorkflowRunHealthBucketSize.Hour,
            "tenant-b");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dataSource.QueryAsync(new(query, [new(0, query.From, query.To)])).AsTask());
        Assert.Equal(0, recording.OpenCount);

        var global = new GroundworkV2WorkflowRunHealthDataSource(
            recording,
            new FixedAccessContextAccessor(PersistenceAccessContext.Global));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            global.QueryAsync(new(query with { TenantId = "tenant-a" }, [new(0, query.From, query.To)])).AsTask());
        Assert.Equal(0, recording.OpenCount);
    }

    [Fact]
    public async Task Excessive_bucket_fanout_is_rejected_before_provider_io()
    {
        var accessor = new FixedAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var recording = new RecordingSessionSource(source);
        var dataSource = new GroundworkV2WorkflowRunHealthDataSource(recording, accessor);
        var from = DateTimeOffset.UnixEpoch;
        var buckets = Enumerable.Range(0, 745)
            .Select(index => new WorkflowRunHealthBucketRange(index, from.AddHours(index), from.AddHours(index + 1)))
            .ToArray();

        await Assert.ThrowsAsync<WorkflowRunHealthQueryException>(() =>
            dataSource.QueryAsync(new(
                new WorkflowRunHealthQuery(from, from.AddHours(745), "Etc/UTC", WorkflowRunHealthBucketSize.Hour, "tenant-a"),
                buckets)).AsTask());
        Assert.Equal(0, recording.OpenCount);
    }

    [Fact]
    public async Task Disjoint_bucket_ranges_are_rejected_before_provider_io()
    {
        var accessor = new FixedAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var recording = new RecordingSessionSource(source);
        var dataSource = new GroundworkV2WorkflowRunHealthDataSource(recording, accessor);
        var from = DateTimeOffset.UnixEpoch;

        await Assert.ThrowsAsync<WorkflowRunHealthQueryException>(() =>
            dataSource.QueryAsync(new(
                new WorkflowRunHealthQuery(from, from.AddHours(3), "Etc/UTC", WorkflowRunHealthBucketSize.Hour, "tenant-a"),
                [
                    new(0, from, from.AddHours(1)),
                    new(1, from.AddHours(2), from.AddHours(3))
                ])).AsTask());
        Assert.Equal(0, recording.OpenCount);
    }

    [Fact]
    public async Task Unknown_status_is_rejected_instead_of_being_reported_as_incomplete()
    {
        var accessor = new FixedAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var dataSource = new GroundworkV2WorkflowRunHealthDataSource(source, accessor);
        var from = DateTimeOffset.UnixEpoch;
        Put("unknown-status", "definition-a", (WorkflowExecutionStatus)999, from.AddMinutes(1), 0, 0);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            dataSource.QueryAsync(new(
                new WorkflowRunHealthQuery(from, from.AddHours(1), "Etc/UTC", WorkflowRunHealthBucketSize.Hour, "tenant-a"),
                [new(0, from, from.AddHours(1))])).AsTask());
    }

    [Fact]
    public async Task Native_source_uses_aggregate_only_and_keeps_one_call_per_status_bucket()
    {
        var accessor = new FixedAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var recording = new AggregateOnlySessionSource(source);
        var dataSource = new GroundworkV2WorkflowRunHealthDataSource(recording, accessor);
        var from = DateTimeOffset.UnixEpoch;
        var query = new WorkflowRunHealthQuery(
            from,
            from.AddHours(2),
            "Etc/UTC",
            WorkflowRunHealthBucketSize.Hour,
            "tenant-a");

        await dataSource.QueryAsync(new(query, [
            new(0, from, from.AddHours(1)),
            new(1, from.AddHours(1), from.AddHours(2))
        ]));

        Assert.Equal(3, recording.AggregateCount);
        Assert.Equal(0, recording.QueryCount);
        Assert.Contains(
            recording.AggregationQueries,
            query => query.Take == 5 && query.OrderByTerms.Count == 2);
    }

    [Fact]
    public async Task Hourly_aggregation_uses_the_arbitrary_query_origin()
    {
        var accessor = new FixedAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var recording = new AggregateOnlySessionSource(source);
        var dataSource = new GroundworkV2WorkflowRunHealthDataSource(recording, accessor);
        var from = new DateTimeOffset(2024, 1, 2, 13, 17, 0, TimeSpan.Zero);
        var query = new WorkflowRunHealthQuery(
            from,
            from.AddHours(2),
            "Etc/UTC",
            WorkflowRunHealthBucketSize.Hour,
            "tenant-a");
        Put("arbitrary-origin", "definition-a", WorkflowExecutionStatus.Completed, from.AddMinutes(59), 0, 0);

        var aggregate = await dataSource.QueryAsync(new(query, [
            new(0, from, from.AddHours(1)),
            new(1, from.AddHours(1), query.To)
        ]));

        Assert.Equal([1, 0], aggregate.Buckets.Select(bucket => bucket.StartedCount));
        var bucketQuery = recording.AggregationQueries.Single(query => query.Profile == ElsaRuntimeV2StorageManifest.WorkflowRunHealthHourlyProfile);
        Assert.Equal(new AggregationTimeRange(query.From, query.To), bucketQuery.TimeRange);
        Assert.Equal(query.From, bucketQuery.TimeBucketOrigin);
    }

    [Fact]
    public async Task Daily_aggregation_maps_partial_calendar_days_across_dst()
    {
        var accessor = new FixedAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var recording = new AggregateOnlySessionSource(source);
        var dataSource = new GroundworkV2WorkflowRunHealthDataSource(recording, accessor);
        var from = new DateTimeOffset(2024, 10, 26, 22, 30, 0, TimeSpan.Zero);
        var firstBoundary = new DateTimeOffset(2024, 10, 27, 23, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2024, 10, 28, 22, 30, 0, TimeSpan.Zero);
        var query = new WorkflowRunHealthQuery(
            from,
            to,
            "Europe/Amsterdam",
            WorkflowRunHealthBucketSize.Day,
            "tenant-a");
        Put("dst-day-one", "definition-a", WorkflowExecutionStatus.Completed, from.AddHours(12), 0, 0);
        Put("dst-day-two", "definition-a", WorkflowExecutionStatus.Completed, firstBoundary.AddHours(12), 0, 0);

        var aggregate = await dataSource.QueryAsync(new(query, [
            new(0, from, firstBoundary),
            new(1, firstBoundary, to)
        ]));

        Assert.Equal([1, 1], aggregate.Buckets.Select(bucket => bucket.StartedCount));
        var bucketQuery = recording.AggregationQueries.Single(query => query.Profile == ElsaRuntimeV2StorageManifest.WorkflowRunHealthDailyProfile);
        Assert.Equal(new AggregationTimeRange(query.From, query.To), bucketQuery.TimeRange);
        Assert.Equal(query.TimeZone, bucketQuery.TimeZoneId);
        Assert.Null(bucketQuery.TimeBucketOrigin);
    }

    private void Put(
        string executionId,
        string definitionId,
        WorkflowExecutionStatus status,
        DateTimeOffset startedAt,
        long incidentCount,
        long incidentBearingCount,
        WorkflowRunKind runKind = WorkflowRunKind.PublishedRun)
        => PutInScope(
            executionId,
            definitionId,
            status,
            startedAt,
            incidentCount,
            incidentBearingCount,
            "tenant-a",
            runKind);

    private void PutInScope(
        string executionId,
        string definitionId,
        WorkflowExecutionStatus status,
        DateTimeOffset startedAt,
        long incidentCount,
        long incidentBearingCount,
        string tenantId,
        WorkflowRunKind runKind = WorkflowRunKind.PublishedRun)
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind);
        var session = connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope(tenantId)));
        var outcome = session.Upsert(
            GroundworkV2WorkflowRunHealthStorageValues(
                executionId,
                definitionId,
                runKind,
                startedAt,
                status,
                incidentCount,
                incidentBearingCount),
            WriteOptions.Unconditional);
        Assert.True(outcome.Succeeded, outcome.Status.ToString());
    }

    private static StorageValues GroundworkV2WorkflowRunHealthStorageValues(
        string executionId,
        string definitionId,
        WorkflowRunKind runKind,
        DateTimeOffset startedAt,
        WorkflowExecutionStatus status,
        long incidentCount,
        long incidentBearingCount) =>
        GroundworkRuntimeRowStore.Values(
            executionId,
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            "{}",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowRunHealthDefinitionIdField] = definitionId,
                [ElsaRuntimeV2StorageManifest.WorkflowRunHealthRunKindField] = (int)runKind,
                [ElsaRuntimeV2StorageManifest.WorkflowRunHealthStartedAtField] = startedAt,
                [ElsaRuntimeV2StorageManifest.WorkflowRunHealthStatusField] = (int)status,
                [ElsaRuntimeV2StorageManifest.WorkflowRunHealthIncidentCountField] = incidentCount,
                [ElsaRuntimeV2StorageManifest.WorkflowRunHealthIncidentBearingCountField] = incidentBearingCount
            });

    public ValueTask DisposeAsync()
    {
        connection.Dispose();
        foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            if (File.Exists(path))
                File.Delete(path);
        return ValueTask.CompletedTask;
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext context) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = context;
    }

    private sealed class StubSessionSource : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            throw new NotSupportedException();

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) =>
            throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null) =>
            ElsaRuntimeV2StorageManifest.Require(unitId);
    }

    private sealed class NativeSessionSource(IStorageProviderConnection connection) : IGroundworkStorageSessionSource
    {
        public int OpenCount { get; private set; }

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            OpenCount++;
            return connection.OpenSession(ElsaRuntimeV2StorageManifest.Require(unitId), access);
        }

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) =>
            throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null) =>
            ElsaRuntimeV2StorageManifest.Require(unitId);
    }

    private sealed class RecordingSessionSource(NativeSessionSource inner) : IGroundworkStorageSessionSource
    {
        public int OpenCount => inner.OpenCount;
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) => inner.Open(unitId, access, targetName);
        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) => throw new NotSupportedException();
        public StorageUnit Unit(string unitId, string? targetName = null) => inner.Unit(unitId, targetName);
    }

    private sealed class AggregateOnlySessionSource(NativeSessionSource inner) : IGroundworkStorageSessionSource
    {
        public int AggregateCount { get; private set; }
        public int QueryCount { get; private set; }
        public List<AggregationQuery> AggregationQueries { get; } = [];

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            new AggregateOnlySession(
                inner.Open(unitId, access, targetName),
                this);

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) =>
            throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null) => inner.Unit(unitId, targetName);

        private sealed class AggregateOnlySession(IStorageSession inner, AggregateOnlySessionSource owner) : IStorageSession
        {
            public StorageUnit Unit => inner.Unit;
            public StorageAccess Access => inner.Access;

            public StoredEntry? Read(StorageKey key) =>
                throw new Xunit.Sdk.XunitException("The v2 dashboard source must not materialize rows.");

            public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
            {
                owner.QueryCount++;
                throw new Xunit.Sdk.XunitException("The v2 dashboard source must not issue row queries.");
            }

            public AggregationResult Aggregate(AggregationQuery query)
            {
                owner.AggregateCount++;
                owner.AggregationQueries.Add(query);
                return inner.Aggregate(query);
            }

            public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) =>
                throw new NotSupportedException();

            public WriteOutcome Update(StorageValues values, WriteOptions? options = null) =>
                throw new NotSupportedException();

            public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) =>
                throw new NotSupportedException();

            public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) =>
                throw new NotSupportedException();

            public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) =>
                throw new NotSupportedException();
        }
    }
}
