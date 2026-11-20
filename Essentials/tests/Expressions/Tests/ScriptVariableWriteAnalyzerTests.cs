using Elsa.Expressions.JavaScript.Services;
using Xunit;

namespace Elsa.Expressions.Tests;

/// <summary>
/// Spec 1228 FR-006a: the JavaScript analyzer reports the writes a source shows, so publish can refuse an input
/// expression that assigns a variable. Writes it cannot see are caught at run time.
/// </summary>
public sealed class ScriptVariableWriteAnalyzerTests
{
    private readonly JavaScriptVariableReadAnalyzer _analyzer = new();

    [Theory]
    [InlineData("variables.x = 1", "x", "variables.x")]
    [InlineData("variables['x'] = 1", "x", "variables.x")]
    [InlineData("variables.order.total = 20", "order", "variables.order.total")]
    [InlineData("variables.order['line items'] += 1", "order", "variables.order[\"line items\"]")]
    [InlineData("variables.order.lines[0] = 1", "order", "variables.order.lines[0]")]
    [InlineData("variables.count++", "count", "variables.count")]
    [InlineData("--variables.count", "count", "variables.count")]
    [InlineData("delete variables.order.total", "order", "variables.order.total")]
    [InlineData("getVariable('order').total = 1", "order", "variables.order.total")]
    [InlineData("(() => { variables.x ??= 2; return 1; })()", "x", "variables.x")]
    public void A_visible_write_is_reported(string source, string name, string path)
    {
        var write = Assert.Single(_analyzer.Analyze(source)!.Writes);

        Assert.Equal(name, write.Name);
        Assert.Equal(path, write.Path);
    }

    [Theory]
    [InlineData("variables.x + 1")]
    [InlineData("(() => { const copy = { ...variables.order }; copy.total = 1; return copy; })()")]
    [InlineData("(() => { const o = variables.order; o.total = 1; return o; })()")]
    [InlineData("(() => { variables[name] = 1; return 1; })()")]
    [InlineData("(() => { args.x = 1; return args.x; })()")]
    public void A_read_a_local_copy_and_a_write_publication_cannot_see_are_not_reported(string source)
    {
        Assert.Empty(_analyzer.Analyze(source)!.Writes);
    }

    // Spec 1231 FR-005, FR-018 to FR-020a (T024): a script body's write forms, escapes and computed writes.

    [Theory]
    [InlineData("setVariable('total', 1); return 1;", "total", "variables.total")]
    [InlineData("setVariable('line items', 1); return 1;", "line items", "variables[\"line items\"]")]
    public void SetVariable_with_a_literal_name_is_a_whole_assignment(string source, string name, string path)
    {
        var analysis = _analyzer.Analyze(source, scriptBody: true)!;

        var write = Assert.Single(analysis.Writes);
        Assert.Equal((name, path, 0), (write.Name, write.Path, write.Start));
        Assert.Equal([name], analysis.LiteralNames);
        Assert.False(analysis.HasComputedWrite);
        Assert.False(analysis.HasComputedRead);
    }

    [Fact]
    public void A_write_reports_where_it_starts()
    {
        var write = Assert.Single(_analyzer.Analyze("const a = 1;\nvariables.order.total = a;", scriptBody: true)!.Writes);

        Assert.Equal(13, write.Start);
    }

    [Theory]
    [InlineData("const o = variables.order; o.total = 1;")]
    [InlineData("helper(variables.order);")]
    [InlineData("variables.order.items.push(3);")]
    [InlineData("const items = variables.order.items;")]
    [InlineData("const f = () => variables.order; f().x = 1;")]
    [InlineData("const copy = [variables.order];")]
    [InlineData("const o = getVariable('order');")]
    [InlineData("const o = variables.order || {};")]
    public void A_read_value_that_escapes_may_be_written(string source)
    {
        var analysis = _analyzer.Analyze(source, scriptBody: true)!;

        Assert.Equal(["order"], analysis.Escapes);
        Assert.Contains("order", analysis.MayWriteNames);
        Assert.False(analysis.HasComputedWrite);
    }

    [Theory]
    [InlineData("return variables.order.total + 1;")]
    [InlineData("if (variables.order.total > 1) { return 1; } return 2;")]
    [InlineData("return `${variables.order.total}`;")]
    [InlineData("return variables.order;")]
    [InlineData("return typeof variables.order === 'object' ? 1 : 2;")]
    [InlineData("variables.order;")]
    [InlineData("return JSON.stringify(variables.order) === '{}' ? 1 : 0;")]
    public void A_value_that_is_only_consumed_does_not_escape(string source)
    {
        // JSON.stringify is a call argument: conservatively an escape. Every other form here only consumes the value.
        var analysis = _analyzer.Analyze(source, scriptBody: true)!;

        if (source.Contains("JSON.stringify", StringComparison.Ordinal))
            Assert.Equal(["order"], analysis.Escapes);
        else
            Assert.Empty(analysis.Escapes);
        Assert.Empty(analysis.Writes);
        Assert.False(analysis.HasComputedWrite);
    }

    [Theory]
    [InlineData("variables[k] = 1;")]
    [InlineData("variables[k].total = 1;")]
    [InlineData("variables[k]++;")]
    [InlineData("setVariable(k, 1);")]
    [InlineData("getVariable(k).total = 1;")]
    [InlineData("const v = variables; v.X = 'Hallo';")]
    [InlineData("const s = setVariable; s('x', 1);")]
    [InlineData("const g = getVariable;")]
    [InlineData("const o = variables[k]; o.total = 1;")]
    [InlineData("globalThis.setVariable('x', 1);")]
    [InlineData("function setVariable() {} setVariable('x', 1);")]
    public void A_computed_or_opaque_write_marks_every_visible_variable(string source)
    {
        Assert.True(_analyzer.Analyze(source, scriptBody: true)!.HasComputedWrite);
    }

    [Theory]
    [InlineData("return variables[k] + 1;")]
    [InlineData("return getVariable(k) === 1;")]
    public void A_computed_read_whose_value_does_not_escape_is_not_a_write(string source)
    {
        var analysis = _analyzer.Analyze(source, scriptBody: true)!;

        Assert.True(analysis.HasComputedRead);
        Assert.False(analysis.HasComputedWrite);
    }
}
