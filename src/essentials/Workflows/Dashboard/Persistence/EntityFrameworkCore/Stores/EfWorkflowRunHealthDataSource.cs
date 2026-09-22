using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.Stores;

/// <summary>Concrete EF reader for the Runtime run-health projection used by Dashboard.</summary>
/// <remarks>
/// The projection is streamed in the requested time range instead of loading all workflow executions.
/// Every row that contributes to an answer is checked against both its lossless physical envelope and
/// its authoritative JSON content before it is folded into the result.
/// </remarks>
public sealed class EfWorkflowRunHealthDataSource(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IWorkflowRunHealthDataSource
{
    private const int MaximumBucketCount = 744;
    private const int MaximumTopDefinitions = 5;
    private const int MaximumSourceRows = 100_000;

    public bool IsAvailable => true;

    public async ValueTask<WorkflowRunHealthAggregate> QueryAsync(
        WorkflowRunHealthDataQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Query);
        ArgumentNullException.ThrowIfNull(request.Buckets);
        if (request.Buckets.Count > MaximumBucketCount)
            throw new WorkflowRunHealthQueryException(
                $"At most {MaximumBucketCount} workflow run-health buckets may be queried at once.");

        ValidateBuckets(request.Query, request.Buckets);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RequireScope(request.Query.TenantId);
        var buckets = request.Buckets.Select(range => new MutableBucket(range)).ToArray();
        var failures = new Dictionary<string, FailureCount>(StringComparer.Ordinal);

        var startedRows = 0;
        await foreach (var row in BoundedSourceRows(QueryStartedRows(scope, request.Query))
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (++startedRows > MaximumSourceRows)
                throw new WorkflowRunHealthQueryException("The workflow run-health query exceeds its bounded source-row limit.");
            var projection = ReadChecked(row, scope);
            if (projection.StartedAt is not { } startedAt)
                continue;

            var bucket = buckets.SingleOrDefault(candidate => startedAt >= candidate.Range.From && startedAt < candidate.Range.To)
                         ?? throw new InvalidDataException(
                             $"The workflow run-health row '{row.Id}' falls outside the requested bucket ranges.");
            ApplyOutcome(projection.Status, bucket.Counts);
            if (projection.Status == WorkflowExecutionStatus.Faulted)
            {
                var definition = failures.GetValueOrDefault(projection.DefinitionId)
                                 ?? new FailureCount(0, projection.DefinitionIdOrderKey);
                failures[projection.DefinitionId] = new(
                    definition.FailedCount + 1,
                    projection.DefinitionIdOrderKey);
            }

            bucket.Counts.IncidentCount = checked(bucket.Counts.IncidentCount + projection.IncidentCount);
            bucket.Counts.IncidentBearingRunCount = checked(
                bucket.Counts.IncidentBearingRunCount + projection.IncidentBearingCount);
        }

        var runningCount = 0;
        var runningRows = 0;
        await foreach (var row in BoundedSourceRows(QueryRunningRows(scope, request.Query))
                           .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (++runningRows > MaximumSourceRows)
                throw new WorkflowRunHealthQueryException("The running workflow query exceeds its bounded source-row limit.");
            var projection = ReadChecked(row, scope);
            if (projection.Status == WorkflowExecutionStatus.Running)
                runningCount = checked(runningCount + 1);
        }

        var snapshots = buckets.Select(bucket => bucket.ToSnapshot()).ToArray();
        var topFailures = failures
            .OrderByDescending(pair => pair.Value.FailedCount)
            .ThenBy(pair => pair.Value.DefinitionIdOrderKey, StringComparer.Ordinal)
            .Take(MaximumTopDefinitions)
            .Select(pair => new WorkflowFailureDefinitionSnapshot(pair.Key, pair.Value.FailedCount))
            .ToArray();

        return new WorkflowRunHealthAggregate(
            snapshots.Sum(bucket => bucket.StartedCount),
            snapshots.Sum(bucket => bucket.SucceededCount),
            snapshots.Sum(bucket => bucket.FailedCount),
            snapshots.Sum(bucket => bucket.CancelledCount),
            snapshots.Sum(bucket => bucket.IncompleteCount),
            snapshots.Sum(bucket => bucket.IncidentBearingRunCount),
            snapshots.Sum(bucket => bucket.IncidentCount),
            runningCount,
            snapshots,
            topFailures);
    }

    private IQueryable<WorkflowRunHealthStateEntity> QueryStartedRows(
        string scope,
        WorkflowRunHealthQuery query)
    {
        var source = BaseQuery(scope)
            .Where(row => row.StartedAtUtcTicks >= query.From.UtcTicks && row.StartedAtUtcTicks < query.To.UtcTicks);
        return ApplyRunKind(source, query.IncludeTestRuns);
    }

    private IQueryable<WorkflowRunHealthStateEntity> QueryRunningRows(
        string scope,
        WorkflowRunHealthQuery query) =>
        ApplyRunKind(BaseQuery(scope).Where(row => row.Status == (int)WorkflowExecutionStatus.Running), query.IncludeTestRuns);

    // A read past the bound is refused rather than truncated, so the order never selects rows. It only makes the
    // limited read deterministic, following the scope indexes and ending in the key.
    private static IQueryable<WorkflowRunHealthStateEntity> BoundedSourceRows(IQueryable<WorkflowRunHealthStateEntity> rows) =>
        rows.OrderBy(row => row.StartedAtUtcTicks)
            .ThenBy(row => row.WorkflowExecutionIdOrderKey)
            .ThenBy(row => row.Id)
            .Take(MaximumSourceRows + 1);

    private IQueryable<WorkflowRunHealthStateEntity> BaseQuery(string scope) =>
        context.WorkflowRunHealthStates
            .AsNoTracking()
            .Where(row => row.ScopeKeyHash == Hash(scope) &&
                          row.ScopeKey == Encode(scope));

    private static IQueryable<WorkflowRunHealthStateEntity> ApplyRunKind(
        IQueryable<WorkflowRunHealthStateEntity> source,
        bool includeTestRuns) =>
        includeTestRuns
            ? source
            : source.Where(row => row.RunKind != (int)WorkflowRunKind.TestRun);

    private string RequireScope(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var current = accessContextAccessor.Current;
        var scope = current.RequireScope().Value;
        current.EnsureTenantScope(tenantId);
        if (scope.Length > 256)
            throw new ArgumentException("Runtime persistence scope cannot exceed 256 UTF-16 code units.", nameof(tenantId));
        return scope;
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
                throw new WorkflowRunHealthQueryException("Workflow run-health bucket indexes must be unique.");
            if (bucket.From >= bucket.To)
                throw new WorkflowRunHealthQueryException("Workflow run-health bucket ranges must be non-empty and ordered.");
            if (bucket.From < query.From || bucket.To > query.To)
                throw new WorkflowRunHealthQueryException("Workflow run-health bucket ranges must be contained within the query range.");
            if (index > 0 && ordered[index - 1].To != bucket.From)
                throw new WorkflowRunHealthQueryException("Workflow run-health bucket ranges must be contiguous, ordered, and non-overlapping.");
        }

        if (ordered[^1].To != query.To)
            throw new WorkflowRunHealthQueryException("Workflow run-health buckets must end at the query's exclusive 'to' instant.");
    }

    private static RunHealthProjection ReadChecked(WorkflowRunHealthStateEntity row, string scope)
    {
        try
        {
            if (row.Revision <= 0 || EfSchemaVersion.NotReadable("RuntimeOperationalState", row.SchemaVersion, RuntimeOperationalStateEfModule.SchemaVersion))
                throw new InvalidDataException("The workflow run-health row envelope is corrupt.");

            var workflowExecutionId = Decode(row.WorkflowExecutionId);
            var definitionId = Decode(row.DefinitionId);
            if (row.Id != CompositeId(scope, workflowExecutionId) ||
                row.ScopeKey != Encode(scope) || row.ScopeKeyHash != Hash(scope) ||
                row.WorkflowExecutionIdHash != Hash(workflowExecutionId) ||
                row.WorkflowExecutionIdOrderKey != Order(workflowExecutionId) ||
                row.DefinitionIdHash != Hash(definitionId) ||
                row.DefinitionIdOrderKey != Order(definitionId))
                throw new InvalidDataException("The workflow run-health row identity envelope is corrupt.");

            var raw = JsonSerializer.Deserialize<RawRunHealthProjection>(row.ContentJson, JsonOptions)
                      ?? throw new InvalidDataException("The workflow run-health content is empty.");
            var projection = new RunHealthProjection(
                Decode(raw.WorkflowExecutionId),
                Decode(raw.DefinitionId),
                raw.RunKind,
                raw.StartedAt,
                raw.Status,
                raw.IncidentCount,
                raw.IncidentBearingCount,
                row.DefinitionIdOrderKey);
            if (!StringComparer.Ordinal.Equals(projection.WorkflowExecutionId, workflowExecutionId) ||
                !StringComparer.Ordinal.Equals(projection.DefinitionId, definitionId) ||
                projection.StartedAt?.UtcTicks != row.StartedAtUtcTicks ||
                projection.StartedAt is { } started && row.StartedAtOffsetMinutes != (int)started.Offset.TotalMinutes ||
                projection.StartedAt is null && row.StartedAtOffsetMinutes is not null ||
                (int)projection.RunKind != row.RunKind || (int)projection.Status != row.Status ||
                projection.IncidentCount != row.IncidentCount ||
                projection.IncidentBearingCount != row.IncidentBearingCount)
                throw new InvalidDataException("The workflow run-health row projection does not match its content.");
            if (!Enum.IsDefined(projection.RunKind) || !Enum.IsDefined(projection.Status) ||
                projection.IncidentCount < 0 || projection.IncidentBearingCount is < 0 or > 1 ||
                projection.IncidentBearingCount > projection.IncidentCount)
                throw new InvalidDataException("The workflow run-health projection contains invalid values.");
            return projection;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or InvalidOperationException or NotSupportedException or OverflowException)
        {
            throw new InvalidDataException("The persisted workflow run-health state is not valid current data.", exception);
        }
    }

    private static string CompositeId(string scope, string workflowExecutionId) =>
        EfRelationalIdentity.HashLengthFramed(scope, workflowExecutionId);

    private static string Encode(string value) => EfRelationalIdentity.Encode(value);
    private static string Decode(string value) => EfRelationalIdentity.Decode(value);
    private static string Hash(string value) => EfRelationalIdentity.Hash(value);
    private static string Order(string value) => Convert.ToHexString(
        EfRelationalIdentity.CreateOrderKey(value, RuntimeOperationalStateEfModule.IdentityMaximumLength));

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record RawRunHealthProjection(
        string WorkflowExecutionId,
        string DefinitionId,
        WorkflowRunKind RunKind,
        DateTimeOffset? StartedAt,
        WorkflowExecutionStatus Status,
        long IncidentCount,
        long IncidentBearingCount);

    private sealed record RunHealthProjection(
        string WorkflowExecutionId,
        string DefinitionId,
        WorkflowRunKind RunKind,
        DateTimeOffset? StartedAt,
        WorkflowExecutionStatus Status,
        long IncidentCount,
        long IncidentBearingCount,
        string DefinitionIdOrderKey);

    private sealed record FailureCount(int FailedCount, string DefinitionIdOrderKey);

    private sealed class MutableBucket(WorkflowRunHealthBucketRange range)
    {
        public WorkflowRunHealthBucketRange Range { get; } = range;
        public MutableCounts Counts { get; } = new();

        public WorkflowRunHealthBucket ToSnapshot() => new(
            Range.From,
            Range.To,
            checked((int)Counts.StartedCount),
            checked((int)Counts.SucceededCount),
            checked((int)Counts.FailedCount),
            checked((int)Counts.CancelledCount),
            checked((int)Counts.IncompleteCount),
            checked((int)Counts.IncidentBearingRunCount),
            checked((int)Counts.IncidentCount));
    }

    private sealed class MutableCounts
    {
        public long StartedCount { get; set; }
        public long SucceededCount { get; set; }
        public long FailedCount { get; set; }
        public long CancelledCount { get; set; }
        public long IncompleteCount { get; set; }
        public long IncidentBearingRunCount { get; set; }
        public long IncidentCount { get; set; }
    }

    private static void ApplyOutcome(WorkflowExecutionStatus status, MutableCounts counts)
    {
        if (!Enum.IsDefined(status))
            throw new InvalidDataException("The workflow run-health projection contains an undefined status.");
        counts.StartedCount = checked(counts.StartedCount + 1);
        if (status == WorkflowExecutionStatus.Completed)
            counts.SucceededCount = checked(counts.SucceededCount + 1);
        else if (status == WorkflowExecutionStatus.Faulted)
            counts.FailedCount = checked(counts.FailedCount + 1);
        else if (status == WorkflowExecutionStatus.Cancelled)
            counts.CancelledCount = checked(counts.CancelledCount + 1);
        else
            counts.IncompleteCount = checked(counts.IncompleteCount + 1);
    }
}
