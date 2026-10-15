using System.Text.Json;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Resolution;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 236 US1 and SC-002: built-in Elsa 3 activities convert to their Elsa 4 counterparts at the version installed in
/// a catalog built by CLR reconciliation of the real Elsa 4 activity assemblies, with every member under its Elsa 4 key.
/// </summary>
public sealed class Elsa3BuiltInConversionTests
{
    private const string WriteLineKey = "Elsa.Activities.Primitives.Activities.WriteLine";

    [Fact]
    public async Task WriteLine_references_the_catalog_version_and_keeps_its_text()
    {
        var catalog = RealActivityCatalog.Create();

        var node = Node(await ConvertAsync(catalog, Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("Hello"))))), "write");

        Assert.Equal(catalog.VersionIdOf(WriteLineKey), node.ActivityVersionId);
        Assert.Equal("Hello", Text(Input(node, "text")));
    }

    [Fact]
    public async Task Renamed_members_arrive_under_their_elsa4_keys()
    {
        var root = Sequence("root",
            Activity("delay", "Elsa.Delay", ("timeSpan", Literal("00:00:05", "TimeSpan"))),
            Activity("loop", "Elsa.ForEach", ("items", Literal("[1,2]", "Object"))),
            Activity("respond", "Elsa.WriteHttpResponse", ("content", Literal("body")), ("responseHeaders", Literal("{}", "Object"))),
            Activity("dispatch", "Elsa.DispatchWorkflow", ("workflowDefinitionId", Literal("child")), ("input", Literal("{}", "Object"))));

        var state = await ConvertAsync(RealActivityCatalog.Create(), root);

        Assert.Equal("00:00:05", Text(Input(Node(state, "delay"), "Duration")));
        Assert.Equal("[1,2]", Text(Input(Node(state, "loop"), "Collection")));
        Assert.Equal("body", Text(Input(Node(state, "respond"), "Body")));
        Assert.Equal("{}", Text(Input(Node(state, "respond"), "Headers")));
        Assert.Equal("{}", Text(Input(Node(state, "dispatch"), "Inputs")));
        // R2: Elsa 4 cancels the child by default; Elsa 3 did not.
        Assert.Equal("false", Text(Input(Node(state, "dispatch"), "CancelChildOnParentCancellation")));
    }

    [Fact]
    public async Task A_start_only_trigger_converts_its_renamed_expression()
    {
        var cron = Activity("cron", "Elsa.Cron", ("cronExpression", Literal("0 * * * *")));
        cron.CustomProperties = new Elsa3ActivityCustomProperties { CanStartWorkflow = true };

        var state = await ConvertAsync(RealActivityCatalog.Create(), Sequence("root", cron));

        Assert.Equal("0 * * * *", Text(Input(Node(state, "cron"), "Expression")));
    }

    [Fact]
    public async Task A_captured_script_result_is_read_from_the_elsa4_result_output()
    {
        var script = Activity("script", "Elsa.RunJavaScript", ("script", Literal("return 1;")), ("result", Captured("script-result")));
        var write = Activity("write", "Elsa.WriteLine", ("text", Captured("script-result")));

        var state = await ConvertAsync(RealActivityCatalog.Create(), Sequence("root", script, write));

        var text = Input(Node(state, "write"), "text");
        Assert.Equal("ActivityResult", text.Value.ExpressionType);
        var reference = Assert.IsType<JsonElement>(text.Value.Value);
        Assert.Equal("script", reference.GetProperty("producerNodeId").GetString());
        Assert.Equal("result", reference.GetProperty("projectionKey").GetString());
    }

    [Fact]
    public async Task The_highest_valid_semver_version_is_referenced_whatever_the_elsa3_version()
    {
        var catalog = RealActivityCatalog.Create().WithVersion(WriteLineKey, "99.1.0+abc").WithVersion(WriteLineKey, "99.0.0");
        var write = Activity("write", "Elsa.WriteLine", ("text", Literal("Hello")));
        write.Version = 99;

        var state = await ConvertAsync(catalog, Sequence("root", write));

        Assert.Equal(ActivityCatalogStableIds.VersionId(WriteLineKey, "99.1.0+abc"), Node(state, "write").ActivityVersionId);
    }

    [Fact]
    public async Task For_without_an_outer_bound_keeps_the_elsa3_inclusive_end()
    {
        var state = await ConvertAsync(RealActivityCatalog.Create(), Sequence("root",
            Activity("for", "Elsa.For", ("start", Literal("0", "Int32")), ("end", Literal("3", "Int32")))));

        Assert.Equal("true", Text(Input(Node(state, "for"), "EndInclusive")));
    }

    [Fact]
    public async Task Intrinsics_use_their_version_ids_and_inputs()
    {
        var variable = new Elsa3Variable { Id = "counter", Name = "Counter", TypeName = "String" };
        var set = Activity("set", "Elsa.SetVariable", ("value", Literal("1")));
        set.AdditionalProperties["variable"] = JsonSerializer.SerializeToElement(new { id = "counter", name = "Counter", typeName = "String" });
        var root = Sequence("root",
            set,
            Activity("output", "Elsa.SetOutput", ("outputName", Literal("Answer")), ("outputValue", Literal("42"))),
            Activity("correlate", "Elsa.Correlate", ("correlationId", Literal("order-1"))),
            Activity("name", "Elsa.SetName", ("value", Literal("Order 1"))),
            Activity("finish", "Elsa.Finish"));

        var state = await ConvertAsync(RealActivityCatalog.Create(), root, variables: [variable]);

        var setNode = Node(state, "set");
        Assert.Equal(Elsa3IntrinsicContracts.Set, setNode.ActivityVersionId);
        Assert.Equal(AuthoredWorkflowIntrinsicKind.Set, setNode.Intrinsic!.Kind);
        Assert.Equal("counter", setNode.Intrinsic.Variable!.ReferenceKey);
        Assert.Equal("1", Text(Input(setNode, "value")));
        Assert.Equal(Elsa3IntrinsicContracts.SetOutput, Node(state, "output").ActivityVersionId);
        Assert.Equal("Answer", Text(Input(Node(state, "output"), "name")));
        Assert.Equal("order-1", Text(Input(Node(state, "correlate"), "value")));
        Assert.Equal("Order 1", Text(Input(Node(state, "name"), "value")));
        Assert.Equal("Done", Text(Input(Node(state, "finish"), "outcome")));
        Assert.Equal(AuthoredWorkflowIntrinsicKind.Finish, Node(state, "finish").Intrinsic!.Kind);
    }

    [Fact]
    public async Task HttpEndpoint_answers_synchronously_like_elsa3()
    {
        var endpoint = Activity("endpoint", "Elsa.HttpEndpoint", ("path", Literal("/orders")));
        endpoint.CustomProperties = new Elsa3ActivityCustomProperties { CanStartWorkflow = true };

        var node = Node(await ConvertAsync(RealActivityCatalog.Create(), Sequence("root", endpoint)), "endpoint");

        Assert.Equal("Sync", Text(Input(node, "ResponseMode")));
        Assert.Equal("true", Text(Input(node, "CanStartWorkflow")));
        Assert.Equal("/orders", Text(Input(node, "Path")));
    }

    [Fact]
    public async Task If_ports_arrive_in_the_native_then_and_else_slots()
    {
        var @if = Activity("if", "Elsa.If", ("condition", Literal("true", "Boolean")));
        @if.AdditionalProperties["then"] = Port(Activity("yes", "Elsa.WriteLine", ("text", Literal("yes"))));
        @if.AdditionalProperties["else"] = Port(Activity("no", "Elsa.WriteLine", ("text", Literal("no"))));

        var node = Node(await ConvertAsync(RealActivityCatalog.Create(), Sequence("root", @if)), "if");

        Assert.Equal("elsa.if.structure", node.Structure!.Kind);
        Assert.Equal("yes", node.Structure.Payload.GetProperty("then").GetProperty("nodeId").GetString());
        Assert.Equal("no", node.Structure.Payload.GetProperty("else").GetProperty("nodeId").GetString());
    }

    [Fact]
    public async Task Flow_decision_and_switch_become_if_and_switch()
    {
        var decision = Activity("decision", "Elsa.FlowDecision", ("condition", Literal("true", "Boolean")));
        var flowSwitch = Activity("switch", "Elsa.FlowSwitch");
        flowSwitch.AdditionalProperties["cases"] = JsonSerializer.SerializeToElement(new object[]
        {
            new { label = "big", condition = new { typeName = "Boolean", expression = new { type = "JavaScript", value = "1 > 0" } } },
            new { label = "small", condition = new { typeName = "Boolean", expression = new { type = "Literal", value = "false" } } }
        });

        var state = await ConvertAsync(RealActivityCatalog.Create(), Sequence("root", decision, flowSwitch));

        var catalog = RealActivityCatalog.Create();
        Assert.Equal(catalog.VersionIdOf("Elsa.Activities.If.Activities.If"), Node(state, "decision").ActivityVersionId);
        var switchNode = Node(state, "switch");
        Assert.Equal(catalog.VersionIdOf("Elsa.Activities.Switch.Activities.Switch"), switchNode.ActivityVersionId);
        Assert.Equal(["big", "small"], switchNode.Structure!.Payload.GetProperty("cases").EnumerateArray().Select(item => item.GetProperty("match").GetString()));
        Assert.Contains("return \"big\"", Source(Input(switchNode, "Value")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_definition_using_every_convertible_built_in_converts_without_errors()
    {
        var variable = new Elsa3Variable { Id = "v", Name = "V", TypeName = "String" };
        var set = Activity("set", "Elsa.SetVariable", ("value", Literal("x")));
        set.AdditionalProperties["variable"] = JsonSerializer.SerializeToElement(new { id = "v", name = "V", typeName = "String" });
        var @if = Activity("if", "Elsa.If", ("condition", Literal("true", "Boolean")));
        @if.AdditionalProperties["then"] = Port(Activity("then", "Elsa.WriteLine", ("text", Literal("t"))));
        var loop = Activity("while", "Elsa.While", ("condition", Literal("false", "Boolean")));
        loop.AdditionalProperties["body"] = Port(Activity("body", "Elsa.WriteLine", ("text", Literal("b"))));
        var parallel = Activity("parallel", "Elsa.Parallel");
        parallel.Activities = [Activity("p1", "Elsa.WriteLine", ("text", Literal("1"))), Activity("p2", "Elsa.Break")];
        var root = Sequence("root",
            Activity("write", "Elsa.WriteLine", ("text", Literal("hi"))),
            Activity("delay", "Elsa.Delay"),
            Activity("fault", "Elsa.Fault", ("message", Literal("boom"))),
            Activity("publish", "Elsa.PublishEvent", ("eventName", Literal("e"))),
            Activity("read", "Elsa.ReadLine"),
            Activity("for", "Elsa.For", ("start", Literal("0", "Int32")), ("end", Literal("1", "Int32"))),
            Activity("script", "Elsa.RunJavaScript", ("script", Literal("return 1;"))),
            Activity("send", "Elsa.FlowSendHttpRequest", ("url", Literal("https://example.org"))),
            Activity("respond", "Elsa.WriteHttpResponse", ("statusCode", Literal("OK"))),
            Activity("execute", "Elsa.ExecuteWorkflow", ("workflowDefinitionId", Literal("child"))),
            set, @if, loop, parallel);

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root, variables: [variable]));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(Elsa3MigrationDiagnosticSeverity.Warning, diagnostic.Severity));
        Assert.Equal("200", Text(Input(Node(result.Value!.State, "respond"), "StatusCode")));
        var branches = Node(result.Value.State, "parallel").Structure!.Payload.GetProperty("branches").EnumerateArray().ToArray();
        Assert.Equal(["Branch 1", "Branch 2"], branches.Select(branch => branch.GetProperty("name").GetString()));
        // W: Break inside a Parallel branch does not cross it in Elsa 4.
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.BreakStopsAtParallel);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.ExecuteWorkflowDurable);
    }

    [Fact]
    public async Task Converting_twice_gives_byte_identical_state_and_diagnostics()
    {
        var root = Sequence("root",
            Activity("write", "Elsa.WriteLine", ("text", Literal("hi"))),
            Activity("send", "Elsa.FlowSendHttpRequest", ("url", Literal("https://example.org"))));
        var definition = ConversionServices.Definition(root);

        var first = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition);
        var second = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition);

        Assert.Equal(JsonSerializer.Serialize(first.Value!.State), JsonSerializer.Serialize(second.Value!.State));
        Assert.Equal(first.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)), second.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
    }

    private static async Task<WorkflowDefinitionState> ConvertAsync(
        RealActivityCatalog catalog,
        Elsa3Activity root,
        IEnumerable<Elsa3Variable>? variables = null)
    {
        var result = await ConversionServices.ConvertAsync(catalog, ConversionServices.Definition(root, variables: variables));
        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        return result.Value!.State;
    }

    internal static string Describe(IEnumerable<Elsa3MigrationDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"{diagnostic.Severity} {diagnostic.Code} at {diagnostic.Path}: {diagnostic.Message}"));

    internal static ActivityNode Node(WorkflowDefinitionState state, string nodeId) =>
        Elsa3ImportedActivityNodes.Walk(state.RootActivity!).Single(node => node.NodeId == nodeId);

    internal static ArgumentState Input(ActivityNode node, string key) =>
        node.Inputs.SingleOrDefault(input => input.ReferenceKey == key)
        ?? throw new Xunit.Sdk.XunitException($"Node '{node.NodeId}' has no input '{key}'; it has {string.Join(", ", node.Inputs.Select(input => input.ReferenceKey))}.");

    /// <summary>An argument's value as text: a string, or a JSON value read back from a structure payload.</summary>
    internal static string? Text(ArgumentState argument) => argument.Value.Value switch
    {
        null => null,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        JsonElement element => element.GetRawText(),
        var value => value.ToString()
    };

    /// <summary>The source of a lowered portable expression (<c>{ language, source }</c>).</summary>
    internal static string Source(ArgumentState argument) => argument.Value.Value switch
    {
        JsonElement element when element.TryGetProperty("source", out var source) => source.GetString() ?? string.Empty,
        var value => JsonSerializer.Serialize(value)
    };

    internal static Elsa3Activity Sequence(string nodeId, params Elsa3Activity[] children)
    {
        var sequence = Activity(nodeId, "Elsa.Sequence");
        sequence.Activities = children.ToList();
        return sequence;
    }

    internal static Elsa3Activity Activity(string nodeId, string type, params (string Name, JsonElement Value)[] properties) => new()
    {
        Id = nodeId,
        NodeId = nodeId,
        Name = nodeId,
        Type = type,
        Version = 1,
        AdditionalProperties = properties.ToDictionary(property => property.Name, property => property.Value),
    };

    internal static JsonElement Literal(string value, string typeName = "String") =>
        JsonSerializer.SerializeToElement(new { typeName, expression = new { type = "Literal", value } });

    internal static JsonElement Expression(string type, string value, string typeName = "String") =>
        JsonSerializer.SerializeToElement(new { typeName, expression = new { type, value } });

    internal static JsonElement Captured(string memoryId, string typeName = "Object") =>
        JsonSerializer.SerializeToElement(new { typeName, memoryReference = new { id = memoryId } });

    internal static JsonElement Port(Elsa3Activity activity) =>
        JsonSerializer.SerializeToElement(activity, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
