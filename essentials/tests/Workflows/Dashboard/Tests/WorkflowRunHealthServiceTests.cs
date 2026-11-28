using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Dashboard.Tests;

public sealed class WorkflowRunHealthServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProjectsProviderTotalsAndKeepsOutcomesIndependentFromIncidents()
    {
        var source = new AvailableDataSource(new WorkflowRunHealthAggregate(
            StartedCount: 125,
            SucceededCount: 100,
            FailedCount: 25,
            CancelledCount: 0,
            IncompleteCount: 0,
            IncidentBearingRunCount: 2,
            IncidentCount: 3,
            RunningCount: 1,
            Buckets: [],
            HighestFailureDefinitions: [new("definition-a", 13), new("definition-b", 12)]));
        var service = new WorkflowRunHealthService(source, new FakeTimeProvider(Now));

        var result = await service.QueryAsync(Query(Now, Now.AddDays(1)));

        Assert.Equal(WorkflowRunHealthStatus.Ready, result.Status);
        Assert.Equal(125, result.StartedCount);
        Assert.Equal(100, result.SucceededCount);
        Assert.Equal(25, result.FailedCount);
        Assert.Equal(0, result.CancelledCount);
        Assert.Equal(0, result.IncompleteCount);
        Assert.Equal(2, result.IncidentBearingRunCount);
        Assert.Equal(3, result.IncidentCount);
        Assert.Equal(1, result.RunningCount);
        Assert.Equal(20m, result.FailurePercentage);
        Assert.Equal(1.6m, result.IncidentBearingPercentage);
        Assert.Equal(13, result.HighestFailureDefinitions.Single(x => x.DefinitionId == "definition-a").FailedCount);
        Assert.Equal(12, result.HighestFailureDefinitions.Single(x => x.DefinitionId == "definition-b").FailedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncludeTestRunsIsForwardedToTheProviderAndResult(bool includeTestRuns)
    {
        var source = new AvailableDataSource();
        var service = new WorkflowRunHealthService(source);
        var query = Query(Now, Now.AddHours(1)) with { IncludeTestRuns = includeTestRuns };

        var result = await service.QueryAsync(query);

        Assert.Equal(query, source.ObservedQuery?.Query);
        Assert.Equal(includeTestRuns, result.IncludeTestRuns);
    }

    [Fact]
    public async Task DayBucketUsesIanaCalendarAcrossSpringDstBoundary()
    {
        var from = new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.FromHours(1));
        var to = new DateTimeOffset(2026, 3, 30, 0, 0, 0, TimeSpan.FromHours(2));
        var source = new AvailableDataSource();
        var service = new WorkflowRunHealthService(source);

        await service.QueryAsync(Query(from, to) with { TimeZone = "Europe/Amsterdam" });

        Assert.NotNull(source.ObservedQuery);
        var bucket = Assert.Single(source.ObservedQuery.Buckets);
        Assert.Equal(from, bucket.From);
        Assert.Equal(to, bucket.To);
        Assert.Equal(TimeSpan.FromHours(23), bucket.To - bucket.From);
    }

    [Fact]
    public async Task HourBucketsRepresentBothRepeatedHoursAcrossAutumnDstBoundary()
    {
        var from = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.FromHours(2));
        var to = new DateTimeOffset(2026, 10, 25, 4, 0, 0, TimeSpan.FromHours(1));
        var source = new AvailableDataSource();
        var service = new WorkflowRunHealthService(source);

        await service.QueryAsync(Query(from, to) with
        {
            TimeZone = "Europe/Amsterdam",
            Bucket = WorkflowRunHealthBucketSize.Hour
        });

        Assert.NotNull(source.ObservedQuery);
        var buckets = source.ObservedQuery.Buckets;
        Assert.Equal(5, buckets.Count);
        Assert.All(buckets, bucket => Assert.Equal(TimeSpan.FromHours(1), bucket.To - bucket.From));
    }

    [Theory]
    [InlineData("invalid-zone")]
    [InlineData("")]
    public async Task RejectsInvalidIanaTimeZone(string timeZone)
    {
        var source = new AvailableDataSource();
        var service = new WorkflowRunHealthService(source);

        await Assert.ThrowsAsync<WorkflowRunHealthQueryException>(async () =>
            await service.QueryAsync(Query(Now, Now.AddHours(1)) with { TimeZone = timeZone }));
        Assert.Null(source.ObservedQuery);
    }

    [Fact]
    public async Task UnsupportedProviderIsExplicitlyUnavailable()
    {
        var service = new WorkflowRunHealthService(new UnavailableWorkflowRunHealthDataSource(), new FakeTimeProvider(Now));

        var result = await service.QueryAsync(Query(Now, Now.AddHours(1)));

        Assert.Equal(WorkflowRunHealthStatus.Unavailable, result.Status);
        Assert.Equal("WORKFLOW_RUN_HEALTH_PROVIDER_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task TenantScopeIsRequiredBeforeTheProviderIsQueried()
    {
        var source = new AvailableDataSource();
        var service = new WorkflowRunHealthService(source);

        await Assert.ThrowsAsync<WorkflowRunHealthQueryException>(async () =>
            await service.QueryAsync(Query(Now, Now.AddHours(1)) with { TenantId = "" }));
        Assert.Null(source.ObservedQuery);
    }

    private static WorkflowRunHealthQuery Query(DateTimeOffset from, DateTimeOffset to) =>
        new(from, to, "Etc/UTC", WorkflowRunHealthBucketSize.Day, "tenant-a");

    private sealed class AvailableDataSource(WorkflowRunHealthAggregate? aggregate = null) : IWorkflowRunHealthDataSource
    {
        public bool IsAvailable => true;
        public WorkflowRunHealthDataQuery? ObservedQuery { get; private set; }

        public ValueTask<WorkflowRunHealthAggregate> QueryAsync(
            WorkflowRunHealthDataQuery query, CancellationToken cancellationToken = default)
        {
            ObservedQuery = query;
            return ValueTask.FromResult(aggregate ?? new(0, 0, 0, 0, 0, 0, 0, 0, [], []));
        }
    }
}
