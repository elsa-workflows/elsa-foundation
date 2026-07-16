using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.Liquid;
using Elsa.Expressions.Liquid.Services;
using Elsa.Primitives.Models;
using Fluid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.Tests;

public sealed class LiquidExplicitParametersTests
{
    [Fact]
    public void Liquid_feature_registers_the_portable_handler()
    {
        var services = new ServiceCollection();
        new LiquidExpressionsFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var handlers = scope.ServiceProvider.GetServices<IPortableExpressionHandler>();

        Assert.IsType<PortableLiquidExpressionHandler>(Assert.Single(handlers));
    }

    [Fact]
    public async Task Exposes_only_declared_json_parameter_roots()
    {
        var request = Request(
            "{{ customer.name }}:{{ customer.address.city }}",
            new Dictionary<string, JsonElement>
            {
                ["customer"] = JsonSerializer.SerializeToElement(new
                {
                    name = "Ada",
                    address = new { city = "London" }
                })
            });

        var result = await Handler().EvaluateAsync(request);

        Assert.Equal(JsonValueKind.String, result.ValueKind);
        Assert.Equal("Ada:London", result.GetString());
    }

    [Fact]
    public async Task Rejects_ambient_workflow_context_or_services_as_undeclared_parameters()
    {
        var request = Request(
            "[{{ Variables }}][{{ Input }}][{{ ExpressionExecutionContext }}][{{ Services }}][{{ Configuration }}]",
            new Dictionary<string, JsonElement>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Handler().EvaluateAsync(request));

        Assert.Contains("undeclared parameter", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Portable_handler_never_inherits_legacy_configuration_access()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Secret"] = "must-not-leak" })
            .Build());
        new LiquidExpressionsFeature { AllowConfigurationAccess = true }.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetServices<IPortableExpressionHandler>().Single();
        var request = Request("{{ Configuration.Secret }}", new Dictionary<string, JsonElement>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await handler.EvaluateAsync(request));

        Assert.Contains("undeclared parameter", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("must-not-leak", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Configuration")]
    [InlineData("Environment")]
    [InlineData("Services")]
    [InlineData("DateTime")]
    [InlineData("Guid")]
    [InlineData("Random")]
    [InlineData("now")]
    [InlineData("today")]
    public async Task Rejects_each_ambient_host_or_nondeterministic_root(string root)
    {
        var request = Request($"{{{{ {root} }}}}", new Dictionary<string, JsonElement>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Handler().EvaluateAsync(request));

        Assert.Contains("undeclared parameter", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_register_the_time_dependent_date_filter()
    {
        var request = Request("{{ 'now' | date: '%s' }}", new Dictionary<string, JsonElement>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await Handler().EvaluateAsync(request));

        Assert.Contains("binding-pure-v1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Local_assignment_cannot_write_back_to_the_parameter_snapshot()
    {
        var request = Request(
            "{% assign order = 'changed' %}{{ order }}",
            new Dictionary<string, JsonElement>
            {
                ["order"] = JsonSerializer.SerializeToElement(new { status = "original" })
            });

        var result = await Handler().EvaluateAsync(request);

        Assert.Equal("changed", result.GetString());
        Assert.Equal("original", request.ParameterValues["order"].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Returns_the_rendered_value_as_json()
    {
        var request = Request(
            "{{ orderNumber }}",
            new Dictionary<string, JsonElement>
            {
                ["orderNumber"] = JsonSerializer.SerializeToElement(42)
            });

        var result = await Handler().EvaluateAsync(request);

        Assert.Equal(JsonValueKind.String, result.ValueKind);
        Assert.Equal("42", result.GetString());
    }

    [Theory]
    [InlineData("Int32", JsonValueKind.Number)]
    [InlineData("Decimal", JsonValueKind.Number)]
    [InlineData("Boolean", JsonValueKind.True)]
    public async Task Normalizes_primitive_results_to_the_declared_json_kind(string resultType, JsonValueKind expectedKind)
    {
        var value = resultType switch
        {
            "Int32" => JsonSerializer.SerializeToElement(42),
            "Decimal" => JsonSerializer.SerializeToElement(12.5m),
            "Boolean" => JsonSerializer.SerializeToElement(true),
            _ => throw new ArgumentOutOfRangeException(nameof(resultType))
        };
        var request = Request("{{ value }}", new Dictionary<string, JsonElement> { ["value"] = value }, resultType);

        var result = await Handler().EvaluateAsync(request);

        Assert.Equal(expectedKind, result.ValueKind);
        if (resultType == "Int32")
            Assert.Equal(42, result.GetInt32());
        else if (resultType == "Decimal")
            Assert.Equal(12.5m, result.GetDecimal());
        else
            Assert.True(result.GetBoolean());
    }

    [Theory]
    [InlineData("Object", JsonValueKind.Object)]
    [InlineData("Any", JsonValueKind.Array)]
    public async Task Parses_valid_structured_json_for_dynamic_result_types(string resultType, JsonValueKind expectedKind)
    {
        var source = resultType == "Object"
            ? "{\"name\":\"{{ name }}\",\"active\":true}"
            : "[1,{{ value }}]";
        var values = resultType == "Object"
            ? new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("Ada") }
            : new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(2) };
        var request = Request(source, values, resultType);

        var result = await Handler().EvaluateAsync(request);

        Assert.Equal(expectedKind, result.ValueKind);
        if (expectedKind == JsonValueKind.Object)
            Assert.Equal("Ada", result.GetProperty("name").GetString());
        else
            Assert.Equal(2, result[1].GetInt32());
    }

    private static PortableLiquidExpressionHandler Handler() => new(new FluidParser());

    private static ExpressionEvaluationRequest Request(
        string source,
        IReadOnlyDictionary<string, JsonElement> values,
        string resultType = "String")
    {
        var bindings = values.ToDictionary(
            item => item.Key,
            item => (ExpressionParameterBinding)new LiteralExpressionParameterBinding(item.Value),
            StringComparer.Ordinal);
        var definition = new ExpressionDefinition(
            "Liquid",
            source,
            new TypeReference(resultType),
            bindings,
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV1);
        return new ExpressionEvaluationRequest(definition, values, CancellationToken.None);
    }
}
