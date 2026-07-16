using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
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

    [Fact]
    public void Evaluation_request_resolves_an_explicit_immutable_capability_contract()
    {
        var request = new ExpressionEvaluationRequest(
            Definition(new Dictionary<string, ExpressionParameterBinding>()),
            new Dictionary<string, JsonElement>(),
            CancellationToken.None);

        Assert.Equal(ExpressionCapabilityProfiles.BindingPureV1, request.Capabilities.Profile);
        Assert.True(request.Capabilities.IsBindingPure);
        Assert.True(request.Capabilities.AllowsDeterministicTransforms);
        Assert.False(request.Capabilities.AllowsAmbientWorkflowState);
        Assert.False(request.Capabilities.AllowsMutation);
        Assert.False(request.Capabilities.AllowsServiceLocation);
        Assert.False(request.Capabilities.AllowsNondeterminism);
    }

    [Fact]
    public void Portable_contract_surface_cannot_carry_delegates_execution_contexts_or_service_locators()
    {
        var contractTypes = new[]
        {
            typeof(ExpressionDefinition),
            typeof(ExpressionParameterBinding),
            typeof(LiteralExpressionParameterBinding),
            typeof(WorkflowRequestExpressionParameterBinding),
            typeof(VariableExpressionParameterBinding),
            typeof(ActivityResultExpressionParameterBinding),
            typeof(ExpressionEvaluationRequest),
            typeof(ExpressionEvaluationCapabilities),
            typeof(IPortableExpressionEvaluator),
            typeof(IPortableExpressionHandler)
        };
        var exposedTypes = contractTypes.SelectMany(type =>
                type.GetConstructors().SelectMany(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType))
                    .Concat(type.GetProperties().Select(property => property.PropertyType))
                    .Concat(type.GetMethods().Where(method => !method.IsSpecialName).Select(method => method.ReturnType))
                    .Concat(type.GetMethods().Where(method => !method.IsSpecialName).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType))))
            .SelectMany(FlattenType)
            .Distinct()
            .ToArray();

        Assert.DoesNotContain(exposedTypes, type => typeof(Delegate).IsAssignableFrom(type));
        Assert.DoesNotContain(typeof(IServiceProvider), exposedTypes);
        Assert.DoesNotContain(typeof(IExpressionExecutionContext), exposedTypes);
    }

    private static ExpressionDefinition Definition(IReadOnlyDictionary<string, ExpressionParameterBinding> parameters) =>
        new(
            "JavaScript",
            "args.a + args.b",
            new TypeReference("Int32"),
            parameters,
            JsonSerializer.SerializeToElement(new { strict = true }),
            ExpressionCapabilityProfiles.BindingPureV1);

    private static IEnumerable<Type> FlattenType(Type type)
    {
        yield return type;
        if (type.HasElementType)
        {
            foreach (var nested in FlattenType(type.GetElementType()!))
                yield return nested;
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nested in FlattenType(argument))
                yield return nested;
        }
    }
}
