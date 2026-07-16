using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class DispatchWorkflowCheckpointTests
{
    [Fact]
    public async Task Activity_completion_atomically_commits_dispatch_responsibility_before_global_delivery()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();

        var run = await fixture.StartParentAsync(
            caseId: "checkpoint-atomic",
            parentWorkflowExecutionId: "parent-checkpoint-atomic",
            parentCorrelationId: "correlation-parent");

        var commit = run.CompletionCommit;
        Assert.Equal(RuntimeCheckpointNames.ActivityCompleted, commit.Checkpoint.Name);
        var activityChange = Assert.Single(commit.StateChanges.ActivityExecutions);
        Assert.Equal(ActivityExecutionStatus.Completed, activityChange.State.Status);
        var inspectionChange = Assert.Single(commit.StateChanges.ActivityExecutionInspections);
        Assert.Equal([DispatchWorkflowOutcomes.Dispatched], inspectionChange.State.OutcomeNames);

        var childIdOutput = Assert.Single(
            commit.StateChanges.DurableValues,
            change => change.State.ValueId == "dispatch-child-id-checkpoint-atomic");
        Assert.Equal(run.Identity.ChildWorkflowExecutionId, childIdOutput.State.InlineValue?.GetString());

        var dispatchChange = Assert.Single(commit.StateChanges.WorkflowDispatches);
        Assert.Equal(run.Identity.DispatchId, dispatchChange.StateId);
        Assert.Equal(WorkflowDispatchStatus.Pending, dispatchChange.State.Status);
        Assert.Equal(run.Identity.ChildWorkflowExecutionId, dispatchChange.State.ChildWorkflowExecutionId);

        Assert.Equal(2, commit.PostCommitIntents.Count);
        var continuation = Assert.Single(
            commit.PostCommitIntents,
            intent => intent.Kind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork);
        var continuationWork = continuation.Payload?.Deserialize<RuntimeSchedulerWorkItem>();
        Assert.NotNull(continuationWork);
        Assert.Equal(WorkflowExecutionCommandKind.CompleteActivity, continuationWork.CommandKind);
        Assert.Single(
            commit.PostCommitIntents,
            intent => intent.Kind == DispatchWorkflowConstants.StartChildIntentKind);

        Assert.Equal(2, commit.StateChanges.PostCommitOutbox.Count);
        Assert.All(
            commit.StateChanges.PostCommitOutbox,
            change => Assert.Equal(RuntimePostCommitOutboxStatus.Pending, change.State.Status));
        Assert.Equal(
            new[] { RuntimePostCommitIntentKinds.EnqueueSchedulerWork, DispatchWorkflowConstants.StartChildIntentKind }.Order(StringComparer.Ordinal),
            commit.StateChanges.PostCommitOutbox.Select(change => change.State.Intent.Kind).Order(StringComparer.Ordinal));

        Assert.Equal(ActivityExecutionStatus.Completed, run.Activity.Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId))?.Status);
        Assert.Null(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));
        Assert.Empty(fixture.ChildProbe.Observations);
    }

    [Fact]
    public async Task Equivalent_checkpoint_replay_converges_on_one_dispatch_child_and_start_intent()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        const string parentWorkflowExecutionId = "parent-checkpoint-replay";
        var run = await fixture.StartParentAsync(
            caseId: "checkpoint-replay",
            parentWorkflowExecutionId: parentWorkflowExecutionId,
            parentCorrelationId: "correlation-parent");

        await fixture.ReplayAsync(run.CompletionCommit);

        var persisted = Assert.Single(await fixture.ListDispatchesAsync(parentWorkflowExecutionId));
        Assert.Equal(run.Identity.DispatchId, persisted.DispatchId);
        Assert.Equal(run.Identity.ChildWorkflowExecutionId, persisted.ChildWorkflowExecutionId);
        Assert.Single(
            run.CompletionCommit.PostCommitIntents,
            intent => intent.Kind == DispatchWorkflowConstants.StartChildIntentKind);
        Assert.Null(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));
        Assert.Empty(fixture.ChildProbe.Observations);
    }

    [Fact]
    public async Task Distinct_activity_executions_receive_distinct_dispatch_and_child_identities()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();

        var first = await fixture.StartParentAsync(
            caseId: "checkpoint-first",
            parentWorkflowExecutionId: "parent-checkpoint-first",
            parentCorrelationId: "correlation-parent");
        var second = await fixture.StartParentAsync(
            caseId: "checkpoint-second",
            parentWorkflowExecutionId: "parent-checkpoint-second",
            parentCorrelationId: "correlation-parent");

        Assert.NotEqual(first.Activity.Execution.ActivityExecutionId, second.Activity.Execution.ActivityExecutionId);
        Assert.NotEqual(first.Identity.DispatchId, second.Identity.DispatchId);
        Assert.NotEqual(first.Identity.ChildWorkflowExecutionId, second.Identity.ChildWorkflowExecutionId);
        Assert.NotEqual(first.Identity.StartIntentId, second.Identity.StartIntentId);
        Assert.Equal(first.Identity.DispatchId, Assert.Single(
            await fixture.ListDispatchesAsync(first.Start.WorkflowExecutionId)).DispatchId);
        Assert.Equal(second.Identity.DispatchId, Assert.Single(
            await fixture.ListDispatchesAsync(second.Start.WorkflowExecutionId)).DispatchId);
    }
}
