namespace Elsa.Workflows.Dashboard;

public sealed class WorkflowRunHealthService(
    IWorkflowRunHealthDataSource dataSource,
    TimeProvider? timeProvider = null) : IWorkflowRunHealthService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<WorkflowRunHealthSnapshot> QueryAsync(
        WorkflowRunHealthQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var timeZone = Validate(query);
        var generatedAt = _timeProvider.GetUtcNow();
        if (!dataSource.IsAvailable)
            return WorkflowRunHealthSnapshot.Unavailable(query, generatedAt);

        var buckets = CreateBuckets(query, timeZone);
        var aggregate = await dataSource.QueryAsync(new WorkflowRunHealthDataQuery(query, buckets), cancellationToken);
        return new(
            WorkflowRunHealthStatus.Ready,
            generatedAt,
            query.From,
            query.To,
            query.TimeZone,
            query.Bucket,
            query.IncludeTestRuns,
            aggregate.StartedCount,
            aggregate.SucceededCount,
            aggregate.FailedCount,
            aggregate.CancelledCount,
            aggregate.IncompleteCount,
            aggregate.IncidentBearingRunCount,
            aggregate.IncidentCount,
            aggregate.RunningCount,
            Percentage(aggregate.FailedCount, aggregate.StartedCount),
            Percentage(aggregate.IncidentBearingRunCount, aggregate.StartedCount),
            aggregate.Buckets,
            aggregate.HighestFailureDefinitions);
    }

    private static TimeZoneInfo Validate(WorkflowRunHealthQuery query)
    {
        if (query.From >= query.To)
            throw new WorkflowRunHealthQueryException("The 'from' instant must be earlier than the exclusive 'to' instant.");
        if (string.IsNullOrWhiteSpace(query.TenantId))
            throw new WorkflowRunHealthQueryException("An authenticated tenant scope is required.");
        if (string.IsNullOrWhiteSpace(query.TimeZone))
            throw new WorkflowRunHealthQueryException("An IANA time zone is required.");
        if (!Enum.IsDefined(query.Bucket))
            throw new WorkflowRunHealthQueryException("Bucket must be 'hour' or 'day'.");

        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(query.TimeZone);
            if (!timeZone.HasIanaId)
                throw new WorkflowRunHealthQueryException("The time zone must be an IANA identifier.");
            return timeZone;
        }
        catch (TimeZoneNotFoundException)
        {
            throw new WorkflowRunHealthQueryException($"Unknown IANA time zone '{query.TimeZone}'.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new WorkflowRunHealthQueryException($"Invalid IANA time zone '{query.TimeZone}'.");
        }
    }

    private static IReadOnlyCollection<WorkflowRunHealthBucketRange> CreateBuckets(
        WorkflowRunHealthQuery query,
        TimeZoneInfo timeZone)
    {
        var result = new List<WorkflowRunHealthBucketRange>();
        var current = query.From;
        while (current < query.To)
        {
            var next = query.Bucket == WorkflowRunHealthBucketSize.Hour
                ? current.AddHours(1)
                : NextLocalDayBoundary(current, timeZone);
            if (next <= current)
                throw new WorkflowRunHealthQueryException("The requested calendar bucket could not advance.");
            if (next > query.To)
                next = query.To;
            result.Add(new(result.Count, current, next));
            current = next;
        }
        return result;
    }

    private static DateTimeOffset NextLocalDayBoundary(DateTimeOffset current, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(current, timeZone);
        var nextDate = local.Date.AddDays(1);
        return new DateTimeOffset(nextDate, timeZone.GetUtcOffset(nextDate));
    }

    private static decimal Percentage(int numerator, int denominator) =>
        denominator == 0 ? 0 : Math.Round(100m * numerator / denominator, 2, MidpointRounding.AwayFromZero);
}
