using Elsa.Activities.Flowchart.Internal;
using Xunit;

namespace Elsa.Activities.Flowchart.Tests;

/// <summary>
/// Spec 238, the start-path rule for a Flowchart: a run started by a trigger enters at that trigger when it is an
/// entry (a direct child with no inbound connection, or the start node), and through the start node otherwise.
/// </summary>
public sealed class FlowchartStartEntryProviderTests
{
    private readonly FlowchartStartEntryProvider _provider = new();

    [Fact]
    public async Task DirectChildWithoutInboundConnection_IsItsOwnEntry()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync([]);
        var flowchart = fixture.NewExecutable(
            [fixture.NewProbeNode("node-a"), fixture.NewProbeNode("node-a2"), fixture.NewProbeNode("node-b")],
            [fixture.NewConnection("node-a", "node-a2")],
            startNodeId: "node-a").RootActivity;

        Assert.Equal("node-b", _provider.SelectStartChild(flowchart, "node-b")?.ExecutableNodeId);
    }

    [Fact]
    public async Task StartNodeWithInboundLoopBack_IsItsOwnEntry()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync([]);
        var flowchart = fixture.NewExecutable(
            [fixture.NewProbeNode("node-a"), fixture.NewProbeNode("node-b")],
            [fixture.NewConnection("node-a", "node-b"), fixture.NewConnection("node-b", "node-a")],
            startNodeId: "node-a").RootActivity;

        Assert.Equal("node-a", _provider.SelectStartChild(flowchart, "node-a")?.ExecutableNodeId);
    }

    [Fact]
    public async Task DirectChildWithInboundConnection_ThatIsNotTheStartNode_HasNoEntry()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync([]);
        var flowchart = fixture.NewExecutable(
            [fixture.NewProbeNode("node-a"), fixture.NewProbeNode("node-b")],
            [fixture.NewConnection("node-a", "node-b")],
            startNodeId: "node-a").RootActivity;

        Assert.Null(_provider.SelectStartChild(flowchart, "node-b"));
    }

    [Fact]
    public async Task TriggerDeeperThanADirectChild_EntersThroughTheStartNode()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync([]);
        var flowchart = fixture.NewExecutable(
            [fixture.NewProbeNode("node-a"), fixture.NewProbeNode("node-b")],
            [fixture.NewConnection("node-a", "node-b")],
            startNodeId: "node-a").RootActivity;

        Assert.Equal("node-a", _provider.SelectStartChild(flowchart, "node-nested")?.ExecutableNodeId);
    }

    [Fact]
    public async Task NoAuthoredStartNode_UsesTheSameFallbackAsTheRuntime()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync([]);
        var flowchart = fixture.NewExecutable(
            [fixture.NewProbeNode("node-a"), fixture.NewProbeNode("node-b")],
            [fixture.NewConnection("node-b", "node-a")]).RootActivity;

        // Without a startNodeId the start node is the first child with no inbound connection: node-b.
        Assert.Equal("node-b", _provider.SelectStartChild(flowchart, "node-nested")?.ExecutableNodeId);
        Assert.Equal("node-b", _provider.SelectStartChild(flowchart, "node-b")?.ExecutableNodeId);
        Assert.Null(_provider.SelectStartChild(flowchart, "node-a"));
    }
}
