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
        cancellationToken.ThrowIfCancellationRequested();

        var query = request.Query;
        var access = RequireScopedAccess(query.TenantId);
        var unit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowRunHealthStateDocumentKind, targetName);
        var session = sessions.Open(
            unit.Id.Value,
            access,
            targetName);

        var bucketCounts = request.Buckets
            .Select(bucket => QueryBucket(session, unit, query, bucket, cancellationToken))
            .ToDictionary(result => result.Index, result => result.Counts);
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

    private static BucketResult QueryBucket(
        IStorageSession session,
        StorageUnit unit,
        WorkflowRunHealthQuery query,
        WorkflowRunHealthBucketRange bucket,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = Column(unit, ElsaRuntimeV2StorageManifest.WorkflowRunHealthStartedAtField);
        var predicate = new Predicate.And([
            Range(startedAt, bucket.From, bucket.To),
            RunKindPredicate(unit, query.IncludeTestRuns)
        ]);
        var result = session.Aggregate(new AggregationQuery(ElsaRuntimeV2StorageManifest.WorkflowRunHealthStatusProfile)
        {
            SourcePredicate = predicate
        });
        var counts = new BucketCounts();
        foreach (var row in result.Rows)
            counts.Add(
                ReadInt32(row, ElsaRuntimeV2StorageManifest.WorkflowRunHealthStatusField),
                ReadInt64(row, "count"),
                ReadInt64(row, "incidentTotal"),
                ReadInt64(row, "incidentBearingTotal"));
        return new BucketResult(bucket.Index, counts);
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

    private sealed record BucketResult(int Index, BucketCounts Counts);

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
