using System.Collections.Immutable;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

public sealed partial class ActivityDataSelectionAnalyzerTests
{
    [Fact]
    public async Task Portable_only_earlier_run_source_has_fresh_exact_metadata_without_native_member_reads()
    {
        var shapes = new ParameterShape();
        using var provider = Provider(services => services.AddSingleton<IActivitySerializedShapeSource>(shapes));
        var document = Document(Placed("flowchart-cycle"));
        var parameter = new ActivityResultExpressionParameterBinding("producer", "$result");
        var listed = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"), new() { ExpressionType = "JavaScript" });
        var root = Assert.Single(listed.Sources, source => source.Id == "activity:producer:$result");
        Assert.Null(root.Binding); Assert.Equal("EarlierProducerRun", root.ReasonCode); Assert.Equal(parameter, root.ParameterSource);
        var result = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"),
            new() { ExpressionType = "JavaScript", ParameterSource = parameter, MemberPath = ["a.b", ""] });
        Assert.Empty(result.Findings); Assert.True(result.ExpressionParametersEnabled); Assert.False(result.MemberReadsEnabled);
        var offer = Assert.Single(result.Sources); Assert.Equal(parameter, offer.ParameterSource);
        Assert.Equal(new[] { "a.b", "" }, offer.ParameterPath!.Value.ToArray()); Assert.Equal("String", offer.Shape.Type!.Alias); Assert.Empty(result.MemberReads);
        var direct = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"), new() { Source = ScopeFreeResult("producer") });
        Assert.Contains(direct.Findings, finding => finding.Type == "ValueFlow/EarlierProducerRun");
        var native = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"), new() { Source = ScopeFreeResult("producer"), MemberPath = ["a.b"] });
        Assert.Contains(native.Findings, finding => finding.Type == "ActivityData/UnsupportedMemberBinding");
        Assert.Contains(shapes.Paths, path => path.SequenceEqual(new[] { "a.b", "" }));
    }

    [Theory]
    [InlineData("request")]
    [InlineData("variable")]
    public async Task Expression_parameters_use_authoritative_request_names_and_variable_scopes(string role)
    {
        using var provider = Provider();
        var document = role == "request" ? RequestDocument() : VariableDocument();
        var listed = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"), new() { ExpressionType = "Liquid" });
        var offer = Assert.Single(listed.Sources);
        ExpressionParameterBinding expected = role == "request"
            ? new WorkflowRequestExpressionParameterBinding("customer")
            : new VariableExpressionParameterBinding("workflow", "customer");
        Assert.Equal(expected, offer.ParameterSource); Assert.Empty(offer.ParameterPath!.Value);
        var focused = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"),
            new() { ExpressionType = "Liquid", ParameterSource = expected, MemberPath = ["customerId"] });
        Assert.Empty(focused.Findings); Assert.Equal(expected, Assert.Single(focused.Sources).ParameterSource);
        Assert.False(focused.MemberReadsEnabled);
    }

    [Theory]
    [InlineData("activity.shape.no-typed-result", false)]
    [InlineData("activity.shape.metadata-unavailable", true)]
    public async Task Portable_metadata_preserves_known_absence_distinct_from_missing_metadata(string reason, bool admitted)
    {
        using var provider = Provider(services => services.AddSingleton<IActivitySerializedShapeSource>(new MissingResultShape(reason)));
        var result = await Analyzer(provider).AnalyzeAsync(Document(Placed("sequence")), new("consumer", "CustomerId"),
            new() { ExpressionType = "JavaScript", ParameterSource = new ActivityResultExpressionParameterBinding("producer", "$result"), MemberPath = ["customerId"] });
        Assert.Equal(admitted, result.Findings.IsEmpty);
        if (admitted) Assert.NotNull(Assert.Single(result.Sources).ParameterSource);
        else { Assert.Empty(result.Sources); Assert.Contains(result.Findings, finding => finding.Type == "ActivityData/UnknownProjection"); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_conditional_portable_source_requires_the_explicit_optional_contract(bool optional)
    {
        using var provider = Provider();
        var chart = Flowchart([Node("start"), Node("producer"), Node("consumer")], [("start", "consumer"), ("producer", "consumer")], "start");
        var parameter = new ActivityResultExpressionParameterBinding("producer", "$result", optional);
        var result = await Analyzer(provider).AnalyzeAsync(Document(chart), new("consumer", "CustomerId"),
            new() { ExpressionType = "JavaScript", ParameterSource = parameter, MemberPath = ["customerId"] });
        Assert.Equal(optional, result.Findings.IsEmpty);
        if (optional) Assert.Equal(parameter, Assert.Single(result.Sources).ParameterSource);
        else Assert.Empty(result.Sources);
    }

    [Theory]
    [InlineData("mixed")]
    [InlineData("no-syntax")]
    [InlineData("literal")]
    [InlineData("unknown-variable")]
    [InlineData("unknown-request")]
    [InlineData("unknown-projection")]
    [InlineData("scope")]
    public async Task Portable_metadata_rejects_mixed_or_unadmitted_sources(string scenario)
    {
        using var provider = Provider();
        var document = Document(Placed("sequence"));
        ExpressionParameterBinding parameter = scenario switch
        {
            "literal" => new LiteralExpressionParameterBinding(JsonSerializer.SerializeToElement(1)),
            "unknown-variable" => new VariableExpressionParameterBinding("missing-scope", "customer"),
            "unknown-request" => new WorkflowRequestExpressionParameterBinding("not-declared"),
            _ => new ActivityResultExpressionParameterBinding("producer", scenario == "unknown-projection" ? "missing" : "$result")
        };
        if (scenario == "scope") document = Document(Sequence(Sequence(Node("producer")) with { NodeId = "inner" }, Node("consumer")));
        var result = await Analyzer(provider).AnalyzeAsync(document, new("consumer", "CustomerId"), new()
        { ExpressionType = scenario == "no-syntax" ? null : "JavaScript", ParameterSource = parameter,
            Source = scenario == "mixed" ? ScopeFreeResult("producer") : null, MemberPath = ["customerId"] });
        Assert.NotEmpty(result.Findings); Assert.Empty(result.Sources);
    }

    [Theory]
    [InlineData("JavaScript", true)]
    [InlineData("Liquid", false)]
    public async Task Owning_target_restrictions_check_the_actual_expression_type(string syntax, bool allowed)
    {
        using var provider = Provider(services => services.AddSingleton<IActivityInputSourceConstraintProvider>(new Constraint(["JavaScript"])));
        var result = await Analyzer(provider).AnalyzeAsync(Document(Placed("sequence")), new("consumer", "CustomerId"), new()
        { ExpressionType = syntax, ParameterSource = new ActivityResultExpressionParameterBinding("producer", "$result") });
        Assert.Equal(allowed, result.Findings.IsEmpty);
        Assert.Equal(allowed ? 1 : 0, result.Sources.Length);
    }

    [Theory]
    [InlineData("parent-sensitive")]
    [InlineData("leaf-credential")]
    [InlineData("missing")]
    public async Task Protected_or_known_invalid_metadata_paths_offer_no_parameter(string scenario)
    {
        using var provider = Provider(services => services.AddSingleton<IActivitySerializedShapeSource>(new ParameterShape(scenario)));
        var result = await Analyzer(provider).AnalyzeAsync(Document(Placed("sequence")), new("consumer", "CustomerId"), new()
        { ExpressionType = "JavaScript", ParameterSource = new ActivityResultExpressionParameterBinding("producer", "$result"), MemberPath = ["a.b", ""] });
        Assert.NotEmpty(result.Findings); Assert.Empty(result.Sources); Assert.False(result.MemberReadsEnabled);
    }

    [Theory]
    [InlineData("limit", true)]
    [InlineData("over-limit", false)]
    [InlineData("unicode-limit", true)]
    [InlineData("unicode-over", false)]
    [InlineData("segments-limit", true)]
    [InlineData("segments-over", false)]
    public async Task Portable_metadata_enforces_exact_shared_UTF16_and_segment_bounds(string scenario, bool allowed)
    {
        using var provider = Provider();
        ImmutableArray<string> path = scenario switch
        {
            "limit" => [new string('x', 4096)], "over-limit" => [new string('x', 4097)],
            "unicode-limit" => [string.Concat(Enumerable.Repeat("😀", 2048))],
            "unicode-over" => [string.Concat(Enumerable.Repeat("😀", 2049))],
            "segments-limit" => [.. Enumerable.Repeat("x", 64)], _ => [.. Enumerable.Repeat("x", 65)]
        };
        var result = await Analyzer(provider).AnalyzeAsync(Document(Placed("sequence")), new("consumer", "CustomerId"), new()
        { ExpressionType = "JavaScript", ParameterSource = new ActivityResultExpressionParameterBinding("producer", "$result"), MemberPath = path });
        Assert.Equal(allowed, result.Findings.IsEmpty);
        if (allowed) Assert.Equal(path.ToArray(), Assert.Single(result.Sources).ParameterPath!.Value.ToArray());
        else Assert.Empty(result.Sources);
    }

    [Theory]
    [InlineData("Literal")]
    [InlineData("Object")]
    [InlineData("Default")]
    [InlineData("Secret")]
    [InlineData("Connection")]
    [InlineData("ActivityResult")]
    [InlineData("ActivityResultMember")]
    [InlineData("Input")]
    [InlineData("InputMember")]
    [InlineData("Variable")]
    [InlineData("VariableMember")]
    [InlineData("WorkflowRequest")]
    [InlineData("WorkflowRequestMember")]
    public async Task Fixed_nonportable_input_kinds_never_offer_expression_parameters(string syntax)
    {
        using var provider = Provider(services => services.AddSingleton<IActivityInputSourceConstraintProvider>(new Constraint([syntax])));
        var result = await Analyzer(provider).AnalyzeAsync(Document(Placed("sequence")), new("consumer", "CustomerId"), new() { ExpressionType = syntax });
        Assert.NotEmpty(result.Findings); Assert.Empty(result.Sources);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sensitive_consuming_value_is_not_a_blanket_source_metadata_ban_but_credentials_are(bool credential)
    {
        var catalog = Catalog(new InputDefinition("CustomerId", "CustomerId", new("String"), null, "Customer ID", null, true,
            IsSensitive: true, IsCredential: credential ? true : null));
        using var provider = ActivityDataSelectionTestSupport.Provider(catalog);
        var result = await Analyzer(provider).AnalyzeAsync(Document(Placed("sequence")), new("consumer", "CustomerId"), new()
        { ExpressionType = "JavaScript", ParameterSource = new ActivityResultExpressionParameterBinding("producer", "$result") });
        Assert.True(result.Target!.IsSensitive);
        Assert.Equal(!credential, result.Findings.IsEmpty);
        Assert.Equal(credential ? 0 : 1, result.Sources.Length);
    }

    [Fact]
    public async Task A_projected_condition_retains_its_owner_contract_in_expression_metadata()
    {
        using var provider = Provider();
        var node = Node("switch") with { Structure = new(Elsa.Activities.Switch.Activities.Switch.StructureKind,
            Elsa.Activities.Switch.Activities.Switch.StructureSchemaVersion, JsonSerializer.SerializeToElement(new {
                cases = new[] { new { caseId = "a", match = "Approved", condition = ArgumentState.Null("foreign"), activity = Node("branch") } },
                @default = (ActivityNode?)null
            }, Web)) };
        var key = Elsa.Activities.Switch.Activities.Switch.ConditionInputKey("a");
        var result = await Analyzer(provider).AnalyzeAsync(Document(Sequence(Node("producer"), node)), new("switch", key), new()
        { ExpressionType = "JavaScript", ParameterSource = new ActivityResultExpressionParameterBinding("producer", "$result") });
        Assert.True(result.Target!.IsStructureOwned); Assert.Equal("Boolean", result.Target.Type.Alias);
        Assert.Empty(result.Findings); Assert.NotNull(Assert.Single(result.Sources).ParameterSource);
    }

    private sealed class ParameterShape(string? refusal = null) : IActivitySerializedShapeSource
    {
        public List<ImmutableArray<string>> Paths { get; } = [];
        public ValueTask<ActivitySerializedShape?> DescribeResultAsync(IActivityDefinitionVersion admittedVersion, string projectionKey,
            ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default)
        {
            Paths.Add(memberPath);
            var leaf = memberPath.Length >= 2;
            return ValueTask.FromResult<ActivitySerializedShape?>(new(leaf && refusal == "missing" ? ActivitySerializedShapeState.Unavailable : ActivitySerializedShapeState.Known,
                leaf ? ActivitySerializedValueKind.Scalar : ActivitySerializedValueKind.Object, new(leaf ? "String" : "Object"), "contract", "profile", "metadata")
            { IsSensitive = refusal == "parent-sensitive" && memberPath.Length == 1, IsCredential = refusal == "leaf-credential" && leaf,
                ReasonCode = refusal == "missing" && leaf ? "activity.shape.member-not-declared" : null });
        }
        public ValueTask<ActivitySerializedShape?> DescribeDeclaredTypeAsync(TypeReference declaredType, string ownerContractIdentity,
            ImmutableArray<string> memberPath, ActivitySerializedShapeLimits limits, CancellationToken cancellationToken = default) => ValueTask.FromResult<ActivitySerializedShape?>(null);
    }
}
