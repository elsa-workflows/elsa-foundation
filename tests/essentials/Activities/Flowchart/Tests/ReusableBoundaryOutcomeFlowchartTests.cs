using System.Text.Json;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Graph.Runtime;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Tests;

public sealed class ReusableBoundaryOutcomeFlowchartTests
{
    [Fact]
    public async Task Mapped_reusable_boundary_outcome_runs_only_the_matching_parent_branch()
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .WithFeature(services => new GraphActivitiesRuntimeFeature().ConfigureServices(services))
            .WithProbeLeaf()
            .Build(["actexec-flowchart", "actexec-boundary", "actexec-entry", "actexec-declined"]);
        var reusableBoundary = NewReusableBoundary("Rejected");
        var executable = NewExecutable(
            children:
            [
                reusableBoundary,
                WorkflowExecutionHarness.NewProbeNode("node-accepted"),
                WorkflowExecutionHarness.NewProbeNode("node-declined")
            ],
            connections:
            [
                NewConnection(reusableBoundary.ExecutableNodeId, "node-accepted", "Accepted"),
                NewConnection(reusableBoundary.ExecutableNodeId, "node-declined", "Declined")
            ]);

        var run = await harness.RunAsync(executable);

        run.AssertRan("node-declined");
        run.AssertDidNotRun("node-accepted");
        run.AssertWorkflowCompleted();
    }

    private static ExecutableNode NewReusableBoundary(string entryOutcome)
    {
        var descriptor = new GraphActivityDescriptor(
            "definition-reusable",
            "version-reusable",
            "1.0.0",
            "sha256:reusable",
            "node-entry",
            null,
            [],
            [],
            [],
            [],
            [],
            [],
            [
                new("Approved", "Accepted"),
                new("Rejected", "Declined")
            ]);
        return new ExecutableNode(
            executableNodeId: "node-reusable",
            authoredActivityId: "authored-reusable",
            activityType: WellKnownRuntimeActivityConsumers.GraphActivity,
            activityTypeVersion: RuntimeActivityDescriptor.InitialSchemaVersion,
            descriptor: new RuntimeActivityDescriptor(
                WellKnownRuntimeActivityConsumers.GraphActivity,
                RuntimeActivityDescriptor.InitialSchemaVersion,
                JsonSerializer.SerializeToElement(descriptor)),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots:
            [
                new ExecutableChildSlot(
                    "activity-graph",
                    [WorkflowExecutionHarness.NewProbeNode("node-entry", [entryOutcome])])
            ],
            activityContract: NewBoundaryContract(descriptor));
    }

    private static ActivityContract NewBoundaryContract(GraphActivityDescriptor descriptor)
    {
        var resultPolicy = ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result };
        return new ActivityContract(
            "reusable-decision",
            "1.0.0",
            WellKnownRuntimeActivityConsumers.GraphActivity,
            JsonSerializer.SerializeToElement(descriptor),
            [],
            new ActivityResultContract(
                new ValueTypeDescriptor("Object"),
                true,
                resultPolicy,
                [],
                ValueRepresentation.StructuredValue),
            ["Accepted", "Declined"],
            new ActivityActivationRequirement(
                WellKnownRuntimeActivityConsumers.GraphActivity,
                WellKnownRuntimeActivityConsumers.GraphActivity));
    }

    private static WorkflowExecutable NewExecutable(
        IReadOnlyCollection<ExecutableNode> children,
        IReadOnlyCollection<FlowchartConnection> connections)
    {
        var root = new ExecutableNode(
            executableNodeId: "node-flowchart",
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

    private static FlowchartConnection NewConnection(string sourceNodeId, string targetNodeId, string sourcePort) =>
        new(new FlowchartEndpoint(sourceNodeId, sourcePort), new FlowchartEndpoint(targetNodeId));

    private sealed record FlowchartDescriptor;
}
