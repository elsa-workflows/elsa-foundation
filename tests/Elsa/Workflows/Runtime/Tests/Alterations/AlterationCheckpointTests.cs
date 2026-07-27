using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
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

        var copied = original.WithPostCommitOutbox([])
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

    private static WorkflowAlterationOutcome Outcome(int ordinal) =>
        new(ordinal, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Skipped, "PlanCancelled", null, DateTimeOffset.UnixEpoch);
}
