using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class AlterationCheckpointTests
{
    [Fact]
    public void StateChangeCopiers_PreserveTerminalAlterationEvidence_AndCanonicalizeOutcomeOrder()
    {
        var later = Outcome(1);
        var first = Outcome(0);
        var terminal = new WorkflowAlterationJobTerminalChange("job-1", "claim-1", WorkflowAlterationJobStatus.Failed, [later, first], "commit-1", DateTimeOffset.UnixEpoch);
        var original = new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [])
            .WithAlterationJobTerminalChange(terminal);

        var copied = original.WithActivityExecutions([]).WithPostCommitOutbox([])
            .WithWorkflowDispatches([])
            .WithWorkflowDispatchCancellations([])
            .WithConsumedSchedulerWorkItems([]);

        Assert.NotNull(copied.AlterationJobTerminalChange);
        Assert.Equal([0, 1], copied.AlterationJobTerminalChange!.Outcomes.Select(outcome => outcome.Ordinal));
    }

    [Fact]
    public async Task RuntimeAlterationJobCheckpoint_IsMandatoryCoalescingBoundary()
    {
        var policy = new CoalescingRuntimeCheckpointPersistencePolicy();
        var checkpoint = new RuntimeCheckpoint("checkpoint", RuntimeCheckpointNames.RuntimeAlterationJob, "workflow", DateTimeOffset.UnixEpoch, [], new Dictionary<string, string>());

        var decision = await policy.DecideAsync(checkpoint);

        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, decision.Mode);
    }

    [Fact]
    public void StateBuilder_StagesIncidentsOfTheAlteredWorkflowOnly_AndRejectsDuplicates()
    {
        var builder = new WorkflowAlterationRuntimeCheckpointStateBuilder(new WorkflowExecutionState(
            "workflow", new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Running,
            null, DateTimeOffset.UnixEpoch, null, null, null, null, null, "tenant", new Dictionary<string, string>()));

        builder.AddIncident(IncidentChange("incident-1", "workflow"));

        Assert.Equal("incident-1", Assert.Single(builder.Incidents).StateId);
        Assert.Throws<InvalidOperationException>(() => builder.AddIncident(IncidentChange("incident-1", "workflow")));
        Assert.Throws<ArgumentException>(() => builder.AddIncident(IncidentChange("incident-2", "other-workflow")));
    }

    private static RuntimeStateChange<IncidentState> IncidentChange(string incidentId, string workflowExecutionId) =>
        new(
            incidentId,
            RuntimeStateChangeOperation.Upsert,
            new IncidentState(incidentId, workflowExecutionId, "activity", "node", IncidentSeverity.Error, IncidentStatus.Blocking, null,
                "TestFailure", "The activity faulted.", DateTimeOffset.UnixEpoch, null),
            new Dictionary<string, string>());

    private static WorkflowAlterationOutcome Outcome(int ordinal) =>
        new(ordinal, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Skipped, "PlanCancelled", null, DateTimeOffset.UnixEpoch);
}
