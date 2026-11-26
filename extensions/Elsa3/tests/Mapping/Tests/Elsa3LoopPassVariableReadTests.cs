using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// #5873: Elsa 3 <c>For</c> and <c>ForEach</c> declare per-pass variables on their body (<c>ForEach</c>: <c>CurrentValue</c>
/// and <c>CurrentIndex</c>; <c>For</c>: <c>CurrentValue</c>), which a body script reads by name ahead of any workflow
/// variable of that name. The nearest enclosing loop that declares the name owns the read, which converts to that loop's
/// Elsa 4 variable pinned to the loop node; only a variable declared inside the body comes first. A computed name inside
/// such a body is refused.
/// </summary>
public sealed class Elsa3LoopPassVariableReadTests
{
    private static readonly Elsa3Variable WorkflowCurrentValue = new() { Id = "acme-current-value", Name = "CurrentValue", TypeName = "String" };

    [Theory]
    [InlineData("getVariable('CurrentValue')")]
    [InlineData("getCurrentValue()")]
    [InlineData("variables.CurrentValue")]
    public async Task A_foreach_body_read_wins_over_a_workflow_variable_of_the_same_name(string read)
    {
        var result = await ConvertAsync(Sequence("root", ForEach("loop", Read(read))), WorkflowCurrentValue);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        AssertReadsLoop(result, "loop", "currentItem");
    }

    [Theory]
    [InlineData("Elsa.ForEach", "CurrentValue", "currentItem")]
    [InlineData("Elsa.ForEach", "CurrentIndex", "currentIndex")]
    [InlineData("Elsa.For", "CurrentValue", "index")]
    public async Task A_body_read_converts_when_no_variable_of_that_name_is_declared(string loopType, string name, string loopVariable)
    {
        var loop = loopType == "Elsa.For" ? For("loop", Read($"getVariable('{name}')")) : ForEach("loop", Read($"getVariable('{name}')"));

        var result = await ConvertAsync(Sequence("root", loop));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        AssertReadsLoop(result, "loop", loopVariable);
    }

    [Fact]
    public async Task A_for_does_not_declare_current_index()
    {
        // Elsa 3 For declares CurrentValue only: CurrentIndex reaches the workflow variable.
        var index = new Elsa3Variable { Id = "acme-index", Name = "CurrentIndex", TypeName = "Int32" };

        var result = await ConvertAsync(Sequence("root", For("loop", Read("getVariable('CurrentIndex')"))), index);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        AssertReads(result, VariableReference.WorkflowScopeId, "acme-index");
    }

    [Theory]
    [InlineData("CurrentValue", "inner", "index")]
    [InlineData("CurrentIndex", "outer", "currentIndex")]
    public async Task In_nested_loops_the_nearest_loop_declaring_the_name_owns_the_read(string name, string owner, string loopVariable)
    {
        var outer = ForEach("outer", For("inner", Read($"getVariable('{name}')")));

        var result = await ConvertAsync(Sequence("root", outer), WorkflowCurrentValue);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        AssertReadsLoop(result, owner, loopVariable);
    }

    [Fact]
    public async Task An_inner_foreach_owns_current_value_inside_an_outer_for()
    {
        var outer = For("outer", ForEach("inner", Read("getVariable('CurrentValue')")));

        var result = await ConvertAsync(Sequence("root", outer));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        AssertReadsLoop(result, "inner", "currentItem");
    }

    [Fact]
    public async Task A_variable_declared_inside_the_body_takes_precedence()
    {
        var body = Sequence("body", Read("getVariable('CurrentValue')"));
        body.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "acme-body-value", name = "CurrentValue", typeName = "String" } });

        var result = await ConvertAsync(Sequence("root", ForEach("loop", body)));

        // Today's handling of a body variable applies; the read never binds to the loop.
        Assert.DoesNotContain(result.Value?.State is null ? [] : ReadExpression(result).Parameters.Values, parameter =>
            parameter is VariableExpressionParameterBinding { DeclaringScopeNodeId: "loop" });
    }

    [Fact]
    public async Task A_body_variable_named_like_the_elsa4_loop_variable_is_refused()
    {
        // Elsa 4 resolves 'currentItem' nearest scope first, so the body's own variable would be read instead.
        var body = Sequence("body", Read("getVariable('CurrentValue')"));
        body.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "acme-item", name = "currentItem", typeName = "String" } });

        var result = await ConvertAsync(Sequence("root", ForEach("loop", body)));

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ResolutionDiffers);
    }

    [Fact]
    public async Task A_read_in_the_loops_own_inputs_is_not_owned_by_the_loop()
    {
        var loop = Activity("loop", "Elsa.ForEach", ("items", Expression("JavaScript", "[getVariable('CurrentValue')]", "Object")));
        loop.AdditionalProperties["body"] = Port(Activity("node", "Elsa.WriteLine", ("text", Literal("acme"))));

        var result = await ConvertAsync(Sequence("root", loop), WorkflowCurrentValue);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var items = ReadExpression(result, "loop", "Collection");
        var parameter = Assert.IsType<VariableExpressionParameterBinding>(Assert.Single(items.Parameters).Value);
        Assert.Equal((VariableReference.WorkflowScopeId, "acme-current-value"), (parameter.DeclaringScopeNodeId, parameter.VariableKey));
    }

    [Theory]
    [InlineData("getVariable(['Current', 'Value'].join(''))")]
    [InlineData("variables[['Current', 'Index'].join('')]")]
    public async Task A_computed_name_in_a_loop_body_is_refused(string read)
    {
        var result = await ConvertAsync(Sequence("root", ForEach("loop", Read(read))), WorkflowCurrentValue);

        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.ValueOutsideContract);
        Assert.Contains("literal", refusal.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_computed_name_in_a_run_javascript_body_inside_a_loop_is_refused()
    {
        var script = Activity("node", "Elsa.RunJavaScript", ("script", Literal("var name = 'Current' + 'Value'; return getVariable(name);")));

        var result = await ConvertAsync(Sequence("root", For("loop", script)));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.ValueOutsideContract);
    }

    [Theory]
    [InlineData("setVariable('CurrentValue', 'acme-x'); return 1;")]
    [InlineData("setCurrentValue('acme-x'); return 1;")]
    public async Task A_write_to_a_per_pass_variable_is_refused(string source)
    {
        // Elsa 3 wrote the pass's own variable; Elsa 4 loop values are read-only. Never the workflow variable instead.
        var script = Activity("node", "Elsa.RunJavaScript", ("script", Literal(source)));

        var result = await ConvertAsync(Sequence("root", ForEach("loop", script)), WorkflowCurrentValue);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability && diagnostic.Message.Contains("loop", StringComparison.Ordinal));
    }

    private static Elsa3Activity Read(string read) =>
        Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", read)));

    private static Elsa3Activity ForEach(string id, Elsa3Activity body)
    {
        var loop = Activity(id, "Elsa.ForEach", ("items", Literal("[\"acme-a\",\"acme-b\"]", "Object")));
        loop.AdditionalProperties["body"] = Port(body);
        return loop;
    }

    private static Elsa3Activity For(string id, Elsa3Activity body)
    {
        var loop = Activity(id, "Elsa.For", ("start", Literal("0", "Int32")), ("end", Literal("1", "Int32")));
        loop.AdditionalProperties["body"] = Port(body);
        return loop;
    }

    private static void AssertReadsLoop(Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition> result, string loopNodeId, string loopVariable)
    {
        AssertReads(result, loopNodeId, loopVariable);
        Assert.Contains($"variables.{loopVariable}", ReadExpression(result).Source, StringComparison.Ordinal);
    }

    private static void AssertReads(Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition> result, string scopeNodeId, string variableKey)
    {
        var parameter = Assert.IsType<VariableExpressionParameterBinding>(Assert.Single(ReadExpression(result).Parameters).Value);
        Assert.Equal((scopeNodeId, variableKey), (parameter.DeclaringScopeNodeId, parameter.VariableKey));
    }

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertAsync(Elsa3Activity root, params Elsa3Variable[] variables) =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root, variables: variables));

    private static ExpressionDefinition ReadExpression(Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition> result, string node = "node", string input = "text")
    {
        var argument = Input(Node(result.Value!.State, node), input);
        return (argument.Value.Value is JsonElement element ? element : JsonSerializer.SerializeToElement(argument.Value.Value))
            .Deserialize<ExpressionDefinition>(Elsa.Serialization.Core.JsonBaseline.Options)!;
    }
}
