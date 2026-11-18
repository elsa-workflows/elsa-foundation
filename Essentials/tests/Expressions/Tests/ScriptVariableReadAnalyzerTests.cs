using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Services;
using Elsa.Expressions.Liquid;
using Elsa.Expressions.Liquid.Services;
using Fluid;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.Tests;

/// <summary>
/// Spec 1222 FR-001, FR-008, FR-014: publish reads a script's variable reads by parsing it, never by regular expression.
/// A literal name is pinned; a computed or opaque read sets the fallback marker.
/// </summary>
public sealed class ScriptVariableReadAnalyzerTests
{
    private readonly JavaScriptVariableReadAnalyzer _javaScript = new();
    private readonly LiquidVariableReadAnalyzer _liquid = new(new FluidParser());

    [Fact]
    public void Both_features_register_their_analyzer()
    {
        var services = new ServiceCollection();
        new JavaScriptFeature().ConfigureServices(services);
        new LiquidExpressionsFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Equal(
            ["JavaScript", "Liquid"],
            scope.ServiceProvider.GetServices<IScriptVariableReadAnalyzer>().Select(analyzer => analyzer.Language).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("variables.order.total")]
    [InlineData("variables['order']")]
    [InlineData("getVariable('order')")]
    [InlineData("getVariable(\"order\").total + 1")]
    public void Each_literal_form_is_a_literal_read(string source)
    {
        var analysis = _javaScript.Analyze(source)!;

        Assert.Equal(["order"], analysis.LiteralNames);
        Assert.False(analysis.HasComputedRead);
    }

    [Theory]
    [InlineData("variables[key]")]
    [InlineData("getVariable(name)")]
    [InlineData("getVariable('or' + 'der')")]
    [InlineData("(() => { const v = variables; return v.order; })()")]
    [InlineData("Object.keys(variables).length")]
    [InlineData("({ ...variables }).order")]
    [InlineData("[1].map(getVariable)")]
    [InlineData("(() => { const variables = { order: 1 }; return variables.order; })()")]
    [InlineData("({ variables }).variables")]
    public void A_computed_or_opaque_read_sets_the_fallback(string source)
    {
        Assert.True(_javaScript.Analyze(source)!.HasComputedRead);
    }

    [Theory]
    [InlineData("args.variables")]
    [InlineData("({ variables: 1 }).variables")]
    [InlineData("args.order.getVariable")]
    [InlineData("'variables.order'")]
    [InlineData("// variables.order\n1")]
    public void Names_and_text_that_only_look_like_reads_are_not_reads(string source)
    {
        var analysis = _javaScript.Analyze(source)!;

        Assert.Empty(analysis.LiteralNames);
        Assert.False(analysis.HasComputedRead);
    }

    [Fact]
    public void Literal_and_computed_reads_are_reported_together()
    {
        var analysis = _javaScript.Analyze("variables.order.total + variables[key] + getVariable('customer').name + variables.order.tax")!;

        Assert.Equal(["order", "customer"], analysis.LiteralNames);
        Assert.True(analysis.HasComputedRead);
    }

    [Fact]
    public void A_script_body_parses_with_a_top_level_return()
    {
        var analysis = _javaScript.Analyze("const total = variables.order.total;\nreturn total;", scriptBody: true)!;

        Assert.Equal(["order"], analysis.LiteralNames);
    }

    [Fact]
    public void Source_that_does_not_parse_is_left_to_the_expression_validation()
    {
        Assert.Null(_javaScript.Analyze("variables.order +"));
    }

    [Fact]
    public void A_call_to_a_host_function_declared_as_reading_variables_is_an_opaque_read()
    {
        // FR-008, T023: the function reads variables the script does not name, so publish sets the fallback marker.
        var services = new ServiceCollection();
        new JavaScriptFeature().ConfigureServices(services);
        services.AddSingleton(new JavaScriptHostFunctionDeclaration("lookupRate", ReadsVariables: true));
        services.AddSingleton(new JavaScriptHostFunctionDeclaration("formatRate"));
        using var provider = services.BuildServiceProvider();
        var analyzer = provider.GetServices<IScriptVariableReadAnalyzer>().Single(candidate => candidate.Language == "JavaScript");

        Assert.True(analyzer.Analyze("lookupRate('eu') * 2")!.HasComputedRead);
        Assert.False(analyzer.Analyze("formatRate(2)")!.HasComputedRead);
    }

    [Theory]
    [InlineData("{{ variables.order.total }}", "order", false)]
    [InlineData("{{ variables['order'] }}", "order", false)]
    [InlineData("{% if variables.order %}x{% endif %}", "order", false)]
    [InlineData("{{ variables[key] }}", null, true)]
    [InlineData("{% for v in variables %}{{ v }}{% endfor %}", null, true)]
    [InlineData("{{ order.variables }}", null, false)]
    public void Liquid_reads_are_parsed_from_the_template(string template, string? literal, bool computed)
    {
        var analysis = _liquid.Analyze(template)!;

        Assert.Equal(literal is null ? [] : [literal], analysis.LiteralNames);
        Assert.Equal(computed, analysis.HasComputedRead);
    }
}
