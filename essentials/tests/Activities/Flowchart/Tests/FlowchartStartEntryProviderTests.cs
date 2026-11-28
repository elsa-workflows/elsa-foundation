using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Tests;

/// <summary>
/// Spec 238, the start-path rule for a Flowchart: a run started by a trigger enters at that trigger when it is an
/// entry (a direct child with no inbound connection, or the start node), and through the start node otherwise.
/// </summary>
public sealed class FlowchartStartEntryProviderTests
{
    private readonly FlowchartStartEntryProvider _provider = new();

    [Fact]
    public void DirectChildWithoutInboundConnection_IsItsOwnEntry()
    {
        var flowchart = FlowchartExecutableNodes.Create(
            "entry",
            ["node-a", "node-a2", "node-b"],
            [Connection("node-a", "node-a2")],
            startNodeId: "node-a");

        Assert.Equal("node-b", _provider.SelectStartChild(flowchart, "node-b")?.ExecutableNodeId);
    }

    [Fact]
    public void StartNodeWithInboundLoopBack_IsItsOwnEntry()
    {
        var flowchart = FlowchartExecutableNodes.Create(
            "entry",
            ["node-a", "node-b"],
            [Connection("node-a", "node-b"), Connection("node-b", "node-a")],
            startNodeId: "node-a");

        Assert.Equal("node-a", _provider.SelectStartChild(flowchart, "node-a")?.ExecutableNodeId);
    }

    [Fact]
    public void DirectChildWithInboundConnection_ThatIsNotTheStartNode_HasNoEntry()
    {
        var flowchart = FlowchartExecutableNodes.Create(
            "entry",
            ["node-a", "node-b"],
            [Connection("node-a", "node-b")],
            startNodeId: "node-a");

        Assert.Null(_provider.SelectStartChild(flowchart, "node-b"));
    }

    [Fact]
    public void TriggerDeeperThanADirectChild_EntersThroughTheStartNode()
    {
        var flowchart = FlowchartExecutableNodes.Create(
            "entry",
            ["node-a", "node-b"],
            [Connection("node-a", "node-b")],
            startNodeId: "node-a");

        Assert.Equal("node-a", _provider.SelectStartChild(flowchart, "node-nested")?.ExecutableNodeId);
    }

    [Fact]
    public void Feature_RegistersTheProviderForTheFlowchartStructureKind()
    {
        var services = new ServiceCollection();
        new ActivitiesFlowchartFeature().ConfigureServices(services);

        var provider = Assert.Single(services.BuildServiceProvider().GetServices<IStructuralStartEntryProvider>());
        Assert.Equal(FlowchartActivity.StructureKind, provider.StructureKind);
    }

    [Fact]
    public void NoAuthoredStartNode_UsesTheSameFallbackAsTheRuntime()
    {
        var flowchart = FlowchartExecutableNodes.Create(
            "entry",
            ["node-a", "node-b"],
            [Connection("node-b", "node-a")]);

        // Without a startNodeId the start node is the first child with no inbound connection: node-b.
        Assert.Equal("node-b", _provider.SelectStartChild(flowchart, "node-nested")?.ExecutableNodeId);
        Assert.Equal("node-b", _provider.SelectStartChild(flowchart, "node-b")?.ExecutableNodeId);
        Assert.Null(_provider.SelectStartChild(flowchart, "node-a"));
    }

    private static FlowchartConnection Connection(string source, string target) => new(new(source), new(target));
}
