using System.Text.Json;
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
    [InlineData("\"not an argument\"")]
    public async Task A_malformed_body_argument_does_not_throw_out_of_validation(string argument)
    {
        var diagnostics = await ValidateBodyAsync(inputs: [JsonDocument.Parse(argument).RootElement.Clone()], outputs: []);

        Assert.DoesNotContain(diagnostics, x => x.Code.StartsWith("activity.graph.body-", StringComparison.Ordinal));
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
