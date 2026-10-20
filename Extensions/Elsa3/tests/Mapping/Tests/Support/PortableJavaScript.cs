using System.Text.Json;
using Elsa.Expressions;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Mapping.Tests.Support;

/// <summary>
/// Evaluates a converted expression with the Elsa 4 binding-pure JavaScript evaluator, so a test can compare its
/// result with what Elsa 3 returned (spec 247 SC-003). Parameter values are passed directly; no runtime is involved.
/// </summary>
internal static class PortableJavaScript
{
    public static async Task<JsonElement> EvaluateAsync(
        ExpressionDefinition definition,
        IReadOnlyDictionary<string, JsonElement>? args = null)
    {
        var services = new ServiceCollection();
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();
        return await evaluator.EvaluateAsync(new ExpressionEvaluationRequest(
            definition,
            args ?? new Dictionary<string, JsonElement>(),
            CancellationToken.None));
    }

    /// <summary>Runs a converted RunJavaScript body with the Elsa 4 script evaluator, with no arguments (spec 261 R-5).</summary>
    public static async Task<JsonElement?> RunScriptAsync(string source)
    {
        var services = new ServiceCollection();
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IJavaScriptScriptEvaluator>();
        return await evaluator.EvaluateAsync(new JavaScriptScriptEvaluationRequest(
            source,
            JsonSerializer.SerializeToElement(new { }),
            CancellationToken.None));
    }
}
