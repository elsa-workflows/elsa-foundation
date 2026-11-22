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
/// No CLR object reaches a script, through the evaluator the public expression features compose. A value handed to
/// Jint, whether a declared parameter or an ambient variable, is a frozen plain JavaScript object built from its JSON:
/// a write to it is refused and changes nothing, and no .NET member or CLR namespace is reachable from it. Liquid
/// renders a member that only the .NET object carrying the value would have as empty. This is what makes the types
/// behind these features safe to keep internal: a script never sees one.
/// </summary>
public sealed class ScriptValueIsolationTests
{
    private static readonly JsonElement Order = JsonSerializer.Deserialize<JsonElement>("""{"status":"open","customer":{"name":"Alice"}}""");

    [Fact]
    public async Task JavaScript_sees_frozen_plain_objects_with_no_clr_surface()
    {
        const string source = """
            (() => {
              const probe = value => {
                let refused = false;
                try { value.status = 'tampered'; } catch (e) { refused = e instanceof TypeError; }
                return {
                  refused,
                  unchanged: value.status === 'open',
                  frozen: Object.isFrozen(value),
                  plain: Object.getPrototypeOf(value) === Object.prototype && value.constructor === Object,
                  clrMembers: [typeof value.GetType, typeof value.ToString, typeof value.Equals, typeof value.GetHashCode].join()
                };
              };
              return JSON.stringify({
                parameter: probe(args.order),
                variable: probe(variables.order),
                clr: [typeof importNamespace, typeof System].join()
              });
            })()
            """;

        var result = await EvaluateAsync("JavaScript", source, new Dictionary<string, JsonElement> { ["order"] = Order });

        const string isolated = """{"refused":true,"unchanged":true,"frozen":true,"plain":true,"clrMembers":"undefined,undefined,undefined,undefined"}""";
        Assert.Equal($$"""{"parameter":{{isolated}},"variable":{{isolated}},"clr":"undefined,undefined"}""", result.GetString());
    }

    [Theory]
    [InlineData("ValueKind")]
    [InlineData("Count")]
    [InlineData("Parent")]
    public async Task Liquid_renders_a_member_only_a_clr_object_has_as_empty(string member)
    {
        // The status read beside it shows the value itself is readable, so the empty render is the member's alone.
        var result = await EvaluateAsync("Liquid", $"{{{{ order.status }}}}|{{{{ order.{member} }}}}");

        Assert.Equal("open|", result.GetString());
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
        var values = new Dictionary<string, JsonElement> { ["order"] = Order };

        return await scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>()
            .EvaluateAsync(new ExpressionEvaluationRequest(definition, values, ambientVariables, CancellationToken.None));
    }
}
