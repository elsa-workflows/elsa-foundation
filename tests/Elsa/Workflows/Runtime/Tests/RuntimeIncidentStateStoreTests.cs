using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeIncidentStateStoreTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InMemoryIncidentStateStore_SavesFindsAndListsByWorkflowExecution()
    {
        var store = new InMemoryIncidentStateStore();
        var blocking = NewIncident("incident-1", "wfexec-1", IncidentStatus.Blocking);
        var resolved = NewIncident("incident-2", "wfexec-1", IncidentStatus.Resolved);
        var otherWorkflow = NewIncident("incident-1", "wfexec-2", IncidentStatus.Blocking);

        Assert.True(await store.TryAddAsync(blocking));
        Assert.False(await store.TryAddAsync(NewIncident(blocking, IncidentStatus.Resolved)));
        await store.SaveAsync(resolved);
        Assert.True(await store.TryAddAsync(otherWorkflow));

        Assert.Same(blocking, await store.FindAsync("wfexec-1", "incident-1"));
        Assert.Equal(2, (await store.ListAsync("wfexec-1")).Count);
        Assert.Equal(2, await store.CountAsync("wfexec-1"));

        var blockingIncidents = await store.ListBlockingAsync("wfexec-1");

        var incident = Assert.Single(blockingIncidents);
        Assert.Equal("incident-1", incident.IncidentId);
        Assert.Single(await store.ListBlockingAsync("wfexec-2"));
    }

    private IncidentState NewIncident(IncidentState state, IncidentStatus withStatus) =>
        NewIncident(state.IncidentId, state.WorkflowExecutionId, withStatus);

    private IncidentState NewIncident(string incidentId, string workflowExecutionId, IncidentStatus status) =>
        new(
            incidentId: incidentId,
            workflowExecutionId: workflowExecutionId,
            activityExecutionId: "actexec-1",
            executableNodeId: "node-1",
            severity: IncidentSeverity.Error,
            status: status,
            resolutionOutcome: status is IncidentStatus.Resolved or IncidentStatus.Suppressed
                ? new IncidentResolutionOutcome(
                    "Acme.TestResolution",
                    _now.AddMinutes(1),
                    strategy: null,
                    systemSource: "TestResolution")
                : null,
            failureType: "ActivityFaulted",
            message: "Activity failed.",
            createdAt: _now,
            resolvedAt: status is IncidentStatus.Resolved or IncidentStatus.Suppressed ? _now.AddMinutes(1) : null,
            metadata: new Dictionary<string, string>());
}
