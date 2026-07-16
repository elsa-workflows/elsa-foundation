using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Sequence.Tests;

public sealed class SequenceRuntimeTests
{
    [Fact]
    public async Task SequenceRoot_SchedulesFirstChildAndPersistsParentExecutionId()
    {
        await using var harness = NewHarness("actexec-sequence", "actexec-a", "actexec-b", "actexec-c");

        var run = await harness.RunAsync(NewExecutable([NewProbeNode("node-a"), NewProbeNode("node-b"), NewProbeNode("node-c")]));

        var sequenceState = Assert.Single(run.States("node-sequence"));
        var firstChildState = Assert.Single(run.States("node-a"));
        Assert.Equal(ActivityExecutionStatus.Completed, sequenceState.Status);
        Assert.Equal("actexec-sequence", firstChildState.ParentActivityExecutionId);
        Assert.Equal("actexec-sequence", firstChildState.SchedulingActivityExecutionId);
    }

    [Fact]
    public async Task CompletedChild_SchedulesNextChildInStructureOrder()
    {
        await using var harness = NewHarness("actexec-sequence", "actexec-a", "actexec-b", "actexec-c");

        var run = await harness.RunAsync(NewExecutable([NewProbeNode("node-a"), NewProbeNode("node-b"), NewProbeNode("node-c")]));

        var secondChildState = Assert.Single(run.States("node-b"));
        var thirdChildState = Assert.Single(run.States("node-c"));
        Assert.Equal(ActivityExecutionStatus.Completed, secondChildState.Status);
        Assert.Equal(ActivityExecutionStatus.Completed, thirdChildState.Status);
        Assert.Equal("actexec-sequence", secondChildState.ParentActivityExecutionId);
        Assert.Equal("actexec-sequence", thirdChildState.ParentActivityExecutionId);
    }

    [Fact]
    public async Task SchedulingActivityExecutionId_ChainsFromPreviousActivityExecution()
    {
        await using var harness = NewHarness("actexec-sequence", "actexec-a", "actexec-b", "actexec-c");

        var run = await harness.RunAsync(NewExecutable([NewProbeNode("node-a"), NewProbeNode("node-b"), NewProbeNode("node-c")]));

        Assert.Equal("actexec-sequence", Assert.Single(run.States("node-a")).SchedulingActivityExecutionId);
        Assert.Equal("actexec-a", Assert.Single(run.States("node-b")).SchedulingActivityExecutionId);
        Assert.Equal("actexec-b", Assert.Single(run.States("node-c")).SchedulingActivityExecutionId);
    }

    [Fact]
    public async Task FinalChildCompletion_CompletesSequenceAndWorkflow()
    {
        await using var harness = NewHarness("actexec-sequence", "actexec-a", "actexec-b", "actexec-c");

        var run = await harness.RunAsync(NewExecutable([NewProbeNode("node-a"), NewProbeNode("node-b"), NewProbeNode("node-c")]));

        Assert.Equal(ActivityExecutionStatus.Completed, Assert.Single(run.States("node-sequence")).Status);
        run.AssertWorkflowCompleted();
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .WithProbeLeaf()
            .Build(activityExecutionIds);

    private static WorkflowExecutable NewExecutable(IReadOnlyCollection<ExecutableNode> children)
    {
        var root = new ExecutableNode(
            executableNodeId: "node-sequence",
            authoredActivityId: "authored-sequence",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: typeof(SequenceDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new SequenceDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = children.Select(child => child.ExecutableNodeId).ToArray() })));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private static ExecutableNode NewProbeNode(string nodeId) => WorkflowExecutionHarness.NewProbeNode(nodeId);

    private sealed record SequenceDescriptor;
}
