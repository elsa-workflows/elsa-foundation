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
/// Spec 247 User Story 1 (research R-1): Elsa 3 parsed JavaScript with return allowed outside a function, so a top-level
/// <c>return</c> is the result. The converted expression returns the same value on every path; converted sources are
/// run in the Elsa 4 evaluator and compared with the value Elsa 3 returned.
/// </summary>
public sealed class Elsa3TopLevelReturnTests
{
    [Theory]
    [InlineData("return false", "false")]
    [InlineData("return { a: 1 }", "{\"a\":1}")]
    [InlineData("return;", "null")]
    [InlineData("if (true) { return }\nreturn 1", "null")]
    [InlineData("var x = 2;\nif (x > 1) { return 'big' }\nreturn 'small'", "\"big\"")]
    [InlineData("var x = 0;\nif (x > 1) { return 'big' }\nreturn 'small'", "\"small\"")]
    [InlineData("if (false) { return 1 }\n40 + 2", "42")]
    [InlineData("for (var i = 0; i < 3; i++) { if (i == 2) return i }\nreturn -1", "2")]
    [InlineData("return 'a' // trailing comment", "\"a\"")]
    public async Task An_expression_returns_what_elsa3_returned(string expression, string expected)
    {
        var result = await ConvertTextAsync(expression);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var value = await PortableJavaScript.EvaluateAsync(ReadExpression(Input(Node(result.Value!.State, "node"), "text")));
        Assert.Equal(expected, value.GetRawText());
    }

    [Fact]
    public async Task An_expression_is_wrapped_in_a_function_that_returns()
    {
        var result = await ConvertTextAsync("if (false) { return 1 }\n40 + 2");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("(function () {\nif (false) { return 1 }\nreturn 40 + 2\n})()", Source(Input(Node(result.Value!.State, "node"), "text")));
    }

    [Fact]
    public async Task A_condition_with_a_top_level_return_converts()
    {
        var condition = Activity("if", "Elsa.If", ("condition", Expression("JavaScript", "return false", "Boolean")));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(Sequence("root", condition)));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var value = await PortableJavaScript.EvaluateAsync(ReadExpression(Input(Node(result.Value!.State, "if"), "Condition")));
        Assert.False(value.GetBoolean());
    }

    [Fact]
    public async Task Reads_inside_a_returning_expression_still_become_arguments()
    {
        var result = await ConvertTextAsync("return { name: getVariable('o').name }");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var expression = ReadExpression(Input(Node(result.Value!.State, "node"), "text"));
        var value = await PortableJavaScript.EvaluateAsync(expression, new Dictionary<string, JsonElement>
        {
            ["o"] = JsonSerializer.SerializeToElement(new { name = "Ada" }),
        });
        Assert.Equal("Ada", value.GetProperty("name").GetString());
        Assert.IsType<VariableExpressionParameterBinding>(expression.Parameters["o"]);
    }

    [Theory]
    [InlineData("if (true) { return 1 } else { 'b' }")]
    [InlineData("if (getVariable('o')) { return 1 }")]
    public async Task An_expression_ending_with_another_statement_is_refused(string expression)
    {
        var result = await ConvertTextAsync(expression);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.DynamicAccessor);
    }

    [Fact]
    public async Task A_return_inside_a_nested_function_keeps_today_s_conversion()
    {
        var result = await ConvertTextAsync("[1, 2].map(function (x) { return x * 2 })[1]");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("[1, 2].map(function (x) { return x * 2 })[1]", Source(Input(Node(result.Value!.State, "node"), "text")));
    }

    [Fact]
    public async Task A_script_with_a_top_level_return_keeps_its_return()
    {
        var script = Activity("node", "Elsa.RunJavaScript",
            ("script", Literal("var total = getVariable('total');\nif (total > 1) { return 'big' }\nreturn 'small'")),
            ("result", Captured("script-result")));
        var write = Activity("write", "Elsa.WriteLine", ("text", Captured("script-result")));

        var result = await ConvertAsync(Sequence("root", script, write), new Elsa3Variable { Id = "total", Name = "total", TypeName = "Int32" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("var total = args.total;\nif (total > 1) { return 'big' }\nreturn 'small'", Text(Input(Node(result.Value!.State, "node"), "script")));
    }

    [Fact]
    public async Task A_script_with_a_top_level_return_and_a_trailing_expression_returns_it()
    {
        var result = await ConvertAsync(Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal("if (false) { return 1 }\n40 + 2")))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("if (false) { return 1 }\nreturn 40 + 2", Text(Input(Node(result.Value!.State, "node"), "script")));
    }

    [Fact]
    public async Task A_script_whose_read_result_ends_with_another_statement_is_refused()
    {
        var script = Activity("node", "Elsa.RunJavaScript",
            ("script", Literal("if (true) { return 1 } else { 'b' }")),
            ("result", Captured("script-result")));
        var write = Activity("write", "Elsa.WriteLine", ("text", Captured("script-result")));

        var result = await ConvertAsync(Sequence("root", script, write));

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.DynamicAccessor);
    }

    private static ExpressionDefinition ReadExpression(ArgumentState argument) =>
        (argument.Value.Value is JsonElement element ? element : JsonSerializer.SerializeToElement(argument.Value.Value))
        .Deserialize<ExpressionDefinition>()!;

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertTextAsync(string expression) =>
        ConvertAsync(Sequence("root", Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", expression)))),
            new Elsa3Variable { Id = "o", Name = "o", TypeName = "Object" });

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertAsync(Elsa3Activity root, params Elsa3Variable[] variables) =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root, variables: variables));
}
