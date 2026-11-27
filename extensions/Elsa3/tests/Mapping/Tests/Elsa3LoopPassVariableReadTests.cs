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
/// variable of that name. The read converts to the owning loop's Elsa 4 variable, pinned to the loop node, where Elsa 3
/// read the pass value on every run; a variable Elsa 3 found first (one of a container nested inside the body) keeps
/// today's handling, and a read Elsa 3 resolved differently from run to run is refused. A computed name inside such a body
/// is refused.
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
    public async Task A_variable_of_a_container_inside_the_body_comes_first_and_keeps_todays_handling()
    {
        var inner = Sequence("inner", Read("getVariable('CurrentValue')"));
        inner.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "acme-inner-value", name = "CurrentValue", typeName = "String" } });

        var result = await ConvertAsync(Sequence("root", ForEach("loop", Sequence("body", inner))));

        // Today's handling of a body variable applies, not the loop's: the by-name read of a container variable has no
        // workflow declaration, so it is refused as before.
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.DanglingReference && diagnostic.Message.Contains("'CurrentValue'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("getVariable('CurrentValue')")]
    [InlineData("getCurrentValue()")]
    public async Task A_variable_of_the_body_container_itself_loses_to_the_per_pass_variable(string read)
    {
        // Elsa 3 created the per-pass variables on the body's context before the body declared its own.
        var body = Sequence("body", Read(read));
        body.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "acme-body-value", name = "CurrentValue", typeName = "String" } });

        var result = await ConvertAsync(Sequence("root", ForEach("loop", body)));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        AssertReadsLoop(result, "loop", "currentItem");
    }

    [Fact]
    public async Task A_generated_accessor_shared_with_a_workflow_variable_reads_the_workflow_variable()
    {
        // Elsa 3 registered getCurrentValue for the per-pass CurrentValue, then again for the workflow's currentValue,
        // which replaced it.
        var workflow = new Elsa3Variable { Id = "acme-lower", Name = "currentValue", TypeName = "String" };

        var result = await ConvertAsync(Sequence("root", ForEach("loop", Read("getCurrentValue()"))), workflow);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        AssertReads(result, VariableReference.WorkflowScopeId, "acme-lower");
    }

    [Fact]
    public async Task An_inner_loops_items_read_the_outer_pass()
    {
        // The inner loop's items are evaluated before its passes overwrite the outer CurrentValue.
        var inner = Activity("inner", "Elsa.ForEach", ("items", Expression("JavaScript", "getVariable('CurrentValue').lines", "Object")));
        inner.AdditionalProperties["body"] = Port(Activity("inner-node", "Elsa.WriteLine", ("text", Literal("acme"))));

        var result = await ConvertAsync(Sequence("root", ForEach("orders", Sequence("body", inner))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var items = ReadExpression(result, "inner", "Collection");
        var parameter = Assert.IsType<VariableExpressionParameterBinding>(Assert.Single(items.Parameters).Value);
        Assert.Equal(("orders", "currentItem"), (parameter.DeclaringScopeNodeId, parameter.VariableKey));
    }

    [Fact]
    public async Task An_inner_loops_items_read_after_a_resume_in_its_body_is_refused()
    {
        // A Delay suspends the workflow; on resume Elsa 3 evaluates the inner items again, after its passes overwrote the
        // outer CurrentValue.
        var inner = Activity("inner", "Elsa.ForEach", ("items", Expression("JavaScript", "getVariable('CurrentValue').lines", "Object")));
        inner.AdditionalProperties["body"] = Port(Activity("inner-delay", "Elsa.Delay"));

        var result = await ConvertAsync(Sequence("root", ForEach("orders", Sequence("body", inner))));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference && diagnostic.Message.Contains("'inner'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_read_ahead_of_an_inner_loop_reads_the_outer_pass(bool flowchart)
    {
        var inner = For("inner", Activity("inner-node", "Elsa.WriteLine", ("text", Literal("acme"))));
        Elsa3Activity body;
        if (flowchart)
        {
            var read = Read("getVariable('CurrentValue')");
            read.CustomProperties = new Elsa3ActivityCustomProperties { CanStartWorkflow = true };
            body = Activity("body", "Elsa.Flowchart");
            body.Activities = [read, inner];
            body.Connections = [Connect("node", "Done", "inner")];
        }
        else
            body = Sequence("body", Read("getVariable('CurrentValue')"), inner);

        var result = await ConvertAsync(Sequence("root", ForEach("orders", body)));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        AssertReadsLoop(result, "orders", "currentItem");
    }

    [Fact]
    public async Task A_read_after_an_inner_loop_that_declares_the_name_is_refused()
    {
        // Elsa 3's inner For overwrites the outer pass's CurrentValue, so the read after it saw the inner index.
        var body = Sequence("body", For("inner", Activity("inner-node", "Elsa.WriteLine", ("text", Literal("acme")))), Read("getVariable('CurrentValue')"));

        var result = await ConvertAsync(Sequence("root", ForEach("loop", body)));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference && diagnostic.Message.Contains("'inner'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_enclosing_containers_variable_that_hides_the_pass_only_until_a_resume_is_refused()
    {
        // The pass's variable lands in the enclosing Sequence after that Sequence's own CurrentValue, which a JavaScript
        // lookup finds first; Liquid, and JavaScript after a resume, read the pass value instead.
        var root = Sequence("root", ForEach("loop", Read("getVariable('CurrentValue')")));
        root.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "acme-root-value", name = "CurrentValue", typeName = "String" } });

        var result = await ConvertAsync(root);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference && diagnostic.Message.Contains("until the workflow suspends", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_read_that_depends_on_an_earlier_sibling_loop_is_refused()
    {
        // When 'first' ran before, 'second' overwrites first's CurrentValue in 'root', and the body's own CurrentValue hides
        // it; otherwise the pass's variable is the body's and wins. Elsa 3's result depended on the run.
        var first = ForEach("first", Activity("first-node", "Elsa.WriteLine", ("text", Literal("acme"))));
        var body = Sequence("body", Read("getVariable('CurrentValue')"));
        body.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "acme-body-value", name = "CurrentValue", typeName = "String" } });

        var result = await ConvertAsync(Sequence("root", first, ForEach("second", body)));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference && diagnostic.Message.Contains("'first'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_read_while_a_parallel_loop_shares_its_variable_is_refused()
    {
        // Both loops' bodies find the outer pass's CurrentValue in the Parallel and overwrite it in turn, so 'first''s read
        // could see 'second''s item.
        var parallel = Activity("parallel", "Elsa.Parallel");
        parallel.Activities =
        [
            ForEach("first", Sequence("first-body", Read("getVariable('CurrentValue')"))),
            ForEach("second", Sequence("second-body", Activity("second-node", "Elsa.WriteLine", ("text", Literal("acme"))))),
        ];

        var result = await ConvertAsync(Sequence("root", ForEach("outer", parallel)));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference && diagnostic.Message.Contains("'second'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_parallel_loop_that_reaches_an_earlier_loops_leftover_is_refused()
    {
        // 'earlier' leaves its CurrentValue in 'root'; both parallel loops then overwrite that block in place instead of
        // their bodies, so 'first''s read could see 'second''s item.
        var parallel = Activity("parallel", "Elsa.Parallel");
        parallel.Activities =
        [
            ForEach("first", Sequence("first-body", Read("getVariable('CurrentValue')"))),
            ForEach("second", Sequence("second-body", Activity("second-node", "Elsa.WriteLine", ("text", Literal("acme"))))),
        ];
        var earlier = ForEach("earlier", Activity("earlier-node", "Elsa.WriteLine", ("text", Literal("acme"))));

        var result = await ConvertAsync(Sequence("root", earlier, parallel));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference && diagnostic.Message.Contains("'second'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_read_while_a_fork_branch_loop_shares_its_variable_is_refused()
    {
        // Fork runs its branches concurrently; both bodies are no container, so both passes share one block in 'root'.
        var fork = Activity("fork", "Elsa.Fork", ("joinMode", JsonSerializer.SerializeToElement("WaitAll")));
        fork.AdditionalProperties["branches"] = JsonSerializer.SerializeToElement(new[]
        {
            Port(ForEach("first", Read("getVariable('CurrentValue')"))),
            Port(ForEach("second", Activity("second-node", "Elsa.WriteLine", ("text", Literal("acme"))))),
        });

        var result = await ConvertAsync(Sequence("root", fork));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference && diagnostic.Message.Contains("'second'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Flowchart_branches_share_a_pass_only_when_no_connection_orders_them(bool connected)
    {
        // Both bodies are no container: both passes land in the flowchart. Unconnected branches may run interleaved.
        var start = Activity("start", "Elsa.WriteLine", ("text", Literal("acme")));
        start.CustomProperties = new Elsa3ActivityCustomProperties { CanStartWorkflow = true };
        var chart = Activity("root", "Elsa.Flowchart");
        chart.Activities =
        [
            start,
            ForEach("first", Read("getVariable('CurrentValue')")),
            ForEach("second", Activity("second-node", "Elsa.WriteLine", ("text", Literal("acme")))),
        ];
        chart.Connections = connected
            ? [Connect("start", "Done", "first"), Connect("first", "Done", "second")]
            : [Connect("start", "Done", "first"), Connect("start", "Done", "second")];

        var result = await ConvertAsync(chart);

        if (connected)
        {
            Assert.True(result.Succeeded, Describe(result.Diagnostics));
            AssertReadsLoop(result, "first", "currentItem");
        }
        else
        {
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference && diagnostic.Message.Contains("'second'", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_read_while_a_reusable_activity_may_run_alongside_is_refused()
    {
        var parallel = Activity("parallel", "Elsa.Parallel");
        parallel.Activities = [ForEach("loop", Read("getVariable('CurrentValue')")), Activity("acme-child", "Acme.ChildWorkflow")];
        var replacements = new Dictionary<string, Elsa3.Mapping.Mappings.Elsa3ActivityExactReplacement>(StringComparer.Ordinal)
        {
            ["acme-child"] = new("acme-child-v1", [], new HashSet<string>(StringComparer.Ordinal)),
        };

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(),
            ConversionServices.Definition(Sequence("root", parallel)), replacements: replacements);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference && diagnostic.Message.Contains("'acme-child'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_variable_with_the_per_pass_variables_elsa3_id_is_refused(bool onContainer)
    {
        // Elsa 3 gave the per-pass CurrentValue the id 'currentValueVariable'; a declared variable with that id shares it.
        var root = Sequence("root", ForEach("loop", Read("getVariable('CurrentValue')")));
        Elsa3Variable[] workflowVariables = [];
        if (onContainer)
            root.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "currentValueVariable", name = "AcmeOther", typeName = "String" } });
        else
            workflowVariables = [new Elsa3Variable { Id = "currentValueVariable", Name = "AcmeOther", TypeName = "String" }];

        var result = await ConvertAsync(root, workflowVariables);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference && diagnostic.Message.Contains("'currentValueVariable'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_read_after_a_loop_that_left_its_pass_variable_behind_is_refused()
    {
        // The body is no container: the last pass's CurrentValue stays in 'root', where Elsa 3 found it before the workflow
        // variable once the loop had run.
        var result = await ConvertAsync(
            Sequence("root", ForEach("loop", Activity("loop-node", "Elsa.WriteLine", ("text", Literal("acme")))), Read("getVariable('CurrentValue')")),
            WorkflowCurrentValue);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference && diagnostic.Message.Contains("'loop'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_read_after_a_reusable_activity_in_the_body_is_refused()
    {
        // A reusable workflow runs inline in Elsa 3; a loop inside it would overwrite the pass's CurrentValue.
        var body = Sequence("body", Activity("acme-child", "Acme.ChildWorkflow"), Read("getVariable('CurrentValue')"));
        var replacements = new Dictionary<string, Elsa3.Mapping.Mappings.Elsa3ActivityExactReplacement>(StringComparer.Ordinal)
        {
            ["acme-child"] = new("acme-child-v1", [], new HashSet<string>(StringComparer.Ordinal)),
        };

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(),
            ConversionServices.Definition(Sequence("root", ForEach("loop", body))), replacements: replacements);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference && diagnostic.Message.Contains("'acme-child'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_generated_accessor_registered_after_a_body_variable_of_another_case_reads_the_pass()
    {
        // Elsa 3 registered the body's currentValue, then the per-pass CurrentValue on the same scope, which replaced it.
        var body = Sequence("body", Read("getCurrentValue()"));
        body.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "acme-lower", name = "currentValue", typeName = "String" } });

        var result = await ConvertAsync(Sequence("root", ForEach("loop", body)));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        AssertReadsLoop(result, "loop", "currentItem");
    }

    [Fact]
    public async Task A_body_variable_named_like_the_elsa4_loop_variable_is_refused()
    {
        // Elsa 4 resolves 'currentItem' nearest scope first, so the body's own variable would be read instead.
        var body = Sequence("body", Read("getVariable('CurrentValue')"));
        body.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[] { new { id = "acme-item", name = "currentItem", typeName = "String" } });

        var result = await ConvertAsync(Sequence("root", ForEach("loop", body)));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.ResolutionDiffers);
    }

    [Fact]
    public async Task A_read_in_the_loops_own_inputs_is_not_owned_by_the_loop()
    {
        var loop = Activity("loop", "Elsa.ForEach", ("items", Expression("JavaScript", "[getVariable('CurrentValue')]", "Object")));
        // A body container holds the passes' variables, so none is left where the loop's own input could find it.
        loop.AdditionalProperties["body"] = Port(Sequence("body", Activity("node", "Elsa.WriteLine", ("text", Literal("acme")))));

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_computed_name_after_a_loop_that_left_its_pass_behind_is_refused(bool script)
    {
        var reader = script
            ? Activity("node", "Elsa.RunJavaScript", ("script", Literal("var name = 'Current' + 'Value'; return getVariable(name);")))
            : Read("getVariable(['Current', 'Value'].join(''))");
        var result = await ConvertAsync(
            Sequence("root", ForEach("loop", Activity("loop-node", "Elsa.WriteLine", ("text", Literal("acme")))), reader),
            WorkflowCurrentValue);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == Elsa3ActivityDiagnosticCodes.ValueOutsideContract && diagnostic.Message.Contains("left behind by a loop", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("var name = 'Current' + 'Value'; return getVariable(name);")]
    [InlineData("var name = 'Current' + 'Value'; setVariable(name, 1); return 1;")]
    public async Task A_computed_name_in_a_run_javascript_body_inside_a_loop_is_refused(string source)
    {
        var script = Activity("node", "Elsa.RunJavaScript", ("script", Literal(source)));

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
            diagnostic.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability && diagnostic.Message.Contains("a loop's current value", StringComparison.Ordinal));
    }

    private static Elsa3Connection Connect(string source, string port, string target) =>
        new() { Source = new Elsa3Endpoint { Activity = source, Port = port }, Target = new Elsa3Endpoint { Activity = target, Port = "In" } };

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
