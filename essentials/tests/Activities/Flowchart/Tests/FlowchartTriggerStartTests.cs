using System.Text.Json;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Tests;

/// <summary>
/// Spec 238: a Flowchart starts at the run's trigger node when that node is one of its direct children, and at its
/// start node otherwise.
/// </summary>
public sealed class FlowchartTriggerStartTests
{
    private static readonly string[] ActivityExecutionIds = Enumerable.Range(1, 16).Select(index => $"actexec-{index}").ToArray();

    [Fact]
    public async Task TriggerChild_IsFirstNode_StartNodeDoesNotRun()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(ActivityExecutionIds);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a"), fixture.NewProbeNode("node-a2"), fixture.NewProbeNode("node-b"), fixture.NewProbeNode("node-b2")],
            connections: [fixture.NewConnection("node-a", "node-a2"), fixture.NewConnection("node-b", "node-b2")],
            startNodeId: "node-a");

        var run = await fixture.ExecuteAsync(executable, triggerNodeId: "node-b");

        run.AssertWorkflowCompleted();
        run.AssertRan("node-b", "node-b2");
        run.AssertDidNotRun("node-a", "node-a2");
        var state = await fixture.GetFlowchartStateAsync();
        var rootScope = Assert.Single(state.Scopes, scope => scope.ExecutionScopeId == "scope:root");
        Assert.Equal("node-b", rootScope.OwnerNodeId);
    }

    [Fact]
    public async Task TriggerBranches_JoinSharedNode_SharedNodeRunsOnce()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(ActivityExecutionIds);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a"), fixture.NewProbeNode("node-b"), fixture.NewProbeNode("node-shared")],
            connections: [fixture.NewConnection("node-a", "node-shared"), fixture.NewConnection("node-b", "node-shared")],
            startNodeId: "node-a");

        var run = await fixture.ExecuteAsync(executable, triggerNodeId: "node-b");

        run.AssertWorkflowCompleted();
        run.AssertRan("node-b");
        Assert.Single(run.States("node-shared"));
        run.AssertDidNotRun("node-a");
    }

    [Fact]
    public async Task TriggerChild_WithInboundConnection_StartsThereAsRootPath()
    {
        // Publishing refuses this placement (the start-path rule); this pins the runtime for artifacts published before it.
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(ActivityExecutionIds);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a"), fixture.NewProbeNode("node-b"), fixture.NewProbeNode("node-c")],
            connections: [fixture.NewConnection("node-a", "node-b"), fixture.NewConnection("node-b", "node-c")],
            startNodeId: "node-a");

        var run = await fixture.ExecuteAsync(executable, triggerNodeId: "node-b");

        run.AssertWorkflowCompleted();
        run.AssertRan("node-b", "node-c");
        run.AssertDidNotRun("node-a");
    }

    [Fact]
    public async Task TriggerIsStartNode_BehavesAsToday()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(ActivityExecutionIds);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a"), fixture.NewProbeNode("node-a2"), fixture.NewProbeNode("node-b")],
            connections: [fixture.NewConnection("node-a", "node-a2")],
            startNodeId: "node-a");

        var run = await fixture.ExecuteAsync(executable, triggerNodeId: "node-a");

        run.AssertWorkflowCompleted();
        run.AssertRan("node-a", "node-a2");
        run.AssertDidNotRun("node-b");
    }

    [Fact]
    public async Task DirectRun_NoTrigger_StartsAtStartNode()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(ActivityExecutionIds);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a"), fixture.NewProbeNode("node-b")],
            connections: [],
            startNodeId: "node-a");

        await fixture.ExecuteAsync(executable);

        var state = await fixture.GetFlowchartStateAsync();
        var rootScope = Assert.Single(state.Scopes, scope => scope.ExecutionScopeId == "scope:root");
        Assert.Equal("node-a", rootScope.OwnerNodeId);
        Assert.DoesNotContain(state.ActiveChildren, child => child.NodeId == "node-b");
    }

    [Fact]
    public async Task TriggerIdNotADirectChild_StartsAtStartNode()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(ActivityExecutionIds);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a"), fixture.NewProbeNode("node-b")],
            connections: [],
            startNodeId: "node-a");

        var run = await fixture.ExecuteAsync(executable, triggerNodeId: "node-elsewhere");

        run.AssertWorkflowCompleted();
        run.AssertRan("node-a");
        run.AssertDidNotRun("node-b");
    }

    [Fact]
    public async Task NestedFlowchart_TriggerIsInnerChild_OuterStartsAtStartNode_InnerStartsAtTrigger()
    {
        // Publishing refuses this placement (the start-path rule); this pins the runtime for artifacts published before it.
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(ActivityExecutionIds);
        var inner = NewFlowchartNode(
            "node-inner",
            [fixture.NewProbeNode("node-i1"), fixture.NewProbeNode("node-i2"), fixture.NewProbeNode("node-i3")],
            [fixture.NewConnection("node-i2", "node-i3")],
            startNodeId: "node-i1");
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a"), inner, fixture.NewProbeNode("node-b")],
            connections: [fixture.NewConnection("node-a", "node-inner")],
            startNodeId: "node-a");

        var run = await fixture.ExecuteAsync(executable, triggerNodeId: "node-i2");

        run.AssertWorkflowCompleted();
        run.AssertRan("node-a", "node-inner", "node-i2", "node-i3");
        run.AssertDidNotRun("node-i1", "node-b");
    }

    /// <summary>
    /// A loop that only a trigger branch reaches is classified from where the run enters it. Ordinal roots alone would
    /// start the walk at <c>node-a</c> and mark the loop's entry edge <c>node-y → node-a</c> as the loop-closing one.
    /// </summary>
    [Fact]
    public async Task TriggerOnlyLoop_ClassifiesLoopClosingEdgeFromTrigger()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(["actexec-flowchart"]);
        var graph = FlowchartGraph.From(fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-s"), NewTriggerNode("node-t"), fixture.NewProbeNode("node-y"), fixture.NewProbeNode("node-a")],
            connections: [fixture.NewConnection("node-t", "node-y"), fixture.NewConnection("node-y", "node-a"), fixture.NewConnection("node-a", "node-y")],
            startNodeId: "node-s").RootActivity);
        var analyzer = new FlowchartReachabilityAnalyzer();

        Assert.True(analyzer.IsBackwardEdge(graph, "node-a", "node-y"));
        Assert.False(analyzer.IsBackwardEdge(graph, "node-y", "node-a"));
        Assert.False(analyzer.IsBackwardEdge(graph, "node-t", "node-y"));
    }

    [Fact]
    public async Task TriggerReachableFromStart_EdgeClassificationUnchanged()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(["actexec-flowchart"]);
        IReadOnlyCollection<FlowchartConnection> connections =
        [
            fixture.NewConnection("node-s", "node-t"),
            fixture.NewConnection("node-t", "node-y"),
            fixture.NewConnection("node-y", "node-a"),
            fixture.NewConnection("node-a", "node-y")
        ];
        var marked = FlowchartGraph.From(fixture.NewExecutable(
            [fixture.NewProbeNode("node-s"), NewTriggerNode("node-t"), fixture.NewProbeNode("node-y"), fixture.NewProbeNode("node-a")],
            connections,
            startNodeId: "node-s").RootActivity);
        var unmarked = FlowchartGraph.From(fixture.NewExecutable(
            [fixture.NewProbeNode("node-s"), fixture.NewProbeNode("node-t"), fixture.NewProbeNode("node-y"), fixture.NewProbeNode("node-a")],
            connections,
            startNodeId: "node-s").RootActivity);
        var analyzer = new FlowchartReachabilityAnalyzer();

        foreach (var connection in connections)
        {
            Assert.Equal(
                analyzer.IsBackwardEdge(unmarked, connection.Source.NodeId, connection.Target.NodeId),
                analyzer.IsBackwardEdge(marked, connection.Source.NodeId, connection.Target.NodeId));
        }
        Assert.True(analyzer.IsBackwardEdge(marked, "node-a", "node-y"));
    }

    private static ExecutableNode NewTriggerNode(string nodeId)
    {
        var probe = WorkflowExecutionHarness.NewProbeNode(nodeId);
        return new ExecutableNode(
            executableNodeId: probe.ExecutableNodeId,
            authoredActivityId: probe.AuthoredActivityId,
            activityType: probe.ActivityType,
            activityTypeVersion: probe.ActivityTypeVersion,
            descriptorType: probe.DescriptorType,
            descriptorPayload: probe.DescriptorPayload,
            inputBindings: probe.InputBindings,
            metadata: new Dictionary<string, string> { [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType });
    }

    private static ExecutableNode NewFlowchartNode(
        string nodeId,
        IReadOnlyCollection<ExecutableNode> children,
        IReadOnlyCollection<FlowchartConnection> connections,
        string startNodeId) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(FlowchartActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartStructure(connections, startNodeId))));
}
