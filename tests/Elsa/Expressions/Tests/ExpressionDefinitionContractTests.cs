using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Expressions.Tests;

public sealed class ExpressionDefinitionContractTests
{
    [Fact]
    public void Definition_round_trips_every_closed_parameter_kind_and_clones_options()
    {
        using var optionsDocument = JsonDocument.Parse("""{"strict":true,"limits":{"statements":100}}""");
        var parameters = new Dictionary<string, ExpressionParameterBinding>(StringComparer.Ordinal)
        {
            ["tax"] = new VariableExpressionParameterBinding("workflow", "tax"),
            ["subtotal"] = new WorkflowRequestExpressionParameterBinding("subtotal"),
            ["discount"] = new ActivityResultExpressionParameterBinding("calculate-discount", "amount"),
            ["factor"] = new LiteralExpressionParameterBinding(JsonSerializer.SerializeToElement(1.2m))
        };
        var definition = new ExpressionDefinition(
            "JavaScript",
            "(args.subtotal - args.discount) * args.factor + args.tax",
            new TypeReference("Decimal"),
            parameters,
            optionsDocument.RootElement,
            ExpressionCapabilityProfiles.BindingPureV1);

        parameters.Clear();
        optionsDocument.Dispose();
        var json = JsonSerializer.Serialize(definition);
        var roundTripped = JsonSerializer.Deserialize<ExpressionDefinition>(json)!;

        Assert.Equal(["discount", "factor", "subtotal", "tax"], definition.Parameters.Keys);
        Assert.Equal(definition, roundTripped);
        Assert.True(roundTripped.Options.GetProperty("strict").GetBoolean());
        Assert.IsType<ActivityResultExpressionParameterBinding>(roundTripped.Parameters["discount"]);
        Assert.IsType<LiteralExpressionParameterBinding>(roundTripped.Parameters["factor"]);
        Assert.IsType<WorkflowRequestExpressionParameterBinding>(roundTripped.Parameters["subtotal"]);
        Assert.IsType<VariableExpressionParameterBinding>(roundTripped.Parameters["tax"]);
        Assert.DoesNotContain("Assembly", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Fingerprint_is_order_independent_but_covers_behavioral_fields()
    {
        var first = Definition(new Dictionary<string, ExpressionParameterBinding>
        {
            ["b"] = new LiteralExpressionParameterBinding(JsonSerializer.SerializeToElement(2)),
            ["a"] = new WorkflowRequestExpressionParameterBinding("amount")
        });
        var reordered = Definition(new Dictionary<string, ExpressionParameterBinding>
        {
            ["a"] = new WorkflowRequestExpressionParameterBinding("amount"),
            ["b"] = new LiteralExpressionParameterBinding(JsonSerializer.SerializeToElement(2))
        });
        var changed = Definition(new Dictionary<string, ExpressionParameterBinding>
        {
            ["a"] = new WorkflowRequestExpressionParameterBinding("amount"),
            ["b"] = new LiteralExpressionParameterBinding(JsonSerializer.SerializeToElement(3))
        });

        Assert.Equal(first.Fingerprint, reordered.Fingerprint);
        Assert.NotEqual(first.Fingerprint, changed.Fingerprint);
        Assert.StartsWith("sha256:", first.Fingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluation_request_requires_exact_declared_parameter_set()
    {
        var definition = Definition(new Dictionary<string, ExpressionParameterBinding>
        {
            ["amount"] = new WorkflowRequestExpressionParameterBinding("amount")
        });

        var missing = Assert.Throws<ArgumentException>(() => new ExpressionEvaluationRequest(
            definition,
            new Dictionary<string, JsonElement>(),
            CancellationToken.None));
        var unknown = Assert.Throws<ArgumentException>(() => new ExpressionEvaluationRequest(
            definition,
            new Dictionary<string, JsonElement>
            {
                ["amount"] = JsonSerializer.SerializeToElement(12m),
                ["other"] = JsonSerializer.SerializeToElement(1)
            },
            CancellationToken.None));

        Assert.Contains("Missing: [amount]", missing.Message, StringComparison.Ordinal);
        Assert.Contains("unknown: [other]", unknown.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ExpressionDefinition Definition(IReadOnlyDictionary<string, ExpressionParameterBinding> parameters) =>
        new(
            "JavaScript",
            "args.a + args.b",
            new TypeReference("Int32"),
            parameters,
            JsonSerializer.SerializeToElement(new { strict = true }),
            ExpressionCapabilityProfiles.BindingPureV1);
}
