using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;
using static Elsa3.Mapping.Tests.Support.Elsa3FlowchartFixtures;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 239 User Stories 1, 2, 3 and 4: Elsa 3 flowcharts convert to Elsa 4 Flowcharts with their connections and an
/// explicit start node; FlowFork, FlowJoin and Start leave no node; several triggers stay in one flowchart. Converted
/// against the real activity catalog.
/// </summary>
public sealed class Elsa3FlowchartConversionTests
{
    private const string FlowchartKey = "Elsa.Activities.Flowchart.Activities.Flowchart";

    [Theory]
    [MemberData(nameof(ConvertingFixtures))]
    public async Task Every_corpus_shape_converts_without_errors(string fixture)
    {
        var catalog = RealActivityCatalog.Create();

        var result = await ConversionServices.ConvertAsync(catalog, Load(fixture));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.GraphConversionPending);
        Assert.Equal(catalog.VersionIdOf(FlowchartKey), result.Value!.State.RootActivity!.ActivityVersionId);
    }

    public static TheoryData<string> ConvertingFixtures() => new(Converting);

    [Fact]
    public async Task A_decision_tree_keeps_its_nodes_ports_and_start()
    {
        var root = await RootAsync("decision-tree");

        Assert.Equal(["endpoint", "decide", "yes", "no", "done"], Children(root));
        Assert.Equal(
            ["endpoint:Done->decide", "decide:True->yes", "decide:False->no", "yes:Done->done", "no:Done->done"],
            Connections(root));
        Assert.Equal("endpoint", StartNodeId(root));
    }

    [Fact]
    public async Task If_and_switch_done_becomes_one_connection_per_elsa4_outcome()
    {
        var root = await RootAsync("if-switch-done");

        Assert.Equal(
            ["first:Done->check", "check:True->route", "check:False->route", "route:a->last", "route:Default->last"],
            Connections(root));
    }

    [Fact]
    public async Task Nested_flowcharts_convert_inside_their_parent()
    {
        var state = await StateAsync("nested-single-child");

        var thenChart = Node(state, "then-chart");
        Assert.Equal(["inside-then"], Children(thenChart));
        Assert.Empty(Connections(thenChart));
        Assert.Equal("inside-then", StartNodeId(thenChart));
        Assert.Equal("inside-body", StartNodeId(Node(state, "body-chart")));
    }

    [Theory]
    [InlineData("fork-wait-all")]
    [InlineData("fork-wait-all-active")]
    public async Task A_fork_and_join_become_fan_out_and_an_implicit_join(string fixture)
    {
        var root = await RootAsync(fixture);

        Assert.Equal(["endpoint", "b1", "b2", "b3", "after"], Children(root));
        Assert.Equal(
            ["endpoint:Done->b1", "endpoint:Done->b2", "endpoint:Done->b3", "b1:Done->after", "b2:Done->after", "b3:Done->after"],
            Connections(root));
    }

    [Fact]
    public async Task An_exclusive_wait_any_join_converts()
    {
        var root = await RootAsync("exclusive-wait-any");

        Assert.Equal(["endpoint:Done->decide", "decide:True->yes", "decide:False->no", "yes:Done->after", "no:Done->after"], Connections(root));
    }

    [Fact]
    public async Task An_implicit_join_keeps_its_inbound_connections()
    {
        var root = await RootAsync("implicit-join");

        Assert.Equal(["endpoint:Done->decide", "decide:True->w2", "decide:False->w1", "w1:Done->w2"], Connections(root));
    }

    [Fact]
    public async Task A_start_activity_becomes_the_start_node()
    {
        var root = await RootAsync("start-activity");

        Assert.Equal(["orphan", "first", "second"], Children(root));
        Assert.Equal(["first:Done->second"], Connections(root));
        Assert.Equal("first", StartNodeId(root));
    }

    [Fact]
    public async Task Several_triggers_stay_in_one_flowchart()
    {
        var root = await RootAsync("two-http-triggers");

        Assert.Equal(["orders", "handle-orders", "invoices", "handle-invoices"], Children(root));
        Assert.Equal(["orders:Done->handle-orders", "invoices:Done->handle-invoices"], Connections(root));
        Assert.Equal("orders", StartNodeId(root));
    }

    [Fact]
    public async Task Two_triggers_into_a_wait_any_join_are_exclusive()
    {
        var root = await RootAsync("two-triggers-into-wait-any");

        Assert.Equal(["a:Done->after", "b:Done->after"], Connections(root));
    }

    [Fact]
    public async Task A_connection_leaving_a_terminal_is_removed_with_a_warning()
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), Load("terminal-outbound"));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(["first:Done->end"], Connections(result.Value!.State.RootActivity!));
        var warning = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.DeadConnectionRemoved);
        Assert.Equal("$.root.connections[1]", warning.Path);
        Assert.Equal("end", warning.Metadata["ActivityNodeId"]);
    }

    [Fact]
    public async Task A_reusable_flowchart_routes_to_its_completes()
    {
        var catalog = RealActivityCatalog.Create();

        var result = await ConversionServices.ConvertAsync(catalog, Load("reusable-two-completes"), isReusable: true);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var root = result.Value!.State.RootActivity!;
        Assert.Equal(["first:Done->decide", "decide:True->ok", "decide:False->ko"], Connections(root));
        Assert.Equal("succeed", Text(Input(Node(result.Value.State, "ok"), "outcome")));
        Assert.Equal(
            ["Done", "fail", "succeed"],
            result.Value.BoundaryOutcomes.Select(outcome => outcome.Name).Order(StringComparer.Ordinal));
        Assert.Empty(Errors(await GraphProviderProof.ValidateAsync(catalog, result.Value)));
    }

    [Fact]
    public async Task Converting_twice_gives_identical_output()
    {
        foreach (var fixture in Converting)
        {
            var first = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), Load(fixture));
            var second = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), Load(fixture));

            Assert.Equal(
                System.Text.Json.JsonSerializer.Serialize(first.Value!.State),
                System.Text.Json.JsonSerializer.Serialize(second.Value!.State));
        }
    }

    private static async Task<Elsa.Workflows.Design.Core.Models.WorkflowDefinitionState> StateAsync(string fixture)
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), Load(fixture));
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        return result.Value!.State;
    }

    private static async Task<Elsa.Workflows.Design.Core.Models.ActivityNode> RootAsync(string fixture) =>
        (await StateAsync(fixture)).RootActivity!;

    private static IEnumerable<Elsa.Activities.Design.Core.Models.ActivityDiagnostic> Errors(IEnumerable<Elsa.Activities.Design.Core.Models.ActivityDiagnostic> diagnostics) =>
        diagnostics.Where(diagnostic => diagnostic.Severity == Elsa.Activities.Design.Core.Models.ActivityDiagnosticSeverity.Error);
}
