using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class TriggerInputMapperTests
{
    [Fact]
    public async Task Model_targets_keep_declared_defaults_without_native_optional_completion()
    {
        var mapper = Mapper();
        var contract = Contract(new WorkflowDeclaredInput("optional", new("String"), false),
            new WorkflowDeclaredInput("required", new("Int32"), true, Json("7")));
        var model = mapper.ValidateMappings(contract, [], WorkflowValuePolicies.Off, TriggerInputSemantics.ModelOwned);
        Assert.True(model.IsValid);
        var mapped = await mapper.MapAsync(contract, [], Json("{}"), WorkflowValuePolicies.Off, semantics: TriggerInputSemantics.ModelOwned);
        Assert.True(mapped.IsValid);
        Assert.Equal(7, mapped.NormalizedInputs["required"].GetInt32());
        Assert.False(mapped.NormalizedInputs.ContainsKey("optional"));
        Assert.False(mapper.ValidateMappings(contract, [], WorkflowValuePolicies.Off).IsValid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Literal_preflight_and_source_mapping_use_the_selected_target_policy(bool enabled)
    {
        var mapper = Mapper();
        var contract = Contract(new WorkflowDeclaredInput("count", new("Int32"), true));
        var policies = new WorkflowValuePolicies(DefaultOnNull: enabled);
        TriggerInputMapping[] literal = [new("count", new TriggerLiteralBinding(Json("null")))];
        var preflight = mapper.ValidateMappings(contract, literal, policies);
        var mappedLiteral = await mapper.MapAsync(contract, literal, Json("{}"), policies);
        var mappedSource = await mapper.MapAsync(contract,
            [new("count", new TriggerSourceBinding(["count"]))], Json("{\"count\":null}"), policies);
        foreach (var result in new[] { preflight, mappedLiteral, mappedSource })
        {
            Assert.Equal(enabled, result.IsValid);
            if (enabled) Assert.Equal(0, result.NormalizedInputs["count"].GetInt32());
            else Assert.Equal(WorkflowExecutableInputValidationReasonCodes.NullNotAllowed, Assert.Single(result.Findings).ReasonCode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Trigger_owned_undefined_is_refused_independently_of_target_policies(bool enabled)
    {
        var evaluator = new RecordingEvaluator(request =>
        {
            Assert.False(request.UndefinedAsNull);
            return default;
        });
        var expression = new TriggerExpressionBinding("JavaScript", "undefined", new("String"), Json("{}"),
            ExpressionCapabilityProfiles.BindingReadonlyV1, new Dictionary<string, TriggerValueBinding>());
        var result = await Mapper(evaluator, "JavaScript").MapAsync(
            Contract(new WorkflowDeclaredInput("value", new("String"), false, isNullable: true)),
            [new("value", expression)], Json("{}"), new WorkflowValuePolicies(enabled, enabled));
        Assert.False(result.IsValid);
        Assert.Equal("triggers.mapping.expression-undefined", Assert.Single(result.Findings).ReasonCode);
    }
}
