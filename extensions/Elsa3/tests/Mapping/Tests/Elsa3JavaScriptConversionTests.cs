using System.Text.Json;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 236 plan D-14 to D-16 (research R-11): no Elsa 3 JavaScript converts silently and then fails or returns null
/// in Elsa 4. What Elsa 4 cannot run is refused, statement-form expressions keep their completion value, and
/// RunJavaScript bodies are rewritten for Elsa 4's argument-only sandbox.
/// </summary>
public sealed class Elsa3JavaScriptConversionTests
{
    [Theory]
    [InlineData("new Date().getFullYear()")]
    [InlineData("DateTime.Now.ToString()")]
    [InlineData("Guid.NewGuid().ToString()")]
    [InlineData("_.join(['a'], ',')")]
    [InlineData("moment().format()")]
    [InlineData("args.value")]
    [InlineData("getVariable('o').name = 'x'")]
    [InlineData("getOrderId()")]
    [InlineData("DateTime.Now")]
    [InlineData("typeof moment")]
    [InlineData("[DateTime][0]")]
    [InlineData("function f(DateTime) { return DateTime; } DateTime.Now")]
    [InlineData("(() => { let moment = 1; return moment; })() + moment")]
    [InlineData("[1].map(Guid => Guid).concat([Guid])[0]")]
    [InlineData("(() => { function getOrderId() { return 1; } return getOrderId(); })() + getOrderId()")]
    [InlineData("(() => { try { return 1; } catch (DateTime) { return 2; } })() + DateTime.Now")]
    public async Task Constructs_elsa4_cannot_run_are_refused(string expression)
    {
        var result = await ConvertTextAsync(expression);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
    }

    [Theory]
    [InlineData("({ Date: 1 }).Date")]
    [InlineData("[1].map(_ => 2)[0]")]
    [InlineData("'a'.length")]
    [InlineData("var Date = 1; Date + 1")]
    [InlineData("[1].map(function (moment) { return moment; })[0]")]
    [InlineData("(({ DateTime }) => DateTime.Now)({ DateTime: { Now: 1 } })")]
    [InlineData("var _ = { map: 1 }; _.map")]
    [InlineData("(() => { try { return 1; } catch (Guid) { return Guid.message; } })()")]
    [InlineData("(() => { if (true) { var Date = 1; } return Date; })()")]
    [InlineData("(function Date() { return Date.name; })()")]
    [InlineData("(() => { const f = x => x; return f(1); })()")]
    [InlineData("(() => { function getOrderId() { return 1; } return getOrderId(); })()")]
    [InlineData("(() => { const variables = { a: 1 }; variables.a = 2; return variables.a; })()")]
    [InlineData("((args) => args.length)([1])")]
    public async Task Names_that_only_look_like_elsa3_globals_convert(string expression)
    {
        var result = await ConvertTextAsync(expression);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Theory]
    [InlineData("1 + 1;", "1 + 1")]
    [InlineData("'a' /* note */", "'a' /* note */")]
    [InlineData("'a' // note", "'a' // note\n")]
    public async Task One_expression_stays_one_expression(string expression, string expected)
    {
        var result = await ConvertTextAsync(expression);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(expected, Source(Input(Node(result.Value!.State, "node"), "text")));
    }

    [Fact]
    public async Task Statements_are_wrapped_and_return_their_completion_value()
    {
        var result = await ConvertTextAsync("var greeting = 'hi'; greeting + '!'");

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("(function () {\nvar greeting = 'hi'; return greeting + '!'\n})()", Source(Input(Node(result.Value!.State, "node"), "text")));
    }

    [Fact]
    public async Task Statements_that_end_without_an_expression_are_refused()
    {
        var result = await ConvertTextAsync("if (true) { 'a' } else { 'b' }");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.DynamicAccessor);
    }

    [Fact]
    public async Task A_script_reads_a_variable_through_the_variables_root_and_returns_its_completion_value()
    {
        // Spec 1222 FR-026a: the body reads variables over its session, so no arguments expression carries them.
        var script = Activity("node", "Elsa.RunJavaScript", ("script", Literal("var total = getVariable('total'); total + 1")));

        var result = await ConvertAsync(Sequence("root", script), new Elsa3Variable { Id = "total", Name = "total", TypeName = "Int32" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var node = Node(result.Value!.State, "node");
        Assert.Equal("var total = variables.total; return total + 1", Text(Input(node, "script")));
        Assert.DoesNotContain(node.Inputs, input => input.ReferenceKey == "arguments");
    }

    [Fact]
    public async Task A_script_reads_a_workflow_input_through_arguments()
    {
        // Inputs, activity results and metadata stay args (FR-035a).
        var definition = ConversionServices.Definition(Sequence("root",
            Activity("node", "Elsa.RunJavaScript", ("script", Literal("var name = getInput('Customer'); name")))));
        definition.Inputs = [new Elsa3WorkflowArgumentDefinition { Name = "Customer", Type = "String" }];

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var node = Node(result.Value!.State, "node");
        Assert.Equal("var name = args.Customer; return name", Text(Input(node, "script")));
        Assert.Equal("({ \"Customer\": args.Customer })", Source(Input(node, "arguments")));
    }

    /// <summary>
    /// #3287: the <c>arguments</c> expression declares the type of RunJavaScript's <c>arguments</c> input, because
    /// publication requires the two to match exactly. It declared <c>Object</c>, and every script that read anything
    /// was refused at publish.
    /// </summary>
    [Fact]
    public async Task The_arguments_expression_declares_the_arguments_input_type()
    {
        var definition = ConversionServices.Definition(Sequence("root",
            Activity("node", "Elsa.RunJavaScript", ("script", Literal("return getInput('greeting');")))));
        definition.Inputs = [new Elsa3WorkflowArgumentDefinition { Name = "greeting", Type = "String" }];

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var arguments = Assert.IsType<JsonElement>(Input(Node(result.Value!.State, "node"), "arguments").Value.Value);
        var resultType = arguments.GetProperty("resultType");
        Assert.False(resultType.TryGetProperty("Alias", out _));
        Assert.Equal("System.Text.Json.JsonElement", resultType.GetProperty("alias").GetString());
    }

    [Fact]
    public async Task A_script_without_reads_gets_no_arguments()
    {
        var result = await ConvertAsync(Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal("40 + 2")))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var node = Node(result.Value!.State, "node");
        Assert.Equal("return 40 + 2", Text(Input(node, "script")));
        Assert.DoesNotContain(node.Inputs, input => input.ReferenceKey == "arguments");
    }

    // setOutcome in a script now converts (spec 1208 FR-027); see Elsa3JavaScriptHostFunctionTests.
    [Theory]
    [InlineData("getOrderId() + 1")]
    public async Task A_script_calling_an_elsa3_host_function_is_refused(string body)
    {
        var result = await ConvertAsync(Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal(body)))));

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
    }

    [Fact]
    public async Task A_script_whose_captured_result_is_a_statement_completion_value_is_refused()
    {
        var script = Activity("node", "Elsa.RunJavaScript",
            ("script", Literal("if (true) { 'a' } else { 'b' }")),
            ("result", Captured("script-result")));
        var write = Activity("write", "Elsa.WriteLine", ("text", Captured("script-result")));

        var result = await ConvertAsync(Sequence("root", script, write));

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.DynamicAccessor);
    }

    [Fact]
    public async Task A_script_ending_with_a_statement_converts_when_nothing_reads_its_result()
    {
        var result = await ConvertAsync(Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal("if (true) { 'a' }")))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("if (true) { 'a' }", Text(Input(Node(result.Value!.State, "node"), "script")));
    }

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertTextAsync(string expression) =>
        ConvertAsync(Sequence("root", Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", expression)))),
            new Elsa3Variable { Id = "o", Name = "o", TypeName = "Object" });

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertAsync(Elsa3Activity root, params Elsa3Variable[] variables) =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root, variables: variables));
}
