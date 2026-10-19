using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 247 User Story 2 (research R-2, R-3): Elsa 3 generated <c>get{Pascalize(variable)}()</c> per variable and
/// <c>get{input}()</c> per workflow input. A call converts to the same binding the explicit <c>getVariable</c> /
/// <c>getInput</c> call produces, exactly when Elsa 3 had that function.
/// </summary>
public sealed class Elsa3GeneratedAccessorTests
{
    [Fact]
    public async Task A_variable_accessor_converts_like_getVariable()
    {
        var accessor = await ConvertTextAsync("getHttpRequestBody().text", variables: [Variable("HttpRequestBody")]);
        var explicitRead = await ConvertTextAsync("getVariable('HttpRequestBody').text", variables: [Variable("HttpRequestBody")]);

        Assert.True(accessor.Succeeded, Describe(accessor.Diagnostics));
        var converted = ReadExpression(accessor);
        Assert.Equal("args.HttpRequestBody.text", converted.Source);
        Assert.Equal(ReadExpression(explicitRead).Parameters, converted.Parameters);
        Assert.IsType<VariableExpressionParameterBinding>(converted.Parameters["HttpRequestBody"]);
    }

    [Fact]
    public async Task A_variable_accessor_uses_the_pascal_cased_name()
    {
        var result = await ConvertTextAsync("getOrderId()", variables: [Variable("orderId")]);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.IsType<VariableExpressionParameterBinding>(ReadExpression(result).Parameters["OrderId"]);
    }

    [Theory]
    [InlineData("getorderId()")]
    [InlineData("getORDERID()")]
    public async Task A_name_elsa3_did_not_generate_is_refused(string expression)
    {
        var result = await ConvertTextAsync(expression, variables: [Variable("orderId")]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
    }

    [Fact]
    public async Task A_variable_whose_name_is_not_letters_and_digits_has_no_accessor()
    {
        var result = await ConvertTextAsync("getOrder_id()", variables: [Variable("order_id")]);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
    }

    [Fact]
    public async Task An_input_accessor_converts_like_getInput()
    {
        var accessor = await ConvertTextAsync("getCustomInput()", inputs: [WorkflowInput("CustomInput")]);
        var explicitRead = await ConvertTextAsync("getInput('CustomInput')", inputs: [WorkflowInput("CustomInput")]);

        Assert.True(accessor.Succeeded, Describe(accessor.Diagnostics));
        Assert.Equal(ReadExpression(explicitRead).Parameters, ReadExpression(accessor).Parameters);
        Assert.IsType<WorkflowRequestExpressionParameterBinding>(ReadExpression(accessor).Parameters["CustomInput"]);
    }

    [Fact]
    public async Task An_input_accessor_uses_the_input_name_verbatim()
    {
        var verbatim = await ConvertTextAsync("getcustomInput()", inputs: [WorkflowInput("customInput")]);
        var pascalCased = await ConvertTextAsync("getCustomInput()", inputs: [WorkflowInput("customInput")]);

        Assert.True(verbatim.Succeeded, Describe(verbatim.Diagnostics));
        Assert.Contains(pascalCased.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
    }

    [Fact]
    public async Task An_input_shadows_a_variable_with_the_identical_accessor_name()
    {
        var result = await ConvertTextAsync("getFoo()", variables: [Variable("Foo")], inputs: [WorkflowInput("Foo")]);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.IsType<WorkflowRequestExpressionParameterBinding>(ReadExpression(result).Parameters["Foo"]);
    }

    [Fact]
    public async Task An_activity_output_accessor_shadows_a_variable_and_is_refused()
    {
        var producer = Activity("producer", "Elsa.WriteLine", ("text", Literal("x")));
        producer.Name = "http";
        var root = Sequence("root", producer, Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", "getResultFromHttp()"))));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root, variables: [Variable("ResultFromHttp")]));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability && diagnostic.Message.Contains("activity output", StringComparison.Ordinal));
    }

    [Fact]
    public async Task In_a_reusable_conversion_an_input_is_reached_through_its_variable_accessor()
    {
        var definition = Definition("getCustomInput()", [], [WorkflowInput("customInput")]);

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition, isReusable: true);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.IsType<WorkflowRequestExpressionParameterBinding>(ReadExpression(result).Parameters["CustomInput"]);
    }

    [Fact]
    public async Task In_a_reusable_conversion_the_verbatim_input_accessor_does_not_exist()
    {
        var definition = Definition("getcustomInput()", [], [WorkflowInput("customInput")]);

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition, isReusable: true);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
    }

    [Fact]
    public async Task Two_declarations_with_one_accessor_name_are_ambiguous()
    {
        var result = await ConvertTextAsync("getOrder()", variables: [Variable("order"), Variable("Order")]);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference);
    }

    [Theory]
    [InlineData("getOrder().total = 1")]
    [InlineData("getOrder().count++")]
    [InlineData("setOrder({ total: 1 })")]
    public async Task Mutation_through_an_accessor_is_refused(string expression)
    {
        var result = await ConvertTextAsync(expression, variables: [Variable("Order")]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
    }

    [Fact]
    public async Task A_setter_message_points_to_a_set_variable_activity()
    {
        var result = await ConvertTextAsync("setOrder(1)", variables: [Variable("Order")]);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("generated setter", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_accessor_called_with_arguments_is_refused()
    {
        var result = await ConvertTextAsync("getOrder(1)", variables: [Variable("Order")]);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
    }

    [Fact]
    public async Task An_accessor_used_as_a_value_is_refused()
    {
        var result = await ConvertTextAsync("var read = getOrder; read()", variables: [Variable("Order")]);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.DynamicAccessor);
    }

    [Fact]
    public async Task A_function_the_script_declares_is_a_local_call()
    {
        var result = await ConvertTextAsync("(() => { function getOrder() { return 1; } return getOrder(); })()", variables: [Variable("Order")]);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Empty(ReadExpression(result).Parameters);
    }

    [Fact]
    public async Task A_converted_accessor_reads_the_value_elsa3_read()
    {
        var result = await ConvertTextAsync("return { text: getHttpRequestBody().text, empty: getHttpRequestBody().items.length == 0 }",
            variables: [Variable("HttpRequestBody")]);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var value = await PortableJavaScript.EvaluateAsync(ReadExpression(result), new Dictionary<string, JsonElement>
        {
            ["HttpRequestBody"] = JsonSerializer.SerializeToElement(new { text = "hi", items = Array.Empty<int>() }),
        });
        Assert.Equal("hi", value.GetProperty("text").GetString());
        Assert.True(value.GetProperty("empty").GetBoolean());
    }

    private static Elsa3Variable Variable(string name) => new() { Id = $"id-{name}", Name = name, TypeName = "Object" };

    private static Elsa3WorkflowArgumentDefinition WorkflowInput(string name) => new() { Name = name, Type = "Object" };

    private static Elsa3WorkflowDefinition Definition(string expression, Elsa3Variable[] variables, Elsa3WorkflowArgumentDefinition[] inputs)
    {
        var definition = ConversionServices.Definition(
            Sequence("root", Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", expression)))),
            variables: variables);
        definition.Inputs = inputs.ToList();
        return definition;
    }

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertTextAsync(
        string expression,
        Elsa3Variable[]? variables = null,
        Elsa3WorkflowArgumentDefinition[]? inputs = null) =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), Definition(expression, variables ?? [], inputs ?? []));

    private static ExpressionDefinition ReadExpression(Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition> result)
    {
        var argument = Input(Node(result.Value!.State, "node"), "text");
        return (argument.Value.Value is JsonElement element ? element : JsonSerializer.SerializeToElement(argument.Value.Value))
            .Deserialize<ExpressionDefinition>()!;
    }
}
