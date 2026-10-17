using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed partial class WorkflowInvokeActivitySchedulerWorkHandlerTests
{
    [Fact]
    public async Task HandleAsync_PublishedBoundaryRemainsInParentScopeAndSchedulesItsChildrenInItsOwnScope()
    {
        var executable = NewStructuralExecutable();
        await _executableStore.SaveAsync(executable);
        var parentState = NewStructuralRunningState(executable) with
        {
            ParentActivityExecutionId = "outer-scope",
            SchedulingActivityExecutionId = "outer-scope",
            ExecutionScopeId = "actexec-1",
            EnclosingExecutionScopeId = "outer-scope",
            Provenance = ActivitySchedulingProvenance.From(
                "wfexec-1",
                parentActivityExecutionId: "outer-scope",
                schedulingActivityExecutionId: "outer-scope",
                branchId: null,
                iterationId: null,
                executionPathId: null,
                executionScopeId: "actexec-1",
                schedulingCause: RuntimeScheduleActivityCommandPayload.ActivityCompletionReason),
            Metadata = new Dictionary<string, string>
            {
                ["activity.definitionVersionId"] = "activity-version"
            }
        };
        await _activityStateStore.SaveAsync(parentState);
        await using var provider = NewProvider(
            new FixedActivityActivator(new ChildSchedulingDeferringStructuralActivity()),
            includeInspection: true);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var parentInspection = await _inspectionStore.FindAsync("wfexec-1", "actexec-1");
        Assert.Equal("actexec-1", parentInspection!.ExecutionScopeId);
        Assert.Equal("outer-scope", parentInspection.EnclosingExecutionScopeId);

        var commit = Assert.Single(
            _checkpointWriter.ListCommits(),
            write => write.Commit.Checkpoint.Metadata["runtime.checkpointReason"] == "ChildActivityScheduling").Commit;
        var scheduledChild = Assert.Single(commit.PostCommitIntents)
            .Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal("actexec-1", scheduledChild.ExecutionScopeId);

        var payload = scheduledChild.Payload!.Value.Deserialize<RuntimeScheduleActivityCommandPayload>()!;
        Assert.Equal("actexec-1", payload.SchedulingProvenance.ExecutionScopeId);
    }

    [Fact]
    public async Task HandleAsync_ExplicitBranchProvenanceInheritsTheBoundaryScopeWhenItsScopeIsUnspecified()
    {
        var executable = NewStructuralExecutable();
        await _executableStore.SaveAsync(executable);
        var parentState = NewStructuralRunningState(executable) with
        {
            ParentActivityExecutionId = "outer-scope",
            SchedulingActivityExecutionId = "outer-scope",
            ExecutionScopeId = "actexec-1",
            EnclosingExecutionScopeId = "outer-scope",
            Provenance = ActivitySchedulingProvenance.From(
                "wfexec-1",
                parentActivityExecutionId: "outer-scope",
                schedulingActivityExecutionId: "outer-scope",
                branchId: null,
                iterationId: null,
                executionPathId: null,
                executionScopeId: "actexec-1",
                schedulingCause: RuntimeScheduleActivityCommandPayload.ActivityCompletionReason),
            Metadata = new Dictionary<string, string>
            {
                ["activity.definitionVersionId"] = "activity-version"
            }
        };
        await _activityStateStore.SaveAsync(parentState);
        var branchProvenance = ActivitySchedulingProvenance.From(
            "wfexec-1",
            parentActivityExecutionId: "outer-scope",
            schedulingActivityExecutionId: "actexec-1",
            branchId: "branch-1",
            iterationId: null,
            executionPathId: null,
            executionScopeId: null,
            schedulingCause: "parallel.fork");
        await using var provider = NewProvider(
            new FixedActivityActivator(new ChildSchedulingDeferringStructuralActivity(branchProvenance)),
            includeInspection: true);

        await NewHandler(provider).HandleAsync(NewInvokeWorkItem(NewIdentity()));

        var commit = Assert.Single(
            _checkpointWriter.ListCommits(),
            write => write.Commit.Checkpoint.Metadata["runtime.checkpointReason"] == "ChildActivityScheduling").Commit;
        var scheduledChild = Assert.Single(commit.PostCommitIntents)
            .Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal("actexec-1", scheduledChild.ExecutionScopeId);
        var payload = scheduledChild.Payload!.Value.Deserialize<RuntimeScheduleActivityCommandPayload>()!;
        Assert.Equal("branch-1", payload.SchedulingProvenance.BranchId);
        Assert.Equal("actexec-1", payload.SchedulingProvenance.ExecutionScopeId);
    }
}
