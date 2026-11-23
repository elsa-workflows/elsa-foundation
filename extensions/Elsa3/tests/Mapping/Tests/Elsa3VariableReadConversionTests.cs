using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 1222 User Story 3 (T024, T025, T027, T031): an Elsa 3 script reads the same variables in Elsa 4. A literal read
/// becomes <c>variables.X</c>, spelled as declared; a computed read keeps its form and is served by the run-time lookup;
/// a script whose own <c>args</c> or <c>variables</c> would capture a converted read is refused (#3296); and a
/// RunJavaScript body keeps reading through <c>args</c> until it has its <c>variables</c> root (FR-026a).
/// </summary>
public sealed class Elsa3VariableReadConversionTests
{
    private static readonly Elsa3Variable Order = new() { Id = "order-id", Name = "Order", TypeName = "Object" };

    [Theory]
    [InlineData("getVariable('Order').total")]
    [InlineData("getVariable('order').total")]
    [InlineData("variables.Order.total")]
    [InlineData("variables['Order'].total")]
    [InlineData("getOrder().total")]
    public async Task A_literal_read_becomes_variables_spelled_as_declared(string source)
    {
        var result = await ConvertTextAsync(source, Order);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var expression = ReadExpression(result);
        Assert.Equal("(variables.Order.total) ?? null", expression.Source);
        var parameter = Assert.IsType<VariableExpressionParameterBinding>(Assert.Single(expression.Parameters).Value);
        Assert.Equal("variables.Order", expression.Parameters.Keys.Single());
        Assert.Equal((VariableReference.WorkflowScopeId, "order-id", ExpressionParameterSurfaces.Variables), (parameter.DeclaringScopeNodeId, parameter.VariableKey, parameter.Surface));
    }

    [Fact]
    public async Task Inputs_still_lower_to_args_beside_variables()
    {
        var definition = ConversionServices.Definition(
            Sequence("root", Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", "getInput('customer') + getVariable('Order')")))),
            variables: [Order]);
        definition.Inputs = [new Elsa3WorkflowArgumentDefinition { Name = "customer", Type = "String" }];

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("(args.customer + variables.Order) ?? null", ReadExpression(result).Source);
    }

    [Theory]
    [InlineData("getVariable(name)")]
    [InlineData("variables[name]")]
    [InlineData("Object.keys(variables).length")]
    public async Task A_computed_read_keeps_its_form_and_is_not_refused(string read)
    {
        // FR-027: served by the Elsa 4 run-time lookup.
        var result = await ConvertTextAsync($"(() => {{ const name = 'Order'; return {read}; }})()", Order);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Contains(read, ReadExpression(result).Source, StringComparison.Ordinal);
        Assert.Empty(ReadExpression(result).Parameters);
    }

    [Fact]
    public async Task A_computed_read_and_its_value_match_what_elsa3_read()
    {
        // SC-003: the converted computed read reads the same value through the lookup.
        var result = await ConvertTextAsync("(() => { const name = 'Ord' + 'er'; return getVariable(name).total; })()", Order);
        var expression = ReadExpression(result);

        var value = await PortableJavaScript.EvaluateAsync(expression, reader: new Reader(new() { ["Order"] = JsonSerializer.SerializeToElement(new { total = 3 }) }));

        Assert.Equal(3, value.GetInt32());
    }

    [Theory]
    [InlineData("((args) => getInput('customer') + args)(1)")]
    [InlineData("(() => { const args = 1; return getInput('customer'); })()")]
    public async Task A_script_declaring_its_own_args_around_a_converted_input_read_is_refused(string source)
    {
        // #3296: the lowered args.customer would read the script's own args.
        var definition = ConversionServices.Definition(Sequence("root", Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", source)))));
        definition.Inputs = [new Elsa3WorkflowArgumentDefinition { Name = "customer", Type = "String" }];

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference && diagnostic.Message.Contains("'args'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_script_declaring_its_own_variables_around_a_converted_read_is_refused()
    {
        var result = await ConvertTextAsync("((variables) => getVariable('Order'))(1)", Order);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference && diagnostic.Message.Contains("'variables'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Args_declared_away_from_the_converted_reads_converts()
    {
        var result = await ConvertTextAsync("((args) => args.length)([1]) + getVariable('Order').total", Order);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Fact]
    public async Task A_read_whose_nearest_declaration_differs_in_elsa4_is_refused()
    {
        // FR-031: the enclosing container also declares Order, which Elsa 4 would read instead of the workflow variable.
        var container = Sequence("container", Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", "getVariable('Order').total"))));
        container.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "local-order", name = "Order", typeName = "Object" } });

        var result = await ConvertAsync(Sequence("root", container), Order);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ResolutionDiffers);
    }

    [Fact]
    public async Task A_workflow_variable_named_like_an_enclosing_loops_value_is_refused()
    {
        // Elsa 4 would read the For's own index, nearest scope first.
        var index = new Elsa3Variable { Id = "index-id", Name = "index", TypeName = "Int32" };
        var loop = Activity("loop", "Elsa.For", ("start", Literal("0", "Int32")), ("end", Literal("1", "Int32")));
        loop.AdditionalProperties["body"] = Port(Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", "getVariable('index')"))));

        var result = await ConvertAsync(Sequence("root", loop), index);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ResolutionDiffers);
    }

    [Fact]
    public async Task An_outer_loops_value_read_inside_a_nested_loop_of_the_same_kind_is_refused()
    {
        // Elsa 4 would read the inner ForEach's currentItem, nearest scope first.
        var inner = Activity("inner", "Elsa.ForEach", ("items", Literal("[1]", "Object")));
        inner.AdditionalProperties["body"] = Port(Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", "getVariable('item')"))));
        var outer = Activity("outer", "Elsa.ForEach", ("items", Literal("[1]", "Object")), ("currentValue", Captured("item")));
        outer.AdditionalProperties["body"] = Port(inner);

        var result = await ConvertAsync(Sequence("root", outer), new Elsa3Variable { Id = "item", Name = "item", TypeName = "Object" });

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ResolutionDiffers);
    }

    [Fact]
    public async Task A_computed_read_that_could_reach_a_loop_held_variable_is_refused()
    {
        // Elsa 4 never writes the Elsa 3 variable that held the loop's current value, so the lookup would read null.
        var loop = Activity("loop", "Elsa.ForEach", ("items", Literal("[1]", "Object")), ("currentValue", Captured("item")));
        loop.AdditionalProperties["body"] = Port(Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", "getVariable(['it', 'em'].join(''))"))));

        var result = await ConvertAsync(Sequence("root", loop), new Elsa3Variable { Id = "item", Name = "item", TypeName = "Object" });

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.ResolutionDiffers && diagnostic.Message.Contains("'item'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_computed_read_inside_a_loop_warns_that_it_can_reach_the_loops_values()
    {
        var loop = Activity("loop", "Elsa.For", ("start", Literal("0", "Int32")), ("end", Literal("1", "Int32")));
        loop.AdditionalProperties["body"] = Port(Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", "getVariable(['Ord', 'er'].join(''))"))));

        var result = await ConvertAsync(Sequence("root", loop), Order);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.ShadowedCandidate && diagnostic.Message.Contains("'index'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_computed_read_with_a_shadowed_candidate_converts_with_a_warning()
    {
        var container = Sequence("container", Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", "getVariable(['Ord', 'er'].join(''))"))));
        container.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "local-order", name = "Order", typeName = "Object" } });

        var result = await ConvertAsync(Sequence("root", container), Order);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var warning = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ShadowedCandidate);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("'Order'", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Liquid_reads_variables_through_the_variables_root()
    {
        var result = await ConvertAsync(
            Sequence("root", Activity("node", "Elsa.WriteLine", ("text", Expression("Liquid", "{{ Variables.order.total }}")))),
            Order);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var expression = ReadExpression(result);
        Assert.Equal("{{ variables.Order.total }}", expression.Source);
        Assert.Equal(["variables.Order"], expression.Parameters.Keys);
    }

    [Fact]
    public async Task A_liquid_loop_variable_named_variables_is_refused()
    {
        var result = await ConvertAsync(
            Sequence("root", Activity("node", "Elsa.WriteLine", ("text", Expression("Liquid", "{% for variables in Variables.Order %}{{ Variables.Order }}{% endfor %}")))),
            Order);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference);
    }

    [Fact]
    public async Task A_liquid_input_named_variables_is_refused()
    {
        var definition = ConversionServices.Definition(Sequence("root", Activity("node", "Elsa.WriteLine", ("text", Expression("Liquid", "{{ Input.variables }}")))));
        definition.Inputs = [new Elsa3WorkflowArgumentDefinition { Name = "variables", Type = "String" }];

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference);
    }

    [Fact]
    public async Task A_run_javascript_body_reads_variables_through_the_variables_root()
    {
        // FR-026a, FR-033: the body reads variables over its session, so its reads become variables.X and no arguments
        // expression carries them.
        var script = Activity("node", "Elsa.RunJavaScript", ("script", Literal("var total = getVariable('Order').total; total + 1")));

        var result = await ConvertAsync(Sequence("root", script), Order);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var node = Node(result.Value!.State, "node");
        Assert.Equal("var total = variables.Order.total; return total + 1", Text(Input(node, "script")));
        Assert.DoesNotContain(node.Inputs, input => input.ReferenceKey == "arguments");
    }

    [Fact]
    public async Task A_run_javascript_body_reads_its_loop_value_through_args_as_a_copy()
    {
        // Elsa 4 freezes a loop's item in a body (spec 1231 FR-006); an Elsa 3 body that changes its item keeps working on
        // a copy, as it did before the body had a variables root.
        var loop = Activity("loop", "Elsa.ForEach", ("items", Literal("[{\"a\":1}]", "Object")), ("currentValue", Captured("item")));
        loop.AdditionalProperties["body"] = Port(Activity("script", "Elsa.RunJavaScript", ("script", Literal("var o = getVariable('item'); o.done = true; o"))));

        var result = await ConvertAsync(Sequence("root", loop), new Elsa3Variable { Id = "item", Name = "item", TypeName = "Object" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var node = Node(result.Value!.State, "script");
        var script = Text(Input(node, "script"))!;
        Assert.Contains("args.item", script, StringComparison.Ordinal);
        Assert.DoesNotContain("variables.", script, StringComparison.Ordinal);
        Assert.Contains("args.item", Source(Input(node, "arguments")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_workflow_variable_named_like_a_loop_value_is_read_through_the_variables_root()
    {
        // Only a loop's own item or index stays args; a workflow variable named index keeps read-your-writes.
        var index = new Elsa3Variable { Id = "index", Name = "index", TypeName = "Int32" };
        var script = Activity("node", "Elsa.RunJavaScript", ("script", Literal("getVariable('index') + 1")));

        var result = await ConvertAsync(Sequence("root", script), index);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("return variables.index + 1", Text(Input(Node(result.Value!.State, "node"), "script")));
    }

    [Fact]
    public async Task A_run_javascript_body_keeps_a_computed_read()
    {
        // FR-027: the body serves a computed read through its session's run-time lookup.
        var script = Activity("node", "Elsa.RunJavaScript", ("script", Literal("const name = 'Order'; return getVariable(name);")));

        var result = await ConvertAsync(Sequence("root", script), Order);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("const name = 'Order'; return getVariable(name);", Text(Input(Node(result.Value!.State, "node"), "script")));
    }

    [Fact]
    public async Task A_run_javascript_statement_body_is_not_wrapped_in_a_function()
    {
        // FR-030, #3010: the body path returns its completion value without an immediately invoked function.
        var script = Activity("node", "Elsa.RunJavaScript", ("script", Literal("var total = 1;\nif (total > 0) { total = 2; }\ntotal")));

        var result = await ConvertAsync(Sequence("root", script));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var body = Text(Input(Node(result.Value!.State, "node"), "script"))!;
        Assert.DoesNotContain("(function", body, StringComparison.Ordinal);
        Assert.EndsWith("return total", body, StringComparison.Ordinal);
    }

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertTextAsync(string expression, params Elsa3Variable[] variables) =>
        ConvertAsync(Sequence("root", Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", expression)))), variables);

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertAsync(Elsa3Activity root, params Elsa3Variable[] variables) =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root, variables: variables));

    private static ExpressionDefinition ReadExpression(Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition> result)
    {
        var argument = Input(Node(result.Value!.State, "node"), "text");
        return (argument.Value.Value is JsonElement element ? element : JsonSerializer.SerializeToElement(argument.Value.Value))
            .Deserialize<ExpressionDefinition>()!;
    }

    private sealed class Reader(Dictionary<string, JsonElement> values) : Elsa.Expressions.Core.Contracts.IExpressionVariableReader
    {
        public IReadOnlyList<string> CandidateNames => values.Keys.ToArray();

        public Elsa.Expressions.Core.Contracts.ExpressionVariableRead Read(string name) =>
            values.TryGetValue(name, out var value)
                ? Elsa.Expressions.Core.Contracts.ExpressionVariableRead.Present(value)
                : Elsa.Expressions.Core.Contracts.ExpressionVariableRead.Missing;
    }
}
