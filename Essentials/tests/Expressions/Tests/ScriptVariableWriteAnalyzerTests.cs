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
}
