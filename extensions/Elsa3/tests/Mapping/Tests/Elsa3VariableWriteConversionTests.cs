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

    [Theory]
    [InlineData("function f(item) { item.Status = 'Paid'; var item; } f(getVariable('Order')); return 1;", false)]
    [InlineData("function f(item) { var item; item.Status = 'Paid'; } f(getVariable('Order')); return 1;", false)]
    [InlineData("function f(item = getVariable('Order')) { item.Status = 'Paid'; var item; } f(); return 1;", false)]
    [InlineData("function f(item, read = () => item) { var item = {}; read().Status = 'Paid'; } f(getVariable('Order')); return 1;", false)]
    [InlineData("function f(write) { write(); var write; } f(() => { getVariable('Order').Status = 'Paid'; }); return 1;", false)]
    [InlineData("function f(write = () => { getVariable('Order').Status = 'Paid'; }) { var write; write(); } f(); return 1;", false)]
    [InlineData("switch (0) { default: const item = getVariable('Order'); item.Status = 'Paid'; } return 1;", false)]
    [InlineData("const item = getVariable('Order'); switch (0) { default: let item = {}; } item.Status = 'Paid'; return 1;", false)]
    [InlineData("switch (0) { case 0: const item = getVariable('Order'); case 1: item.Status = 'Paid'; } return 1;", false)]
    [InlineData("function f(item) { var item; item.Status = 'Local'; } f({}); return 1;", true)]
    [InlineData("function f(item = {}) { var item; item.Status = 'Local'; } f(); return 1;", true)]
    [InlineData("switch (0) { default: const setVariable = () => 1; setVariable(); } return 1;", true)]
    [InlineData("switch (0) { default: const getVariable = () => ({}); const item = getVariable(); item.Status = 'Local'; } return 1;", true)]
    [InlineData("const item = getVariable('Order'); switch (0) { default: let item = {}; item.Status = 'Local'; } return 1;", true)]
    [InlineData("function f(item) { item = {}; var item; item.Status = 'Local'; } f(getVariable('Order')); return 1;", true)]
    [InlineData("function f(item, read = () => item) { var item = {}; item.Status = 'Local'; } f(getVariable('Order')); return 1;", true)]
    public async Task Reusable_scope_analysis_preserves_parameter_values_and_switch_bindings(string source, bool succeeds)
    {
        var root = Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal(source))));
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root, variables: [Total, Order]), isReusable: true);
        Assert.Equal(succeeds, result.Succeeded);
        if (!succeeds)
            Assert.Contains(result.Diagnostics, item => item.Code == Elsa3ImportDiagnosticCodes.ForbiddenCapability &&
                item.Guidance.Contains("reusable workflow", StringComparison.Ordinal));
    }

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

    /// <summary>
    /// Elsa 3 generated setters for variables only: a workflow input is read-only. With an input and a variable of one name,
    /// get{Name} read the input but set{Name} wrote the variable (spec 247 R-2).
    /// </summary>
    [Fact]
    public async Task A_generated_setter_writes_the_variable_even_when_an_input_has_the_name()
    {
        var result = await ConvertWithInputAsync("setStatus('Paid');\nreturn 1;", "Status",
            new Elsa3Variable { Id = "status-id", Name = "Status", TypeName = "String" });

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("variables.Status = 'Paid';\nreturn 1;", Text(Input(Node(result.Value!.State, "node"), "script")));
    }

    /// <summary>Two variables Elsa 3 generated one setter for: the setter is ambiguous, whatever an input of the name does.</summary>
    [Fact]
    public async Task An_ambiguous_setter_next_to_an_input_is_refused_as_ambiguous_not_read_only()
    {
        var result = await ConvertWithInputAsync("setStatus(1);\nreturn 1;", "Status",
            new Elsa3Variable { Id = "v1", Name = "status", TypeName = "String" },
            new Elsa3Variable { Id = "v2", Name = "Status", TypeName = "String" });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.AmbiguousReference);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Message.Contains("read-only", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_setter_named_after_a_workflow_input_is_refused_as_read_only()
    {
        var result = await ConvertWithInputAsync("setStatus('Paid');\nreturn 1;", "Status");

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.IsError);
        Assert.Equal(Elsa3ImportDiagnosticCodes.ForbiddenCapability, diagnostic.Code);
        Assert.Contains("read-only", diagnostic.Message, StringComparison.Ordinal);
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
    [InlineData("setInput('x', 1);")]
    [InlineData("setResult(1);")]
    public async Task The_other_elsa3_setters_stay_refused_in_a_script(string source)
    {
        // S-13 replaces the output/identity refusal; input and result mutation remain unsupported.
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
    [InlineData("let o = getVariable('Order'); const alias = o; o = {}; alias.Status = 'Paid'; return 1;")]
    [InlineData("let o = getVariable('Order'); const lines = o.Lines; o = {}; lines.push(1); return 1;")]
    [InlineData("let o = {}; for (let i = 0; i < 2; i++) { o.Status = 'Paid'; o = getVariable('Order'); } return 1;")]
    [InlineData("let o = {}; while (getVariable('Total')) { o.Status = 'Paid'; o = getVariable('Order'); } return 1;")]
    [InlineData("let o = getVariable('Order'); for (const x of []) o = {}; o.Status = 'Paid'; return 1;")]
    [InlineData("let o = getVariable('Order'); for (const x in {}) o = {}; o.Status = 'Paid'; return 1;")]
    [InlineData("let o = {}; while (getVariable('Total')) { if (getVariable('Total')) { o = getVariable('Order'); break; } else o = {}; } o.Status = 'Paid'; return 1;")]
    [InlineData("let o = {}; for (let i = 0; i < 2; i++) { if (getVariable('Total')) { o = getVariable('Order'); continue; } o.Status = 'Paid'; o = {}; } return 1;")]
    [InlineData("let o = getVariable('Order'); switch (getVariable('Total')) { case 0: o = {}; break; } o.Status = 'Paid'; return 1;")]
    [InlineData("const o = getVariable('Order'); switch (getVariable('Total')) { case 0: return 1; } o.Status = 'Paid'; return 1;")]
    [InlineData("let o = getVariable('Order'); try { o = {}; } catch (e) {} o.Status = 'Paid'; return 1;")]
    [InlineData("function mutate(x) { x.Status = 'Paid'; } mutate(getVariable('Order')); return 1;")]
    [InlineData("function identity(x) { return x; } identity(getVariable('Order')).Status = 'Paid'; return 1;")]
    [InlineData("let o = {}; function mutate() { o.Status = 'Paid'; } o = getVariable('Order'); mutate(); return 1;")]
    [InlineData("const a = []; a.push(getVariable('Order')); a[0].Status = 'Paid'; return 1;")]
    [InlineData("[1].map(() => getVariable('Order'))[0].Status = 'Paid'; return 1;")]
    [InlineData("const o = [1].reduce(() => getVariable('Order'), {}); o.Status = 'Paid'; return 1;")]
    [InlineData("function make(o) { function mutate() { o.Status = 'Paid'; } return mutate; } make(getVariable('Order'))(); return 1;")]
    [InlineData("function make(o) { return () => { o.Status = 'Paid'; }; } make(getVariable('Order'))(); return 1;")]
    [InlineData("getVariable('Order').Lines.forEach(x => { x.Status = 'Paid'; }); return 1;")]
    [InlineData("function recurse(x) { return recurse(x); } recurse(getVariable('Order')); return 1;")]
    [InlineData("const o = [getVariable('Order')].pop(); o.Status = 'Paid'; return 1;")]
    [InlineData("const a = [].concat(getVariable('Order')); a[0].Status = 'Paid'; return 1;")]
    [InlineData("function mutate(s, o) { o.Status = 'Paid'; } mutate`${getVariable('Order')}`; return 1;")]
    [InlineData("const h = { o: getVariable('Order'), mutate() { this.o.Status = 'Paid'; } }; h.mutate(); return 1;")]
    [InlineData("const h = { get x() { getVariable('Order').Status = 'Paid'; return 1; } }; return h.x;")]
    [InlineData("function Change(o) { o.Status = 'Paid'; } new Change(getVariable('Order')); return 1;")]
    [InlineData("let o = getVariable('Order'); o.Status = (o = {}, 'Paid'); return 1;")]
    [InlineData("Reflect.apply(o => { o.Status = 'Paid'; }, null, [getVariable('Order')]); return 1;")]
    [InlineData("Reflect.get(getVariable('Order'), 'Lines').push(1); return 1;")]
    [InlineData("Object.getOwnPropertyDescriptor(getVariable('Order'), 'Lines').value.push(1); return 1;")]
    [InlineData("function mutate(...xs) { xs[0].Status = 'Paid'; } mutate(getVariable('Order')); return 1;")]
    [InlineData("function mutate(o) { o.Status = 'Paid'; } mutate(...[getVariable('Order')]); return 1;")]
    [InlineData("const o = getVariable('Order'); [2, 1].sort((a, b) => { o.Status = 'Paid'; return a - b; }); return 1;")]
    [InlineData("const o = getVariable('Order'); [2, 1].toSorted((a, b) => { o.Status = 'Paid'; return a - b; }); return 1;")]
    [InlineData("const o = getVariable('Order'); const h = { get x() { o.Status = 'Paid'; return 1; } }; const copy = { ...h }; return 1;")]
    [InlineData("const o = getVariable('Order'); const h = { get x() { o.Status = 'Paid'; return 1; } }; Object.values(h); return 1;")]
    [InlineData("const o = getVariable('Order'); JSON.stringify({ toJSON() { o.Status = 'Paid'; return 1; } }); return 1;")]
    [InlineData("let o = getVariable('Order'); o ||= {}; o.Status = 'Paid'; return 1;")]
    [InlineData("let o = getVariable('Order'); o ??= {}; o.Status = 'Paid'; return 1;")]
    [InlineData("function mutate() { getVariable('Order').Status = 'Paid'; return 'x'; } const { [mutate()]: x } = {}; return 1;")]
    [InlineData("let o = {}; outer: { o = getVariable('Order'); break outer; } o.Status = 'Paid'; return 1;")]
    [InlineData("const a = { o: getVariable('Order') }; const b = { o: {} }; const t = getVariable('Total') ? a : b; t.o = {}; a.o.Status = 'Paid'; return 1;")]
    [InlineData("function box(o) { return { o }; } const a = box(getVariable('Order')); const b = box({}); a.o.Status = 'Paid'; return 1;")]
    [InlineData("Object.keys = o => { o.Status = 'Paid'; return []; }; Object.keys(getVariable('Order')); return 1;")]
    [InlineData("class C { constructor() { const o = getVariable('Order'); o.Status = 'Paid'; } } new C(); return 1;")]
    [InlineData("class C { mutate(o) { o.Status = 'Paid'; } } new C().mutate(getVariable('Order')); return 1;")]
    [InlineData("let o = {}; outer: { inner: { o = getVariable('Order'); break outer; } o = {}; } o.Status = 'Paid'; return 1;")]
    [InlineData("let o = {}; outer: for (let i = 0; i < 2; i++) { for (let j = 0; j < 2; j++) { o.Status = 'Paid'; o = getVariable('Order'); continue outer; } } return 1;")]
    [InlineData("function mutate(...xs) { xs[1].Status = 'Paid'; } mutate(...[{}, getVariable('Order')].slice()); return 1;")]
    [InlineData("function mutate() { arguments[0].Status = 'Paid'; } mutate(getVariable('Order')); return 1;")]
    [InlineData("function mutate() { const write = () => { arguments[0].Status = 'Paid'; }; write(); } mutate(getVariable('Order')); return 1;")]
    [InlineData("const o = getVariable('Order'), k = 'x'; const h = { get [k]() { o.Status = 'Paid'; return 1; } }; return h.x;")]
    [InlineData("const o = getVariable('Order'), k = 'x'; const h = { set [k](x) { o.Status = 'Paid'; } }; h.x = 1; return 1;")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { o.Status = 'Paid'; return 1; } }; return h + 1;")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { o.Status = 'Paid'; return 1; } }; return +h;")]
    [InlineData("const o = getVariable('Order'); let h = { valueOf() { o.Status = 'Paid'; return 1; } }; return h++;")]
    [InlineData("const o = getVariable('Order'); const h = { get x() { o.Status = 'Paid'; return 1; }, set x(v) {} }; return h.x++;")]
    [InlineData("const o = getVariable('Order'); const h = { get x() { o.Status = 'Paid'; return 1; }, set x(v) {} }; h.x += 1; return 1;")]
    [InlineData("const o = getVariable('Order'); 'x'.replace('x', () => { o.Status = 'Paid'; return 'x'; }); return 1;")]
    [InlineData("const o = getVariable('Order'); 'x'.replaceAll('x', () => { o.Status = 'Paid'; return 'x'; }); return 1;")]
    [InlineData("[0].forEach(function () { this.Status = 'Paid'; }, getVariable('Order')); return 1;")]
    [InlineData("Array.from([0], function () { this.Status = 'Paid'; return 1; }, getVariable('Order')); return 1;")]
    [InlineData("const h = { get x() { this.Status = 'Paid'; return 1; } }; Reflect.get(h, 'x', getVariable('Order')); return 1;")]
    [InlineData("function* gen() { yield getVariable('Order'); } const o = gen().next().value; o.Status = 'Paid'; return 1;")]
    [InlineData("const o = getVariable('Order'); const h = { toString() { o.Status = 'Paid'; return 'x'; } }; new String(h); return 1;")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { o.Status = 'Paid'; return 1; } }; new Number(h); return 1;")]
    [InlineData("const o = getVariable('Order'); const h = { toString() { o.Status = 'Paid'; return 'x'; } }; new RegExp(h); return 1;")]
    [InlineData("const o = getVariable('Order'); const h = { toString() { o.Status = 'Paid'; return 'g'; } }; new RegExp('x', h); return 1;")]
    [InlineData("const o = getVariable('Order'); const h = { toString() { o.Status = 'Paid'; return 'x'; } }; return `${h}`;")]
    [InlineData("const o = getVariable('Order'); const k = { toString() { o.Status = 'Paid'; return 'x'; } }; return ({})[k];")]
    [InlineData("const o = getVariable('Order'); const k = { toString() { o.Status = 'Paid'; return 'x'; } }; return { [k]: 1 };")]
    [InlineData("const o = getVariable('Order'); const k = { toString() { o.Status = 'Paid'; return 'x'; } }; const { [k]: x } = {}; return x;")]
    [InlineData("const o = getVariable('Order'); const k = { toString() { o.Status = 'Paid'; return 'x'; } }; const h = {}; h[k] = 1; return 1;")]
    [InlineData("const o = getVariable('Order'); const k = { toString() { o.Status = 'Paid'; return 'x'; } }; const h = { x: 1 }; delete h[k]; return 1;")]
    [InlineData("const o = getVariable('Order'); const k = { toString() { o.Status = 'Paid'; return 'x'; } }; class C { [k]() {} } return 1;")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { o.Status = 'Paid'; return 10; } }; return Number.parseInt('1', h);")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { o.Status = 'Paid'; return 10; }, toString() { return '10'; } }; return parseInt('1', h);")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { o.Status = 'Paid'; return 0; } }; return 'x'.charAt(h);")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { o.Status = 'Paid'; return 0; } }; return [].slice(h);")]
    [InlineData("const o = getVariable('Order'); const h = { toString() { o.Status = 'Paid'; return 'x'; } }; return [h].join(',');")]
    [InlineData("const o = getVariable('Order'); const h = { toString() { o.Status = 'Paid'; return ','; } }; return [1].join(h);")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { o.Status = 'Paid'; return 0; } }; return [2, 1].sort(() => h);")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { o.Status = 'Paid'; return 0; } }; return [2, 1].toSorted(() => h);")]
    [InlineData("let o = getVariable('Order'); o.Status ||= (o = {}, 'Paid'); return 1;")]
    [InlineData("let o = getVariable('Order'); o.Status &&= (o = {}, 'Paid'); return 1;")]
    [InlineData("let o = getVariable('Order'); o.Status ??= (o = {}, 'Paid'); return 1;")]
    [InlineData("const o = getVariable('Order'); JSON.stringify({ o }, function (key, value) { if (key === 'o') this.o.Status = 'Paid'; return value; }); return 1;")]
    [InlineData("const o = getVariable('Order'); const xs = { *[Symbol.iterator]() { o.Status = 'Paid'; yield 1; } }; for (const x of xs) {} return 1;")]
    [InlineData("const o = getVariable('Order'); const xs = { *[Symbol.iterator]() { o.Status = 'Paid'; yield 1; } }; return [...xs];")]
    [InlineData("const o = getVariable('Order'); const xs = { *[Symbol.iterator]() { o.Status = 'Paid'; yield 1; } }; function take(x) { return x; } return take(...xs);")]
    [InlineData("const o = getVariable('Order'); const xs = { *[Symbol.iterator]() { o.Status = 'Paid'; yield 1; } }; const [x] = xs; return x;")]
    [InlineData("const o = getVariable('Order'); const xs = { *[Symbol.iterator]() { o.Status = 'Paid'; yield 1; } }; return Array.from(xs);")]
    [InlineData("const o = getVariable('Order'); const xs = { [Symbol.iterator]() { return { next() { o.Status = 'Paid'; return { value: 1, done: false }; } }; } }; for (const x of xs) {} return 1;")]
    [InlineData("const o = getVariable('Order'); const search = { toString() { o.Status = 'Paid'; return 'x'; } }; return 'x'.includes(search);")]
    [InlineData("const o = getVariable('Order'); const search = { toString() { o.Status = 'Paid'; return 'x'; } }; return 'x'.indexOf(search);")]
    [InlineData("const o = getVariable('Order'); const search = { toString() { o.Status = 'Paid'; return 'x'; } }; return 'x'.lastIndexOf(search);")]
    [InlineData("const o = getVariable('Order'); const search = { toString() { o.Status = 'Paid'; return 'x'; } }; const text = 'x'; return text.includes(search);")]
    [InlineData("const o = getVariable('Order'); const h = { toString() { o.Status = 'Paid'; return 'x'; } }; return [h].toString();")]
    [InlineData("const o = getVariable('Order'); const h = { toString() { o.Status = 'Paid'; return 'x'; } }; return [h].toLocaleString('en-US');")]
    [InlineData("const o = getVariable('Order'); const h = { toLocaleString() { o.Status = 'Paid'; return 'x'; } }; return [h].toLocaleString('en-US');")]
    [InlineData("const o = getVariable('Order'); const a = []; a.join = () => { o.Status = 'Paid'; return 'x'; }; return a.toString();")]
    [InlineData("const o = getVariable('Order'); const h = { toString() { o.Status = 'Paid'; return 'x'; } }; return String([h]);")]
    [InlineData("const xs = { [Symbol.iterator]() { return { next() { return { value: 1, done: false }; }, return() { const o = getVariable('Order'); o.Status = 'Paid'; return { done: true }; } }; } }; for (const x of xs) { break; } return 1;")]
    [InlineData("const xs = { [Symbol.iterator]() { return { next() { return { value: 1, done: false }; }, return() { const o = getVariable('Order'); o.Status = 'Paid'; return { done: true }; } }; } }; const [x] = xs; return 1;")]
    [InlineData("const xs = { [Symbol.iterator]() { return { next() { return { value: 1, done: false }; }, return() { const o = getVariable('Order'); o.Status = 'Paid'; return { done: true }; } }; } }; function take() { for (const x of xs) { return x; } } return take();")]
    [InlineData("const xs = { [Symbol.iterator]() { const o = getVariable('Order'); o.Status = 'Paid'; return [][Symbol.iterator](); } }; new Set(xs); return 1;")]
    [InlineData("const xs = { [Symbol.iterator]() { const o = getVariable('Order'); o.Status = 'Paid'; return [][Symbol.iterator](); } }; new Map(xs); return 1;")]
    [InlineData("const o = getVariable('Order'); let target = {}; const shared = { get x() { target.Status = 'Paid'; return 1; } }; JSON.stringify({ first: shared, switcher: { toJSON() { target = o; return 0; } }, second: shared }); return 1;")]
    [InlineData("const o = getVariable('Order'); let target = {}; const h = { get first() { target = o; return 1; }, second: { toJSON() { target.Status = 'Paid'; return 0; } } }; JSON.stringify(h); return 1;")]
    [InlineData("const o = getVariable('Order'); let target = {}; JSON.stringify({ 2: { toJSON() { target.Status = 'Paid'; return 0; } }, 1: { toJSON() { target = o; return 0; } } }); return 1;")]
    [InlineData("const o = getVariable('Order'); const h = { get first() { this.second = o; return 1; }, second: {} }; const copy = { ...h }; copy.second.Status = 'Paid'; return 1;")]
    [InlineData("const o = getVariable('Order'); let target = {}; Object.values({ get 2() { target.Status = 'Paid'; return 0; }, get 1() { target = o; return 0; } }); return 1;")]
    [InlineData("const o = getVariable('Order'); let target = {}; Object.assign({}, { get 2() { target.Status = 'Paid'; return 0; }, get 1() { target = o; return 0; } }); return 1;")]
    [InlineData("const o = getVariable('Order'); const search = { toString() { o.Status = 'Paid'; return 'x'; } }; return 'x'.slice(0).includes(search);")]
    [InlineData("const o = getVariable('Order'); const search = { toString() { o.Status = 'Paid'; return 'x'; } }; return 'x'.concat('y').includes(search);")]
    [InlineData("const o = getVariable('Order'); const h = {}; Object.defineProperty(h, 'x', { get() { o.Status = 'Paid'; return 1; } }); try { delete h.x; } catch {} return h.x;")]
    [InlineData("const h = {}; Object.defineProperty(h, 'x', { get() { const o = getVariable('Order'); o.Status = 'Paid'; return 1; } }); try { delete h.x; } catch {} return h.x;")]
    [InlineData("const o = getVariable('Order'); class H { get x() { o.Status = 'Paid'; return 1; } } return new H().x;")]
    [InlineData("const o = getVariable('Order'); class H { get x() { o.Status = 'Paid'; return 1; } } class C extends H {} return new C().x;")]
    [InlineData("const o = getVariable('Order'); const h = { toJSON() { o.Status = 'Paid'; return 1; } }; return JSON.stringify([h]);")]
    [InlineData("const o = getVariable('Order'); const h = { x: o }; if (getVariable('Total')) delete h.x; h.x.Status = 'Paid'; return 1;")]
    [InlineData("const o = getVariable('Order'); const a = { x: o }, b = { x: {} }; const h = getVariable('Total') ? a : b; delete h.x; a.x.Status = 'Paid'; return 1;")]
    [InlineData("const o = getVariable('Order'); class H { get x() { return o; } } const h = new H(); h.x = {}; h.x.Status = 'Paid'; return 1;")]
    [InlineData("const o = getVariable('Order'); class H { get x() { o.Status = 'Paid'; return 1; } } unknown(new H()); return 1;")]
    [InlineData("const o = getVariable('Order'); const h = {}; Object.defineProperty(h, 'x', { value: o }); h.x = {}; try { delete h.x; } catch {} h.x.Status = 'Paid'; return 1;")]
    [InlineData("const h = { x: getVariable('Order') }; try { Object.defineProperty(h, 'x', undefined); } catch {} h.x.Status = 'Paid'; return 1;")]
    [InlineData("const h = { x: getVariable('Order') }; try { Object.defineProperty(h, 'x', { get: 1 }); } catch {} h.x.Status = 'Paid'; return 1;")]
    [InlineData("const h = { o: getVariable('Order') }; Object.defineProperty(h, 'o', { enumerable: true }); h.o.Status = 'Paid'; return 1;")]
    [InlineData("const o = getVariable('Order'); const h = { get x() { o.Status = 'Paid'; return 1; } }; Object.defineProperty(h, 'x', { enumerable: true }); return h.x;")]
    [InlineData("const o = getVariable('Order'); class H { get x() { o.Status = 'Paid'; return 1; } } const h = new H(); if (getVariable('Total')) Object.defineProperty(h, 'x', { value: 1 }); return h.x;")]
    [InlineData("const o = getVariable('Order'); class Base { static get x() { o.Status = 'Paid'; return 1; } } class Derived extends Base {} return Derived.x;")]
    [InlineData("const o = getVariable('Order'); const h = { __proto__: { get x() { o.Status = 'Paid'; return 1; } } }; return h.x;")]
    [InlineData("const o = getVariable('Order'); const h = { ['__proto__']: { get x() { o.Status = 'Paid'; return 1; } } }; return JSON.stringify(h);")]
    [InlineData("const o = getVariable('Order'); const __proto__ = { get x() { o.Status = 'Paid'; return 1; } }; return JSON.stringify({ __proto__ });")]
    [InlineData("const o = getVariable('Order'); const a = []; Object.defineProperty(a, '0', { get() { o.Status = 'Paid'; return 1; } }); return JSON.stringify(a);")]
    [InlineData("const o = getVariable('Order'); const a = []; Object.defineProperty(a, '0', { get() { o.Status = 'Paid'; return 1; } }); return a.join(',');")]
    [InlineData("class Base { constructor() { const o = getVariable('Order'); o.Status = 'Paid'; } } class Derived extends Base {} new Derived(); return 1;")]
    [InlineData("const o = getVariable('Order'); class Base { write() { o.Status = 'Paid'; } } class Derived extends Base { write() { super.write(); } } new Derived().write(); return 1;")]
    [InlineData("class Base { constructor(o) { o.Status = 'Paid'; } } class Derived extends Base { constructor() { super(getVariable('Order')); } } new Derived(); return 1;")]
    [InlineData("const o = getVariable('Order'); class Base { get x() { this.o.Status = 'Paid'; return 1; } } class Derived extends Base { constructor() { super(); this.o = o; } read() { return super.x; } } return new Derived().read();")]
    [InlineData("const h = { o: getVariable('Order') }; const alias = h.valueOf(); alias.o.Status = 'Paid'; return 1;")]
    [InlineData("const set = setVariable; set('Total', 1); return 1;")]
    [InlineData("(fn => fn('Total', 1))(setVariable); return 1;")]
    [InlineData("const h = { set: setVariable }; h.set('Total', 1); return 1;")]
    [InlineData("const fns = [setVariable]; fns[0]('Total', 1); return 1;")]
    [InlineData("['Total'].forEach(setVariable); return 1;")]
    [InlineData("function identity(fn) { return fn; } identity(setVariable)('Total', 1); return 1;")]
    [InlineData("const o = getVariable('Order'); class Base { set x(value) { this.o.Status = 'Paid'; } } class Derived extends Base { constructor() { super(); this.o = o; } write() { super.x = 1; } } new Derived().write(); return 1;")]
    [InlineData("const o = getVariable('Order'); class Base { get x() { this.o.Status = 'Paid'; return 1; } set x(value) {} } class Derived extends Base { constructor() { super(); this.o = o; } write() { super.x++; } } new Derived().write(); return 1;")]
    [InlineData("const h = JSON.parse('{}'); h.o = getVariable('Order'); h.o.Status = 'Paid'; return 1;")]
    [InlineData("const h = JSON.parse('{\"child\":{}}'); h.child.o = getVariable('Order'); h.child.o.Status = 'Paid'; return 1;")]
    [InlineData("const h = Object.create(null); h.o = getVariable('Order'); h.o.Status = 'Paid'; return 1;")]
    [InlineData("const p = { o: getVariable('Order') }; const h = { __proto__: p }; const alias = Object.getPrototypeOf(h); alias.o.Status = 'Paid'; return 1;")]
    [InlineData("const p = { o: getVariable('Order') }; const h = { __proto__: p }; const alias = Reflect.getPrototypeOf(h); alias.o.Status = 'Paid'; return 1;")]
    [InlineData("const o = getVariable('Order'); const a = []; const h = { valueOf() { o.Status = 'Paid'; return 0; } }; a.length = h; return 1;")]
    [InlineData("const o = getVariable('Order'); let target = {}; const a = []; const h = { valueOf() { const old = target; target = o; old.Status = 'Paid'; return 0; } }; a.length = h; return 1;")]
    [InlineData("const o = getVariable('Order'); const a = []; Object.defineProperty(a, '0', { get() { o.Status = 'Paid'; return 1; } }); try { a.length = 0; } catch {} return JSON.stringify(a);")]
    [InlineData("const h = Object(1); h.o = getVariable('Order'); h.o.Status = 'Paid'; return 1;")]
    [InlineData("const o = getVariable('Order'); const h = JSON.parse('{\"x\":1}', function (key, value) { this.o = o; return value; }); h.o.Status = 'Paid'; return 1;")]
    [InlineData("eval(\"setVariable('Total', 1)\"); return 1;")]
    [InlineData("const e = eval; e(\"setVariable('Total', 1)\"); return 1;")]
    [InlineData("(e => e(\"setVariable('Total', 1)\"))(eval); return 1;")]
    [InlineData("new Function(\"setVariable('Total', 1)\")(); return 1;")]
    [InlineData("const F = Function; F(\"setVariable('Total', 1)\")(); return 1;")]
    [InlineData("const g = globalThis; g.setVariable('Total', 1); return 1;")]
    [InlineData("const g = globalThis; const set = g.setVariable; set('Total', 1); return 1;")]
    [InlineData("(() => {}).constructor(\"setVariable('Total', 1)\")(); return 1;")]
    [InlineData("({}).constructor.constructor(\"setVariable('Total', 1)\")(); return 1;")]
    [InlineData("function holder() {} holder.o = getVariable('Order'); holder.o.Status = 'Paid'; return 1;")]
    [InlineData("function holder() {} holder.prototype.o = getVariable('Order'); holder.prototype.o.Status = 'Paid'; return 1;")]
    [InlineData("const input = { [Symbol.iterator]() { return { next() { const o = getVariable('Order'); o.Status = 'Paid'; return { done: true }; } }; } }; return Object.fromEntries(input);")]
    [InlineData("[getVariable('Order'), {}].reduce((acc, x) => { acc.Status = 'Paid'; return x; }, {}); return 1;")]
    [InlineData("[{}, getVariable('Order')].reduceRight((acc, x) => { acc.Status = 'Paid'; return x; }, {}); return 1;")]
    [InlineData("let o = {}; [getVariable('Order'), {}].forEach(x => { o.Status = 'Paid'; o = x; }); return 1;")]
    [InlineData("let o = {}; Array.from([getVariable('Order'), {}], x => { o.Status = 'Paid'; o = x; return 0; }); return 1;")]
    [InlineData("let o = {}; [3, 2, 1].sort(() => { o.Status = 'Paid'; o = getVariable('Order'); return 0; }); return 1;")]
    [InlineData("let o = {}; [3, 2, 1].toSorted(() => { o.Status = 'Paid'; o = getVariable('Order'); return 0; }); return 1;")]
    [InlineData("let o = {}; 'xx'.replaceAll('x', () => { o.Status = 'Paid'; o = getVariable('Order'); return 'x'; }); return 1;")]
    [InlineData("let o = {}; 'xx'.replace(/x/g, () => { o.Status = 'Paid'; o = getVariable('Order'); return 'x'; }); return 1;")]
    [InlineData("let o = {}; const result = { valueOf() { o.Status = 'Paid'; o = getVariable('Order'); return 0; } }; [3, 2, 1].sort(() => result); return 1;")]
    [InlineData("let o = {}; const result = { toString() { o.Status = 'Paid'; o = getVariable('Order'); return 'x'; } }; 'xx'.replaceAll('x', () => result); return 1;")]
    [InlineData("let o = {}; const value = { toString() { o.Status = 'Paid'; o = getVariable('Order'); return 'x'; } }; [value, value, value].sort(); return 1;")]
    [InlineData("const a = [getVariable('Order')]; a.push({}); let o = {}; a.forEach(x => { o.Status = 'Paid'; o = x; }); return 1;")]
    [InlineData("let o = {}, next = getVariable('Order'); [1, 2].some(() => { o = next; next = {}; return true; }); o.Status = 'Paid'; return 1;")]
    [InlineData("let o = {}, next = getVariable('Order'); [1, 2].every(() => { o = next; next = {}; return false; }); o.Status = 'Paid'; return 1;")]
    [InlineData("let o = {}, next = getVariable('Order'); [1, 2].find(() => { o = next; next = {}; return true; }); o.Status = 'Paid'; return 1;")]
    [InlineData("let o = {}, next = getVariable('Order'); [1, ,].forEach(() => { o = next; next = {}; }); o.Status = 'Paid'; return 1;")]
    [InlineData("let o = {}, next = getVariable('Order'); [1, 2].forEach((x, i, a) => { o = next; next = {}; a.length = 0; }); o.Status = 'Paid'; return 1;")]
    [InlineData("let o = {}; let step = () => { o = getVariable('Order'); step = () => { o = {}; }; return true; }; [1, 2].some(() => step()); o.Status = 'Paid'; return 1;")]
    [InlineData("const o = [].reduce(() => ({}), getVariable('Order')); o.Status = 'Paid'; return 1;")]
    [InlineData("const owned = getVariable('Order'); let o = {}; const a = []; Object.defineProperty(a, '0', { get() { o.Status = 'Paid'; o = owned; return 1; } }); [1, 2].flatMap(() => a); return 1;")]
    [InlineData("const owned = getVariable('Order'); let o = {}; const a = Reflect.construct(Array, []); Object.defineProperty(a, '0', { get() { o.Status = 'Paid'; o = owned; return 1; } }); [1, 2].flatMap(() => a); return 1;")]
    [InlineData("const h = new Object(1); h.o = getVariable('Order'); h.o.Status = 'Paid'; return 1;")]
    [InlineData("const owned = getVariable('Order'); const original = {}; const h = new Object(getVariable('Total') ? original : 1); h.o = owned; original.o = {}; h.o.Status = 'Paid'; return 1;")]
    [InlineData("const owned = getVariable('Order'); const original = {}; let input = original; if (getVariable('Total')) input = 1; const h = new Object(input); h.o = owned; original.o = {}; h.o.Status = 'Paid'; return 1;")]
    [InlineData("const owned = getVariable('Order'); const original = {}; function choose() { if (getVariable('Total')) return original; } const h = new Object(choose()); h.o = owned; original.o = {}; h.o.Status = 'Paid'; return 1;")]
    [InlineData("const owned = getVariable('Order'); const original = {}; const holder = {}; if (getVariable('Total')) holder.value = original; const h = new Object(holder.value); h.o = owned; original.o = {}; h.o.Status = 'Paid'; return 1;")]
    [InlineData("const owned = getVariable('Order'); const original = {}; const h = new Object(getVariable('Total') && original); h.o = owned; original.o = {}; h.o.Status = 'Paid'; return 1;")]
    [InlineData("const owned = getVariable('Order'); const original = {}; const holder = { value: original }; const maybe = getVariable('Total') ? holder : null; const h = new Object(maybe?.value); h.o = owned; original.o = {}; h.o.Status = 'Paid'; return 1;")]
    [InlineData("const owned = getVariable('Order'); const original = {}; const a = [1].map(() => original); const h = new Object(a.at(1)); h.o = owned; original.o = {}; h.o.Status = 'Paid'; return 1;")]
    [InlineData("const owned = getVariable('Order'); const original = {}; const a = [1].map(() => original); const h = new Object(a.find(() => false)); h.o = owned; original.o = {}; h.o.Status = 'Paid'; return 1;")]
    [InlineData("const owned = getVariable('Order'); const original = {}; const a = [1].map(() => original); const h = new Object(a.findLast(() => false)); h.o = owned; original.o = {}; h.o.Status = 'Paid'; return 1;")]
    [InlineData("const owned = getVariable('Order'); const original = {}; const a = [1].map(() => original); a.length = 0; const h = new Object(a.pop()); h.o = owned; original.o = {}; h.o.Status = 'Paid'; return 1;")]
    [InlineData("const owned = getVariable('Order'); const original = {}; const a = [1].map(() => original); a.length = 0; const h = new Object(a.shift()); h.o = owned; original.o = {}; h.o.Status = 'Paid'; return 1;")]
    [InlineData("const owned = getVariable('Order'); let o = {}; function receiver() { o = owned; return null; } receiver()?.x(o = {}); o.Status = 'Paid'; return 1;")]
    [InlineData("let o = {}; ((o = getVariable('Order')), null)?.[(o = {})]; o.Status = 'Paid'; return 1;")]
    [InlineData("const owned = getVariable('Order'); let o = {}; const h = { get fn() { o = owned; return null; } }; h.fn?.(o = {}); o.Status = 'Paid'; return 1;")]
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
    [InlineData("let o = {}; const alias = o; o = getVariable('Order'); alias.Status = 'Paid'; return 1;")]
    [InlineData("function read(x) { return x.Status; } return read(getVariable('Order'));")]
    [InlineData("function identity(x) { return x; } return identity(getVariable('Order'));")]
    [InlineData("function copy(x) { return { ...x }; } const o = copy(getVariable('Order')); o.Status = 'Paid'; return o;")]
    [InlineData("function mutate(x) { x.Status = 'Paid'; } mutate({}); return 1;")]
    [InlineData("let o = {}; function mutate() { o.Status = 'Paid'; } mutate(); o = getVariable('Order'); return o;")]
    [InlineData("function unused() { getVariable('Order').Lines.push(1); } return 1;")]
    [InlineData("const o = getVariable('Order'); return o.Lines.map(x => x.Status);")]
    [InlineData("const o = getVariable('Order'); o.Lines.map(() => ({}))[0].Status = 'Paid'; return 1;")]
    [InlineData("let o = getVariable('Order'); do { o = {}; } while (false); o.Status = 'Paid'; return 1;")]
    [InlineData("let o = getVariable('Order'); try { o = {}; } catch (e) { o = {}; } finally { o = {}; } o.Status = 'Paid'; return 1;")]
    [InlineData("let o = {}; while (getVariable('Total')) { try { o = getVariable('Order'); break; } finally { o = {}; } } o.Status = 'Paid'; return 1;")]
    [InlineData("function read(s, o) { return o.Status; } return read`${getVariable('Order')}`;")]
    [InlineData("const h = { o: getVariable('Order'), read() { return this.o.Status; } }; return h.read();")]
    [InlineData("const h = { get x() { return getVariable('Order').Status; } }; return h.x;")]
    [InlineData("function Read(o) { this.status = o.Status; } return new Read(getVariable('Order'));")]
    [InlineData("let o = {}; o.Status = (o = getVariable('Order'), 'Paid'); return 1;")]
    [InlineData("const o = getVariable('Order'); return new String(o.Status);")]
    [InlineData("return Reflect.apply(o => o.Status, null, [getVariable('Order')]);")]
    [InlineData("return Reflect.get(getVariable('Order'), 'Status');")]
    [InlineData("return Object.getOwnPropertyDescriptor(getVariable('Order'), 'Lines').value;")]
    [InlineData("function mutate(...xs) { xs[0].Status = 'Paid'; } mutate({}); return 1;")]
    [InlineData("function read(...xs) { return xs[0].Status; } return read(getVariable('Order'));")]
    [InlineData("function read(o) { return o.Status; } return read(...[getVariable('Order')]);")]
    [InlineData("const o = getVariable('Order'); return [2, 1].sort((a, b) => o.Status ? a - b : b - a);")]
    [InlineData("const o = getVariable('Order'); return [2, 1].toSorted((a, b) => o.Status ? a - b : b - a);")]
    [InlineData("const o = getVariable('Order'); const h = { get x() { return o.Status; } }; return { ...h };")]
    [InlineData("const o = getVariable('Order'); const h = { get x() { return o.Status; } }; return Object.values(h);")]
    [InlineData("const o = getVariable('Order'); return JSON.stringify({ toJSON() { return o.Status; } });")]
    [InlineData("let o = {}; o ||= getVariable('Order'); return o;")]
    [InlineData("function key() { return getVariable('Order').Status; } const { [key()]: x } = {}; return x;")]
    [InlineData("let o = getVariable('Order'); outer: { o = {}; break outer; } o.Status = 'Paid'; return 1;")]
    [InlineData("const a = { o: getVariable('Order') }; a.o = {}; a.o.Status = 'Paid'; return 1;")]
    [InlineData("const Object = { keys(o) { return o.Status; } }; return Object.keys(getVariable('Order'));")]
    [InlineData("class C { constructor() { this.status = 'Paid'; } } return new C();")]
    [InlineData("class C { read(o) { return o.Status; } } return new C().read(getVariable('Order'));")]
    [InlineData("const o = getVariable('Order'); const h = { get x() { o.Status = 'Paid'; return 1; } }; return Object.getOwnPropertyDescriptor(h, 'x');")]
    [InlineData("const o = getVariable('Order'); return JSON.stringify({ toJSON() { return 1; }, get x() { o.Status = 'Paid'; return 1; } });")]
    [InlineData("function mutate(...xs) { xs[1].Status = 'Paid'; } mutate(...[{}, {}].slice()); return 1;")]
    [InlineData("function read(...xs) { return xs[1].Status; } return read(...[{}, getVariable('Order')].slice());")]
    [InlineData("function read() { return arguments[0].Status; } return read(getVariable('Order'));")]
    [InlineData("function read() { const get = () => arguments[0].Status; return get(); } return read(getVariable('Order'));")]
    [InlineData("const o = getVariable('Order'), k = 'x'; const h = { get [k]() { return o.Status; } }; return h.x;")]
    [InlineData("const k = 'x'; const h = { set [k](x) { this.local = x; } }; h.x = 1; return h;")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { o.Status = 'Paid'; return 1; } }; return h === h;")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { o.Status = 'Paid'; return 1; } }; return !h;")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { o.Status = 'Paid'; return 1; } }; return Number.isFinite(h);")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { o.Status = 'Paid'; return 1; } }; return Number.isNaN(h);")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { o.Status = 'Paid'; return 1; } }; return Object.keys(h);")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { o.Status = 'Paid'; return 1; } }; return Object.is(h, h);")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { return 1; }, toString() { o.Status = 'Paid'; return 'x'; } }; return h + 1;")]
    [InlineData("const o = getVariable('Order'); return 'x'.replace('x', () => o.Status);")]
    [InlineData("const o = getVariable('Order'); return 'x'.replaceAll('x', () => o.Status);")]
    [InlineData("[0].forEach(function () { return this.Status; }, getVariable('Order')); return 1;")]
    [InlineData("return Array.from([0], function () { return this.Status; }, getVariable('Order'));")]
    [InlineData("const h = { get x() { return this.Status; } }; return Reflect.get(h, 'x', getVariable('Order'));")]
    [InlineData("function* gen() { yield { Status: 'Draft' }; } const o = gen().next().value; o.Status = 'Paid'; return 1;")]
    [InlineData("function* unused() { yield getVariable('Order'); } return 1;")]
    [InlineData("const o = getVariable('Order'); const h = { toString() { return o.Status; } }; return new String(h);")]
    [InlineData("const h = { valueOf() { return 1; } }; return new Number(h);")]
    [InlineData("const h = { toString() { return 'x'; } }; return new RegExp(h);")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { o.Status = 'Paid'; return 1; }, toString() { o.Status = 'Paid'; return 'x'; } }; return new Boolean(h);")]
    [InlineData("const o = getVariable('Order'); const h = { toString() { return o.Status; } }; return `${h}`;")]
    [InlineData("const o = getVariable('Order'); const k = { toString() { return o.Status; } }; return ({})[k];")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { o.Status = 'Paid'; return 1; } }; return [h].includes(h);")]
    [InlineData("const h = { valueOf() { return 10; } }; return Number.parseInt('1', h);")]
    [InlineData("const h = { valueOf() { return 0; } }; return 'x'.charAt(h);")]
    [InlineData("const h = { valueOf() { return 0; } }; return [].slice(h);")]
    [InlineData("const o = getVariable('Order'); const h = { toString() { return o.Status; } }; return [h].join(',');")]
    [InlineData("const h = { valueOf() { return 0; } }; return [2, 1].sort(() => h);")]
    [InlineData("let o = {}; o.Status ||= (o = getVariable('Order'), 'Paid'); return 1;")]
    [InlineData("let o = {}; o.Status &&= (o = getVariable('Order'), 'Paid'); return 1;")]
    [InlineData("let o = {}; o.Status ??= (o = getVariable('Order'), 'Paid'); return 1;")]
    [InlineData("const o = getVariable('Order'); return JSON.stringify({ o }, function (key, value) { return this.o ? this.o.Status : value; });")]
    [InlineData("const o = getVariable('Order'); const xs = { *[Symbol.iterator]() { const status = o.Status; yield 1; } }; for (const x of xs) {} return 1;")]
    [InlineData("const xs = { *[Symbol.iterator]() { const o = {}; o.Status = 'Paid'; yield o; } }; return [...xs];")]
    [InlineData("const o = getVariable('Order'); const xs = { *[Symbol.iterator]() { const status = o.Status; yield 1; } }; const [x] = xs; return x;")]
    [InlineData("const o = getVariable('Order'); const xs = { *[Symbol.iterator]() { const status = o.Status; yield 1; } }; return Array.from(xs);")]
    [InlineData("const o = getVariable('Order'); const search = { toString() { o.Status = 'Paid'; return 'x'; } }; return [search].includes(search);")]
    [InlineData("const o = getVariable('Order'); const search = { toString() { o.Status = 'Paid'; return 'x'; } }; return [search].indexOf(search);")]
    [InlineData("const o = getVariable('Order'); const search = { toString() { o.Status = 'Paid'; return 'x'; } }; return [search].lastIndexOf(search);")]
    [InlineData("const o = getVariable('Order'); const h = { toString() { return o.Status; } }; return [h].toString();")]
    [InlineData("const o = getVariable('Order'); const h = { toLocaleString() { return o.Status; } }; return [h].toLocaleString('en-US');")]
    [InlineData("const o = getVariable('Order'); const a = []; a.join = () => o.Status; return a.toString();")]
    [InlineData("const xs = { [Symbol.iterator]() { return { next() { return { value: 1, done: false }; }, return() { return { done: true }; } }; } }; for (const x of xs) { break; } return 1;")]
    [InlineData("const xs = { [Symbol.iterator]() { return { next() { return { value: 1, done: false }; }, return() { return { done: true }; } }; } }; const [x] = xs; return x;")]
    [InlineData("const o = getVariable('Order'); const xs = { *[Symbol.iterator]() { const status = o.Status; yield 1; } }; return new Set(xs);")]
    [InlineData("const o = getVariable('Order'); let target = {}; const shared = { get x() { return target.Status; } }; return JSON.stringify({ first: shared, switcher: { toJSON() { target = o; return 0; } }, second: shared });")]
    [InlineData("const o = getVariable('Order'); let target = o; JSON.stringify({ 2: { toJSON() { target.Status = 'Paid'; return 0; } }, 1: { toJSON() { target = {}; return 0; } } }); return 1;")]
    [InlineData("const o = getVariable('Order'); let target = o; Object.values({ get 2() { target.Status = 'Paid'; return 0; }, get 1() { target = {}; return 0; } }); return 1;")]
    [InlineData("const o = getVariable('Order'); const search = { toString() { o.Status = 'Paid'; return 'x'; } }; return [search].slice(0).includes(search);")]
    [InlineData("const o = getVariable('Order'); const search = { toString() { o.Status = 'Paid'; return 'x'; } }; return [search].concat([]).includes(search);")]
    [InlineData("const o = getVariable('Order'); const h = { get x() { o.Status = 'Paid'; return 1; } }; delete h.x; return JSON.stringify(h);")]
    [InlineData("const o = getVariable('Order'); const h = { set x(value) { o.Status = 'Paid'; } }; delete h.x; return JSON.stringify(h);")]
    [InlineData("const o = getVariable('Order'); const h = { x: o }; delete h.x; h.x.Status = 'Paid'; return 1;")]
    [InlineData("const o = getVariable('Order'); class H { get x() { o.Status = 'Paid'; return 1; } } return JSON.stringify(new H());")]
    [InlineData("const o = getVariable('Order'); class H { get x() { o.Status = 'Paid'; return 1; } } class C extends H {} return JSON.stringify(new C());")]
    [InlineData("const o = getVariable('Order'); class H { get x() { o.Status = 'Paid'; return 1; } } const h = new H(); delete h.x; return JSON.stringify(h);")]
    [InlineData("const o = getVariable('Order'); class H { get x() { o.Status = 'Paid'; return 1; } } const h = new H(); Object.defineProperty(h, 'x', { value: {}, enumerable: true, configurable: true, writable: true }); return JSON.stringify(h);")]
    [InlineData("const o = getVariable('Order'); const a = []; a.extra = { toJSON() { o.Status = 'Paid'; return 1; } }; return JSON.stringify(a);")]
    [InlineData("const o = getVariable('Order'); const a = []; Object.defineProperty(a, 'extra', { get() { o.Status = 'Paid'; return 1; } }); return JSON.stringify(a);")]
    [InlineData("const h = { x: 0 }; while (false) { delete h.x; h.x = 1; } return 1;")]
    [InlineData("const o = getVariable('Order'); const h = {}; Object.defineProperty(h, 'x', { get() { o.Status = 'Paid'; return 1; }, configurable: true }); delete h.x; return JSON.stringify(h);")]
    [InlineData("const o = getVariable('Order'); const h = { get x() { return o.Status; } }; Object.defineProperty(h, 'x', { enumerable: true }); return h.x;")]
    [InlineData("const o = getVariable('Order'); class H { get x() { return o.Status; } } const h = new H(); if (getVariable('Total')) Object.defineProperty(h, 'x', { value: 1 }); return h.x;")]
    [InlineData("const o = getVariable('Order'); class Base { static get x() { return o.Status; } } class Derived extends Base {} return Derived.x;")]
    [InlineData("const o = getVariable('Order'); const h = { __proto__: { get x() { o.Status = 'Paid'; return 1; } } }; return JSON.stringify(h);")]
    [InlineData("const o = getVariable('Order'); const a = []; Object.defineProperty(a, '0', { get() { return o.Status; } }); return JSON.stringify(a);")]
    [InlineData("class Base { constructor(o) { o.Status = 'Paid'; } } class Derived extends Base {} new Derived({}); return 1;")]
    [InlineData("const o = getVariable('Order'); class Base { read() { return o.Status; } } class Derived extends Base { read() { return super.read(); } } return new Derived().read();")]
    [InlineData("class Base { constructor(o) { o.Status = 'Paid'; } } class Derived extends Base { constructor() { super({}); } } new Derived(); return 1;")]
    [InlineData("const h = { o: getVariable('Order') }; return h.valueOf().o.Status;")]
    [InlineData("const h = new Number(1); h.o = getVariable('Order'); const alias = h.valueOf(); alias.o.Status = 'Paid'; return 1;")]
    [InlineData("class Base { constructor(o) { this.status = o.Status; } } class Derived extends Base { constructor() { super(getVariable('Order')); } } return new Derived();")]
    [InlineData("const set = setVariable; return 1;")]
    [InlineData("const h = { set: setVariable }; return 1;")]
    [InlineData("function identity(fn) { return fn; } const set = identity(setVariable); return 1;")]
    [InlineData("function setVariable(name, value) { return value; } return setVariable('Total', 1);")]
    [InlineData("class Base { set x(value) { this.o.Status = 'Paid'; } } class Derived extends Base { constructor() { super(); this.o = {}; } write() { super.x = 1; } } new Derived().write(); return 1;")]
    [InlineData("const h = JSON.parse('{}'); h.o = {}; h.o.Status = 'Paid'; return h;")]
    [InlineData("const h = Object.create(null); h.o = {}; h.o.Status = 'Paid'; return h;")]
    [InlineData("const p = { o: {} }; const h = { __proto__: p }; const alias = Object.getPrototypeOf(h); alias.o.Status = 'Paid'; return 1;")]
    [InlineData("const o = getVariable('Order'); const a = []; const h = { valueOf() { const status = o.Status; return 0; } }; a.length = h; return 1;")]
    [InlineData("const o = getVariable('Order'); const a = []; Object.defineProperty(a, '0', { get() { o.Status = 'Paid'; return 1; }, configurable: true }); a.length = 0; return JSON.stringify(a);")]
    [InlineData("const o = getVariable('Order'); const a = [{ toJSON() { o.Status = 'Paid'; return 1; } }]; a.length = 0; return JSON.stringify(a);")]
    [InlineData("const o = getVariable('Order'); const h = JSON.parse('{}', function (key, value) { this.o = o; return value; }); h.o.Status = 'Paid'; return 1;")]
    [InlineData("const h = JSON.parse('1'); h.o = getVariable('Order'); h.o.Status = 'Paid'; return 1;")]
    [InlineData("const o = getVariable('Order'); return JSON.parse('{\"x\":1}', function (key, value) { const status = o.Status; return value; });")]
    [InlineData("const e = eval; return 1;")]
    [InlineData("const F = Function; return 1;")]
    [InlineData("const helpers = { eval(text) { return text; } }; return helpers.eval('read only');")]
    [InlineData("function Function(text) { this.text = text; } return new Function('read only');")]
    [InlineData("const globalThis = { setVariable(name, value) { return value; } }; return globalThis.setVariable('Total', 1);")]
    [InlineData("const h = { constructor(value) { return value; } }; return h.constructor(1);")]
    [InlineData("function holder() {} holder.o = getVariable('Order'); return holder.o.Status;")]
    [InlineData("const o = getVariable('Order'); function holder() {} Object.defineProperty(holder, 'x', { get() { o.Status = 'Paid'; return 1; } }); return JSON.stringify(holder);")]
    [InlineData("const input = { [Symbol.iterator]() { return { next() { return { done: true }; } }; } }; return Object.fromEntries(input);")]
    [InlineData("[getVariable('Order'), {}].reduce((acc, x) => { const status = acc.Status; return x; }, {}); return 1;")]
    [InlineData("let o = {}; [getVariable('Order'), {}].forEach(x => { o = {}; o.Status = 'Paid'; o = x; }); return 1;")]
    [InlineData("let o = {}; Array.from([getVariable('Order'), {}], x => { const status = o.Status; o = x; return 0; }); return 1;")]
    [InlineData("let o = {}; [3, 2, 1].sort(() => { o = {}; o.Status = 'Paid'; o = getVariable('Order'); return 0; }); return 1;")]
    [InlineData("let o = {}; 'xx'.replaceAll('x', () => { o = {}; o.Status = 'Paid'; o = getVariable('Order'); return 'x'; }); return 1;")]
    [InlineData("let o = {}; [getVariable('Order')].forEach(x => { o.Status = 'Paid'; o = x; }); return 1;")]
    [InlineData("const o = getVariable('Order'); [].forEach(() => { o.Status = 'Paid'; }); return 1;")]
    [InlineData("[getVariable('Order')].reduce((acc, x) => { acc.Status = 'Paid'; return x; }, {}); return 1;")]
    [InlineData("const o = getVariable('Order'); [o].reduce(() => { o.Status = 'Paid'; }); return 1;")]
    [InlineData("let o = {}; Array.from([getVariable('Order')], x => { o.Status = 'Paid'; o = x; return 0; }); return 1;")]
    [InlineData("const o = getVariable('Order'); [1].sort(() => { o.Status = 'Paid'; return 0; }); return 1;")]
    [InlineData("const o = getVariable('Order'); [{ toString() { o.Status = 'Paid'; return 'x'; } }].sort(); return 1;")]
    [InlineData("const o = getVariable('Order'); [].toSorted(() => { o.Status = 'Paid'; return 0; }); return 1;")]
    [InlineData("let o = {}, next = getVariable('Order'); Array.from([1, 2], () => { o = next; next = {}; }); o.Status = 'Paid'; return 1;")]
    [InlineData("const o = [1].reduce(() => ({}), getVariable('Order')); o.Status = 'Paid'; return 1;")]
    [InlineData("const o = [1].reduceRight(() => ({}), getVariable('Order')); o.Status = 'Paid'; return 1;")]
    [InlineData("const o = [getVariable('Order'), 1].reduce(() => ({})); o.Status = 'Paid'; return 1;")]
    [InlineData("const owned = getVariable('Order'); const a = []; Object.defineProperty(a, '0', { get() { return owned.Status; } }); return [1, 2].flatMap(() => a);")]
    [InlineData("const owned = getVariable('Order'); let o = {}; const a = []; Object.defineProperty(a, '0', { get() { o = {}; o.Status = 'Paid'; o = owned; return 1; } }); [1, 2].flatMap(() => a); return 1;")]
    [InlineData("const o = getVariable('Order'); const h = { get x() { o.Status = 'Paid'; return 1; } }; return [1, 2].flatMap(() => h);")]
    [InlineData("const o = getVariable('Order'); const h = { get 0() { o.Status = 'Paid'; return 1; } }; return [1, 2].flatMap(() => h);")]
    [InlineData("const o = getVariable('Order'); const a = []; Object.defineProperty(a, 'extra', { get() { o.Status = 'Paid'; return 1; } }); return [1, 2].flatMap(() => a);")]
    [InlineData("const o = getVariable('Order'); const h = Object.create(null); Object.defineProperty(h, '0', { get() { o.Status = 'Paid'; return 1; } }); return [1].flatMap(() => h);")]
    [InlineData("const o = getVariable('Order'); const h = Object.create([]); Object.defineProperty(h, '0', { get() { o.Status = 'Paid'; return 1; } }); return [1].flatMap(() => h);")]
    [InlineData("const o = getVariable('Order'); class H { get 0() { o.Status = 'Paid'; return 1; } } const h = new H(); return [1].flatMap(() => h);")]
    [InlineData("const o = getVariable('Order'); class Base { get 0() { o.Status = 'Paid'; return 1; } } class H extends Base {} const h = new H(); return [1].flatMap(() => h);")]
    [InlineData("const o = getVariable('Order'); const h = JSON.parse('{}'); Object.defineProperty(h, '0', { get() { o.Status = 'Paid'; return 1; } }); return [1].flatMap(() => h);")]
    [InlineData("const o = getVariable('Order'); const h = new Object(1); Object.defineProperty(h, '0', { get() { o.Status = 'Paid'; return 1; } }); return [1].flatMap(() => h);")]
    [InlineData("const owned = getVariable('Order'); const original = {}; const h = new Object(original); h.o = owned; original.o = {}; h.o.Status = 'Paid'; return 1;")]
    [InlineData("const owned = getVariable('Order'); const original = {}; function identity(value) { return value; } const h = new Object(identity(original)); h.o = owned; original.o = {}; h.o.Status = 'Paid'; return 1;")]
    [InlineData("const owned = getVariable('Order'); const original = {}; const holder = { value: original }; const h = new Object(holder?.value); h.o = owned; original.o = {}; h.o.Status = 'Paid'; return 1;")]
    [InlineData("const owned = getVariable('Order'); let o = owned; function receiver() { o = {}; return null; } receiver()?.x(); o.Status = 'Paid'; return 1;")]
    public async Task A_reusable_script_can_read_aliases_and_mutate_local_copies(string source)
    {
        var root = Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal(source))));
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root, variables: [Total, Order]), isReusable: true);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Theory]
    [InlineData("return toJson(getVariable('Order'));")]
    [InlineData("return stringToBase64(getVariable('Order').Status);")]
    [InlineData("return stringFromBase64(getVariable('Order').Status);")]
    [InlineData("return parseGuid(getVariable('Order').Status);")]
    [InlineData("const o = getVariable('Order'); return toJson({ toJSON() { return o.Status; } });")]
    [InlineData("const o = getVariable('Order'); const h = { toString() { o.Status = 'Paid'; return 'x'; } }; return String(1, h);")]
    [InlineData("const o = getVariable('Order'); const h = { valueOf() { o.Status = 'Paid'; return 1; } }; return Math.random(h);")]
    public async Task Known_read_only_helpers_keep_reusable_variable_reads_convertible(string source)
    {
        var root = Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal(source))));
        var result = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root, variables: [Total, Order]), isReusable: true);

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

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertWithInputAsync(
        string script, string input, params Elsa3Variable[] variables)
    {
        var definition = ConversionServices.Definition(
            Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal(script)))), variables: variables);
        definition.Inputs = [new Elsa3WorkflowArgumentDefinition { Name = input, Type = "String" }];
        return ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition);
    }

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertAsync(Elsa3Activity root, params Elsa3Variable[] variables) =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root, variables: variables));
}
