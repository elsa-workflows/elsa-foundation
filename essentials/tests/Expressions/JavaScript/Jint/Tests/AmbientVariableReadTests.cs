using System.Text.Json;
using Elsa.Expressions;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Primitives.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// Issue #984: a native-authored JavaScript value expression must be able to read the variables lexically visible to
/// it through the host-pinned <c>variables.X</c> / <c>getVariable('X')</c> / <c>get&lt;Name&gt;()</c> surface, while a
/// pure binding expression (no ambient variables supplied) continues to see no such surface at all.
/// </summary>
public sealed class AmbientVariableReadTests
{
    [Fact]
    public async Task Reads_a_visible_variable_through_the_variables_object()
    {
        var result = await EvaluateAsync(
            "variables.count + 1",
            ambient: new Dictionary<string, JsonElement> { ["count"] = Number(5) });

        Assert.Equal(6, result.GetInt32());
    }

    [Fact]
    public async Task Reads_a_visible_variable_through_get_variable()
    {
        var result = await EvaluateAsync(
            "getVariable('count') + 1",
            ambient: new Dictionary<string, JsonElement> { ["count"] = Number(5) });

        Assert.Equal(6, result.GetInt32());
    }

    [Fact]
    public async Task Reads_a_visible_variable_through_the_generated_getter()
    {
        var result = await EvaluateAsync(
            "getCount() + 1",
            ambient: new Dictionary<string, JsonElement> { ["count"] = Number(5) });

        Assert.Equal(6, result.GetInt32());
    }

    [Fact]
    public async Task Generated_getter_pascal_cases_a_camelCase_variable_name()
    {
        var result = await EvaluateAsync(
            "getKeepGoing()",
            ambient: new Dictionary<string, JsonElement> { ["keepGoing"] = JsonSerializer.SerializeToElement(true) });

        Assert.True(result.GetBoolean());
    }

    [Fact]
    public async Task Non_identifier_variable_names_stay_reachable_through_bracket_and_get_variable()
    {
        var result = await EvaluateAsync(
            "variables['order-id'] + ':' + getVariable('order-id')",
            ambient: new Dictionary<string, JsonElement> { ["order-id"] = JsonSerializer.SerializeToElement("A1") });

        Assert.Equal("A1:A1", result.GetString());
    }

    [Fact]
    public async Task Unknown_variable_reads_as_undefined_from_get_variable()
    {
        var result = await EvaluateAsync(
            "typeof getVariable('missing')",
            ambient: new Dictionary<string, JsonElement> { ["count"] = Number(5) });

        Assert.Equal("undefined", result.GetString());
    }

    [Fact]
    public async Task The_variables_object_is_read_only()
    {
        var result = await EvaluateAsync(
            "(() => { try { variables.count = 99; } catch { } try { variables.injected = 1; } catch { } return variables.count + ':' + typeof variables.injected; })()",
            ambient: new Dictionary<string, JsonElement> { ["count"] = Number(5) });

        Assert.Equal("5:undefined", result.GetString());
    }

    [Theory]
    [InlineData("typeof variables")]
    [InlineData("typeof getVariable")]
    public async Task Pure_binding_expressions_still_see_no_ambient_surface(string source)
    {
        var result = await EvaluateAsync(source, ambient: new Dictionary<string, JsonElement>());

        Assert.Equal("undefined", result.GetString());
    }

    [Fact]
    public async Task Declared_args_and_ambient_variables_share_the_engine_as_separate_namespaces()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();
        var parameters = new Dictionary<string, JsonElement> { ["factor"] = Number(3) };
        var definition = new ExpressionDefinition(
            "JavaScript",
            "args.factor * variables.count",
            new TypeReference("Any"),
            new Dictionary<string, ExpressionParameterBinding> { ["factor"] = new LiteralExpressionParameterBinding(Number(3)) },
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV1);
        var request = new ExpressionEvaluationRequest(
            definition,
            parameters,
            new Dictionary<string, JsonElement> { ["count"] = Number(5) },
            CancellationToken.None);

        var result = await evaluator.EvaluateAsync(request);

        Assert.Equal(15, result.GetInt32());
    }

    private static async Task<JsonElement> EvaluateAsync(string source, IReadOnlyDictionary<string, JsonElement> ambient)
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();
        var definition = new ExpressionDefinition(
            "JavaScript",
            source,
            new TypeReference("Any"),
            new Dictionary<string, ExpressionParameterBinding>(),
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV1);
        var request = new ExpressionEvaluationRequest(
            definition,
            new Dictionary<string, JsonElement>(),
            ambient,
            CancellationToken.None);
        return await evaluator.EvaluateAsync(request);
    }

    private static JsonElement Number(int value) => JsonSerializer.SerializeToElement(value);

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }
}
