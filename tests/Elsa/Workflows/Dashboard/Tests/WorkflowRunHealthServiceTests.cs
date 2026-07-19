using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Dashboard.Tests;

public sealed class WorkflowRunHealthServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AggregatesEveryAuthorizedRunBeyondANormalPageAndKeepsOutcomesIndependentFromIncidents()
    {
        var executions = Enumerable.Range(0, 125)
            .Select(index => Execution(
                $"run-{index}",
                index % 5 == 0 ? WorkflowExecutionStatus.Faulted : WorkflowExecutionStatus.Completed,
                Now.AddMinutes(index),
                definitionId: index % 2 == 0 ? "definition-a" : "definition-b"))
            .Concat([
                Execution("running-old", WorkflowExecutionStatus.Running, Now.AddDays(-2)),
                Execution("other-tenant", WorkflowExecutionStatus.Faulted, Now.AddHours(1), tenantId: "tenant-b"),
                Execution("test-run", WorkflowExecutionStatus.Faulted, Now.AddHours(1), runKind: WorkflowRunKind.TestRun),
                Execution("at-exclusive-to", WorkflowExecutionStatus.Faulted, Now.AddDays(1))
            ])
            .ToArray();
        var incidents = new[]
        {
            Incident("incident-1", "run-0"),
            Incident("incident-2", "run-0"),
            Incident("incident-3", "run-1"),
            Incident("incident-other", "other-tenant")
        };
        var service = Service(executions, incidents);

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

    [Fact]
    public async Task IncludeTestRunsIsExplicit()
    {
        var service = Service([
            Execution("published", WorkflowExecutionStatus.Completed, Now),
            Execution("test", WorkflowExecutionStatus.Faulted, Now, runKind: WorkflowRunKind.TestRun)
        ]);

        var excluded = await service.QueryAsync(Query(Now, Now.AddHours(1)));
        var included = await service.QueryAsync(Query(Now, Now.AddHours(1)) with { IncludeTestRuns = true });

        Assert.Equal(1, excluded.StartedCount);
        Assert.Equal(2, included.StartedCount);
        Assert.Equal(1, included.FailedCount);
    }

    [Fact]
    public async Task DayBucketUsesIanaCalendarAcrossSpringDstBoundary()
    {
        var from = new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.FromHours(1));
        var to = new DateTimeOffset(2026, 3, 30, 0, 0, 0, TimeSpan.FromHours(2));
        var service = Service([Execution("run", WorkflowExecutionStatus.Completed, from.AddHours(22))]);

        var result = await service.QueryAsync(Query(from, to) with { TimeZone = "Europe/Amsterdam" });

        var bucket = Assert.Single(result.Buckets);
        Assert.Equal(TimeSpan.FromHours(23), bucket.To - bucket.From);
        Assert.Equal(1, bucket.StartedCount);
    }

    [Fact]
    public async Task HourBucketsRepresentBothRepeatedHoursAcrossAutumnDstBoundary()
    {
        var from = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.FromHours(2));
        var to = new DateTimeOffset(2026, 10, 25, 4, 0, 0, TimeSpan.FromHours(1));
        var service = Service([]);

        var result = await service.QueryAsync(Query(from, to) with
        {
            TimeZone = "Europe/Amsterdam",
            Bucket = WorkflowRunHealthBucketSize.Hour
        });

        Assert.Equal(5, result.Buckets.Count);
        Assert.All(result.Buckets, bucket => Assert.Equal(TimeSpan.FromHours(1), bucket.To - bucket.From));
    }

    [Theory]
    [InlineData("invalid-zone")]
    [InlineData("")]
    public async Task RejectsInvalidIanaTimeZone(string timeZone)
    {
        var service = Service([]);

        await Assert.ThrowsAsync<WorkflowRunHealthQueryException>(async () =>
            await service.QueryAsync(Query(Now, Now.AddHours(1)) with { TimeZone = timeZone }));
    }

    [Fact]
    public async Task UnsupportedProviderIsExplicitlyUnavailable()
    {
        var service = new WorkflowRunHealthService(new UnavailableWorkflowRunHealthDataSource(), new FixedTimeProvider(Now));

        var result = await service.QueryAsync(Query(Now, Now.AddHours(1)));

        Assert.Equal(WorkflowRunHealthStatus.Unavailable, result.Status);
        Assert.Equal("WORKFLOW_RUN_HEALTH_PROVIDER_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task TenantScopeIsRequiredBeforeTheProviderIsQueried()
    {
        var service = Service([]);

        await Assert.ThrowsAsync<WorkflowRunHealthQueryException>(async () =>
            await service.QueryAsync(Query(Now, Now.AddHours(1)) with { TenantId = "" }));
    }

    private static WorkflowRunHealthService Service(
        IReadOnlyCollection<WorkflowExecutionState> executions,
        IReadOnlyCollection<IncidentState>? incidents = null)
    {
        var executionStore = new InMemoryWorkflowExecutionStateStore();
        var incidentStore = new InMemoryIncidentStateStore();
        foreach (var execution in executions)
            executionStore.SaveAsync(execution).AsTask().GetAwaiter().GetResult();
        foreach (var incident in incidents ?? [])
            incidentStore.SaveAsync(incident).AsTask().GetAwaiter().GetResult();
        return new(new InMemoryWorkflowRunHealthDataSource(executionStore, incidentStore), new FixedTimeProvider(Now));
    }

    private static WorkflowRunHealthQuery Query(DateTimeOffset from, DateTimeOffset to) =>
        new(from, to, "Etc/UTC", WorkflowRunHealthBucketSize.Day, "tenant-a");

    private static WorkflowExecutionState Execution(
        string id,
        WorkflowExecutionStatus status,
        DateTimeOffset startedAt,
        string definitionId = "definition",
        string tenantId = "tenant-a",
        WorkflowRunKind runKind = WorkflowRunKind.PublishedRun) =>
        new(
            id,
            new WorkflowExecutableIdentity($"artifact-{id}", definitionId, $"version-{id}", "1", "hash"),
            status,
            null,
            startedAt,
            startedAt,
            startedAt,
            status.IsTerminal() ? startedAt.AddMinutes(1) : null,
            null,
            null,
            tenantId,
            new Dictionary<string, string>())
        {
            RunKind = runKind
        };

    private static IncidentState Incident(string id, string executionId) =>
        new(id, executionId, null, null, IncidentSeverity.Error, IncidentStatus.Open,
            IncidentResolutionAction.Retry, "Failure", "Failed", Now, null);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
