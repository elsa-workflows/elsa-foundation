using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Liquid.Services;
using Elsa.Primitives.Models;
using Fluid;
using Xunit;

namespace Elsa.Expressions.Tests;

/// <summary>
/// Liquid reads JSON numbers as decimals and does not narrow them, so it needs no precision policy (spec 1228 T043,
/// edge case "Liquid", docs/reference/value-adapters.md).
/// </summary>
public sealed class LiquidNumberTests
{
    [Theory]
    [InlineData("9007199254740993")]
    [InlineData("-9007199254740993")]
    [InlineData("10.50")]
    [InlineData("0.1000000000000000000000000001")]
    public async Task Liquid_renders_a_large_or_long_number_without_narrowing(string number)
    {
        var request = Request("{{ order.id }}", new Dictionary<string, JsonElement>
        {
            ["order"] = JsonSerializer.Deserialize<JsonElement>($$"""{"id":{{number}}}""")
        });

        var result = await new PortableLiquidExpressionHandler(new FluidParser()).EvaluateAsync(request);

        Assert.Equal(number, result.GetString());
    }

    private static ExpressionEvaluationRequest Request(string source, IReadOnlyDictionary<string, JsonElement> values)
    {
        var bindings = values.ToDictionary(
            item => item.Key,
            item => (ExpressionParameterBinding)new LiteralExpressionParameterBinding(item.Value),
            StringComparer.Ordinal);
        var definition = new ExpressionDefinition(
            "Liquid",
            source,
            new TypeReference("String"),
            bindings,
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingReadonlyV1);
        return new ExpressionEvaluationRequest(definition, values, CancellationToken.None);
    }
}
