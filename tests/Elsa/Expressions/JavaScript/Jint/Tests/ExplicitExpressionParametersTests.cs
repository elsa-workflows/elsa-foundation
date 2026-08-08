using Elsa.ConsumerGuide.Testing;
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
    [ConsumerContract("javascript.json-parameters-are-native-frozen-values")]
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

    [Fact]
    public async Task Rejects_arrays_larger_than_the_configured_sandbox_limit()
    {
        await using var provider = BuildProvider(new JintFeature { MaxArrayLength = 8 });
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => evaluator
            .EvaluateAsync(Request("new Array(9)", new Dictionary<string, JsonElement>()))
            .AsTask());

        Assert.Contains("array", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_parameter_arrays_larger_than_the_configured_sandbox_limit()
    {
        await using var provider = BuildProvider(new JintFeature { MaxArrayLength = 2 });
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => evaluator
            .EvaluateAsync(Request(
                "args.items.length",
                new Dictionary<string, JsonElement>
                {
                    ["items"] = JsonSerializer.SerializeToElement(new[] { 1, 2, 3 })
                }))
            .AsTask());

        Assert.Contains("2 items", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_parameters_larger_than_the_configured_input_byte_limit()
    {
        await using var provider = BuildProvider(new JintFeature { MaxInputBytes = 32 });
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => evaluator
            .EvaluateAsync(Request(
                "args.value",
                new Dictionary<string, JsonElement>
                {
                    ["value"] = JsonSerializer.SerializeToElement(new string('x', 100))
                }))
            .AsTask());

        Assert.Contains("32 bytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Input_byte_limit_counts_materialized_utf8_instead_of_json_encoder_expansion()
    {
        await using var provider = BuildProvider(new JintFeature { MaxInputBytes = 16 });
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var result = await evaluator.EvaluateAsync(Request(
            "args.value",
            new Dictionary<string, JsonElement>
            {
                ["value"] = JsonSerializer.SerializeToElement("é<&")
            }));

        Assert.Equal("é<&", result.GetString());
    }

    [Fact]
    public async Task Rejects_parameters_deeper_than_the_configured_input_depth_limit()
    {
        await using var provider = BuildProvider(new JintFeature { MaxInputDepth = 3 });
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => evaluator
            .EvaluateAsync(Request(
                "args.value",
                new Dictionary<string, JsonElement>
                {
                    ["value"] = JsonSerializer.SerializeToElement(new { a = new { b = new { c = 1 } } })
                }))
            .AsTask());

        Assert.Contains("depth limit of 3", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_parameters_with_more_than_the_configured_input_node_limit()
    {
        await using var provider = BuildProvider(new JintFeature { MaxInputNodes = 4 });
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => evaluator
            .EvaluateAsync(Request(
                "args.items.length",
                new Dictionary<string, JsonElement>
                {
                    ["items"] = JsonSerializer.SerializeToElement(new[] { 1, 2, 3, 4 })
                }))
            .AsTask());

        Assert.Contains("4 nodes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Accepts_nested_parameters_within_all_configured_input_limits()
    {
        await using var provider = BuildProvider(new JintFeature
        {
            MaxArrayLength = 2,
            MaxInputBytes = 128,
            MaxInputDepth = 4,
            MaxInputNodes = 6
        });
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var result = await evaluator.EvaluateAsync(Request(
            "args.order.lines[1].quantity",
            new Dictionary<string, JsonElement>
            {
                ["order"] = JsonSerializer.SerializeToElement(new
                {
                    lines = new[] { new { quantity = 1 }, new { quantity = 3 } }
                })
            }));

        Assert.Equal(3, result.GetInt32());
    }

    [Fact]
    public async Task Rejects_expression_results_larger_than_the_configured_json_limit()
    {
        await using var provider = BuildProvider(new JintFeature { MaxResultBytes = 32 });
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => evaluator
            .EvaluateAsync(Request("'x'.repeat(100)", new Dictionary<string, JsonElement>()))
            .AsTask());

        Assert.Contains("32 bytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_expression_results_deeper_than_the_configured_json_limit()
    {
        await using var provider = BuildProvider(new JintFeature { MaxResultDepth = 3 });
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => evaluator
            .EvaluateAsync(Request("({ a: { b: { c: 1 } } })", new Dictionary<string, JsonElement>()))
            .AsTask());

        Assert.Contains("depth", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_expression_results_with_more_than_the_configured_json_node_limit()
    {
        await using var provider = BuildProvider(new JintFeature { MaxResultNodes = 4 });
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => evaluator
            .EvaluateAsync(Request("[1, 2, 3, 4]", new Dictionary<string, JsonElement>()))
            .AsTask());

        Assert.Contains("4 nodes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Result_materialization_uses_the_captured_intrinsic_serializer()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var result = await evaluator.EvaluateAsync(Request(
            "(() => { JSON.stringify = () => '\"tampered\"'; return { answer: 42 }; })()",
            new Dictionary<string, JsonElement>()));

        Assert.Equal(42, result.GetProperty("answer").GetInt32());
    }

    private static ServiceProvider BuildProvider(JintFeature? jintFeature = null)
    {
        var services = new ServiceCollection();
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        (jintFeature ?? new JintFeature()).ConfigureServices(services);
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
