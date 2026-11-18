using System.Text.Json;
using Elsa.Expressions;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.Liquid;
using Elsa.Primitives.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// Spec 1222: a script reads workflow variables through a <c>variables</c> root, apart from <c>args</c>. Names publish
/// pinned read their pinned value; a computed name goes through the host's recorded lookup; there are no generated
/// <c>get&lt;Name&gt;()</c> accessors.
/// </summary>
public sealed class VariablesRootEvaluationTests
{
    [Fact]
    public async Task A_pinned_read_reads_its_value_through_every_literal_form()
    {
        var result = await EvaluateAsync(
            "[variables.count, variables['count'], getVariable('count')].join()",
            pinned: new() { ["count"] = Json(5) });

        Assert.Equal("5,5,5", result.GetString());
    }

    [Fact]
    public async Task Every_read_path_returns_the_same_javascript_value()
    {
        // FR-034: one value per variable per evaluation, so spec 1228 can see a change made through any path.
        var result = await EvaluateAsync(
            "variables.order === getVariable('order') && variables['order'] === getVariable(['or', 'der'].join(''))",
            pinned: new() { ["order"] = JsonSerializer.SerializeToElement(new { total = 3 }) },
            reader: new Reader());

        Assert.True(result.GetBoolean());
    }

    [Fact]
    public async Task An_unset_declared_variable_reads_null()
    {
        var result = await EvaluateAsync("variables.order === null", pinned: new() { ["order"] = Json<object?>(null) });

        Assert.True(result.GetBoolean());
    }

    [Fact]
    public async Task The_runtime_provides_no_generated_accessor()
    {
        // FR-036, ADR 0089 decision 6: get<Name>() is only an Elsa 3 import form.
        var result = await EvaluateAsync("typeof getCount", pinned: new() { ["count"] = Json(5) });

        Assert.Equal("undefined", result.GetString());
    }

    [Fact]
    public async Task Args_and_variables_are_separate_roots()
    {
        // FR-003: variables.count and an explicit args.count never collide (Q6).
        await using var provider = BuildProvider();
        var evaluator = provider.GetRequiredService<IPortableExpressionEvaluator>();
        var definition = Definition("args.count * 10 + variables.count", new Dictionary<string, ExpressionParameterBinding>
        {
            ["count"] = new LiteralExpressionParameterBinding(Json(3)),
            ["variables.count"] = new VariableExpressionParameterBinding("workflow", "count", ExpressionParameterSurfaces.Variables)
        });

        var result = await evaluator.EvaluateAsync(new ExpressionEvaluationRequest(
            definition,
            new Dictionary<string, JsonElement> { ["count"] = Json(3), ["variables.count"] = Json(5) },
            CancellationToken.None));

        Assert.Equal(35, result.GetInt32());
    }

    [Fact]
    public async Task The_variables_root_is_frozen()
    {
        var result = await EvaluateAsync(
            "(() => { try { variables.count = 99; } catch { } try { variables.injected = 1; } catch { } return variables.count + ':' + typeof variables.injected; })()",
            pinned: new() { ["count"] = Json(5) });

        Assert.Equal("5:undefined", result.GetString());
    }

    [Theory]
    [InlineData("typeof variables")]
    [InlineData("typeof getVariable")]
    public async Task An_expression_that_reads_no_variable_sees_no_variables_surface(string source)
    {
        var result = await EvaluateAsync(source);

        Assert.Equal("undefined", result.GetString());
    }

    [Theory]
    [InlineData("variables[name]")]
    [InlineData("getVariable(name)")]
    public async Task A_computed_name_is_served_by_the_host_lookup(string read)
    {
        var reader = new Reader { ["order"] = Json(7) };

        var result = await EvaluateAsync($"(() => {{ const name = 'or' + 'der'; return {read}; }})()", reader: reader);

        Assert.Equal(7, result.GetInt32());
        Assert.Equal(["order"], reader.Reads);
    }

    [Fact]
    public async Task A_computed_name_that_matches_nothing_reads_undefined_not_null()
    {
        // FR-011: undefined in JavaScript; the host records the absent read with its warning.
        var reader = new Reader();

        var result = await EvaluateAsync("(() => { const k = 'missing'; return typeof variables[k] + ':' + typeof getVariable(k); })()", reader: reader);

        Assert.Equal("undefined:undefined", result.GetString());
        Assert.Equal(["missing"], reader.Reads);
    }

    [Fact]
    public async Task Enumerating_the_variables_root_lists_the_candidates_and_reads_only_what_is_read()
    {
        var reader = new Reader { ["a"] = Json(1), ["b"] = Json(2) };

        var result = await EvaluateAsync("Object.keys(variables).join() + '|' + variables[['a'][0]]", reader: reader);

        Assert.Equal("a,b|1", result.GetString());
        Assert.Equal(["a"], reader.Reads);
    }

    [Fact]
    public async Task A_refused_read_throws_and_the_host_keeps_the_refusal()
    {
        // FR-017a: the script may catch the error; the host's record is what faults the step.
        var reader = new Reader { Refused = { "secret" } };

        var result = await EvaluateAsync("(() => { try { return getVariable(['sec', 'ret'].join('')); } catch (e) { return 'caught'; } })()", reader: reader);

        Assert.Equal("caught", result.GetString());
        Assert.Equal(["secret"], reader.Reads);
    }

    [Fact]
    public async Task Liquid_reads_pinned_and_computed_variables_through_the_same_root()
    {
        // FR-005, FR-011: a missing computed name renders nil.
        await using var provider = BuildProvider();
        var evaluator = provider.GetRequiredService<IPortableExpressionEvaluator>();
        var reader = new Reader { ["region"] = Json("eu") };
        var definition = new ExpressionDefinition(
            "Liquid",
            "{{ variables.order }}|{{ variables[key] }}|{{ variables[missing] }}|",
            new TypeReference("String"),
            new Dictionary<string, ExpressionParameterBinding>
            {
                ["key"] = new LiteralExpressionParameterBinding(Json("region")),
                ["missing"] = new LiteralExpressionParameterBinding(Json("nope")),
                ["variables.order"] = new VariableExpressionParameterBinding("workflow", "order", ExpressionParameterSurfaces.Variables)
            },
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV1);

        var result = await evaluator.EvaluateAsync(new ExpressionEvaluationRequest(
            definition,
            new Dictionary<string, JsonElement> { ["key"] = Json("region"), ["missing"] = Json("nope"), ["variables.order"] = Json("A1") },
            reader,
            CancellationToken.None));

        Assert.Equal("A1|eu||", result.GetString());
        Assert.Equal(["region", "nope"], reader.Reads);
    }

    private static async Task<JsonElement> EvaluateAsync(
        string source,
        Dictionary<string, JsonElement>? pinned = null,
        IExpressionVariableReader? reader = null)
    {
        await using var provider = BuildProvider();
        var evaluator = provider.GetRequiredService<IPortableExpressionEvaluator>();
        var parameters = (pinned ?? []).ToDictionary(
            item => ExpressionParameterSurfaces.VariablesKey(item.Key),
            item => (ExpressionParameterBinding)new VariableExpressionParameterBinding("workflow", item.Key, ExpressionParameterSurfaces.Variables),
            StringComparer.Ordinal);
        var values = (pinned ?? []).ToDictionary(item => ExpressionParameterSurfaces.VariablesKey(item.Key), item => item.Value, StringComparer.Ordinal);
        return await evaluator.EvaluateAsync(new ExpressionEvaluationRequest(Definition(source, parameters), values, reader, CancellationToken.None));
    }

    private static ExpressionDefinition Definition(string source, IReadOnlyDictionary<string, ExpressionParameterBinding> parameters) =>
        new("JavaScript", source, new TypeReference("Any"), parameters, JsonSerializer.SerializeToElement(new { }), ExpressionCapabilityProfiles.BindingPureV1);

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        new LiquidExpressionsFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    /// <summary>A host lookup over fixed values that records every name it is asked for.</summary>
    private sealed class Reader : Dictionary<string, JsonElement>, IExpressionVariableReader
    {
        public List<string> Reads { get; } = [];
        public HashSet<string> Refused { get; } = [];

        public IReadOnlyList<string> CandidateNames => Keys.Concat(Refused).ToArray();

        public ExpressionVariableRead Read(string name)
        {
            Reads.Add(name);
            if (Refused.Contains(name))
                return ExpressionVariableRead.Refused($"variable '{name}' is withheld");
            return TryGetValue(name, out var value) ? ExpressionVariableRead.Present(value) : ExpressionVariableRead.Missing;
        }
    }
}
