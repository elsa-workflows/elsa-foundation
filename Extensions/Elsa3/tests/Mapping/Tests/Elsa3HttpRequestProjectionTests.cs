using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Resolution;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 236 plan D-11 to D-13 (HttpEndpoint request projections) and D-17 (output capture): an Elsa 3 output captured
/// into a workflow variable is written in Elsa 4, and captured headers or query strings are rebuilt in their Elsa 3
/// shape by a synthesized set node right after the endpoint.
/// </summary>
public sealed class Elsa3HttpRequestProjectionTests
{
    [Fact]
    public async Task Headers_captured_into_a_workflow_variable_are_rebuilt_into_that_variable()
    {
        var root = Sequence("root",
            Endpoint(("headers", Captured("hdrs"))),
            Activity("direct", "Elsa.WriteLine", ("text", Captured("hdrs"))),
            Activity("script", "Elsa.WriteLine", ("text", Expression("JavaScript", "getVariable('hdrs')['x-api-key']"))));

        var result = await ConvertAsync(root, new Elsa3Variable { Id = "hdrs", Name = "hdrs", TypeName = "Object" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var state = result.Value!.State;
        var order = state.RootActivity!.Structure!.Payload.GetProperty("activities").EnumerateArray().Select(node => node.GetProperty("nodeId").GetString()).ToArray();
        Assert.Equal(["endpoint", "endpoint.elsa3-http-headers", "direct", "script"], order);

        var set = Node(state, "endpoint.elsa3-http-headers");
        Assert.Equal(Elsa3IntrinsicContracts.Set, set.ActivityVersionId);
        Assert.Equal("hdrs", set.Intrinsic!.Variable!.ReferenceKey);
        Assert.Equal(VariableReference.WorkflowScopeId, set.Intrinsic.Variable.DeclaringScopeId);
        var value = ReadExpression(Input(set, "value"));
        Assert.Contains("args.request.headers", value.Source, StringComparison.Ordinal);
        var request = Assert.IsType<ActivityResultExpressionParameterBinding>(value.Parameters["request"]);
        Assert.Equal("endpoint", request.ProducerNodeId);
        Assert.Equal("Request", request.ProjectionKey);

        var direct = Input(Node(state, "direct"), "text");
        Assert.Equal("Variable", direct.Value.ExpressionType);
        Assert.Equal("hdrs", ((JsonElement)direct.Value.Value!).GetProperty("referenceKey").GetString());
        var script = ReadExpression(Input(Node(state, "script"), "text"));
        Assert.Equal("hdrs", Assert.IsType<VariableExpressionParameterBinding>(Assert.Single(script.Parameters).Value).VariableKey);
    }

    [Fact]
    public async Task A_query_captured_into_a_bare_memory_slot_gets_a_synthesized_workflow_variable()
    {
        var root = Sequence("root",
            Endpoint(("queryStringData", Captured("legacy:query"))),
            Activity("direct", "Elsa.WriteLine", ("text", Captured("legacy:query"))));

        var result = await ConvertAsync(root);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var state = result.Value!.State;
        Assert.Contains(state.Variables, variable => variable.ReferenceKey == "elsa3.http.endpoint.query");
        Assert.Equal("elsa3.http.endpoint.query", Node(state, "endpoint.elsa3-http-query").Intrinsic!.Variable!.ReferenceKey);
        var direct = Input(Node(state, "direct"), "text");
        Assert.Equal("elsa3.http.endpoint.query", ((JsonElement)direct.Value.Value!).GetProperty("referenceKey").GetString());
    }

    [Fact]
    public async Task Both_projections_on_one_endpoint_get_one_node_each()
    {
        var root = Sequence("root", Endpoint(("headers", Captured("h")), ("queryStringData", Captured("q"))));

        var result = await ConvertAsync(root, new Elsa3Variable { Id = "h", Name = "h", TypeName = "Object" }, new Elsa3Variable { Id = "q", Name = "q", TypeName = "Object" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("h", Node(result.Value!.State, "endpoint.elsa3-http-headers").Intrinsic!.Variable!.ReferenceKey);
        Assert.Equal("q", Node(result.Value.State, "endpoint.elsa3-http-query").Intrinsic!.Variable!.ReferenceKey);
    }

    [Fact]
    public async Task An_endpoint_no_node_can_follow_cannot_project()
    {
        var result = await ConvertAsync(Endpoint(("headers", Captured("hdrs"))), new Elsa3Variable { Id = "hdrs", Name = "hdrs", TypeName = "Object" });

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.OutputNotRepresentable);
    }

    [Fact]
    public async Task A_projection_into_a_container_variable_is_not_representable()
    {
        var container = Sequence("scope", Endpoint(("headers", Captured("local"))));
        container.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "local", name = "local", typeName = "Object" } });

        var result = await ConvertAsync(Sequence("root", container));

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.OutputNotRepresentable);
    }

    [Fact]
    public async Task An_output_captured_into_a_workflow_variable_is_written_there()
    {
        var root = Sequence("root",
            Endpoint(("parsedContent", Captured("body"))),
            Activity("write", "Elsa.WriteLine", ("text", Captured("body"))));

        var result = await ConvertAsync(root, new Elsa3Variable { Id = "body", Name = "body", TypeName = "Object" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var output = Assert.Single(Node(result.Value!.State, "endpoint").Outputs, item => item.ReferenceKey == "ParsedContent");
        Assert.Equal("Variable", output.Value.ExpressionType);
        var target = (JsonElement)output.Value.Value!;
        Assert.Equal("body", target.GetProperty("referenceKey").GetString());
        Assert.Equal(VariableReference.WorkflowScopeId, target.GetProperty("declaringScopeId").GetString());
        Assert.Equal("body", ((JsonElement)Input(Node(result.Value.State, "write"), "text").Value.Value!).GetProperty("referenceKey").GetString());
    }

    [Fact]
    public async Task An_output_captured_into_a_container_variable_is_not_representable()
    {
        var container = Sequence("scope", Activity("read", "Elsa.ReadLine", ("result", Captured("local"))));
        container.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "local", name = "local", typeName = "String" } });

        var result = await ConvertAsync(Sequence("root", container));

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.OutputNotRepresentable);
    }

    [Fact]
    public async Task A_loop_value_captured_into_a_variable_is_the_loop_variable_inside_the_body()
    {
        var loop = Activity("loop", "Elsa.ForEach", ("items", Literal("[1,2]", "Object")), ("currentValue", Captured("item")));
        loop.AdditionalProperties["body"] = Port(Activity("inside", "Elsa.WriteLine", ("text", Captured("item"))));

        var result = await ConvertAsync(Sequence("root", loop), new Elsa3Variable { Id = "item", Name = "item", TypeName = "Object" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var text = Input(Node(result.Value!.State, "inside"), "text");
        var reference = (JsonElement)text.Value.Value!;
        Assert.Equal("loop", reference.GetProperty("declaringScopeId").GetString());
        Assert.Equal("currentItem", reference.GetProperty("referenceKey").GetString());
    }

    [Fact]
    public async Task A_loop_value_read_outside_its_loop_is_not_representable()
    {
        var loop = Activity("loop", "Elsa.ForEach", ("items", Literal("[1,2]", "Object")), ("currentValue", Captured("item")));
        var after = Activity("after", "Elsa.WriteLine", ("text", Captured("item")));

        var result = await ConvertAsync(Sequence("root", loop, after), new Elsa3Variable { Id = "item", Name = "item", TypeName = "Object" });

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.OutputNotRepresentable);
    }

    [Fact]
    public async Task In_a_flowchart_the_projections_are_connected_between_the_endpoint_and_its_successors()
    {
        // Spec 239 R-10, FR-013: the endpoint's Done leads through the projections; its former connections leave from the last.
        var root = Elsa3FlowchartDiagnosticsTests.Flowchart("root",
            Endpoint(("headers", Captured("hdrs")), ("queryStringData", Captured("qs"))),
            Activity("direct", "Elsa.WriteLine", ("text", Captured("hdrs"))));
        root.Connections = [Elsa3FlowchartDiagnosticsTests.Connect("endpoint", "Done", "direct")];

        var result = await ConvertAsync(root,
            new Elsa3Variable { Id = "hdrs", Name = "hdrs", TypeName = "Object" },
            new Elsa3Variable { Id = "qs", Name = "qs", TypeName = "Object" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var chart = result.Value!.State.RootActivity!;
        Assert.Equal(
            [
                "endpoint.elsa3-http-headers:Done->direct",
                "endpoint:Done->endpoint.elsa3-http-query",
                "endpoint.elsa3-http-query:Done->endpoint.elsa3-http-headers"
            ],
            Support.Elsa3FlowchartFixtures.Connections(chart));
        Assert.Equal("endpoint", Support.Elsa3FlowchartFixtures.StartNodeId(chart));
    }

    private static Elsa3Activity Endpoint(params (string Name, JsonElement Value)[] captures)
    {
        var endpoint = Activity("endpoint", "Elsa.HttpEndpoint", [("path", Literal("/orders")), .. captures]);
        endpoint.CustomProperties = new Elsa3ActivityCustomProperties { CanStartWorkflow = true };
        return endpoint;
    }

    private static ExpressionDefinition ReadExpression(ArgumentState argument) =>
        (argument.Value.Value is JsonElement element ? element : JsonSerializer.SerializeToElement(argument.Value.Value))
        .Deserialize<ExpressionDefinition>()!;

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertAsync(Elsa3Activity root, params Elsa3Variable[] variables) =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root, variables: variables));
}
