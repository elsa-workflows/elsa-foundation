using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Expressions.Liquid;
using Elsa.Primitives.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// What a script can read, through the evaluator the public expression features compose. Neither engine is handed a
/// CLR object: a declared parameter or an ambient variable enters Jint as a frozen native JavaScript object built from
/// its JSON, and Liquid as a <see cref="System.Text.Json.Nodes.JsonNode"/>. So no Elsa type is visible to a script, and
/// making the evaluators, handlers and helpers behind these features internal cannot hide a member from one. These
/// reads fail if either engine stops reaching the members of the values it is given.
/// </summary>
public sealed class ComposedScriptMemberReadTests
{
    private const string Order = """{"customer":{"name":"Alice"},"lines":[{"sku":"A1"},{"sku":"B2"}]}""";

    [Fact]
    public async Task JavaScript_reads_members_of_a_declared_parameter_and_of_an_ambient_variable()
    {
        var result = await EvaluateAsync(
            "JavaScript",
            "args.order.customer.name + ':' + args.order.lines[1].sku + ':' + variables.limits.max",
            ambientVariables: new Dictionary<string, JsonElement> { ["limits"] = JsonSerializer.Deserialize<JsonElement>("""{"max":5}""") });

        Assert.Equal("Alice:B2:5", result.GetString());
    }

    [Fact]
    public async Task Liquid_reads_members_of_a_declared_parameter()
    {
        var result = await EvaluateAsync("Liquid", "{{ order.customer.name }}:{{ order.lines[1].sku }}");

        Assert.Equal("Alice:B2", result.GetString());
    }

    private static async Task<JsonElement> EvaluateAsync(
        string language,
        string source,
        IReadOnlyDictionary<string, JsonElement>? ambientVariables = null)
    {
        var services = new ServiceCollection();
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        new LiquidExpressionsFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var definition = new ExpressionDefinition(
            language,
            source,
            new TypeReference("String"),
            new Dictionary<string, ExpressionParameterBinding> { ["order"] = new WorkflowRequestExpressionParameterBinding("order") },
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV1);
        var values = new Dictionary<string, JsonElement> { ["order"] = JsonSerializer.Deserialize<JsonElement>(Order) };

        return await scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>()
            .EvaluateAsync(new ExpressionEvaluationRequest(definition, values, ambientVariables, CancellationToken.None));
    }
}
