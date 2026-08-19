using System.Text.Json;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Tests;

/// <summary>
/// Execution coverage for the fault-aware Flowchart fork/join (#308). A diamond fork/join whose inbound branch
/// faults must resolve deterministically — the Flowchart faults (surfacing a composite incident) rather than
/// leaving the join waiting forever for a completion that will never arrive — mirroring the <c>Parallel</c>
/// composite's default-threshold behavior. Built on the shared <see cref="WorkflowExecutionHarness"/> using the
/// <c>FaultingActivity</c> leaf added in PR #327.
/// </summary>
public sealed class FlowchartFaultJoinTests
{
    private const string FlowchartNodeId = "node-flowchart";

    [Fact]
    public async Task ParallelJoin_WithFaultingInboundBranch_FaultsFlowchart_InsteadOfHanging()
    {
        // Diamond: node-a forks to node-b and node-c, both reconverging at the join node-d. node-b faults, so the
        // all-inbound join at node-d can never fire. The Flowchart must fault deterministically instead of staying
        // Running forever waiting on node-b's completion.
        await using var harness = NewHarness("actexec-flowchart", "actexec-a", "actexec-b", "actexec-c");
        var executable = NewDiamondWithFaultingBranch();

        var run = await harness.RunAsync(executable);

        // The faulting inbound branch faulted and recorded its own blocking incident.
        var faultedBranch = run.State("node-b");
        Assert.Equal(ActivityExecutionStatus.Faulted, faultedBranch.Status);
        Assert.NotEmpty(faultedBranch.IncidentIds);

        // The sibling branch still ran; the join never fired.
        run.AssertRan("node-a", "node-c");
        run.AssertDidNotRun("node-d");

        // The Flowchart faulted (with a composite incident) and never completed — no longer stuck Running.
        var flowchart = run.State(FlowchartNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, flowchart.Status);
        Assert.NotEmpty(flowchart.IncidentIds);
        var persistedFlowchartState = JsonSerializer.Deserialize<FlowchartExecutionState>(
            flowchart.PrivateState!.Value.InlineValue!.Value.GetRawText(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains(persistedFlowchartState!.Diagnostics, diagnostic => diagnostic.Kind == FlowchartDiagnosticKind.Faulted);
        Assert.DoesNotContain(run.States(FlowchartNodeId), state => state.Status == ActivityExecutionStatus.Completed);

        // The run resolved deterministically: the join did not silently complete the workflow as a success.
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartRuntimeFeature().ConfigureServices(services))
            .WithProbeLeaf()
            .WithFaultingLeaf()
            .Build(activityExecutionIds);

    private static WorkflowExecutable NewDiamondWithFaultingBranch()
    {
        var children = new[]
        {
            WorkflowExecutionHarness.NewProbeNode("node-a"),
            WorkflowExecutionHarness.NewFaultingNode("node-b"),
            WorkflowExecutionHarness.NewProbeNode("node-c"),
            WorkflowExecutionHarness.NewProbeNode("node-d")
        };

        var connections = new[]
        {
            NewConnection("node-a", "node-b"),
            NewConnection("node-a", "node-c"),
            NewConnection("node-b", "node-d"),
            NewConnection("node-c", "node-d")
        };

        var root = new ExecutableNode(
            executableNodeId: FlowchartNodeId,
            authoredActivityId: "authored-flowchart",
            activityType: typeof(FlowchartActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: typeof(FlowchartDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new FlowchartDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartStructure(connections))));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private static FlowchartConnection NewConnection(string sourceNodeId, string targetNodeId, string? sourcePort = null) =>
        new(new FlowchartEndpoint(sourceNodeId, sourcePort), new FlowchartEndpoint(targetNodeId));

    private sealed record FlowchartDescriptor;
}
