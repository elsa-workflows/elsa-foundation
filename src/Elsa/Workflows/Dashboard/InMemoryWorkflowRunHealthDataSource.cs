using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Dashboard;

public sealed class InMemoryWorkflowRunHealthDataSource(
    InMemoryWorkflowExecutionStateStore executions,
    InMemoryIncidentStateStore incidents) : IWorkflowRunHealthDataSource
{
    private const int MaximumTopDefinitions = 5;

    public bool IsAvailable => true;

    public async ValueTask<WorkflowRunHealthAggregate> QueryAsync(
        WorkflowRunHealthDataQuery request,
        CancellationToken cancellationToken = default)
    {
        var query = request.Query;
        var executionSnapshot = await executions.ListAsync(cancellationToken);
        var incidentSnapshot = await incidents.ListAllAsync(cancellationToken);
        var buckets = request.Buckets.Select(x => new MutableBucket(x)).ToArray();
        var relevant = new Dictionary<string, MutableBucket>(StringComparer.Ordinal);
        var failures = new Dictionary<string, int>(StringComparer.Ordinal);
        var totals = new MutableCounts();
        var running = 0;

        foreach (var execution in executionSnapshot)
        {
            if (!StringComparer.Ordinal.Equals(execution.TenantId, query.TenantId) ||
                (!query.IncludeTestRuns && execution.Origin == WorkflowExecutionOrigin.TestRun))
                continue;
            if (execution.Status == WorkflowExecutionStatus.Running)
                running++;
            if (execution.StartedAt is not { } started || started < query.From || started >= query.To)
                continue;

            var bucket = buckets.First(x => started >= x.Range.From && started < x.Range.To);
            ApplyOutcome(execution.Status, totals);
            ApplyOutcome(execution.Status, bucket.Counts);
            relevant.Add(execution.WorkflowExecutionId, bucket);
            if (execution.Status == WorkflowExecutionStatus.Faulted)
                failures[execution.PinnedExecutable.DefinitionId] = failures.GetValueOrDefault(execution.PinnedExecutable.DefinitionId) + 1;
        }

        var incidentBearing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var incident in incidentSnapshot)
        {
            if (!relevant.TryGetValue(incident.WorkflowExecutionId, out var bucket))
                continue;
            totals.IncidentCount++;
            bucket.Counts.IncidentCount++;
            if (!incidentBearing.Add(incident.WorkflowExecutionId))
                continue;
            totals.IncidentBearingRunCount++;
            bucket.Counts.IncidentBearingRunCount++;
        }

        return new(
            totals.StartedCount,
            totals.SucceededCount,
            totals.FailedCount,
            totals.CancelledCount,
            totals.IncompleteCount,
            totals.IncidentBearingRunCount,
            totals.IncidentCount,
            running,
            buckets.Select(x => x.ToSnapshot()).ToArray(),
            failures.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).Take(MaximumTopDefinitions)
                .Select(x => new WorkflowFailureDefinitionSnapshot(x.Key, x.Value)).ToArray());
    }

    private static void ApplyOutcome(WorkflowExecutionStatus status, MutableCounts counts)
    {
        counts.StartedCount++;
        if (status == WorkflowExecutionStatus.Completed) counts.SucceededCount++;
        else if (status == WorkflowExecutionStatus.Faulted) counts.FailedCount++;
        else if (status == WorkflowExecutionStatus.Cancelled) counts.CancelledCount++;
        else counts.IncompleteCount++;
    }

    private sealed class MutableBucket(WorkflowRunHealthBucketRange range)
    {
        public WorkflowRunHealthBucketRange Range { get; } = range;
        public MutableCounts Counts { get; } = new();
        public WorkflowRunHealthBucket ToSnapshot() => new(
            Range.From, Range.To, Counts.StartedCount, Counts.SucceededCount, Counts.FailedCount,
            Counts.CancelledCount, Counts.IncompleteCount, Counts.IncidentBearingRunCount, Counts.IncidentCount);
    }

    private sealed class MutableCounts
    {
        public int StartedCount { get; set; }
        public int SucceededCount { get; set; }
        public int FailedCount { get; set; }
        public int CancelledCount { get; set; }
        public int IncompleteCount { get; set; }
        public int IncidentBearingRunCount { get; set; }
        public int IncidentCount { get; set; }
    }
}

public sealed class UnavailableWorkflowRunHealthDataSource : IWorkflowRunHealthDataSource
{
    public bool IsAvailable => false;

    public ValueTask<WorkflowRunHealthAggregate> QueryAsync(
        WorkflowRunHealthDataQuery query,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Unavailable workflow run-health providers cannot execute queries.");
}
