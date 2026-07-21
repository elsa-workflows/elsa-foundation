using System.Text.Json;
using Elsa.Expressions;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint.Services;
using Elsa.Primitives.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// Regression seam for #921: the deterministic binding sandbox strips ambient time/randomness/locale/environment
/// intrinsics (Date, Temporal, Intl, Math.random, ...). Reaching for one used to surface only an opaque native
/// Jint error ("Date is not a constructor") that poisoned the scheduler work item with no indication of the cause.
/// The evaluator now rethrows a <see cref="JavaScriptBindingEvaluationException"/> that names the withheld
/// capability and tells the author to pass the value as an input/variable, while preserving the original error and
/// still failing (no swallowing). Profile-legal expressions keep evaluating unchanged.
/// </summary>
public sealed class StrippedAmbientCapabilityDiagnosticTests
{
    [Theory]
    [InlineData("\"x \" + new Date().toISOString()", "Date")]
    [InlineData("\"n \" + Math.random()", "Math.random")]
    public async Task Reaching_for_a_stripped_ambient_capability_yields_an_actionable_error(string source, string capability)
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var exception = await Assert.ThrowsAsync<JavaScriptBindingEvaluationException>(
            () => evaluator.EvaluateAsync(Request(source)).AsTask());

        Assert.Contains(capability, exception.Message, StringComparison.Ordinal);
        Assert.Contains("deterministic binding sandbox", exception.Message, StringComparison.Ordinal);
        Assert.Contains("inputs or variables", exception.Message, StringComparison.Ordinal);
        // The original Jint error is preserved, not swallowed.
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public async Task A_profile_legal_expression_that_does_not_touch_a_stripped_capability_still_evaluates()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        var result = await evaluator.EvaluateAsync(Request("\"a \" + 1"));

        Assert.Equal("a 1", result.GetString());
    }

    [Fact]
    public async Task An_unrelated_runtime_error_is_not_reframed_as_a_capability_error()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>();

        // Referencing an undeclared identifier is a plain author error, not a stripped-capability reach; it must
        // keep surfacing as its native error rather than being mislabelled a sandbox/determinism problem.
        var exception = await Assert.ThrowsAnyAsync<Exception>(
            () => evaluator.EvaluateAsync(Request("nope.value")).AsTask());

        Assert.IsNotType<JavaScriptBindingEvaluationException>(exception);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private static ExpressionEvaluationRequest Request(string source)
    {
        var definition = new ExpressionDefinition(
            "JavaScript",
            source,
            new TypeReference("Any"),
            new Dictionary<string, ExpressionParameterBinding>(),
            JsonSerializer.SerializeToElement(new { }),
            ExpressionCapabilityProfiles.BindingPureV1);
        return new ExpressionEvaluationRequest(definition, new Dictionary<string, JsonElement>(), CancellationToken.None);
    }
}
