using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 265 (lifts spec 246 D-8): an Elsa 4 graph activity body is a closed scope, so its request inputs are the
/// activity's own inputs and its root variables are the activity's own variables. A reusable body that reads them
/// converts exactly as a workflow does.
/// </summary>
public sealed class Elsa3ReusableValueReadTests
{
    [Theory]
    [InlineData("getInput(\"message\")")]
    // Elsa 3's generated input accessor (spec 247) resolves to the same read.
    [InlineData("getMessage()")]
    public async Task A_reusable_body_reading_an_input_converts_to_a_request_read(string script)
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), Definition(script, reusable: true), isReusable: true);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var binding = Assert.IsType<WorkflowRequestExpressionParameterBinding>(Assert.Single(ReadExpression(result).Parameters).Value);
        Assert.Equal("message", binding.MemberKey);
    }

    [Theory]
    [InlineData("getVariable(\"total\")")]
    [InlineData("getTotal()")]
    public async Task A_reusable_body_reading_a_variable_converts_to_a_root_variable_read(string script)
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), Definition(script, reusable: true), isReusable: true);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var binding = Assert.IsType<VariableExpressionParameterBinding>(Assert.Single(ReadExpression(result).Parameters).Value);
        Assert.Equal("variable-total", binding.VariableKey);
        Assert.Equal(VariableReference.WorkflowScopeId, binding.DeclaringScopeNodeId);
    }

    [Fact]
    public async Task A_reusable_body_reading_a_memory_reference_converts_to_a_root_variable_read()
    {
        var definition = Definition("'x'", reusable: true);
        SourceNode(definition, "write").AdditionalProperties["text"] = Captured("variable-total", "String");

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition, isReusable: true);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var argument = Input(Node(result.Value!.State, "write"), "text");
        Assert.Equal("Variable", argument.Value.ExpressionType);
        var reference = ToElement(argument.Value.Value);
        Assert.Equal("variable-total", reference.GetProperty("referenceKey").GetString());
        Assert.Equal(VariableReference.WorkflowScopeId, reference.GetProperty("declaringScopeId").GetString());
    }

    [Fact]
    public async Task A_reusable_body_reading_an_input_through_getVariable_stays_refused_as_an_undeclared_variable()
    {
        // getVariable reads declared variables only, as in every workflow; an input is reached through getInput or its
        // generated accessor (spec 265 T057).
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), Definition("getVariable(\"message\")", reusable: true), isReusable: true);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.DanglingReference);
    }

    [Theory]
    [InlineData("getInput(\"message\")")]
    [InlineData("getVariable(\"total\")")]
    public async Task A_non_reusable_workflow_keeps_reading_inputs_and_variables(string script)
    {
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), Definition(script, reusable: false), isReusable: false);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    private static ExpressionDefinition ReadExpression(Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition> result) =>
        ToElement(Input(Node(result.Value!.State, "write"), "text").Value.Value).Deserialize<ExpressionDefinition>()!;

    private static JsonElement ToElement(object? value) =>
        value is JsonElement element ? element : JsonSerializer.SerializeToElement(value);

    private static Elsa3WorkflowDefinition Definition(string script, bool reusable)
    {
        var definition = ConversionServices.Definition(
            Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Expression("JavaScript", script)))),
            variables: [new Elsa3Variable { Id = "variable-total", Name = "total", TypeName = "String", Value = "0" }]);
        definition.Inputs = [new Elsa3WorkflowArgumentDefinition { Name = "message", Type = "String" }];
        if (reusable)
            definition.Options = new Elsa3WorkflowOptions { UsableAsActivity = true };
        return definition;
    }

    private static Elsa3Activity SourceNode(Elsa3WorkflowDefinition definition, string nodeId) =>
        definition.Root!.Activities!.Single(activity => activity.NodeId == nodeId);
}
