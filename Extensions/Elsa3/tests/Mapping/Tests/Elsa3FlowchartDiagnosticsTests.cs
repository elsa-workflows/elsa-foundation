using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;
using static Elsa3.Mapping.Tests.Support.Elsa3FlowchartFixtures;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 239 User Stories 2 and 5: a flowchart shape Elsa 4 cannot express exactly fails with a stable code at the
/// offending node or connection (G-1); nothing throws, and nothing is dropped silently.
/// </summary>
public sealed class Elsa3FlowchartDiagnosticsTests
{
    public static TheoryData<string, string, string, string> Causes() => new()
    {
        // case, code, JSON path, node id
        { "fork-into-wait-any", Elsa3ActivityDiagnosticCodes.GraphJoinUnsupported, "$.root.activities[5]", "join" },
        { "conditional-wait-all", Elsa3ActivityDiagnosticCodes.GraphJoinUnsupported, "$.root.activities[5]", "join" },
        { "join-mode-expression", Elsa3ActivityDiagnosticCodes.GraphJoinUnsupported, "$.root.activities[3]", "join" },
        { "unknown-target", Elsa3ActivityDiagnosticCodes.GraphConnectionInvalid, "$.root.connections[0]", "root" },
        { "unsupported-port", Elsa3ActivityDiagnosticCodes.NoCounterpart, "$.root.connections[0]", "endpoint" },
        { "unmapped-port", Elsa3ActivityDiagnosticCodes.GraphOutcomeUnmapped, "$.root.connections[0]", "a" },
        { "expression-status-codes", Elsa3ActivityDiagnosticCodes.GraphOutcomeUnmapped, "$.root.connections[0]", "call" },
        { "loop-into-entered-node", Elsa3ActivityDiagnosticCodes.GraphLoopUnsupported, "$.root.connections[2]", "b" },
        { "fork-branches-expression", Elsa3ActivityDiagnosticCodes.UnsupportedMode, "$.root.activities[0].branches", "fork" },
        { "start-into-two", Elsa3ActivityDiagnosticCodes.GraphStartAmbiguous, "$.root.activities[0]", "start" },
        { "connections-on-sequence", Elsa3ActivityDiagnosticCodes.GraphConnectionInvalid, "$.root.connections", "root" },
        { "start-trigger-with-inbound", Elsa3ActivityDiagnosticCodes.GraphStartTriggerMisplaced, "$.root.activities[2]", "second" },
    };

    [Theory]
    [MemberData(nameof(Causes))]
    public async Task Each_unsupported_shape_returns_its_code_at_its_location(string @case, string code, string path, string nodeId)
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), Definition(@case));

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Equal(code, error.Code);
        Assert.Equal(path, error.Path);
        Assert.Equal(nodeId, error.Metadata["ActivityNodeId"]);
        Assert.False(string.IsNullOrWhiteSpace(error.Guidance));
    }

    [Fact]
    public async Task A_loop_back_to_a_start_node_nothing_else_enters_converts()
    {
        var chart = Flowchart("root", Write("a"), Write("b"));
        chart.Connections = [Connect("a", "Done", "b"), Connect("b", "Done", "a")];

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(chart));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(["a:Done->b", "b:Done->a"], Connections(result.Value!.State.RootActivity!));
        Assert.Equal("a", StartNodeId(result.Value.State.RootActivity!));
    }

    [Fact]
    public async Task A_fork_port_outside_its_branches_is_removed_with_a_warning()
    {
        var fork = Activity("fork", "Elsa.FlowFork", ("branches", Expression("Object", "[\"left\"]", "String[]")));
        var chart = Flowchart("root", Write("first"), fork, Write("left"), Write("right"));
        chart.Connections = [Connect("first", "Done", "fork"), Connect("fork", "left", "left"), Connect("fork", "right", "right")];

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(chart));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(["first:Done->left"], Connections(result.Value!.State.RootActivity!));
        var warning = Assert.Single(result.Diagnostics);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.DeadConnectionRemoved, warning.Code);
        Assert.Equal("$.root.connections[2]", warning.Path);
    }

    [Fact]
    public async Task A_fork_straight_into_a_join_collapses_to_one_connection()
    {
        var fork = Activity("fork", "Elsa.FlowFork", ("branches", Expression("Object", "[\"b1\",\"b2\"]", "String[]")));
        var join = Activity("join", "Elsa.FlowJoin", ("mode", Literal("WaitAllActive")));
        var chart = Flowchart("root", Write("first"), fork, join, Write("after"));
        chart.Connections =
        [
            Connect("first", "Done", "fork"), Connect("fork", "b1", "join"), Connect("fork", "b2", "join"), Connect("join", "Done", "after")
        ];

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(chart));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(["first", "after"], Children(result.Value!.State.RootActivity!));
        Assert.Equal(["first:Done->after"], Connections(result.Value.State.RootActivity!));
    }

    [Fact]
    public async Task Several_causes_are_all_reported_in_path_order()
    {
        var chart = Flowchart("root", Write("a"), Write("b"));
        chart.Connections = [Connect("a", "Oops", "b"), Connect("a", "Done", "missing")];

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(chart));

        Assert.False(result.Succeeded);
        Assert.Equal(
            [
                ("$.root.connections[0]", Elsa3ActivityDiagnosticCodes.GraphOutcomeUnmapped),
                ("$.root.connections[1]", Elsa3ActivityDiagnosticCodes.GraphConnectionInvalid)
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Path!, diagnostic.Code)));
    }

    private static Elsa3WorkflowDefinition Definition(string @case)
    {
        switch (@case)
        {
            case "fork-into-wait-any":
                return Load("fork-wait-any");
            case "conditional-wait-all":
                return Load("conditional-wait-all");
        }

        Elsa3Activity chart;
        switch (@case)
        {
            case "join-mode-expression":
                chart = Flowchart("root", Write("a"), Write("b"), Write("c"),
                    Activity("join", "Elsa.FlowJoin", ("mode", Expression("JavaScript", "pickMode()"))), Write("after"));
                chart.Connections = [Connect("a", "Done", "join"), Connect("b", "Done", "join"), Connect("join", "Done", "after")];
                break;
            case "unknown-target":
                chart = Flowchart("root", Write("a"));
                chart.Connections = [Connect("a", "Done", "missing")];
                break;
            case "unsupported-port":
                chart = Flowchart("root", Activity("endpoint", "Elsa.HttpEndpoint", ("path", Literal("/upload"))), Write("tooLarge"));
                chart.Connections = [Connect("endpoint", "File too large", "tooLarge")];
                break;
            case "unmapped-port":
                chart = Flowchart("root", Write("a"), Write("b"));
                chart.Connections = [Connect("a", "Oops", "b")];
                break;
            case "expression-status-codes":
                chart = Flowchart("root",
                    Activity("call", "Elsa.FlowSendHttpRequest",
                        ("url", Literal("https://example.org")),
                        ("expectedStatusCodes", Expression("JavaScript", "codes()", "Int32[]"))),
                    Write("ok"));
                chart.Connections = [Connect("call", "200", "ok")];
                break;
            case "loop-into-entered-node":
                chart = Flowchart("root", Write("start"), Write("a"), Write("b"));
                chart.Connections = [Connect("start", "Done", "a"), Connect("a", "Done", "b"), Connect("b", "Done", "a")];
                break;
            case "fork-branches-expression":
                chart = Flowchart("root", Activity("fork", "Elsa.FlowFork", ("branches", Expression("JavaScript", "branches()", "String[]"))), Write("a"));
                chart.Connections = [Connect("fork", "x", "a")];
                break;
            case "start-into-two":
                chart = Flowchart("root", Activity("start", "Elsa.Start"), Write("a"), Write("b"));
                chart.Connections = [Connect("start", "Done", "a"), Connect("start", "Done", "b")];
                break;
            case "connections-on-sequence":
                chart = Sequence("root", Write("a"), Write("b"));
                chart.Connections = [Connect("a", "Done", "b")];
                break;
            case "start-trigger-with-inbound":
                // Spec 238 D8: a run started by 'second' cannot begin there, because 'a' leads into it.
                chart = Flowchart("root", StartTrigger("first"), Write("a"), StartTrigger("second"));
                chart.Connections = [Connect("first", "Done", "a"), Connect("a", "Done", "second")];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(@case), @case, null);
        }

        return ConversionServices.Definition(chart);
    }

    internal static Elsa3Activity Flowchart(string nodeId, params Elsa3Activity[] children)
    {
        var flowchart = Activity(nodeId, "Elsa.Flowchart");
        flowchart.Activities = children.ToList();
        return flowchart;
    }

    private static Elsa3Activity StartTrigger(string nodeId)
    {
        var endpoint = Activity(nodeId, "Elsa.HttpEndpoint", ("path", Literal($"/{nodeId}")));
        endpoint.CustomProperties = new Elsa3ActivityCustomProperties { CanStartWorkflow = true };
        return endpoint;
    }

    internal static Elsa3Activity Write(string nodeId) => Activity(nodeId, "Elsa.WriteLine", ("text", Literal(nodeId)));

    internal static Elsa3Connection Connect(string source, string port, string target) =>
        new() { Source = new Elsa3Endpoint { Activity = source, Port = port }, Target = new Elsa3Endpoint { Activity = target, Port = "In" } };
}
