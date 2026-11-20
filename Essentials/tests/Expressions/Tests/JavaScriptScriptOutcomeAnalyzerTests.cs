using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.Tests;

/// <summary>
/// The design-time script outcome analyzer (spec 1208): the literal outcomes of <c>complete(result, outcomes)</c> calls,
/// the top-level returned string literals and the declarations of the reserved name <c>complete</c>.
/// </summary>
public sealed class JavaScriptScriptOutcomeAnalyzerTests
{
    private readonly JavaScriptScriptOutcomeAnalyzer _analyzer = new();

    [Fact]
    public void The_JavaScript_feature_registers_the_analyzer()
    {
        var services = new ServiceCollection();
        new JavaScriptFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<JavaScriptScriptOutcomeAnalyzer>(provider.GetRequiredService<IJavaScriptScriptOutcomeAnalyzer>());
    }

    [Fact]
    public void Literal_outcomes_are_read_from_every_complete_call_nested_functions_included()
    {
        var analysis = Analyze("function route() { return complete(1, ['B', 'C']); } if (x) return complete(1, 'A'); return route();");

        Assert.Equal(["B", "C", "A"], analysis.LiteralOutcomes);
    }

    [Theory]
    [InlineData("const name = 'X'; return complete(1, name);")]
    [InlineData("return complete(1, `X`);")]
    [InlineData("const finish = complete; return finish(1, 'X');")]
    [InlineData("return complete(1);")]
    public void Computed_aliased_or_omitted_outcomes_are_not_reported(string script)
    {
        Assert.Empty(Analyze(script).LiteralOutcomes);
    }

    [Fact]
    public void Only_top_level_returned_strings_are_reported()
    {
        var analysis = Analyze("function f() { return 'Inner'; } if (x) return 'Approved'; return f();");

        Assert.Equal(["Approved"], analysis.ReturnedStrings);
    }

    [Theory]
    [InlineData("var complete = 1;")]
    [InlineData("function complete() { }")]
    [InlineData("class complete { }")]
    [InlineData("function f(complete) { }")]
    [InlineData("const { complete } = {};")]
    [InlineData("const { a: complete } = {};")]
    [InlineData("const [complete] = [];")]
    [InlineData("try { } catch (complete) { }")]
    [InlineData("const f = function complete() { };")]
    [InlineData("const C = class complete { };")]
    [InlineData("function f(complete = 1) { }")]
    [InlineData("function f(...complete) { }")]
    public void Every_binding_form_of_complete_is_a_reserved_name_declaration(string script)
    {
        var declaration = Assert.Single(Analyze(script).ReservedNameDeclarations);

        Assert.Equal(0, declaration.Start.Line);
        Assert.Equal("complete".Length, declaration.End.Character - declaration.Start.Character);
        Assert.Equal("complete", script[declaration.Start.Character..declaration.End.Character]);
    }

    [Theory]
    [InlineData("const { complete: done } = {};")]
    [InlineData("const o = { complete: 1 }; o.complete;")]
    [InlineData("return complete(1, 'A');")]
    public void Using_or_keying_complete_declares_nothing(string script)
    {
        Assert.Empty(Analyze(script).ReservedNameDeclarations);
    }

    [Fact]
    public void A_script_that_does_not_parse_has_no_analysis()
    {
        Assert.Null(_analyzer.Analyze("return complete(1, 'X'"));
    }

    private JavaScriptScriptOutcomeAnalysis Analyze(string script) =>
        _analyzer.Analyze(script) ?? throw new InvalidOperationException($"'{script}' did not parse.");
}
