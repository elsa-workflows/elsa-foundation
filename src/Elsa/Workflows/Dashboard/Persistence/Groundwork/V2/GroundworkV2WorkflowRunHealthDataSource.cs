using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Workflows.Dashboard.Persistence.Groundwork.V2;

/// <summary>
/// Provider-neutral run-health source over the dedicated Groundwork v2 projection.
/// </summary>
/// <remarks>
/// Every result is obtained through the public aggregation API. The caller's tenant identity is
/// checked against the ambient persistence scope, while the provider-owned scoped session remains
/// authoritative for row isolation.
/// </remarks>
public sealed class GroundworkV2WorkflowRunHealthDataSource(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    string? targetName = null) : IWorkflowRunHealthDataSource
{
    private const int MaximumBucketCount = 744;

    public bool IsAvailable => true;

    public ValueTask<WorkflowRunHealthAggregate> QueryAsync(
        WorkflowRunHealthDataQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Query);
        ArgumentNullException.ThrowIfNull(request.Buckets);
        if (request.Buckets.Count > MaximumBucketCount)
        {
            throw new WorkflowRunHealthQueryException(
                $"At most {MaximumBucketCount} workflow run-health buckets may be queried at once.");
        }
        ValidateBuckets(request.Query, request.Buckets);
        var bucketStarts = CreateBucketStarts(request.Query, request.Buckets);
        cancellationToken.ThrowIfCancellationRequested();

        var query = request.Query;
        var access = RequireScopedAccess(query.TenantId);
        var unit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind, targetName);
        var session = sessions.Open(
            unit.Id.Value,
            access,
            targetName);

        var bucketCounts = QueryBuckets(session, unit, query, request.Buckets, bucketStarts, cancellationToken);
        var runningCount = QueryRunning(session, unit, query, cancellationToken);
        var failures = QueryTopFailures(session, unit, query, cancellationToken);

        var buckets = request.Buckets
            .Select(bucket => bucketCounts.TryGetValue(bucket.Index, out var counts)
                ? counts.ToSnapshot(bucket)
                : EmptyBucket(bucket))
            .ToArray();

        return ValueTask.FromResult(new WorkflowRunHealthAggregate(
            buckets.Sum(bucket => bucket.StartedCount),
            buckets.Sum(bucket => bucket.SucceededCount),
            buckets.Sum(bucket => bucket.FailedCount),
            buckets.Sum(bucket => bucket.CancelledCount),
            buckets.Sum(bucket => bucket.IncompleteCount),
            buckets.Sum(bucket => bucket.IncidentBearingRunCount),
            buckets.Sum(bucket => bucket.IncidentCount),
            runningCount,
            buckets,
            failures));
    }

    private static IReadOnlyDictionary<int, BucketCounts> QueryBuckets(
        IStorageSession session,
        StorageUnit unit,
        WorkflowRunHealthQuery query,
        IReadOnlyCollection<WorkflowRunHealthBucketRange> buckets,
        IReadOnlyDictionary<DateTimeOffset, int> bucketStarts,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var predicate = new Predicate.And([
            RunKindPredicate(unit, query.IncludeTestRuns)
        ]);
        var profile = query.Bucket == WorkflowRunHealthBucketSize.Hour
            ? ElsaRuntimeV2StorageManifest.WorkflowRunHealthHourlyProfile
            : ElsaRuntimeV2StorageManifest.WorkflowRunHealthDailyProfile;
        var aggregationQuery = new AggregationQuery(profile)
        {
            SourcePredicate = predicate,
            TimeRange = new AggregationTimeRange(query.From, query.To),
            TimeBucketOrigin = query.Bucket == WorkflowRunHealthBucketSize.Hour ? query.From : null,
            TimeZoneId = query.Bucket == WorkflowRunHealthBucketSize.Day ? query.TimeZone : null
        };
        var result = session.Aggregate(aggregationQuery);
        var counts = buckets.ToDictionary(bucket => bucket.Index, _ => new BucketCounts());
        foreach (var row in result.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bucketStart = ReadDateTimeOffset(row, ElsaRuntimeV2StorageManifest.WorkflowRunHealthBucketField);
            var bucketIndex = FindBucketIndex(bucketStart, buckets, bucketStarts);
            if (bucketIndex is null)
                continue;
            counts[bucketIndex.Value].Add(
                ReadInt32(row, ElsaRuntimeV2StorageManifest.WorkflowRunHealthStatusField),
                ReadInt64(row, "count"),
                ReadInt64(row, "incidentTotal"),
                ReadInt64(row, "incidentBearingTotal"));
        }
        return counts;
    }

    private static void ValidateBuckets(
        WorkflowRunHealthQuery query,
        IReadOnlyCollection<WorkflowRunHealthBucketRange> buckets)
    {
        if (buckets.Count == 0)
            throw new WorkflowRunHealthQueryException("At least one workflow run-health bucket is required.");

        var ordered = buckets.ToArray();
        var indexes = new HashSet<int>();
        if (ordered[0].From != query.From)
            throw new WorkflowRunHealthQueryException(
                "Workflow run-health buckets must start at the query's inclusive 'from' instant.");

        for (var index = 0; index < ordered.Length; index++)
        {
            var bucket = ordered[index];
            if (!indexes.Add(bucket.Index))
                throw new WorkflowRunHealthQueryException(
                    "Workflow run-health bucket indexes must be unique.");
            if (bucket.From >= bucket.To)
                throw new WorkflowRunHealthQueryException(
                    "Workflow run-health bucket ranges must be non-empty and ordered.");
            if (bucket.From < query.From || bucket.To > query.To)
                throw new WorkflowRunHealthQueryException(
                    "Workflow run-health bucket ranges must be contained within the query range.");
            if (index > 0 && ordered[index - 1].To != bucket.From)
                throw new WorkflowRunHealthQueryException(
                    "Workflow run-health bucket ranges must be contiguous, ordered, and non-overlapping.");
        }

        if (ordered[^1].To != query.To)
            throw new WorkflowRunHealthQueryException(
                "Workflow run-health buckets must end at the query's exclusive 'to' instant.");
    }

    private static IReadOnlyDictionary<DateTimeOffset, int> CreateBucketStarts(
        WorkflowRunHealthQuery query,
        IReadOnlyCollection<WorkflowRunHealthBucketRange> buckets)
    {
        var kind = query.Bucket == WorkflowRunHealthBucketSize.Hour
            ? AggregationTimeBucketKind.FixedUtc
            : AggregationTimeBucketKind.LocalCalendarDay;
        var width = query.Bucket == WorkflowRunHealthBucketSize.Hour
            ? TimeSpan.FromHours(1)
            : TimeSpan.Zero;
        var result = new Dictionary<DateTimeOffset, int>();
        var ordered = buckets.ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var bucket = ordered[index];
            var start = AggregationTimeBucketCalculator.Bucket(
                bucket.From,
                kind,
                width,
                query.TimeZone,
                query.Bucket == WorkflowRunHealthBucketSize.Hour ? query.From : null);
            if (index > 0 && start != bucket.From)
                throw new WorkflowRunHealthQueryException(
                    "Workflow run-health bucket starts must align with the selected native aggregation profile.");
            if (!result.TryAdd(start, bucket.Index) && query.Bucket == WorkflowRunHealthBucketSize.Day)
                throw new WorkflowRunHealthQueryException(
                    "Workflow run-health bucket ranges must map to distinct native aggregation buckets.");
        }
        return result;
    }

    private static int? FindBucketIndex(
        DateTimeOffset bucketStart,
        IReadOnlyCollection<WorkflowRunHealthBucketRange> buckets,
        IReadOnlyDictionary<DateTimeOffset, int> bucketStarts)
    {
        if (bucketStarts.TryGetValue(bucketStart, out var exactIndex))
            return exactIndex;

        foreach (var bucket in buckets)
            if (bucketStart >= bucket.From && bucketStart < bucket.To)
                return bucket.Index;
        return null;
    }

    private static int QueryRunning(
        IStorageSession session,
        StorageUnit unit,
        WorkflowRunHealthQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = Column(unit, ElsaRuntimeV2StorageManifest.WorkflowRunHealthStatusField);
        var predicate = new Predicate.And([
            new Predicate.Equal(
                status,
                QueryConstant.Of(status, (int)WorkflowExecutionStatus.Running)),
            RunKindPredicate(unit, query.IncludeTestRuns)
        ]);
        var result = session.Aggregate(new AggregationQuery(ElsaRuntimeV2StorageManifest.WorkflowRunHealthRunningProfile)
        {
            SourcePredicate = predicate
        });
        return checked((int)result.Rows.Sum(row => ReadInt64(row, "count")));
    }

    private static IReadOnlyCollection<WorkflowFailureDefinitionSnapshot> QueryTopFailures(
        IStorageSession session,
        StorageUnit unit,
        WorkflowRunHealthQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = Column(unit, ElsaRuntimeV2StorageManifest.WorkflowRunHealthStartedAtField);
        var status = Column(unit, ElsaRuntimeV2StorageManifest.WorkflowRunHealthStatusField);
        var predicate = new Predicate.And([
            Range(startedAt, query.From, query.To),
            new Predicate.Equal(
                status,
                QueryConstant.Of(status, (int)WorkflowExecutionStatus.Faulted)),
            RunKindPredicate(unit, query.IncludeTestRuns)
        ]);
        var result = session.Aggregate(new AggregationQuery(ElsaRuntimeV2StorageManifest.WorkflowRunHealthTopFailuresProfile)
        {
            SourcePredicate = predicate,
            OrderByTerms = [
                new AggregationOrderTerm("failedCount", SortDirection.Descending),
                new AggregationOrderTerm(ElsaRuntimeV2StorageManifest.WorkflowRunHealthDefinitionIdField, SortDirection.Ascending)
            ],
            Take = 5
        });
        return result.Rows
            .Select(row => new WorkflowFailureDefinitionSnapshot(
                ReadString(row, ElsaRuntimeV2StorageManifest.WorkflowRunHealthDefinitionIdField),
                checked((int)ReadInt64(row, "failedCount"))))
            .ToArray();
    }

    private StorageAccess RequireScopedAccess(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var context = accessContextAccessor.Current ??
                      throw new InvalidOperationException("Workflow run-health persistence access context is missing.");
        if (context.Scope is null || context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Groundwork workflow run-health queries require one explicit persistence scope; global and across-scope access are refused.");
        }

        context.EnsureTenantScope(tenantId);
        return StorageAccess.Scoped(new StorageScope(context.Scope.Value));
    }

    private static Predicate RunKindPredicate(StorageUnit unit, bool includeTestRuns)
    {
        var runKind = Column(unit, ElsaRuntimeV2StorageManifest.WorkflowRunHealthRunKindField);
        var testRun = new Predicate.Equal(
            runKind,
            QueryConstant.Of(runKind, (int)WorkflowRunKind.TestRun));
        return includeTestRuns ? Predicate.AlwaysTrue.Instance : new Predicate.Not(testRun);
    }

    private static Predicate.Range Range(ColumnRef column, DateTimeOffset from, DateTimeOffset to) =>
        new(
            column,
            Bound.Inclusive(QueryConstant.Of(column, from)),
            Bound.Exclusive(QueryConstant.Of(column, to)));

    private static ColumnRef Column(StorageUnit unit, string name)
    {
        var definition = unit.Columns.SingleOrDefault(column =>
            StringComparer.Ordinal.Equals(column.Name, name))
            ?? throw new InvalidOperationException(
                $"Groundwork workflow run-health unit '{unit.Id.Value}' does not declare query column '{name}'.");
        var type = definition.Type switch
        {
            PortableType.String => QueryType.String,
            PortableType.DateTimeOffset => QueryType.DateTimeOffset,
            PortableType.Int32 => QueryType.Int32,
            PortableType.Int64 => QueryType.Int64,
            _ => throw new InvalidOperationException(
                $"Groundwork workflow run-health query column '{name}' has unsupported type '{definition.Type}'.")
        };
        return new ColumnRef(
            new TableId(unit.Name),
            name,
            type,
            definition.IsNullable,
            definition.MaxLength);
    }

    private static WorkflowRunHealthBucket EmptyBucket(WorkflowRunHealthBucketRange range) =>
        new(range.From, range.To, 0, 0, 0, 0, 0, 0, 0);

    private static string ReadString(AggregationRow row, string field) =>
        row.Values.TryGetValue(field, out var value) && value is string text
            ? text
            : throw new InvalidDataException($"Groundwork workflow run-health aggregation did not return '{field}'.");

    private static int ReadInt32(AggregationRow row, string field) =>
        checked((int)ReadInt64(row, field));

    private static DateTimeOffset ReadDateTimeOffset(AggregationRow row, string field)
    {
        if (!row.Values.TryGetValue(field, out var value) || value is null)
            throw new InvalidDataException(
                $"Groundwork workflow run-health aggregation did not return timestamp field '{field}'.");
        if (value is DateTimeOffset timestamp)
            return timestamp;
        if (value is DateTime dateTime)
            return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
        throw new InvalidDataException(
            $"Groundwork workflow run-health aggregation field '{field}' was not a timestamp.");
    }

    private static long ReadInt64(AggregationRow row, string field)
    {
        if (!row.Values.TryGetValue(field, out var value) || value is null)
            throw new InvalidDataException(
                $"Groundwork workflow run-health aggregation did not return numeric field '{field}'.");
        try
        {
            return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidDataException(
                $"Groundwork workflow run-health aggregation field '{field}' was not numeric.", exception);
        }
    }

    private sealed class BucketCounts
    {
        public long Started { get; private set; }
        public long Succeeded { get; private set; }
        public long Failed { get; private set; }
        public long Cancelled { get; private set; }
        public long Incomplete { get; private set; }
        public long IncidentBearing { get; private set; }
        public long Incidents { get; private set; }

        public void Add(int status, long count, long incidents, long incidentBearing)
        {
            if (!Enum.IsDefined((WorkflowExecutionStatus)status))
            {
                throw new InvalidDataException(
                    $"Groundwork workflow run-health aggregation returned unsupported workflow status '{status}'.");
            }

            Started = checked(Started + count);
            switch ((WorkflowExecutionStatus)status)
            {
                case WorkflowExecutionStatus.Completed:
                    Succeeded = checked(Succeeded + count);
                    break;
                case WorkflowExecutionStatus.Faulted:
                    Failed = checked(Failed + count);
                    break;
                case WorkflowExecutionStatus.Cancelled:
                    Cancelled = checked(Cancelled + count);
                    break;
                default:
                    Incomplete = checked(Incomplete + count);
                    break;
            }

            IncidentBearing = checked(IncidentBearing + incidentBearing);
            Incidents = checked(Incidents + incidents);
        }

        public WorkflowRunHealthBucket ToSnapshot(WorkflowRunHealthBucketRange range) =>
            new(
                range.From,
                range.To,
                checked((int)Started),
                checked((int)Succeeded),
                checked((int)Failed),
                checked((int)Cancelled),
                checked((int)Incomplete),
                checked((int)IncidentBearing),
                checked((int)Incidents));
    }
}
