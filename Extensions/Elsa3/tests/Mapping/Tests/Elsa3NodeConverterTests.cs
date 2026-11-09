using System.Text.Json;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 236 FR-006 and the PR #2805 review: node conversion goes through the same lowering and refusal path as
/// whole-definition conversion. A Flowchart child converted through <see cref="IElsa3NodeConverter"/> gets exactly what
/// the same node gets inside a Sequence through <see cref="IElsa3DefinitionConverter"/>.
/// </summary>
public sealed class Elsa3NodeConverterTests
{
    private static readonly Elsa3Variable Variable = new() { Id = "o", Name = "o", TypeName = "Object" };

    /// <summary>The JavaScript rows: what lowers or is approved, and what is refused today (#2717 lowers some of them).</summary>
    public static TheoryData<string> JavaScriptCases() =>
    [
        // Lower or approve.
        "getVariable('o')",
        "variables.o",
        "getVariable('o') + '!'",
        "1 + 1;",
        "var a = 1; a + getVariable('o')",
        "(() => { const f = x => x; return f(1); })()",
        "'a'.length",
        // Refused today.
        "new Date()",
        "Date.now",
        "Math.random()",
        "getCorrelationId()",
        "newGuid()",
        "getOrderId()",
        "setVariable('o', 1)",
        "getVariable('o').name = 'x'",
        "args.value",
        "_.join(['a'], ',')",
        "getVariable(name)",
    ];

    [Theory]
    [MemberData(nameof(JavaScriptCases))]
    public async Task A_flowchart_child_converts_exactly_as_the_definition_converter_converts_it(string expression)
    {
        var catalog = RealActivityCatalog.Create();
        Elsa3Activity Write() => Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", expression)));

        var whole = await ConversionServices.ConvertAsync(catalog, ConversionServices.Definition(Sequence("root", Write()), variables: [Variable]));
        var (prepared, node) = await ConvertChildAsync(catalog, Flowchart("root", Write()), "node");

        if (!whole.Succeeded)
        {
            Assert.False(prepared.Succeeded);
            Assert.Equal(Codes(whole.Diagnostics), Codes(prepared.Diagnostics));
            Assert.Contains(prepared.Diagnostics, diagnostic => diagnostic.Code.StartsWith("VF-IMP-", StringComparison.Ordinal));
            return;
        }

        Assert.True(prepared.Succeeded, Describe(prepared.Diagnostics));
        Assert.True(node!.Succeeded, Describe(node.Diagnostics));
        Assert.Equal(
            JsonSerializer.Serialize(Input(Node(whole.Value!.State, "node"), "text")),
            JsonSerializer.Serialize(Input(node.Value!, "text")));
    }

    [Fact]
    public async Task Value_flow_between_flowchart_siblings_is_lowered_as_in_a_sequence()
    {
        var catalog = RealActivityCatalog.Create();
        Elsa3Activity[] Siblings() =>
        [
            Activity("read", "Elsa.ReadLine", ("result", Captured("line"))),
            Activity("write", "Elsa.WriteLine", ("text", Captured("line"))),
        ];

        var whole = await ConversionServices.ConvertAsync(catalog, ConversionServices.Definition(Sequence("root", Siblings())));
        var (prepared, node) = await ConvertChildAsync(catalog, Flowchart("root", Siblings()), "write");

        Assert.True(whole.Succeeded, Describe(whole.Diagnostics));
        Assert.True(prepared.Succeeded, Describe(prepared.Diagnostics));
        Assert.True(node!.Succeeded, Describe(node.Diagnostics));
        Assert.Equal(
            JsonSerializer.Serialize(Input(Node(whole.Value!.State, "write"), "text")),
            JsonSerializer.Serialize(Input(node.Value!, "text")));
    }

    [Fact]
    public async Task The_flowchart_itself_converts_as_a_node_and_nothing_is_deferred()
    {
        var catalog = RealActivityCatalog.Create();
        await using var provider = ConversionServices.Create(catalog);
        await using var scope = provider.CreateAsyncScope();
        var converter = scope.ServiceProvider.GetRequiredService<IElsa3NodeConverter>();
        var definition = ConversionServices.Definition(Flowchart("chart", Activity("node", "Elsa.WriteLine", ("text", Literal("x")))));

        var prepared = await converter.PrepareAsync(new Elsa3ConversionRequest(definition));
        var chart = await converter.ConvertNodeAsync(prepared.Value!, definition.Root!);

        // Graph conversion runs inside preparation (spec 239 R-1, R-9), so a Flowchart converts as one node.
        Assert.True(prepared.Succeeded, Describe(prepared.Diagnostics));
        Assert.True(chart.Succeeded, Describe(chart.Diagnostics));
        Assert.Equal("elsa.flowchart.structure", chart.Value!.Structure!.Kind);
        Assert.Equal("node", chart.Value.Structure.Payload.GetProperty("startNodeId").GetString());
    }

    [Fact]
    public async Task A_flowchart_construct_has_no_node_of_its_own()
    {
        var catalog = RealActivityCatalog.Create();
        await using var provider = ConversionServices.Create(catalog);
        await using var scope = provider.CreateAsyncScope();
        var converter = scope.ServiceProvider.GetRequiredService<IElsa3NodeConverter>();
        var start = Activity("start", "Elsa.Start");
        var write = Activity("write", "Elsa.WriteLine", ("text", Literal("x")));
        var chart = Flowchart("chart", start, write);
        chart.Connections = [Connect("start", "Done", "write")];
        var definition = ConversionServices.Definition(chart);

        var prepared = await converter.PrepareAsync(new Elsa3ConversionRequest(definition));
        var construct = await converter.ConvertNodeAsync(prepared.Value!, start);

        Assert.True(prepared.Succeeded, Describe(prepared.Diagnostics));
        Assert.False(construct.Succeeded);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.UnsupportedMode, Assert.Single(construct.Diagnostics).Code);
    }

    private static Elsa3Connection Connect(string source, string port, string target) =>
        new() { Source = new Elsa3Endpoint { Activity = source, Port = port }, Target = new Elsa3Endpoint { Activity = target, Port = "In" } };

    [Fact]
    public async Task Preparing_fails_when_a_node_other_than_a_graph_node_is_unconvertible()
    {
        var (prepared, _) = await ConvertChildAsync(
            RealActivityCatalog.Create(),
            Flowchart("root", Activity("node", "Acme.Unknown")),
            "node");

        Assert.False(prepared.Succeeded);
        Assert.Contains(prepared.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.UnmappedType);
    }

    [Fact]
    public async Task A_node_from_another_definition_is_rejected()
    {
        await using var provider = ConversionServices.Create(RealActivityCatalog.Create());
        await using var scope = provider.CreateAsyncScope();
        var converter = scope.ServiceProvider.GetRequiredService<IElsa3NodeConverter>();
        var prepared = await converter.PrepareAsync(new Elsa3ConversionRequest(
            ConversionServices.Definition(Flowchart("root", Activity("node", "Elsa.WriteLine", ("text", Literal("x")))))));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            converter.ConvertNodeAsync(prepared.Value!, Activity("elsewhere", "Elsa.WriteLine")).AsTask());
    }

    [Fact]
    public async Task A_context_another_node_converter_created_is_rejected()
    {
        await using var provider = ConversionServices.Create(RealActivityCatalog.Create());
        await using var scope = provider.CreateAsyncScope();
        var converter = scope.ServiceProvider.GetRequiredService<IElsa3NodeConverter>();
        var definition = ConversionServices.Definition(Flowchart("root", Activity("node", "Elsa.WriteLine", ("text", Literal("x")))));
        var foreign = new ForeignContext(new Elsa3ConversionRequest(definition));

        var refusal = await Assert.ThrowsAsync<ArgumentException>(() =>
            converter.ConvertNodeAsync(foreign, definition.Root!.Activities![0]).AsTask());
        Assert.Equal("context", refusal.ParamName);
    }

    private sealed class ForeignContext(Elsa3ConversionRequest request) : Elsa3NodeConversionContext(request);

    private static async Task<(Elsa3MigrationResult<Elsa3NodeConversionContext> Prepared, Elsa3MigrationResult<Elsa.Workflows.Design.Core.Models.ActivityNode>? Node)> ConvertChildAsync(
        RealActivityCatalog catalog,
        Elsa3Activity root,
        string nodeId)
    {
        await using var provider = ConversionServices.Create(catalog);
        await using var scope = provider.CreateAsyncScope();
        var converter = scope.ServiceProvider.GetRequiredService<IElsa3NodeConverter>();
        var definition = ConversionServices.Definition(root, variables: [Variable]);

        var prepared = await converter.PrepareAsync(new Elsa3ConversionRequest(definition));
        if (!prepared.Succeeded)
            return (prepared, null);

        var child = definition.Root!.Activities!.Single(activity => Elsa3ConversionDiagnosticsNodeId(activity) == nodeId);
        return (prepared, await converter.ConvertNodeAsync(prepared.Value!, child));
    }

    private static string Elsa3ConversionDiagnosticsNodeId(Elsa3Activity activity) =>
        !string.IsNullOrWhiteSpace(activity.NodeId) ? activity.NodeId : activity.Id ?? string.Empty;

    private static Elsa3Activity Flowchart(string nodeId, params Elsa3Activity[] children)
    {
        var flowchart = Activity(nodeId, "Elsa.Flowchart");
        flowchart.Activities = children.ToList();
        return flowchart;
    }

    private static IEnumerable<string> Codes(IEnumerable<Elsa3MigrationDiagnostic> diagnostics) =>
        diagnostics.Select(diagnostic => diagnostic.Code).Order(StringComparer.Ordinal);
}
