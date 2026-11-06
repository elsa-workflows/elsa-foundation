using System.Text.Json;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Tests;

public sealed class FlowchartChildResultFactsTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureService _structures;

    public FlowchartChildResultFactsTests()
    {
        var services = new ServiceCollection();
        new ActivitiesFlowchartFeature().ConfigureServices(services);
        services.AddSingleton<IActivityStructureService, DefaultActivityStructureService>();
        _provider = services.BuildServiceProvider();
        _structures = _provider.GetRequiredService<IActivityStructureService>();
    }

    public void Dispose() => _provider.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Linear_causality_does_not_depend_on_the_stored_child_order(bool reverseChildren)
    {
        string[] children = reverseChildren ? ["consumer", "producer", "start"] : ["start", "producer", "consumer"];
        var flow = Flowchart(children, [("start", "producer"), ("producer", "consumer")]);

        var facts = _structures.AssessChildResult(flow, "producer", "consumer");

        Assert.Equal(ActivityChildResultAvailability.Definite, facts.Availability);
        Assert.False(facts.IsCyclicBackEdge);
        Assert.False(facts.MayRunConcurrently);
    }

    [Fact]
    public void Independent_legal_entries_preserve_local_dominance_and_exclusivity()
    {
        var flow = Flowchart(["start", "start-next", "trigger", "consumer"],
            [("start", "start-next"), ("trigger", "consumer")]);

        Assert.Equal(ActivityChildResultAvailability.Definite, _structures.AssessChildResult(flow, "trigger", "consumer").Availability);
        var otherEntry = _structures.AssessChildResult(flow, "start-next", "consumer");
        Assert.Equal(ActivityChildResultAvailability.Unavailable, otherEntry.Availability);
        Assert.True(otherEntry.AreMutuallyExclusive);
        Assert.False(otherEntry.MayRunConcurrently);
    }

    [Fact]
    public void A_join_reached_from_another_entry_makes_the_producer_conditional()
    {
        var flow = Flowchart(["start", "trigger", "consumer"], [("start", "consumer"), ("trigger", "consumer")]);

        var facts = _structures.AssessChildResult(flow, "trigger", "consumer");

        Assert.Equal(ActivityChildResultAvailability.Conditional, facts.Availability);
        Assert.False(facts.AreMutuallyExclusive);
        Assert.False(facts.MayRunConcurrently);
    }

    [Fact]
    public void A_node_with_an_incoming_connection_is_not_an_additional_entry()
    {
        var flow = Flowchart(["start", "producer", "mid-flow-trigger", "consumer"],
            [("start", "producer"), ("producer", "mid-flow-trigger"), ("mid-flow-trigger", "consumer")]);

        Assert.Equal(ActivityChildResultAvailability.Definite, _structures.AssessChildResult(flow, "producer", "consumer").Availability);
    }

    [Fact]
    public void A_fork_from_the_same_entry_retains_the_existing_concurrent_write_relation()
    {
        var flow = Flowchart(["start", "left", "right"], [("start", "left"), ("start", "right")]);

        var facts = _structures.AssessChildResult(flow, "left", "right");

        Assert.Equal(ActivityChildResultAvailability.Unavailable, facts.Availability);
        Assert.True(facts.MayRunConcurrently);
        Assert.False(facts.AreMutuallyExclusive);
    }

    [Fact]
    public void Cycle_direction_and_self_reads_do_not_admit_a_previous_iteration_result()
    {
        var flow = Flowchart(["first", "second"], [("first", "second"), ("second", "first")], "first");

        var backward = _structures.AssessChildResult(flow, "second", "first");
        Assert.True(backward.IsCyclicBackEdge);
        Assert.Equal(ActivityChildResultAvailability.Conditional, backward.Availability);
        var forward = _structures.AssessChildResult(flow, "first", "second");
        Assert.False(forward.IsCyclicBackEdge);
        Assert.Equal(ActivityChildResultAvailability.Definite, forward.Availability);
        var self = _structures.AssessChildResult(flow, "first", "first");
        Assert.True(self.IsCyclicBackEdge);
        Assert.Equal(ActivityChildResultAvailability.Unavailable, self.Availability);
    }

    [Fact]
    public void An_unreachable_cycle_does_not_become_an_exclusive_legal_entry()
    {
        var flow = Flowchart(["start", "left", "right"], [("left", "right"), ("right", "left")]);

        var facts = _structures.AssessChildResult(flow, "left", "right");

        Assert.Equal(ActivityChildResultAvailability.Unavailable, facts.Availability);
        Assert.False(facts.AreMutuallyExclusive);
        Assert.False(facts.MayRunConcurrently);
        Assert.True(facts.IsCyclicBackEdge);
    }

    [Fact]
    public void Undeclared_start_facts_are_unknown_and_can_be_refined_by_the_next_draft()
    {
        var unknown = _structures.AssessChildResult(Flowchart(["producer", "consumer"], [("producer", "consumer")], null), "producer", "consumer");
        var declared = _structures.AssessChildResult(Flowchart(["producer", "consumer"], [("producer", "consumer")], "producer"), "producer", "consumer");

        Assert.Equal(ActivityChildResultAvailability.Unknown, unknown.Availability);
        Assert.Null(unknown.MayRunConcurrently);
        Assert.Null(unknown.IsCyclicBackEdge);
        Assert.Equal(ActivityChildResultAvailability.Definite, declared.Availability);
    }

    [Fact]
    public void Missing_direct_child_identity_is_not_inferred_from_a_dangling_edge()
    {
        var flow = Flowchart(["start", "consumer"], [("start", "missing"), ("missing", "consumer")]);

        Assert.Equal(ActivityChildResultAvailability.Unavailable, _structures.AssessChildResult(flow, "missing", "consumer").Availability);
    }

    private static ActivityNode Flowchart(string[] children, (string Source, string Target)[] edges, string? start = "start") =>
        new("flow", "flow@1", [], [], new ActivityNodeStructure(
            FlowchartActivity.StructureKind,
            FlowchartActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new FlowchartAuthoredStructure(
                children.Select(id => new ActivityNode(id, "test@1", [], [])).ToArray(),
                edges.Select((edge, index) => new FlowchartConnection(new(edge.Source), new(edge.Target), $"edge-{index}")).ToArray(),
                start))));
}
