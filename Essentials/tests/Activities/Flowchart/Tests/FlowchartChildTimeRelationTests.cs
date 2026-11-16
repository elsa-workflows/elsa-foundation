using System.Text.Json;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Tests;

/// <summary>Spec 266 (D-2a, FR-003, FR-008): how a Flowchart's children relate in time.</summary>
public sealed class FlowchartChildTimeRelationTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureService _structures;

    public FlowchartChildTimeRelationTests()
    {
        var services = new ServiceCollection();
        new ActivitiesFlowchartFeature().ConfigureServices(services);
        services.AddSingleton<IActivityStructureService, DefaultActivityStructureService>();
        _provider = services.BuildServiceProvider();
        _structures = _provider.GetRequiredService<IActivityStructureService>();
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void Nodes_on_one_path_are_ordered_and_a_fork_through_one_port_is_unordered()
    {
        var flow = Flowchart(["start", "a", "b", "c"], [("start", "a", null), ("a", "b", null), ("start", "c", null)]);

        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _structures.RelateChildren(flow, "a", "b"));
        Assert.Equal(ActivityChildTimeRelation.SecondBeforeFirst, _structures.RelateChildren(flow, "b", "a"));
        Assert.Equal(ActivityChildTimeRelation.Unordered, _structures.RelateChildren(flow, "b", "c"));
    }

    // ADR 0045 section 6, amendment of 2026-11-16: any activity may select several outcomes, so different ports fork too.
    [Fact]
    public void Paths_leaving_different_ports_are_unordered()
    {
        var flow = Flowchart(["decision", "yes", "no"], [("decision", "yes", "Yes"), ("decision", "no", "No")], "decision");

        Assert.Equal(ActivityChildTimeRelation.Unordered, _structures.RelateChildren(flow, "yes", "no"));
    }

    [Fact]
    public void Branches_from_different_trigger_entries_are_exclusive()
    {
        var flow = Flowchart(["start", "a", "trigger", "b"], [("start", "a", null), ("trigger", "b", null)]);

        Assert.Equal(ActivityChildTimeRelation.MutuallyExclusive, _structures.RelateChildren(flow, "a", "b"));
    }

    [Theory]
    [InlineData(null, ActivityChildTimeRelation.FirstBeforeSecond, ActivityChildTimeRelation.FirstBeforeSecond)]
    [InlineData("merge", ActivityChildTimeRelation.Unordered, ActivityChildTimeRelation.Unordered)]
    public void A_waiting_join_orders_its_branches_before_the_continuation_and_a_merge_join_does_not(
        string? joinPolicy, ActivityChildTimeRelation branchToContinuation, ActivityChildTimeRelation continuationWithItself)
    {
        var flow = Flowchart(["fork", "a", "b", "join", "after"],
            [("fork", "a", null), ("fork", "b", null), ("a", "join", null), ("b", "join", null), ("join", "after", null)],
            "fork", joinPolicy is null ? null : new Dictionary<string, FlowchartNodeMetadata> { ["join"] = new(joinPolicy) });

        Assert.Equal(branchToContinuation, _structures.RelateChildren(flow, "b", "after"));
        Assert.Equal(continuationWithItself, _structures.RelateChildren(flow, "after", "after"));
        Assert.Equal(ActivityChildTimeRelation.Unordered, _structures.RelateChildren(flow, "a", "b"));
    }

    [Fact]
    public void A_merge_join_reached_by_exclusive_arrivals_keeps_its_continuation_ordered()
    {
        var flow = Flowchart(["start", "a", "trigger", "b", "join", "after"],
            [("start", "a", null), ("trigger", "b", null), ("a", "join", null), ("b", "join", null), ("join", "after", null)],
            "start", new Dictionary<string, FlowchartNodeMetadata> { ["join"] = new("merge") });

        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _structures.RelateChildren(flow, "b", "after"));
        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _structures.RelateChildren(flow, "after", "after"));
    }

    [Fact]
    public void A_loop_back_into_a_merge_node_stays_sequential()
    {
        var flow = Flowchart(["start", "head", "body"],
            [("start", "head", null), ("head", "body", null), ("body", "head", null)],
            "start", new Dictionary<string, FlowchartNodeMetadata> { ["head"] = new("merge") });

        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _structures.RelateChildren(flow, "body", "body"));
        Assert.NotEqual(ActivityChildTimeRelation.Unordered, _structures.RelateChildren(flow, "head", "body"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("merge", true)]
    [InlineData("firstWins", false)]
    public void A_first_wins_race_lets_the_flowchart_complete_while_a_losing_path_runs(string? policy, bool completesAfterChildren)
    {
        var flow = Flowchart(["start", "a"], [("start", "a", null)], "start",
            policy is null ? null : new Dictionary<string, FlowchartNodeMetadata> { ["start"] = new(policy) });

        Assert.Equal(completesAfterChildren, _structures.CompletesAfterItsChildren(flow));
    }

    private static ActivityNode Flowchart(
        string[] children,
        (string Source, string Target, string? Port)[] edges,
        string? start = "start",
        IReadOnlyDictionary<string, FlowchartNodeMetadata>? metadata = null) =>
        new("flow", "flow@1", [], [], new ActivityNodeStructure(
            FlowchartActivity.StructureKind,
            FlowchartActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new FlowchartAuthoredStructure(
                children.Select(id => new ActivityNode(id, "test@1", [], [])).ToArray(),
                edges.Select((edge, index) => new FlowchartConnection(new(edge.Source, edge.Port), new(edge.Target), $"edge-{index}")).ToArray(),
                start,
                metadata))));
}
