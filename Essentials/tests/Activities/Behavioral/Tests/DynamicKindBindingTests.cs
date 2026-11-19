using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Http;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Constants;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Behavioral;

/// <summary>
/// Spec 1228 User Story 5: the dynamic kind binds across its .NET names. An expression declared <c>Any</c> publishes
/// on a <c>JsonElement</c>, <c>JsonElement?</c> or <c>JsonNode</c> input with no conversion step, and on a
/// <c>JsonObject</c> input with a run-time shape check; a dictionary of string arrays has a type that names the whole
/// shape, so a header map publishes and reaches the response instruction.
/// </summary>
public sealed class DynamicKindBindingTests
{
    private const string ReaderVersion = "json-reader-version";
    private const string ResponseVersion = "write-response-version";

    public static TheoryData<string> DynamicInputs => new()
    {
        nameof(JsonInputsReader.Element), nameof(JsonInputsReader.MaybeElement), nameof(JsonInputsReader.Node)
    };

    [Theory]
    [MemberData(nameof(DynamicInputs))]
    public async Task An_Any_expression_on_a_JsonElement_or_JsonNode_input_publishes_with_no_conversion_and_runs(string inputKey)
    {
        using var publication = Publication();

        var executable = await publication.Compile(Reader(inputKey, """{"total":12.50,"tags":["a"]}"""));

        var binding = executable.RootActivity.InputBindings[inputKey];
        Assert.Equal(RuntimeInputBindingSource.Expression, binding.Source);
        Assert.Null(binding.ConversionPlan);
        Assert.Equal("Any", binding.Expression!.ResultType!.Id);

        await using var harness = Harness();
        var run = await harness.RunAsync(executable);

        run.AssertWorkflowCompleted();
        var value = run.AssertCompleted("reader").Completion!.Result.InlineValue!.Value.GetProperty("value").GetString();
        Assert.Equal("""{"total":12.50,"tags":["a"]}""", Compact(value!));
    }

    [Fact]
    public async Task An_Any_expression_on_a_JsonObject_input_publishes_and_an_object_runs()
    {
        using var publication = Publication();

        var executable = await publication.Compile(Reader(nameof(JsonInputsReader.Shaped), """{"id":"o-1"}"""));

        Assert.Null(executable.RootActivity.InputBindings[nameof(JsonInputsReader.Shaped)].ConversionPlan);
        await using var harness = Harness();
        var run = await harness.RunAsync(executable);

        run.AssertWorkflowCompleted();
        Assert.Equal("""{"id":"o-1"}""",
            Compact(run.AssertCompleted("reader").Completion!.Result.InlineValue!.Value.GetProperty("value").GetString()!));
    }

    [Fact]
    public async Task An_Any_expression_on_a_JsonObject_input_refuses_a_non_object_at_run_time_with_a_diagnostic()
    {
        using var publication = Publication();
        var executable = await publication.Compile(Reader(nameof(JsonInputsReader.Shaped), """[1,2]"""));
        await using var harness = Harness();

        // The input is materialized before the activity runs, so the start itself faults with the shape diagnostic.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.RunAsync(executable));

        Assert.Contains(
            "Input 'Shaped' on executable node 'reader' is declared 'System.Text.Json.Nodes.JsonObject' and accepts only a JSON object, but its expression returned a JSON array.",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_declared_type_that_is_not_the_dynamic_kind_is_still_refused_with_the_whole_type()
    {
        using var publication = Publication();

        var error = await Assert.ThrowsAnyAsync<Exception>(() =>
            publication.Compile(Reader(nameof(JsonInputsReader.Element), "\"x\"", resultType: new TypeReference("String", CollectionKind.Array))).AsTask());

        Assert.Contains("portable expression declares result type 'String[]'", Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public void The_header_map_input_names_the_dictionary_and_its_element_arrays()
    {
        var headers = InputType(typeof(WriteHttpResponse), nameof(WriteHttpResponse.Headers));

        Assert.Equal(CollectionKind.Dictionary, headers.CollectionKind);
        Assert.Equal(new TypeReference("String", CollectionKind.Array), headers.Element);
        Assert.Equal("Dictionary<String, String[]>", headers.ToDisplayString());
    }

    [Fact]
    public async Task A_header_map_literal_publishes_and_the_response_carries_the_header()
    {
        using var publication = Publication();

        var executable = await publication.Compile(new ActivityNode("write", ResponseVersion,
        [
            new(nameof(WriteHttpResponse.Headers), new ArgumentValue("""{"X-Test": ["1"]}""", "Object"), null, null, null, null)
        ], []));

        var binding = executable.RootActivity.InputBindings[nameof(WriteHttpResponse.Headers)];
        Assert.Equal(new TypeReference("String", CollectionKind.Array), binding.TargetType.Element);
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesHttpFeature().ConfigureServices(services))
            .Build("actexec-write-response");
        var run = await harness.RunAsync(executable);

        run.AssertWorkflowCompleted();
        var headers = run.AssertCompleted("write").Completion!.Result.InlineValue!.Value.GetProperty("headers");
        Assert.Equal("1", Assert.Single(headers.GetProperty("X-Test").EnumerateArray()).GetString());
    }

    // Spec 1228 User Story 4, acceptance 2: a definition naming Object or Elsa.Any loads as Any, publishes and runs.
    [Theory]
    [InlineData("Object")]
    [InlineData("Elsa.Any")]
    [InlineData("Any")]
    public async Task A_variable_named_with_an_older_dynamic_name_loads_as_Any_publishes_and_runs(string storedAlias)
    {
        var stored = JsonSerializer.Deserialize<VariableDefinition>(
            $$$"""{"referenceKey":"customer","name":"Customer","type":{"alias":"{{{storedAlias}}}","collectionKind":"Single"}}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var customer = stored with { Default = new ArgumentValue(JsonSerializer.SerializeToElement(new { address = new { street = "Main" } }), "Object") };
        Assert.Equal("Any", customer.Type.Alias);
        using var publication = new MemberReadPublicationRunTests.Publication(memberReads: true, typed: true);
        var reader = new ActivityNode("reader", "reader-version", [new(nameof(MemberReadPublicationRunTests.MemberReader.Value),
            new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "customer", declaringScopeId = "workflow", memberPath = new[] { "address", "street" } }),
                "VariableMember"), null, null, null, null)], []);

        var executable = await publication.Compile(reader, [customer]);

        Assert.Equal("Any", Assert.Single(executable.WorkflowVariables).Type.Alias);
        await using var harness = MemberReadPublicationRunTests.Harness();
        var run = await harness.RunAsync(executable);
        run.AssertWorkflowCompleted();
        Assert.Equal("Main", run.AssertCompleted("reader").Completion!.Result.InlineValue!.Value.GetProperty("value").GetString());
    }

    // One reader version per input, each declaring only that input, so the inputs a test leaves unbound are not
    // part of the contract.
    private static MemberReadPublicationRunTests.Publication Publication() =>
        new(memberReads: false, typed: false,
            ReaderVersionFor(nameof(JsonInputsReader.Element)),
            ReaderVersionFor(nameof(JsonInputsReader.MaybeElement)),
            ReaderVersionFor(nameof(JsonInputsReader.Node)),
            ReaderVersionFor(nameof(JsonInputsReader.Shaped)),
            (typeof(WriteHttpResponse), ResponseVersion, [Input(typeof(WriteHttpResponse), nameof(WriteHttpResponse.Headers))]));

    private static (Type, string, InputDefinition[]) ReaderVersionFor(string inputKey) =>
        (typeof(JsonInputsReader), ReaderVersion + "-" + inputKey, [Input(typeof(JsonInputsReader), inputKey)]);

    private static WorkflowExecutionHarness Harness() => WorkflowExecutionHarness.Create()
        .WithFeature(services => new SerializationFeature().ConfigureServices(services))
        .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
        .WithFeature(services => services.AddSingleton<IPortableExpressionEvaluator>(new SourceAsJsonEvaluator()))
        .Build(Enumerable.Range(1, 4).Select(index => $"actexec-{index}"));

    private static ActivityNode Reader(string inputKey, string json, TypeReference? resultType = null) =>
        new("reader", ReaderVersion + "-" + inputKey,
        [
            new(inputKey, new ArgumentValue(new ExpressionDefinition(
                "JavaScript",
                json,
                resultType ?? new TypeReference("Any"),
                new Dictionary<string, ExpressionParameterBinding>(),
                JsonSerializer.SerializeToElement(new { }),
                ExpressionCapabilityProfiles.BindingPureV1), "JavaScript"), null, null, null, null)
        ], []);

    private static TypeReference InputType(Type activityType, string property) =>
        TypeReferenceFactory.FromClrType(activityType.GetProperty(property)!.PropertyType, TypeAliasConvention.CanonicalAlias);

    private static InputDefinition Input(Type activityType, string property) =>
        new(property, property, InputType(activityType, property), null, property, null, IsNullable: true);

    private static string Compact(string json) => JsonNode.Parse(json)!.ToJsonString();

    private static string Flatten(Exception exception) =>
        exception.InnerException is null ? exception.Message : exception.Message + "\n" + Flatten(exception.InnerException);

    /// <summary>Stands in for a script engine: the expression's source is the JSON it returns.</summary>
    private sealed class SourceAsJsonEvaluator : IPortableExpressionEvaluator
    {
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request)
        {
            using var document = JsonDocument.Parse(request.Definition.Source);
            return ValueTask.FromResult(document.RootElement.Clone());
        }
    }

    public sealed record JsonReadValue([property: Output(Key = "Value", Path = "value")] string? Value);

    /// <summary>Returns the JSON text of whichever dynamic or shaped JSON input was bound.</summary>
    public sealed class JsonInputsReader : Activity<JsonReadValue>
    {
        [ActivityInput] public JsonElement Element { get; set; }
        [ActivityInput] public JsonElement? MaybeElement { get; set; }
        [ActivityInput] public JsonNode? Node { get; set; }
        [ActivityInput] public JsonObject? Shaped { get; set; }

        protected override ValueTask<ActivityTransition<JsonReadValue>> ExecuteAsync(ActivityExecutionContext context)
        {
            var text = Element.ValueKind != JsonValueKind.Undefined
                ? Element.GetRawText()
                : MaybeElement?.GetRawText() ?? Node?.ToJsonString() ?? Shaped?.ToJsonString();
            return ValueTask.FromResult(ActivityTransition.Complete(new JsonReadValue(text)));
        }
    }
}
