using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 261 User Stories 2–4 (research R-3 to R-6): Elsa 3 ran JavaScript in sloppy mode, Elsa 4 runs it in strict mode.
/// What only works in sloppy mode is refused with <c>VF-IMP-007</c>; the same shapes written for strict mode convert.
/// Each refusal is checked as a bound expression and as a RunJavaScript body.
/// </summary>
public sealed class Elsa3StrictModeTests
{
    [Theory]
    [InlineData("total = 1; total", "total")]
    [InlineData("total += 1; 1", "total")]
    [InlineData("total++; 1", "total")]
    [InlineData("[a, b] = [1, 2]; 1", "a")]
    [InlineData("({ a } = { a: 1 }); 1", "a")]
    [InlineData("for (k in { x: 1 }) {} 1", "k")]
    [InlineData("for (k of [1]) {} 1", "k")]
    [InlineData("(function () { total = 1; })(); 1", "total")]
    [InlineData("undefined = 1; 1", "undefined")]
    public async Task An_assignment_to_an_undeclared_name_is_refused(string source, string name)
    {
        foreach (var result in await ConvertBothAsync(source))
        {
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code == Elsa3ImportDiagnosticCodes.UnsupportedShape && diagnostic.Message.Contains($"'{name}'", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("var total; total = 1; total")]
    [InlineData("let total = 0; total += 1; total")]
    [InlineData("total = 1; var total; total")]
    [InlineData("function f(total) { total = 2; return total } f(1)")]
    [InlineData("try { throw 1 } catch (e) { e = 2 } 1")]
    [InlineData("for (var k in { x: 1 }) {} k")]
    [InlineData("const o = {}; o.total = 1; o.total")]
    public async Task An_assignment_to_a_declared_name_converts(string source)
    {
        var result = await ConvertTextAsync(source);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    [Theory]
    [InlineData("with ({ a: 1 }) { a }\n1")]
    [InlineData("010 + 1")]
    [InlineData("'\\010'")]
    [InlineData("(function (a, a) { return a })(1, 2)")]
    [InlineData("var x = 1; delete x; x")]
    [InlineData("var static = 1; static")]
    [InlineData("var yield = 1; yield")]
    [InlineData("var arguments = 1; arguments")]
    public async Task Syntax_that_only_parses_in_sloppy_mode_is_refused(string source)
    {
        foreach (var result in await ConvertBothAsync(source))
        {
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code == Elsa3ImportDiagnosticCodes.UnsupportedShape && diagnostic.Message.Contains("strict mode, where it does not parse", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Javascript_that_does_not_parse_at_all_keeps_the_parse_refusal()
    {
        var result = await ConvertTextAsync("return (");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("could not be parsed", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Message.Contains("strict mode", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("this.x", "'this'")]
    [InlineData("(() => this)()", "'this'")]
    [InlineData("arguments.length", "'arguments' outside a function")]
    [InlineData("(function (a) { arguments[0] = 2; return a })(1)", "'arguments[…]'")]
    [InlineData("(function (a) { a = 2; return arguments[0] })(1)", "'a'")]
    [InlineData("(function (a) { const f = () => arguments[0]; a = 2; return f() })(1)", "'a'")]
    public async Task This_and_arguments_that_strict_mode_changes_are_refused(string source, string named)
    {
        foreach (var result in await ConvertBothAsync(source))
        {
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code == Elsa3ImportDiagnosticCodes.UnsupportedShape && diagnostic.Message.Contains(named, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("({ v: 1, f() { return this.v } }).f()")]
    [InlineData("(function () { return this })")]
    [InlineData("(function () { return arguments.length })(1, 2)")]
    [InlineData("(function (a) { a = 2; return a })(1)")]
    public async Task This_in_a_function_and_read_only_arguments_convert(string source)
    {
        var result = await ConvertTextAsync(source);

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
    }

    private static async Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>[]> ConvertBothAsync(string source) =>
    [
        await ConvertTextAsync(source),
        await ConvertAsync(Sequence("root", Activity("node", "Elsa.RunJavaScript", ("script", Literal(source))))),
    ];

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertTextAsync(string expression) =>
        ConvertAsync(Sequence("root", Activity("node", "Elsa.WriteLine", ("text", Expression("JavaScript", expression)))));

    private static Task<Elsa3MigrationResult<Conversion.Elsa3ConvertedDefinition>> ConvertAsync(Elsa3Activity root) =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root));
}
