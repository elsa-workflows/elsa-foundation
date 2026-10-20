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
