using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Xunit;
using DesignActivityContract = Elsa.Activities.Design.Core.Models.ActivityContract;

namespace Elsa.Activities.Graph.Tests;

/// <summary>
/// A closed-scope graph body may refer only to what the activity declares or contains, and a
/// reference to anything else is refused at publication with the node and the reference named.
/// </summary>
public sealed partial class GraphActivityProviderTests
{
    [Fact]
    public async Task A_body_that_reads_only_what_the_activity_declares_has_no_reference_findings()
    {
        var diagnostics = await ValidateBodyAsync(
            BodyArgument("a", "WorkflowRequest", new { memberKey = "amount" }),
            BodyArgument("b", "Input", new { referenceKey = "amount" }),
            BodyArgument("c", "Variable", new { referenceKey = "running-total", declaringScopeId = "workflow" }),
            BodyArgument("d", "Variable", new { referenceKey = "running-total", declaringScopeId = "activity-graph" }),
            BodyArgument("f", "Variable", new { referenceKey = "running-total", declaringScopeId = (string?)null }),
            BodyArgument("e", "JavaScript", new
            {
                source = "args.amount + args.total",
                parameters = new Dictionary<string, object>
                {
                    ["amount"] = new { kind = "WorkflowRequest", memberKey = "amount" },
                    ["total"] = new { kind = "Variable", declaringScopeNodeId = "workflow", variableKey = "running-total" }
                }
            }));

        Assert.DoesNotContain(diagnostics, x => x.Code.StartsWith("activity.graph.body-", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("WorkflowRequest", """{"memberKey":"unknown"}""", GraphBodyReferenceCodes.InputUnknown)]
    [InlineData("Input", """{"referenceKey":"unknown"}""", GraphBodyReferenceCodes.InputUnknown)]
    [InlineData("Variable", """{"referenceKey":"unknown","declaringScopeId":"workflow"}""", GraphBodyReferenceCodes.VariableUnknown)]
    [InlineData("Variable", """{"referenceKey":"running-total","declaringScopeId":"elsewhere"}""", GraphBodyReferenceCodes.VariableScopeInvisible)]
    [InlineData("Variable", """{"referenceKey":"running-total","declaringScopeId":" "}""", GraphBodyReferenceCodes.VariableScopeInvisible)]
    // The string form of a variable reference, read as the compiler reads it.
    [InlineData("Variable", "\"unknown\"", GraphBodyReferenceCodes.VariableUnknown)]
    [InlineData("ActivityResult", """{"producerNodeId":"ghost","projectionKey":"result","producerScopeId":"graph-root"}""", GraphBodyReferenceCodes.ProducerUnknown)]
    [InlineData("JavaScript", """{"source":"args.x","parameters":{"x":{"kind":"WorkflowRequest","memberKey":"unknown"}}}""", GraphBodyReferenceCodes.InputUnknown)]
    [InlineData("JavaScript", """{"source":"args.x","parameters":{"x":{"kind":"ActivityResult","producerNodeId":"ghost","projectionKey":"result"}}}""", GraphBodyReferenceCodes.ProducerUnknown)]
    [InlineData("JavaScript", """{"source":"args.x","parameters":{"x":{"kind":"Variable","declaringScopeNodeId":"workflow","variableKey":"unknown"}}}""", GraphBodyReferenceCodes.VariableUnknown)]
    public async Task A_body_reference_to_something_the_activity_does_not_hold_is_refused(string syntax, string value, string code)
    {
        var diagnostics = await ValidateBodyAsync(BodyArgument("value", syntax, JsonDocument.Parse(value).RootElement.Clone()));

        var finding = Assert.Single(diagnostics, x => x.Code == code);
        Assert.Equal(ActivityDiagnosticSeverity.Error, finding.Severity);
        Assert.Equal("/rootActivity/inputs/0", finding.Location?.JsonPointer);
        Assert.Equal("value", finding.Location?.ReferenceKey);
        Assert.Contains("'graph-root'", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"referenceKey":"unknown","declaringScopeId":"workflow"}""")]
    [InlineData("""{"referenceKey":"running-total","declaringScopeId":"caller-container"}""")]
    [InlineData("""{"referenceKey":"running-total","declaringScopeId":" "}""")]
    public async Task A_capture_into_anything_but_the_activitys_own_variables_is_refused(string target)
    {
        var diagnostics = await ValidateBodyAsync(
            inputs: [],
            outputs: [BodyArgument("result", "Variable", JsonDocument.Parse(target).RootElement.Clone())]);

        var finding = Assert.Single(diagnostics, x => x.Code == GraphBodyReferenceCodes.CaptureTargetUnknown);
        Assert.Equal("/rootActivity/outputs/0", finding.Location?.JsonPointer);
    }

    [Fact]
    public async Task Syntax_names_match_without_regard_to_case()
    {
        var diagnostics = await ValidateBodyAsync(
            inputs: [BodyArgument("value", "WORKFLOWREQUEST", new { memberKey = "unknown" })],
            outputs: [BodyArgument("result", "VARIABLE", new { referenceKey = "running-total", declaringScopeId = "workflow" })]);

        Assert.Contains(diagnostics, x => x.Code == GraphBodyReferenceCodes.InputUnknown);
        Assert.DoesNotContain(diagnostics, x => x.Code == GraphBodyReferenceCodes.CaptureTargetUnknown);
    }

    [Fact]
    public async Task A_capture_into_a_declared_graph_variable_is_accepted()
    {
        var diagnostics = await ValidateBodyAsync(
            inputs: [],
            outputs: [BodyArgument("result", "Variable", new { referenceKey = "running-total", declaringScopeId = "workflow" })]);

        Assert.DoesNotContain(diagnostics, x => x.Code.StartsWith("activity.graph.body-", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"running-total\"")]
    [InlineData("""{"referenceKey":"running-total","declaringScopeId":null}""")]
    public async Task A_capture_into_a_graph_variable_without_a_named_scope_is_accepted(string target)
    {
        var diagnostics = await ValidateBodyAsync(inputs: [], outputs: [BodyArgument("result", "Variable", JsonDocument.Parse(target).RootElement.Clone())]);

        Assert.DoesNotContain(diagnostics, x => x.Code.StartsWith("activity.graph.body-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Duplicate_variables_are_reported_by_the_graph_check_instead_of_throwing()
    {
        var diagnostics = await _provider.ValidateAsync(
            BodyManifest([BodyArgument("value", "Variable", "running-total")], [], duplicateVariable: true),
            BodyContract());

        Assert.Contains(diagnostics, x => x.Code == "activity.graph.variable-reference-duplicate");
        Assert.DoesNotContain(diagnostics, x => x.Code.StartsWith("activity.graph.body-", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"referenceKey":5,"value":"not an argument value"}""")]
    [InlineData("""{"referenceKey":"value","value":{"expressionType":7,"value":{}}}""")]
    [InlineData("""{"referenceKey":"value","value":[1,2]}""")]
    [InlineData("""{"value":{"expressionType":"Literal","value":1}}""")]
    [InlineData("\"not an argument\"")]
    public async Task A_malformed_body_argument_is_refused_where_it_is(string argument)
    {
        var manifest = BodyManifest([JsonDocument.Parse(argument).RootElement.Clone()], []);

        var draft = await _provider.ValidateAsync(manifest, BodyContract());
        var compilation = await _provider.CompileAsync(CompilationRequest(manifest));

        foreach (var diagnostics in new[] { draft, compilation.Diagnostics })
        {
            var finding = Assert.Single(diagnostics, x => x.Code == "activity.graph.argument-malformed");
            Assert.Equal("/rootActivity/inputs/0", finding.Location?.JsonPointer);
            Assert.DoesNotContain(diagnostics, x => x.Code == "activity.graph.root-activity-malformed");
        }
    }

    [Fact]
    public async Task An_argument_without_a_value_is_unbound_and_accepted()
    {
        var diagnostics = await ValidateBodyAsync(inputs: [JsonDocument.Parse("""{"referenceKey":"value"}""").RootElement.Clone()], outputs: []);

        Assert.DoesNotContain(diagnostics, x => x.Code == "activity.graph.argument-malformed" || x.Code.StartsWith("activity.graph.body-", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("outputs")]
    [InlineData("Outputs")]
    public async Task Arguments_that_are_not_a_list_are_refused(string propertyName)
    {
        var manifest = WithRootProperty(BodyManifest([], []), propertyName, JsonNode.Parse("""{"referenceKey":"result"}"""));

        var diagnostics = await _provider.ValidateAsync(manifest, BodyContract());

        var finding = Assert.Single(diagnostics, x => x.Code == "activity.graph.argument-malformed");
        Assert.Equal($"/rootActivity/{propertyName}", finding.Location?.JsonPointer);
    }

    [Fact]
    public async Task A_body_that_cannot_be_read_is_refused_rather_than_left_unchecked()
    {
        var manifest = WithRootProperty(BodyManifest([], []), "executionPlacement", JsonNode.Parse("""{"not":"a placement"}"""));

        var draft = await _provider.ValidateAsync(manifest, BodyContract());
        var compilation = await _provider.CompileAsync(CompilationRequest(manifest));

        Assert.Contains(draft, x => x.Code == "activity.graph.root-activity-malformed");
        Assert.Contains(compilation.Diagnostics, x => x.Code == "activity.graph.root-activity-malformed");
    }

    [Theory]
    [InlineData("intrinsic")]
    [InlineData("Intrinsic")]
    public async Task An_engine_intrinsic_in_a_graph_body_is_refused(string propertyName)
    {
        var manifest = WithRootProperty(
            BodyManifest([], []),
            propertyName,
            JsonNode.Parse("""{"kind":"Set","valueType":{"alias":"String","collectionKind":"Single"},"variable":{"referenceKey":"label","declaringScopeId":"workflow"}}"""));

        var draft = await _provider.ValidateAsync(manifest, BodyContract());
        var compilation = await _provider.CompileAsync(CompilationRequest(manifest));

        foreach (var diagnostics in new[] { draft, compilation.Diagnostics })
        {
            Assert.Single(diagnostics, x => x.Code == "activity.graph.intrinsic-unsupported" && x.Location?.JsonPointer == $"/rootActivity/{propertyName}");
        }
    }

    [Fact]
    public async Task Compilation_accepts_two_body_nodes_capturing_into_one_graph_variable()
    {
        // No concurrency rule applies to graph-variable captures yet; at run time the last committed capture wins.
        var capture = """[{ "referenceKey": "total", "value": { "expressionType": "Variable", "value": { "referenceKey": "running-total", "declaringScopeId": "workflow" } } }]""";
        var manifest = new ActivityProviderManifest(
            GraphActivityProvider.Key,
            Design.Models.ActivityGraphManifest.SchemaVersion,
            JsonDocument.Parse($$"""
                {
                  "variables": [{ "referenceKey": "running-total", "name": "RunningTotal", "type": { "alias": "Int64", "collectionKind": "Single" }, "storageDriverKey": "elsa.json" }],
                  "rootActivity": {
                    "nodeId": "root", "activityVersionId": "activity-ver-root", "inputs": [], "outputs": [],
                    "structure": { "kind": "Sequence", "schemaVersion": "1", "payload": { "activities": [
                      { "nodeId": "first", "activityVersionId": "activity-ver-step", "inputs": [], "outputs": {{capture}}, "structure": null },
                      { "nodeId": "second", "activityVersionId": "activity-ver-step", "inputs": [], "outputs": {{capture}}, "structure": null }
                    ] } }
                  },
                  "outputMappings": []
                }
                """).RootElement.Clone());
        var contract = new DesignActivityContract("1", [], [], [new ActivityOutcomeContract("done", "Done", true)]);
        var request = new ActivityTemplateCompilationRequest(
            "definition-1", "activity-type-1", "draft-1", 1, "1.0.0", contract, manifest,
            [
                CreateDependency("root", "activity-ver-root", "root-origin", OutcomeContract(("done", "Done")), declaredStructure: new("Sequence", "1")),
                CreateDependency("first", "activity-ver-step", "first-origin"),
                CreateDependency("second", "activity-ver-step", "second-origin")
            ],
            "provider-fingerprint");

        var compilation = await _provider.CompileAsync(request);

        Assert.DoesNotContain(compilation.Diagnostics, x => x.Severity == ActivityDiagnosticSeverity.Error);
        foreach (var occurrenceId in new[] { "first", "second" })
        {
            var occurrence = Assert.Single(compilation.Occurrences, x => x.OccurrenceId == occurrenceId);
            Assert.Equal("running-total", occurrence.OutputBindings!.Value[0].GetProperty("value").GetProperty("value").GetProperty("referenceKey").GetString());
        }
    }

    [Fact]
    public async Task Compilation_accepts_a_read_of_a_body_nodes_whole_result()
    {
        var manifest = BodyManifest(
            inputs: [BodyArgument("value", "ActivityResult", new { producerNodeId = "graph-root", projectionKey = "$result", producerScopeId = "graph-root" })],
            outputs: []);
        var request = new ActivityTemplateCompilationRequest(
            "definition-1", "activity-type-1", "draft-1", 1, "1.0.0", BodyContract(), manifest,
            [CreateDependency("graph-root", "activity-ver-root", "root-origin")], "provider-fingerprint");

        var compilation = await _provider.CompileAsync(request);

        Assert.DoesNotContain(compilation.Diagnostics, x => x.Code == GraphBodyReferenceCodes.ProducerUnknown);
    }

    [Fact]
    public async Task Compilation_refuses_a_missing_result_projection_and_an_incompatible_capture()
    {
        // These two need the body nodes' resolved contracts, so they are checked when the activity compiles.
        var manifest = BodyManifest(
            inputs: [BodyArgument("value", "ActivityResult", new { producerNodeId = "graph-root", projectionKey = "missing", producerScopeId = "graph-root" })],
            outputs: [BodyArgument("total", "Variable", new { referenceKey = "label", declaringScopeId = "workflow" })]);
        var request = new ActivityTemplateCompilationRequest(
            "definition-1",
            "activity-type-1",
            "draft-1",
            1,
            "1.0.0",
            BodyContract(),
            manifest,
            [CreateDependency("graph-root", "activity-ver-root", "root-origin")],
            "provider-fingerprint");

        var compilation = await _provider.CompileAsync(request);

        Assert.Contains(compilation.Diagnostics, x => x.Code == GraphBodyReferenceCodes.ProducerUnknown && x.Location?.ReferenceKey == "value");
        var incompatible = Assert.Single(compilation.Diagnostics, x => x.Code == GraphBodyReferenceCodes.CaptureTypeIncompatible);
        Assert.Equal("/rootActivity/outputs/0", incompatible.Location?.JsonPointer);
    }

    private ActivityTemplateCompilationRequest CompilationRequest(ActivityProviderManifest manifest) =>
        new("definition-1", "activity-type-1", "draft-1", 1, "1.0.0", BodyContract(), manifest,
            [CreateDependency("graph-root", "activity-ver-root", "root-origin")], "provider-fingerprint");

    private static ActivityProviderManifest WithRootProperty(ActivityProviderManifest manifest, string name, JsonNode? value)
    {
        var payload = JsonNode.Parse(manifest.Payload.GetRawText())!;
        var root = payload["rootActivity"]!.AsObject();
        foreach (var existing in root.Select(x => x.Key).Where(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)).ToArray())
            root.Remove(existing);
        root[name] = value;
        return manifest with { Payload = JsonSerializer.SerializeToElement(payload) };
    }

    private Task<IReadOnlyList<ActivityDiagnostic>> ValidateBodyAsync(params object[] inputs) =>
        ValidateBodyAsync(inputs, []).AsTask();

    private ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateBodyAsync(object[] inputs, object[] outputs) =>
        _provider.ValidateAsync(BodyManifest(inputs, outputs), BodyContract());

    private static ActivityProviderManifest BodyManifest(object[] inputs, object[] outputs, bool duplicateVariable = false) =>
            new ActivityProviderManifest(
                GraphActivityProvider.Key,
                Design.Models.ActivityGraphManifest.SchemaVersion,
                JsonSerializer.SerializeToElement(new
                {
                    variables = (duplicateVariable
                        ? new[] { new { referenceKey = "label", name = "Again", type = new { alias = "String", collectionKind = "Single" }, storageDriverKey = "elsa.json" } }
                        : []).Concat(new[]
                    {
                        new
                        {
                            referenceKey = "running-total",
                            name = "RunningTotal",
                            type = new { alias = "Object", collectionKind = "Single" },
                            storageDriverKey = "elsa.json"
                        },
                        new
                        {
                            referenceKey = "label",
                            name = "Label",
                            type = new { alias = "String", collectionKind = "Single" },
                            storageDriverKey = "elsa.json"
                        }
                    }),
                    rootActivity = new
                    {
                        nodeId = "graph-root",
                        activityVersionId = "activity-ver-root",
                        inputs,
                        outputs,
                        structure = (object?)null
                    },
                    outputMappings = Array.Empty<object>()
                }));

    private static DesignActivityContract BodyContract() =>
            new DesignActivityContract(
                "1",
                [new ActivityInputContract("amount", "Amount", new TypeReference("Int64"), false, true, null, "elsa.json")],
                [],
                [new ActivityOutcomeContract("done", "Done", true)]);

    private static object BodyArgument(string referenceKey, string expressionType, object value) =>
        new { referenceKey, value = new { expressionType, value } };
}

/// <summary>The refusal codes, as the published contract names them.</summary>
internal static class GraphBodyReferenceCodes
{
    public const string InputUnknown = "activity.graph.body-input-unknown";
    public const string VariableUnknown = "activity.graph.body-variable-unknown";
    public const string VariableScopeInvisible = "activity.graph.body-variable-scope-invisible";
    public const string ProducerUnknown = "activity.graph.body-producer-unknown";
    public const string CaptureTargetUnknown = "activity.graph.body-capture-target-unknown";
    public const string CaptureTypeIncompatible = "activity.graph.body-capture-type-incompatible";
}
