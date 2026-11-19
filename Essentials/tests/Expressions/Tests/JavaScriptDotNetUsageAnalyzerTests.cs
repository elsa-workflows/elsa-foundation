using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.Tests;

/// <summary>
/// Spec 1223 FR-016 to FR-019 (contracts/conversion-and-diagnostics.md D3): the default strategy's .NET-usage analysis,
/// used by publish (no context) and by the Elsa 3 converter (with the values it knows were .NET objects).
/// </summary>
public sealed class JavaScriptDotNetUsageAnalyzerTests
{
    private readonly JavaScriptDotNetUsageAnalyzer _analyzer = new([new JavaScriptHostFunctionDeclaration("acme.Lookup", _ => default)]);

    [Fact]
    public void The_JavaScript_feature_registers_the_analyzer()
    {
        var services = new ServiceCollection();
        new JavaScriptFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<JavaScriptDotNetUsageAnalyzer>(provider.GetRequiredService<IJavaScriptDotNetUsageAnalyzer>());
    }

    [Theory]
    [InlineData("TimeSpan.FromHours(1)", "TimeSpan.FromHours(1)", "TimeSpan")]
    [InlineData("new Uri(s)", "new Uri(s)", "Uri")]
    [InlineData("Enumerable.Range(0, 3)", "Enumerable.Range(0, 3)", "Enumerable")]
    [InlineData("a.Where(f)", "a.Where(f)", "Where")]
    [InlineData("s.ToUpper()", "s.ToUpper()", "ToUpper")]
    [InlineData("payload.Items.ToArray()", "payload.Items.ToArray()", "ToArray")]
    public void Dotnet_types_and_pascal_case_invocations_are_refused_quoting_the_expression(string source, string quoted, string named)
    {
        var usage = Assert.Single(_analyzer.Analyze(source)!);

        Assert.Equal(quoted, usage.Expression);
        Assert.Contains($"'{quoted}'", usage.Message, StringComparison.Ordinal);
        Assert.Contains(named, usage.Message, StringComparison.Ordinal);
        Assert.Contains("JSON", usage.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("payload.OrderId")]
    [InlineData("variables.Order.Status")]
    [InlineData("s.toUpperCase()")]
    [InlineData("a.map(f)")]
    [InlineData("JSON.stringify(o)")]
    [InlineData("new Date()")]
    [InlineData("Intl.DateTimeFormat('en-GB')")]
    [InlineData("Math.max(1, 2)")]
    [InlineData("o[name]()")]
    [InlineData("const Path = { join: (a, b) => a + b }; Path.join(x, y)")]
    [InlineData("const helpers = { Format: x => x }; helpers.Format(v)")]
    [InlineData("class C { Format(x) { return x; } } new C().Format(1)")]
    [InlineData("const o = {}; o.Run = () => 1; o.Run()")]
    [InlineData("acme.Lookup(x)")]
    [InlineData("newGuid().toString('N')")]
    public void Json_reads_native_calls_and_script_defined_members_are_allowed(string source)
    {
        Assert.Empty(_analyzer.Analyze(source)!);
    }

    [Fact]
    public void A_script_body_is_analyzed_with_a_top_level_return()
    {
        var usage = Assert.Single(_analyzer.Analyze("const t = TimeSpan.FromHours(1);\nreturn t;", scriptBody: true)!);

        Assert.Equal("TimeSpan.FromHours(1)", usage.Expression);
        Assert.Equal(0, usage.Range.Start.Line);
    }

    [Fact]
    public void Source_that_does_not_parse_is_left_to_the_syntax_check()
    {
        Assert.Null(_analyzer.Analyze("a.Where("));
    }

    // ---- With a conversion context: values known to be .NET objects in Elsa 3 (FR-016) ----

    private static readonly JavaScriptDotNetUsageContext Context = new(new Dictionary<string, JavaScriptDotNetValueKind>
    {
        ["variables.upload"] = JavaScriptDotNetValueKind.Object,
        ["variables.items"] = JavaScriptDotNetValueKind.Collection,
        ["args.id"] = JavaScriptDotNetValueKind.Guid,
    });

    [Theory]
    [InlineData("newGuid().toString('N')", "newGuid().toString('N')")]
    [InlineData("newGuid().ToByteArray()", "newGuid().ToByteArray()")]
    [InlineData("const g = parseGuid(s); g.toByteArray()", "g.toByteArray()")]
    [InlineData("variables.upload.openReadStream()", "variables.upload.openReadStream()")]
    [InlineData("variables.upload.FileName", "variables.upload.FileName")]
    [InlineData("let u = variables.upload; u.length", "u.length")]
    [InlineData("variables.items.Add(x)", "variables.items.Add(x)")]
    [InlineData("args.id.Equals(x)", "args.id.Equals(x)")]
    [InlineData("JSON.stringify(variables.upload)", "JSON.stringify(variables.upload)")]
    public void Member_use_on_a_known_dotnet_value_is_refused(string source, string quoted)
    {
        var usage = Assert.Single(_analyzer.Analyze(source, context: Context)!);

        Assert.Equal(quoted, usage.Expression);
        Assert.Contains("JSON", usage.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("'id-' + newGuid()")]
    [InlineData("newGuid().toString()")]
    [InlineData("const g = newGuid(); return g;")]
    [InlineData("JSON.stringify(newGuid())")]
    [InlineData("variables.items.map(f)")]
    [InlineData("variables.items.length")]
    [InlineData("variables.items[0]")]
    [InlineData("variables.upload === null")]
    [InlineData("var g = newGuid(); g.toByteArray()")]
    public void Known_dotnet_values_used_as_whole_values_are_allowed(string source)
    {
        Assert.Empty(_analyzer.Analyze(source, scriptBody: true, context: Context)!);
    }

    [Fact]
    public void Without_a_context_guid_results_and_variables_are_not_known_dotnet_values()
    {
        Assert.Empty(_analyzer.Analyze("newGuid().toString('N') + variables.upload.name")!);
    }
}
