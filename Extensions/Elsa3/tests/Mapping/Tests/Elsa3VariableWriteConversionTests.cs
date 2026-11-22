using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 1231 User Story 6 (T038–T040), #4899: inside a Run JavaScript body, Elsa 3's <c>setVariable('x', v)</c> and
/// generated <c>setX(v)</c> become whole assignments through <c>variables</c> (Q6), and a member change of a value read
/// through <c>getVariable</c> or <c>variables</c> stays a member change, and a computed setter name becomes
/// <c>variables[name] = v</c> (FR-033 as amended). A setter of an undeclared variable or of a loop's value, a write in an
/// input expression and a write in a reusable workflow's script are refused with guidance; the other Elsa 3 setters stay
/// refused (FR-034).
/// </summary>
public sealed class Elsa3VariableWriteConversionTests
{
    private static readonly Elsa3Variable Total = new() { Id = "total-id", Name = "Total", TypeName = "Int32" };
    private static readonly Elsa3Variable Order = new() { Id = "order-id", Name = "Order", TypeName = "Object" };
    private static readonly Elsa3Variable OrderTotal = new() { Id = "order-total-id", Name = "order-total", TypeName = "Int32" };

    [Fact]
    public async Task SetVariable_with_a_literal_name_becomes_a_whole_assignment()
    {
        // Acceptance 1; #4899's example.
        var script = await ConvertScriptAsync("setVariable('Total', getVariable('Total') + 1);\nreturn 1;", Total);

        Assert.Equal("variables.Total = variables.Total + 1;\nreturn 1;", script);
    }

    [Fact]
    public async Task SetVariable_of_a_name_that_is_not_an_identifier_uses_the_bracket_form()
    {
        var script = await ConvertScriptAsync("setVariable('order-total', 5);\nreturn 1;", OrderTotal);

        Assert.Equal("variables[\"order-total\"] = 5;\nreturn 1;", script);
    }

    [Fact]
    public async Task A_generated_setter_becomes_a_whole_assignment()
    {
        // Acceptance 2.
        var script = await ConvertScriptAsync("setTotal(getTotal() * 2);\nreturn 1;", Total);

        Assert.Equal("variables.Total = variables.Total * 2;\nreturn 1;", script);
    }

    [Theory]
    [InlineData("setVariable('Total', 3)", "return void (variables.Total = 3)")]
    [InlineData("var r = setVariable('Total', 3); r", "var r = void (variables.Total = 3); return r")]
    [InlineData("setTotal((1, 3))", "return void (variables.Total = (1, 3))")]
    public async Task A_setter_whose_value_could_be_read_still_gives_nothing(string source, string expected)
    {
        // Elsa 3's setters returned nothing; the trailing statement becomes the script's return.
        Assert.Equal(expected, await ConvertScriptAsync(source, Total));
    }

    [Theory]
    [InlineData("getVariable('Order').Status = 'Paid';", "return variables.Order.Status = 'Paid';")]
    [InlineData("getOrder().Lines++;", "return variables.Order.Lines++;")]
    [InlineData("variables.Order.Status = 'Paid';", "return variables.Order.Status = 'Paid';")]
    [InlineData("getVariable('Order').Lines[0].Qty += 1;", "return variables.Order.Lines[0].Qty += 1;")]
    public async Task A_member_change_of_a_read_value_stays_a_member_change(string source, string expected)
    {
        // Acceptance 3 (C2): written as the base plus that change, with the step. A trailing statement is the script's
        // completion value in Elsa 3, so it is returned.
        Assert.Equal(expected, await ConvertScriptAsync(source, Order));
    }

    [Theory]
    [InlineData("const name = 'Total';\nsetVariable(name, 1);\nreturn 1;", "const name = 'Total';\nvariables[name] = 1;\nreturn 1;")]
    [InlineData("setVariable('To' + 'tal', getVariable('Total') + 1);\nreturn 1;", "variables['To' + 'tal'] = variables.Total + 1;\nreturn 1;")]
    [InlineData("const name = 'Total'; setVariable(name, 1)", "const name = 'Total'; return void (variables[name] = 1)")]
    public async Task A_computed_setter_name_becomes_a_computed_write(string source, string expected)
    {
        // Acceptance 4, as amended on 2026-11-20 (#4899): the name resolves at run time; an undeclared one faults the step.
        Assert.Equal(expected, await ConvertScriptAsync(source, Total));
    }

    [Fact]
    public async Task A_setter_of_an_undeclared_variable_is_refused_with_guidance_to_declare_it()
    {
        // Acceptance 5: Elsa 3 created the variable on its first write; Elsa 4 never does.
        var issue = await RefusedScriptAsync("setVariable('Missing', 1);", Total);

        Assert.Equal(Elsa3ImportDiagnosticCodes.SetterOfUndeclaredVariable, issue.Code);
        Assert.Contains("Declare variable 'Missing'", issue.Guidance, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("setVariable('Total', 1)")]
    [InlineData("setTotal(1)")]
    [InlineData("getVariable('Order').Status = 'Paid'")]
    public async Task Every_write_form_in_an_input_expression_stays_refused_naming_a_run_javascript_step(string expression)
    {
        // Acceptance 6: input expressions only read.
        var result = await ConvertAsync(Sequence("root", Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", expression)))), Total, Order);

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
        Assert.Contains("Run JavaScript", diagnostic.Guidance, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("setOutput('x', 1);")]
    [InlineData("setInput('x', 1);")]
    [InlineData("setResult(1);")]
    [InlineData("setCorrelationId('c');")]
    [InlineData("setWorkflowInstanceName('n');")]
    public async Task The_other_elsa3_setters_stay_refused_in_a_script(string source)
    {
        // FR-034 (Q1): workflow outputs, inputs, correlation and instance name are not variables.
        var issue = await RefusedScriptAsync(source, Total);

        Assert.Equal(Elsa3ImportDiagnosticCodes.ForbiddenCapability, issue.Code);
    }

    [Theory]
    [InlineData("variables.Total = 1;")]
    [InlineData("getVariable('Total')++;")]
    public async Task A_whole_assignment_through_an_ambient_read_stays_refused_and_names_setVariable(string source)
    {
        var issue = await RefusedScriptAsync(source, Total);

        Assert.Equal(Elsa3ImportDiagnosticCodes.ForbiddenCapability, issue.Code);
        Assert.Contains("setVariable('name', value)", issue.Guidance, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("setVariable('Total', 1);")]
    [InlineData("setTotal(1);")]
    [InlineData("getVariable('Order').Status = 'Paid';")]
    [InlineData("getVariable('Order').Lines.push(1); return 1;")]
    [InlineData("getOrder().Lines['push'](1); return 1;")]
    [InlineData("const o = getVariable('Order'); o.Status = 'Paid'; return 1;")]
    [InlineData("const o = variables.Order; const lines = o.Lines; lines.push(1); return 1;")]
    [InlineData("const o = getVariable('Order'); delete o.Status; return 1;")]
    [InlineData("const o = getVariable('Order'); o.Count++; return 1;")]
    [InlineData("const o = getVariable('Order'); Object.assign(o, { Status: 'Paid' }); return 1;")]
    [InlineData("const o = getVariable('Order'); function write() { o.Status = 'Paid'; } write(); return 1;")]
    [InlineData("function write() { o.Status = 'Paid'; } const o = getVariable('Order'); write(); return 1;")]
    [InlineData("let o = getVariable('Order'); o.Status = 'Paid'; o = {}; return 1;")]
    [InlineData("let o = {}; o = getVariable('Order'); o.Status = 'Paid'; return 1;")]
    [InlineData("let o = getVariable('Order'); if (false) o = {}; o.Status = 'Paid'; return 1;")]
    [InlineData("const o = getVariable('Order') || {}; o.Status = 'Paid'; return 1;")]
    [InlineData("(getVariable('Order').Lines || []).push(1); return 1;")]
    [InlineData("const { Lines } = getVariable('Order'); Lines.push(1); return 1;")]
    [InlineData("let o = getVariable('Order'); while (false) o = {}; o.Status = 'Paid'; return 1;")]
    [InlineData("let o = getVariable('Order'); for (; false;) o = {}; o.Status = 'Paid'; return 1;")]
    [InlineData("let o = getVariable('Order'); function reset() { o = {}; } o.Status = 'Paid'; return 1;")]
    [InlineData("function write() { o.Status = 'Paid'; } let o = getVariable('Order'); write(); o = {}; return 1;")]
    [InlineData("let Lines; ({ Lines } = getVariable('Order')); Lines.push(1); return 1;")]
    public async Task A_write_in_a_reusable_workflows_script_is_refused(string source)
    {
        // A graph activity's variables are a closed scope a step cannot write yet (#4805): the script would fail every run.
        var root = Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal(source))));

        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root, variables: [Total, Order]), isReusable: true);

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability);
        Assert.Contains("reusable workflow", diagnostic.Guidance, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("const o = getVariable('Order'); return o.Status;")]
    [InlineData("const o = getVariable('Order'); return o;")]
    [InlineData("return getVariable('Order').Lines.join(',');")]
    [InlineData("const o = getVariable('Order'); { const o = {}; o.Status = 'Paid'; } return 1;")]
    [InlineData("const o = getVariable('Order'); function change(o) { o.Status = 'Paid'; } change({}); return 1;")]
    [InlineData("let o = getVariable('Order'); o = {}; o.Status = 'Paid'; return 1;")]
    [InlineData("const o = getVariable('Order'); Object.assign({}, o); return 1;")]
    [InlineData("const o = getVariable('Order'); o.Lines.slice().push(1); return 1;")]
    [InlineData("const o = getVariable('Order'); return Object.assign(o);")]
    [InlineData("const o = getVariable('Order'); return Object.assign(o, {}, null, undefined, 1, true, '');")]
    [InlineData("const o = getVariable('Order'); return o.Lines.push();")]
    [InlineData("const { Lines } = { Lines: [] }; Lines.push(1); return 1;")]
    [InlineData("const o = getVariable('Order') && {}; o.Status = 'Paid'; return 1;")]
    public async Task A_reusable_script_can_read_aliases_and_mutate_local_copies(string source)
    {
        var root = Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal(source))));
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root, variables: [Order]), isReusable: true);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Fact]
    public async Task A_setter_of_a_loops_current_value_is_refused()
    {
        var loop = Activity("loop", "Elsa.ForEach", ("items", Literal("[1, 2]", "Object")), ("currentValue", Captured("item")));
        loop.AdditionalProperties["body"] = Port(Activity("script", "Elsa.RunJavaScript", ("script", Literal("setVariable('item', 3);"))));

        var result = await ConvertAsync(Sequence("root", loop), new Elsa3Variable { Id = "item", Name = "item", TypeName = "Object" });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, item => item.Message.Contains("loop's current value", StringComparison.Ordinal));
    }

    private static async Task<string> ConvertScriptAsync(string source, params Elsa3Variable[] variables)
    {
        var result = await ConvertAsync(Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal(source)))), variables);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        return Text(Input(Node(result.Value!.State, "node"), "script"))!;
    }

    private static async Task<Elsa3MigrationDiagnostic> RefusedScriptAsync(string source, params Elsa3Variable[] variables)
    {
        var result = await ConvertAsync(Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal(source)))), variables);

        Assert.False(result.Succeeded);
        return Assert.Single(result.Diagnostics, item => item.IsError);
    }

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertAsync(Elsa3Activity root, params Elsa3Variable[] variables) =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root, variables: variables));
}
