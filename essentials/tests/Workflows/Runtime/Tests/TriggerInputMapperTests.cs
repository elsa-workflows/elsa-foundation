using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Triggers;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class TriggerInputMapperTests
{
    [Fact]
    public async Task Source_omission_applies_the_target_default_while_explicit_null_is_validated()
    {
        var mapper = Mapper();
        var contract = Contract(new WorkflowDeclaredInput("count", new("Int32"), true, Json("7")));
        TriggerInputMapping[] mappings = [new("count", new TriggerSourceBinding(["payload", "count"]))];

        var omitted = await mapper.MapAsync(contract, mappings, Json("{\"payload\":{}}"));
        var explicitNull = await mapper.MapAsync(contract, mappings, Json("{\"payload\":{\"count\":null}}"));

        Assert.True(omitted.IsValid);
        Assert.Equal(7, omitted.NormalizedInputs["count"].GetInt32());
        Assert.False(explicitNull.IsValid);
        Assert.Equal(WorkflowExecutableInputValidationReasonCodes.IncompatibleValue, Assert.Single(explicitNull.Findings).ReasonCode);
    }

    [Fact]
    public async Task Source_fields_and_literals_preserve_exact_declared_numeric_values()
    {
        var mapper = Mapper();
        var result = await mapper.MapAsync(Contract(new WorkflowDeclaredInput("account", new("Int64"), true), new("amount", new("Decimal"), true)),
            [new("account", new TriggerSourceBinding(["payload", "account"])), new("amount", new TriggerLiteralBinding(Json("79228162514264337593543950335")))],
            Json("{\"payload\":{\"account\":9007199254740993}}"));

        Assert.True(result.IsValid);
        Assert.Equal(9007199254740993L, result.NormalizedInputs["account"].GetInt64());
        Assert.Equal(decimal.MaxValue, result.NormalizedInputs["amount"].GetDecimal());
    }

    [Fact]
    public async Task Expressions_receive_only_exact_declared_literal_parameters_and_no_ambient_workflow_state()
    {
        var evaluator = new RecordingEvaluator(request =>
        {
            Assert.True(request.Capabilities.IsBindingPure);
            Assert.Empty(request.AmbientVariables);
            Assert.Equal(new[] { "factor", "sourceValue" }, request.ParameterValues.Keys);
            Assert.All(request.Definition.Parameters.Values, binding => Assert.IsType<LiteralExpressionParameterBinding>(binding));
            Assert.Equal(4, request.ParameterValues["sourceValue"].GetInt32());
            Assert.Equal(3, request.ParameterValues["factor"].GetInt32());
            return Json("12");
        });
        var mapper = Mapper(evaluator, "JavaScript");
        var expression = new TriggerExpressionBinding("JavaScript", "sourceValue * factor", new("Int32"), Json("{}"),
            ExpressionCapabilityProfiles.BindingPureV1,
            new Dictionary<string, TriggerValueBinding>
            {
                ["sourceValue"] = new TriggerSourceBinding(["payload", "value"]),
                ["factor"] = new TriggerLiteralBinding(Json("3"))
            });

        var result = await mapper.MapAsync(Contract(new WorkflowDeclaredInput("result", new("Int32"), true)),
            [new("result", expression)], Json("{\"payload\":{\"value\":4},\"tenant\":\"untrusted\"}"));

        Assert.True(result.IsValid);
        Assert.Equal(12, result.NormalizedInputs["result"].GetInt32());
        Assert.Equal(1, evaluator.Calls);
    }

    [Fact]
    public async Task Missing_expression_parameter_is_refused_without_evaluating_or_substituting_null()
    {
        var evaluator = new RecordingEvaluator(_ => throw new InvalidOperationException("must not evaluate"));
        var expression = new TriggerExpressionBinding("JavaScript", "sourceValue", new("String"), Json("{}"),
            ExpressionCapabilityProfiles.BindingPureV1,
            new Dictionary<string, TriggerValueBinding> { ["sourceValue"] = new TriggerSourceBinding(["payload", "absent"]) });

        var result = await Mapper(evaluator, "JavaScript").MapAsync(Contract(new WorkflowDeclaredInput("result", new("String"), true)),
            [new("result", expression)], Json("{\"payload\":{}}"));

        Assert.False(result.IsValid);
        Assert.Equal("triggers.mapping.parameter-missing", Assert.Single(result.Findings).ReasonCode);
        Assert.Equal(0, evaluator.Calls);
        Assert.Empty(result.NormalizedInputs);
    }

    [Fact]
    public async Task Duplicate_or_unknown_destination_is_refused_before_any_evaluation()
    {
        var mapper = Mapper();
        var result = await mapper.MapAsync(Contract(new WorkflowDeclaredInput("Count", new("Int32"), true)),
            [new("count", new TriggerLiteralBinding(Json("1"))), new("count", new TriggerLiteralBinding(Json("2")))], Json("{}"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Findings, item => item.ReasonCode == WorkflowExecutableInputValidationReasonCodes.UnknownName);
        Assert.Contains(result.Findings, item => item.ReasonCode == WorkflowExecutableInputValidationReasonCodes.DuplicateName);
        Assert.Empty(result.NormalizedInputs);
    }

    [Fact]
    public async Task Expression_failure_does_not_disclose_source_or_evaluator_exception_text()
    {
        const string secret = "private-source-value";
        var evaluator = new RecordingEvaluator(_ => throw new InvalidOperationException(secret));
        var expression = new TriggerExpressionBinding("JavaScript", "x", new("String"), Json("{}"),
            ExpressionCapabilityProfiles.BindingPureV1,
            new Dictionary<string, TriggerValueBinding> { ["x"] = new TriggerLiteralBinding(Json($"\"{secret}\"")) });

        var result = await Mapper(evaluator, "JavaScript").MapAsync(Contract(new WorkflowDeclaredInput("result", new("String"), true)),
            [new("result", expression)], Json("{}"));

        var finding = Assert.Single(result.Findings);
        Assert.Equal("triggers.mapping.expression-failed", finding.ReasonCode);
        Assert.DoesNotContain(secret, finding.Message, StringComparison.Ordinal);
        Assert.Empty(result.NormalizedInputs);
    }

    [Theory]
    [InlineData("JavaScript")]
    [InlineData("javascript")]
    public async Task Installed_language_validates_without_evaluation_and_maps_with_the_composed_evaluator(string language)
    {
        var evaluator = new RecordingEvaluator(_ => Json("42"));
        var mapper = Mapper(evaluator, "JavaScript");
        var contract = Contract(new WorkflowDeclaredInput("result", new("Int32"), true));
        TriggerInputMapping[] mappings = [new("result", Expression(language))];

        var preflight = mapper.ValidateMappings(contract, mappings);

        Assert.True(preflight.IsValid);
        Assert.Equal(0, evaluator.Calls);
        var result = await mapper.MapAsync(contract, mappings, Json("{}"));
        Assert.True(result.IsValid);
        Assert.Equal(42, result.NormalizedInputs["result"].GetInt32());
        Assert.Equal(1, evaluator.Calls);
    }

    [Theory]
    [InlineData("not-installed", "JavaScript")]
    [InlineData("JavaScript", "Liquid")]
    [InlineData("JavaScript", null)]
    public async Task Unknown_or_removed_language_is_unavailable_before_evaluation(string language, string? installedLanguage)
    {
        var evaluator = new RecordingEvaluator(_ => throw new InvalidOperationException("must not evaluate"));
        var mapper = Mapper(evaluator, installedLanguage is null ? [] : [installedLanguage]);
        var contract = Contract(new WorkflowDeclaredInput("result", new("Int32"), true));
        TriggerInputMapping[] mappings = [new("result", Expression(language))];

        var preflight = mapper.ValidateMappings(contract, mappings);
        var result = await mapper.MapAsync(contract, mappings, Json("{}"));

        Assert.False(preflight.IsValid);
        Assert.Equal("triggers.mapping.expression-unavailable", Assert.Single(preflight.Findings).ReasonCode);
        Assert.False(result.IsValid);
        Assert.Equal("triggers.mapping.expression-unavailable", Assert.Single(result.Findings).ReasonCode);
        Assert.Empty(result.NormalizedInputs);
        Assert.Equal(0, evaluator.Calls);
    }

    [Theory]
    [InlineData(false, "binding-pure-v1")]
    [InlineData(true, "unknown-profile")]
    [InlineData(true, "Binding-Pure-V1")]
    public async Task Missing_evaluator_or_non_pure_profile_is_unavailable_with_an_installed_language(bool evaluatorAvailable, string profile)
    {
        var evaluator = new RecordingEvaluator(_ => throw new InvalidOperationException("must not evaluate"));
        var mapper = Mapper(evaluatorAvailable ? evaluator : null, "JavaScript");
        var contract = Contract(new WorkflowDeclaredInput("result", new("Int32"), true));
        TriggerInputMapping[] mappings = [new("result", Expression("JavaScript", profile))];

        var preflight = mapper.ValidateMappings(contract, mappings);
        var result = await mapper.MapAsync(contract, mappings, Json("{}"));

        Assert.False(preflight.IsValid);
        Assert.Equal("triggers.mapping.expression-unavailable", Assert.Single(preflight.Findings).ReasonCode);
        Assert.False(result.IsValid);
        Assert.Equal("triggers.mapping.expression-unavailable", Assert.Single(result.Findings).ReasonCode);
        Assert.Equal(0, evaluator.Calls);
    }

    [Fact]
    public async Task Literal_mapping_survives_the_authoring_json_document_lifetime()
    {
        TriggerLiteralBinding literal;
        using (var document = JsonDocument.Parse("42")) literal = new(document.RootElement);
        var result = await Mapper().MapAsync(Contract(new WorkflowDeclaredInput("count", new("Int32"), true)), [new("count", literal)], Json("{}"));
        Assert.True(result.IsValid);
        Assert.Equal(42, result.NormalizedInputs["count"].GetInt32());
    }

    private static TriggerInputMapper Mapper(IPortableExpressionEvaluator? expressions = null, params string[] installedLanguages) =>
        new(new WorkflowExecutableInputValidator(new Types()), expressions, installedLanguages.Select(language => new InstalledLanguage(language)));
    private static TriggerExpressionBinding Expression(string language, string profile = ExpressionCapabilityProfiles.BindingPureV1) =>
        new(language, "42", new("Int32"), Json("{}"), profile, new Dictionary<string, TriggerValueBinding>());
    private static WorkflowExecutableInputContract Contract(params WorkflowDeclaredInput[] inputs) => new(1, inputs);
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private sealed class RecordingEvaluator(Func<ExpressionEvaluationRequest, JsonElement> evaluate) : IPortableExpressionEvaluator
    {
        public int Calls { get; private set; }
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request)
        {
            Calls++;
            return ValueTask.FromResult(evaluate(request));
        }
    }

    private sealed class InstalledLanguage(string language) : IPortableExpressionHandler
    {
        public string Language { get; } = language;
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) =>
            throw new InvalidOperationException("The mapper must evaluate through the composed evaluator.");
    }

    private sealed class Types : IWellKnownTypeRegistry
    {
        private readonly Dictionary<string, Type> values = new(StringComparer.Ordinal)
        { ["String"] = typeof(string), ["Int32"] = typeof(int), ["Int64"] = typeof(long), ["Decimal"] = typeof(decimal) };
        public void RegisterType(Type type, string alias) => values.Add(alias, type);
        public bool TryGetAlias(Type type, out string alias) { alias = values.FirstOrDefault(pair => pair.Value == type).Key!; return alias is not null; }
        public bool TryGetType(string alias, out Type type) => values.TryGetValue(alias, out type!);
        public IEnumerable<Type> ListTypes() => values.Values;
        public string GetAliasOrDefault(Type type) => values.FirstOrDefault(pair => pair.Value == type).Key ?? type.FullName!;
        public Type GetTypeOrDefault(string alias) => values.GetValueOrDefault(alias, typeof(object));
        public bool TryGetTypeOrDefault(string alias, out Type type) => TryGetType(alias, out type);
    }
}
