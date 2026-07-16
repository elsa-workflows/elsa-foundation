using System.Text.Json;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Tests;

/// <summary>
/// Sample consumer proving the shared <see cref="WorkflowExecutionHarness"/> generalizes beyond a single
/// composite: it drives a multi-node Flowchart graph (with a named-outcome branch) through the in-process
/// agent and asserts which nodes ran, branch selection, and scheduling/parent identity using the harness's
/// <see cref="WorkflowExecutionRun"/> helpers. This is the same coverage shape downstream activity issues
/// (#260–#268) reuse — see <c>FlowchartRuntimeTests</c> for the equivalent bespoke fixture it replaces.
/// </summary>
public sealed class FlowchartHarnessSampleTests
{
    [Fact]
    public async Task CompletedChild_SchedulesConnectedTarget_WithSchedulingIdentity()
    {
        await using var harness = NewHarness("actexec-flowchart", "actexec-a", "actexec-b");
        var executable = NewExecutable(
            children: [WorkflowExecutionHarness.NewProbeNode("node-a"), WorkflowExecutionHarness.NewProbeNode("node-b")],
            connections: [NewConnection("node-a", "node-b")]);

        var run = await harness.RunAsync(executable);

        var target = run.AssertCompleted("node-b");
        Assert.Equal("actexec-flowchart", target.ParentActivityExecutionId);
        Assert.Equal("actexec-a", target.SchedulingActivityExecutionId);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task NamedOutcome_FollowsOnlyMatchingBranch()
    {
        await using var harness = NewHarness("actexec-flowchart", "actexec-a", "actexec-c");
        var executable = NewExecutable(
            children:
            [
                WorkflowExecutionHarness.NewProbeNode("node-a", ["Rejected"]),
                WorkflowExecutionHarness.NewProbeNode("node-b"),
                WorkflowExecutionHarness.NewProbeNode("node-c")
            ],
            connections:
            [
                NewConnection("node-a", "node-b", "Approved"),
                NewConnection("node-a", "node-c", "Rejected")
            ]);

        var run = await harness.RunAsync(executable);

        run.AssertRan("node-c");
        run.AssertDidNotRun("node-b");
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .WithConstructor(new FlowchartActivityConstructor())
            .WithProbeLeaf()
            .Build(activityExecutionIds);

    private static WorkflowExecutable NewExecutable(
        IReadOnlyCollection<ExecutableNode> children,
        IReadOnlyCollection<FlowchartConnection> connections)
    {
        var root = new ExecutableNode(
            executableNodeId: "node-flowchart",
            authoredActivityId: "authored-flowchart",
            activityType: typeof(FlowchartActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(FlowchartActivityConstructor.ConsumerKeyValue, RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new FlowchartDescriptor())),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
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

    private sealed class FlowchartActivityConstructor : IActivityConstructor<FlowchartDescriptor>
    {
        public static string ConsumerKeyValue => typeof(FlowchartDescriptor).FullName!;
        public string ConsumerKey => ConsumerKeyValue;

        public ValueTask<IActivity> Construct(
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            new(new FlowchartActivity());

        public ValueTask<IActivity> Construct(
            FlowchartDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            new(new FlowchartActivity());
    }

    private sealed record FlowchartDescriptor;
}
