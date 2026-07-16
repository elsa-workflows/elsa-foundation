using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Liquid.Services;
using Elsa.Primitives.Models;
using Fluid;
using Xunit;

namespace Elsa.Expressions.Tests.Unit;

/// <summary>
/// Invalid portable Liquid definitions fail predictably instead of rendering parser diagnostics as
/// workflow values or requiring an ambient expression execution context.
/// </summary>
public sealed class LiquidTemplateManagerInvalidTemplateTests
{
    [Theory]
    [InlineData("{% if true %}{% endraw %}{% endif %}")]
    [InlineData("{% assign %}")]
    public async Task EvaluateAsync_InvalidTemplate_ThrowsDescriptiveError(string source)
    {
        var evaluator = new PortableLiquidExpressionHandler(new FluidParser());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await evaluator.EvaluateAsync(Request(source)));

        Assert.Contains("Failed to parse Liquid binding expression", exception.Message, StringComparison.Ordinal);
    }

    private static ExpressionEvaluationRequest Request(string source)
    {
        var definition = new ExpressionDefinition(
            "Liquid",
            source,
            new TypeReference("String"),
            new Dictionary<string, ExpressionParameterBinding>(),
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV1);
        return new ExpressionEvaluationRequest(
            definition,
            new Dictionary<string, JsonElement>(),
            CancellationToken.None);
    }
}
