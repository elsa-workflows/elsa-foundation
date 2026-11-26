using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

/// <summary>
/// A child whose completion something else waits for is recorded even when the host drops the completion record of runs
/// that were never committed: dropping it would leave the waiting parent suspended forever.
/// </summary>
public sealed class EphemeralDispatchTests
{
    [Fact]
    public async Task A_waited_child_that_was_never_committed_is_still_recorded_and_its_parent_resumes()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(
            persistenceModes: options =>
            {
                options.Mode = PersistenceMode.Ephemeral;
                options.RecordCompletion = false;
            },
            replaySafeChild: true);
        var run = await fixture.StartParentAsync(
            caseId: "ephemeral-wait",
            parentWorkflowExecutionId: "parent-ephemeral-wait",
            parentCorrelationId: "correlation-parent",
            waitForCompletion: true);
        Assert.Equal(ActivityExecutionStatus.Suspended, run.Activity.Status);

        // The child runs one ReplaySafe activity, so nothing commits it before its completion. That completion carries
        // the parent's resume, which is why it is committed.
        Assert.Equal(1, (await fixture.SweepAsync()).OutboxDeliveredCount);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId))?.Status);
        Assert.Single(fixture.ChildProbe.Observations);

        Assert.Equal(1, (await fixture.SweepAsync()).OutboxDeliveredCount);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId))?.Status);
        Assert.Equal(WorkflowDispatchStatus.Completed, Assert.Single(await fixture.ListDispatchesAsync(run.Start.WorkflowExecutionId)).Status);
    }
}
