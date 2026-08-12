using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Attention.Tests;

public sealed class WorkflowRuntimeAttentionRecordsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-13T10:00:00Z");

    [Fact]
    public void MapIncident_MapsBlockingAndOpenKindsWithFingerprintAndObservation()
    {
        var execution = Execution("run-1", createdAt: Now.AddMinutes(-10));

        var blocking = WorkflowRuntimeAttentionRecords.MapIncident(
            Incident("incident-1", "run-1", IncidentStatus.Blocking, Now.AddMinutes(-5)), execution, Now);
        var open = WorkflowRuntimeAttentionRecords.MapIncident(
            Incident("incident-2", "run-1", IncidentStatus.Open, Now.AddMinutes(5)), execution, Now);

        Assert.Equal(WorkflowRuntimeAttentionKind.BlockingIncident, blocking.Kind);
        Assert.Equal(WorkflowRuntimeAttentionKind.OpenIncident, open.Kind);
        Assert.Equal("run-1", blocking.WorkflowExecutionId);
        Assert.Equal(execution.PinnedExecutable.DefinitionId, blocking.WorkflowDefinitionId);
        Assert.Equal("incident-1", blocking.IncidentId);
        Assert.Equal(
            $"incident-1:{Now.AddMinutes(-5).UtcTicks}:{IncidentStatus.Blocking}:{IncidentSeverity.Critical}",
            blocking.Generation);
        // LastObservedAt is the later of observation time and incident creation, in both directions.
        Assert.Equal(Now, blocking.LastObservedAt);
        Assert.Equal(Now.AddMinutes(5), open.LastObservedAt);
    }

    [Theory]
    [InlineData(true, true, true, "completed")]
    [InlineData(false, true, true, "updated")]
    [InlineData(false, false, true, "started")]
    [InlineData(false, false, false, "created")]
    public void MapFault_FallsThroughTheOccurredAtChain(bool hasCompleted, bool hasUpdated, bool hasStarted, string expected)
    {
        var timestamps = new Dictionary<string, DateTimeOffset>
        {
            ["created"] = Now.AddMinutes(-40),
            ["started"] = Now.AddMinutes(-30),
            ["updated"] = Now.AddMinutes(-20),
            ["completed"] = Now.AddMinutes(-10)
        };
        var execution = Execution(
            "run-1",
            createdAt: timestamps["created"],
            startedAt: hasStarted ? timestamps["started"] : null,
            updatedAt: hasUpdated ? timestamps["updated"] : null,
            completedAt: hasCompleted ? timestamps["completed"] : null);

        var record = WorkflowRuntimeAttentionRecords.MapFault(execution, Now);

        Assert.Equal(WorkflowRuntimeAttentionKind.FaultedExecution, record.Kind);
        Assert.Null(record.IncidentId);
        Assert.Equal(timestamps[expected], record.OccurredAt);
        Assert.Equal($"run-1:{timestamps[expected].UtcTicks}:{WorkflowExecutionStatus.Faulted}", record.Generation);
        Assert.Equal(Now, record.LastObservedAt);
    }

    [Fact]
    public void UrgencyComparer_OrdersKindThenRecencyThenExecutionThenIncident()
    {
        var openNewer = Record("run-a", "incident-1", WorkflowRuntimeAttentionKind.OpenIncident, Now);
        var blockingOlder = Record("run-b", "incident-2", WorkflowRuntimeAttentionKind.BlockingIncident, Now.AddMinutes(-30));
        var faultNewer = Record("run-c", null, WorkflowRuntimeAttentionKind.FaultedExecution, Now);
        var faultOlder = Record("run-d", null, WorkflowRuntimeAttentionKind.FaultedExecution, Now.AddMinutes(-5));
        var faultOlderTwin = Record("run-e", null, WorkflowRuntimeAttentionKind.FaultedExecution, Now.AddMinutes(-5));

        var ordered = new[] { openNewer, faultOlderTwin, faultNewer, blockingOlder, faultOlder }
            .Order(WorkflowRuntimeAttentionRecords.UrgencyComparer)
            .ToArray();

        // Blocking incidents outrank faults regardless of recency; within a kind newest first;
        // equal timestamps fall back to the ordinal execution id.
        Assert.Equal(new[] { blockingOlder, faultNewer, faultOlder, faultOlderTwin, openNewer }, ordered);
    }

    [Fact]
    public void UrgencyComparer_BreaksSameExecutionTiesOnIncidentId()
    {
        var first = Record("run-a", "incident-1", WorkflowRuntimeAttentionKind.OpenIncident, Now);
        var second = Record("run-a", "incident-2", WorkflowRuntimeAttentionKind.OpenIncident, Now);
        var noIncident = Record("run-a", null, WorkflowRuntimeAttentionKind.OpenIncident, Now);

        var ordered = new[] { second, first, noIncident }
            .Order(WorkflowRuntimeAttentionRecords.UrgencyComparer)
            .ToArray();

        // A null incident id compares as empty and sorts before any value.
        Assert.Equal(new[] { noIncident, first, second }, ordered);
    }

    private static WorkflowRuntimeAttentionRecord Record(
        string executionId,
        string? incidentId,
        WorkflowRuntimeAttentionKind kind,
        DateTimeOffset observedAt) => new(
        executionId,
        "definition",
        incidentId,
        kind,
        "fingerprint",
        observedAt,
        observedAt,
        1,
        null);

    private static IncidentState Incident(string incidentId, string executionId, IncidentStatus status, DateTimeOffset createdAt) => new(
        incidentId,
        executionId,
        null,
        null,
        IncidentSeverity.Critical,
        status,
        null,
        "TestFailure",
        "Failure detail",
        createdAt,
        null);

    private static WorkflowExecutionState Execution(
        string id,
        DateTimeOffset createdAt,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? updatedAt = null,
        DateTimeOffset? completedAt = null) => new(
        id,
        new("artifact", "definition", "version", "1.0.0", "hash"),
        WorkflowExecutionStatus.Faulted,
        null,
        createdAt,
        startedAt,
        updatedAt,
        completedAt,
        null,
        null,
        "tenant-1",
        new Dictionary<string, string>());
}
