using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.Liquid.Services;
using Elsa.Primitives.Models;
using Fluid;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// A schemaless <c>Any</c> value reads identically after the portable expression boundary freezes
/// either its fresh <see cref="JsonNode"/> form or its durable <see cref="JsonElement"/> form.
/// </summary>
public sealed class AnyValueReadParityTests
{
    private const string PayloadJson = """{"customer":{"name":"Alice","age":42},"tags":["red","green"]}""";

    public static TheoryData<string, string> Fields() => new()
    {
        { "customer.name", "Alice" },
        { "customer.age", "42" },
        { "tags[0]", "red" },
    };

    [Theory]
    [MemberData(nameof(Fields))]
    public async Task JsonNodeForm_ReadsIdenticallyFromPortableJavaScriptAndLiquid(string path, string expected) =>
        await AssertParity(JsonNode.Parse(PayloadJson)!, path, expected);

    [Theory]
    [MemberData(nameof(Fields))]
    public async Task JsonElementForm_ReadsIdenticallyFromPortableJavaScriptAndLiquid(string path, string expected) =>
        await AssertParity(JsonSerializer.Deserialize<JsonElement>(PayloadJson), path, expected);

    [Fact]
    public async Task FormattedJsonConversionResult_ReadsPropertiesAndArrayItemsFromPortableJavaScript()
    {
        var converted = JsonSerializer.Deserialize<JsonElement>(PayloadJson);
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["payload"] = converted
        };

        var property = await ReadWithJavaScript(values, "customer.name");
        var arrayItem = await ReadWithJavaScript(values, "tags[1]");

        Assert.Equal("Alice", property);
        Assert.Equal("green", arrayItem);
    }

    private static async Task AssertParity(object value, string path, string expected)
    {
        var frozenValue = JsonSerializer.SerializeToElement(value, value.GetType());
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["payload"] = frozenValue
        };

        var javaScript = await ReadWithJavaScript(values, path);
        var liquid = await ReadWithLiquid(values, path);

        Assert.Equal(expected, javaScript);
        Assert.Equal(expected, liquid);
        Assert.Equal(javaScript, liquid);
        Assert.Equal(PayloadJson, frozenValue.GetRawText());
    }

    private static async Task<string?> ReadWithJavaScript(
        IReadOnlyDictionary<string, JsonElement> values,
        string path)
    {
        await using var provider = JintTestHost.Build();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableJavaScriptEvaluator>();
        var result = await evaluator.EvaluateAsync(Request("JavaScript", $"args.payload.{path}", values));
        return result.ToString();
    }

    private static async Task<string?> ReadWithLiquid(
        IReadOnlyDictionary<string, JsonElement> values,
        string path)
    {
        var evaluator = new PortableLiquidExpressionHandler(new FluidParser());
        var result = await evaluator.EvaluateAsync(Request("Liquid", "{{ payload." + path + " }}", values));
        return result.ToString();
    }

    private static ExpressionEvaluationRequest Request(
        string language,
        string source,
        IReadOnlyDictionary<string, JsonElement> values)
    {
        var parameters = values.ToDictionary(
            item => item.Key,
            item => (ExpressionParameterBinding)new LiteralExpressionParameterBinding(item.Value),
            StringComparer.Ordinal);
        var definition = new ExpressionDefinition(
            language,
            source,
            new TypeReference("Any"),
            parameters,
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV1);
        return new ExpressionEvaluationRequest(definition, values, CancellationToken.None);
    }
}
