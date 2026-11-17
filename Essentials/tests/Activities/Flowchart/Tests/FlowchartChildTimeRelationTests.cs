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

    // Each pass of a cycle that forks off the cycle starts another run of the branch, which can still be running when
    // the next pass starts.
    [Fact]
    public void A_fork_on_a_cycle_makes_its_off_cycle_branch_overlap_itself_and_the_next_pass()
    {
        var flow = Flowchart(["start", "a", "loop", "b"],
            [("start", "a", null), ("a", "loop", null), ("loop", "a", null), ("a", "b", null)]);

        Assert.Equal(ActivityChildTimeRelation.Unordered, _structures.RelateChildren(flow, "b", "b"));
        Assert.Equal(ActivityChildTimeRelation.Unordered, _structures.RelateChildren(flow, "a", "b"));
    }

    // D-2a as amended: the decision may select Continue and Exit together, so the exit path can run on every pass while
    // the next pass runs. The join initiative (#4193) decides whether this may be narrowed.
    [Fact]
    public void A_loop_with_an_exit_port_is_a_fork_on_the_cycle()
    {
        var flow = Flowchart(["start", "check", "body", "after"],
            [("start", "check", null), ("check", "body", "Continue"), ("body", "check", null), ("check", "after", "Exit")]);

        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _structures.RelateChildren(flow, "body", "body"));
        Assert.Equal(ActivityChildTimeRelation.Unordered, _structures.RelateChildren(flow, "after", "after"));
        Assert.Equal(ActivityChildTimeRelation.Unordered, _structures.RelateChildren(flow, "body", "after"));
    }

    // A first-wins race cancels the losing path only in the Flowchart's state; the join downstream does not wait for it.
    [Fact]
    public void A_first_wins_race_does_not_order_a_losing_branch_before_the_shared_join()
    {
        var flow = Flowchart(["race", "a", "b", "join"],
            [("race", "a", null), ("race", "b", null), ("a", "join", null), ("b", "join", null)],
            "race", new Dictionary<string, FlowchartNodeMetadata> { ["race"] = new("firstWins") });

        Assert.Equal(ActivityChildTimeRelation.Unordered, _structures.RelateChildren(flow, "b", "join"));
        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _structures.RelateChildren(flow, "join", "join"));
    }

    // A merge's first arrival runs it while the path to the second arrival is still in flight.
    [Fact]
    public void A_merge_fed_by_a_node_and_its_successor_overlaps_itself()
    {
        var flow = Flowchart(["a", "b", "m"], [("a", "b", null), ("a", "m", null), ("b", "m", null)], "a",
            new Dictionary<string, FlowchartNodeMetadata> { ["m"] = new("merge") });

        Assert.Equal(ActivityChildTimeRelation.Unordered, _structures.RelateChildren(flow, "m", "m"));
    }

    // Only the join where the raced branches meet is released early; joins after it see one continuation and wait.
    [Fact]
    public void A_fork_and_join_after_a_first_wins_race_stays_ordered()
    {
        var flow = Flowchart(["race", "a", "b", "j", "p", "x", "y", "k", "z"],
            [("race", "a", null), ("race", "b", null), ("a", "j", null), ("b", "j", null), ("j", "p", null),
             ("p", "x", null), ("p", "y", null), ("x", "k", null), ("y", "k", null), ("k", "z", null)],
            "race", new Dictionary<string, FlowchartNodeMetadata> { ["race"] = new("firstWins") });

        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _structures.RelateChildren(flow, "x", "z"));
        Assert.Equal(ActivityChildTimeRelation.Unordered, _structures.RelateChildren(flow, "b", "j"));
    }

    // A writer on an earlier loop is not on the later forking cycle, so it stays ordered before that cycle's branch;
    // its own exit (Done) is a fork on its own cycle and runs on every pass of that loop.
    [Fact]
    public void A_writer_on_an_unrelated_earlier_loop_stays_ordered_before_a_later_fork_cycle_branch()
    {
        var flow = Flowchart(["p", "a", "loop", "b"],
            [("p", "p", "Retry"), ("p", "a", "Done"), ("a", "loop", null), ("loop", "a", null), ("a", "b", null)], "p");

        Assert.Equal(ActivityChildTimeRelation.Unordered, _structures.RelateChildren(flow, "a", "b"));
        Assert.Equal(ActivityChildTimeRelation.Unordered, _structures.RelateChildren(flow, "p", "a"));
    }

    // The exit port of a loop is a fork on the cycle like any other (D-2a as amended).
    [Fact]
    public void A_fan_out_on_a_loops_repeat_port_and_its_exit_run_unordered()
    {
        var flow = Flowchart(["start", "check", "body1", "body2", "after"],
            [("start", "check", null), ("check", "body1", "Continue"), ("check", "body2", "Continue"),
             ("body1", "check", null), ("body2", "check", null), ("check", "after", "Exit")]);

        Assert.Equal(ActivityChildTimeRelation.Unordered, _structures.RelateChildren(flow, "after", "after"));
        Assert.Equal(ActivityChildTimeRelation.Unordered, _structures.RelateChildren(flow, "body1", "body2"));
    }

    // Since spec 1208 (FR-022) a completion schedules each target once, so two connections from one node into a merge
    // are one arrival, not two overlapping runs.
    [Fact]
    public void Two_connections_from_one_node_into_a_merge_are_one_arrival()
    {
        var flow = Flowchart(["decision", "m"], [("decision", "m", "Yes"), ("decision", "m", "No")], "decision",
            new Dictionary<string, FlowchartNodeMetadata> { ["m"] = new("merge") });

        Assert.Equal(ActivityChildTimeRelation.FirstBeforeSecond, _structures.RelateChildren(flow, "m", "m"));
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
