using System.Text.Json;
using Elsa.Expressions;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Primitives.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

public sealed class ExplicitExpressionParametersTests
{
    [Fact]
    public void Jint_feature_registers_only_the_isolated_expression_and_typed_script_evaluators()
    {
        var services = new ServiceCollection();

        new JintFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IPortableJavaScriptEvaluator));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IJavaScriptScriptEvaluator));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType.FullName is
            "Elsa.Expressions.JavaScript.Core.Contracts.IJavaScriptExecutionContext" or
            "Elsa.Expressions.JavaScript.Jint.Contracts.IJintEngineFactory" or
            "Elsa.Expressions.JavaScript.Jint.Contracts.IPreparedScriptFactory");
    }

    [Fact]
    public async Task Evaluates_declared_json_parameters_through_args()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();
        var request = Request(
            "args.customer.name + ':' + args.lines[1].quantity + ':' + args.single[0]",
            new Dictionary<string, JsonElement>
            {
                ["customer"] = JsonSerializer.SerializeToElement(new { name = "Ada" }),
                ["lines"] = JsonSerializer.SerializeToElement(new[] { new { quantity = 1 }, new { quantity = 3 } }),
                ["single"] = JsonSerializer.SerializeToElement(new[] { 7 })
            });

        var result = await evaluator.EvaluateAsync(request);

        Assert.Equal("Ada:3:7", result.GetString());
    }

    [Fact]
    public async Task Returns_structured_results_as_json()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var result = await evaluator.EvaluateAsync(Request(
            "({ total: args.amount * 2, approved: true, tags: ['pure'] })",
            new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement(21) }));

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.Equal(42, result.GetProperty("total").GetInt32());
        Assert.True(result.GetProperty("approved").GetBoolean());
        Assert.Equal("pure", result.GetProperty("tags")[0].GetString());
    }

    [Fact]
    public async Task Args_are_deeply_read_only_and_cannot_mutate_the_request_snapshot()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();
        var request = Request(
            "(() => { try { args = {}; } catch { } try { args.order.total = 0; } catch { } try { args.order.lines.push(99); } catch { } try { args.order.extra = 1; } catch { } return args.order.total + ':' + args.order.lines.length + ':' + typeof args.order.extra; })()",
            new Dictionary<string, JsonElement>
            {
                ["order"] = JsonSerializer.SerializeToElement(new { total = 21, lines = new[] { 1, 2 } })
            });

        var result = await evaluator.EvaluateAsync(request);

        Assert.Equal("21:2:undefined", result.GetString());
        Assert.Equal(21, request.ParameterValues["order"].GetProperty("total").GetInt32());
        Assert.Equal(2, request.ParameterValues["order"].GetProperty("lines").GetArrayLength());
    }

    [Theory]
    [InlineData("variables")]
    [InlineData("input")]
    [InlineData("output")]
    [InlineData("ExpressionExecutionContext")]
    [InlineData("getVariable")]
    [InlineData("setVariable")]
    [InlineData("getInput")]
    [InlineData("getOutput")]
    [InlineData("getOutputFrom")]
    [InlineData("getConfiguration")]
    [InlineData("services")]
    public async Task Ambient_workflow_values_and_host_functions_are_unavailable(string name)
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var result = await evaluator.EvaluateAsync(Request($"typeof {name}", new Dictionary<string, JsonElement>()));

        Assert.Equal("undefined", result.GetString());
    }

    [Fact]
    public async Task Pure_evaluation_does_not_expose_clr_access()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var result = await evaluator.EvaluateAsync(Request("typeof importNamespace + ':' + typeof System", new Dictionary<string, JsonElement>()));

        Assert.Equal("undefined:undefined", result.GetString());
    }

    [Fact]
    public async Task Pure_evaluation_has_no_configuration_host_function()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var result = await evaluator.EvaluateAsync(Request(
            "typeof getConfiguration + ':' + typeof configuration",
            new Dictionary<string, JsonElement>()));

        Assert.Equal("undefined:undefined", result.GetString());
    }

    [Fact]
    public async Task Time_random_locale_and_environment_intrinsics_are_unavailable()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var result = await evaluator.EvaluateAsync(Request(
            "[typeof globalThis.Date, typeof globalThis.Temporal, typeof globalThis.Intl, typeof Math.random, typeof globalThis.crypto, typeof globalThis.performance, typeof globalThis.process].join(':')",
            new Dictionary<string, JsonElement>()));

        Assert.Equal("undefined:undefined:undefined:undefined:undefined:undefined:undefined", result.GetString());
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private static ExpressionEvaluationRequest Request(
        string source,
        IReadOnlyDictionary<string, JsonElement> values)
    {
        var bindings = values.ToDictionary(
            item => item.Key,
            item => (ExpressionParameterBinding)new LiteralExpressionParameterBinding(item.Value),
            StringComparer.Ordinal);
        var definition = new ExpressionDefinition(
            "JavaScript",
            source,
            new TypeReference("Any"),
            bindings,
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV1);
        return new ExpressionEvaluationRequest(definition, values, CancellationToken.None);
    }
}
