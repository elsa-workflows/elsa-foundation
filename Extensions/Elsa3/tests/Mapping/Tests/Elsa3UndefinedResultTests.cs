using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 261 User Story 1 (research R-1, R-2): Elsa 3 turned an <c>undefined</c> result into <c>null</c>, while the Elsa 4
/// binding evaluator refuses it. Converted expressions end every value that leaves them with <c>?? null</c>; they are run
/// in the Elsa 4 evaluator and compared with the value Elsa 3 returned.
/// </summary>
public sealed class Elsa3UndefinedResultTests
{
    [Theory]
    [InlineData("({}).discount", "null")]
    [InlineData("getVariable('o').discount", "null")]
    [InlineData("(function () {})()", "null")]
    [InlineData("function f() {}\nf()", "null")]
    [InlineData("void 0", "null")]
    [InlineData("({}).a || ({}).b", "null")]
    [InlineData("0 || 'x'", "\"x\"")]
    [InlineData("(1, ({}).a)", "null")]
    [InlineData("1, 2", "2")]
    [InlineData("null", "null")]
    [InlineData("0", "0")]
    [InlineData("''", "\"\"")]
    [InlineData("false", "false")]
    [InlineData("({ a: 1 })", "{\"a\":1}")]
    [InlineData("[1, 2]", "[1,2]")]
    [InlineData("if (true) { return ({}).a }\nreturn 1", "null")]
    [InlineData("var x = ({}).a;\nx", "null")]
    [InlineData("return 'a' // trailing comment", "\"a\"")]
    [InlineData("({}).a // trailing comment", "null")]
    public async Task An_expression_gives_what_elsa3_gave(string expression, string expected)
    {
        var result = await ConvertTextAsync(expression);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        // Every declared parameter (here only the variable o) reads an empty object.
        var definition = ReadExpression(Input(Node(result.Value!.State, "node"), "text"));
        var value = await PortableJavaScript.EvaluateAsync(definition,
            definition.Parameters.Keys.ToDictionary(name => name, _ => JsonSerializer.SerializeToElement(new { })));
        Assert.Equal(expected, value.GetRawText());
    }

    [Fact]
    public async Task A_read_replaced_at_the_start_of_the_expression_stays_inside_the_parentheses()
    {
        var result = await ConvertTextAsync("getVariable('o').x");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("(args.o.x) ?? null", Source(Input(Node(result.Value!.State, "node"), "text")));
    }

    [Fact]
    public async Task A_statement_form_returns_its_completion_value_or_null()
    {
        var result = await ConvertTextAsync("var greeting = 'hi'; greeting + '!'");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("(function () {\nvar greeting = 'hi'; return (greeting + '!') ?? null\n})()", Source(Input(Node(result.Value!.State, "node"), "text")));
    }

    [Fact]
    public async Task A_return_inside_a_nested_function_is_unchanged()
    {
        var result = await ConvertTextAsync("[1, 2].map(function (x) { return x * 2 })[1]");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("([1, 2].map(function (x) { return x * 2 })[1]) ?? null", Source(Input(Node(result.Value!.State, "node"), "text")));
    }

    [Fact]
    public async Task A_script_gets_no_null_coalescing()
    {
        var result = await ConvertAsync(Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal("if (false) { return 1 }\n40 + 2")))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("if (false) { return 1 }\nreturn 40 + 2", Text(Input(Node(result.Value!.State, "node"), "script")));
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
