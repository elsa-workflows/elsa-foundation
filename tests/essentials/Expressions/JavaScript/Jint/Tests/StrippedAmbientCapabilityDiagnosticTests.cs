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
    [InlineData("Math.random()", "Math.random")]
    [InlineData("\"multi \" +\n    Math.random()", "Math.random")]
    [InlineData("Math.abs(1) + Math.random()", "Math.random")]
    [InlineData("(() => { globalThis = { Date: {} }; return typeof globalThis.Date?.[Math.random()]; })()", "Math.random")]
    public async Task Reaching_for_a_stripped_ambient_capability_yields_an_actionable_error(string source, string capability)
    {
        var exception = await Assert.ThrowsAsync<JavaScriptBindingEvaluationException>(
            () => EvaluateAsync(source));

        Assert.Contains(capability, exception.Message, StringComparison.Ordinal);
        Assert.Contains("deterministic binding sandbox", exception.Message, StringComparison.Ordinal);
        Assert.Contains("inputs or variables", exception.Message, StringComparison.Ordinal);
        // The original Jint error is preserved, not swallowed.
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public async Task A_profile_legal_expression_that_does_not_touch_a_stripped_capability_still_evaluates()
    {
        var result = await EvaluateAsync("\"a \" + 1");

        Assert.Equal("a 1", result.GetString());
    }

    [Theory]
    [InlineData("(Math = { random: () => 7 }, Math.random())")]
    [InlineData("(globalThis.Math = { random: () => 7 }, globalThis.Math.random())")]
    [InlineData("(globalThis['Math'] = { random: () => 7 }, globalThis.Math.random())")]
    [InlineData("(globalThis = { Math: { random: () => 7 } }, globalThis.Math.random())")]
    [InlineData("(globalThis = { Date: { now: () => 7 } }, globalThis.Date.now())")]
    public async Task Explicit_root_replacement_expressions_still_evaluate_without_sandbox_attribution(string source)
    {
        var result = await EvaluateAsync(source);

        Assert.Equal(7, result.GetInt32());
    }

    [Theory]
    [InlineData("typeof Math?.random")]
    [InlineData("typeof globalThis?.Math.random")]
    [InlineData("typeof Date?.now")]
    [InlineData("typeof globalThis?.Date?.now")]
    [InlineData("typeof globalThis.Date?.now")]
    [InlineData("typeof Date?.()")]
    [InlineData("typeof Date?.now()")]
    [InlineData("typeof Date?.now(Math.random())")]
    [InlineData("typeof Date?.[Math.random()]")]
    [InlineData("typeof globalThis.Date?.[Math.random()]")]
    [InlineData("typeof Math.random?.(Date.now())")]
    public async Task Optional_typeof_probes_short_circuit_without_ambient_access(string source)
    {
        var result = await EvaluateAsync(source);

        Assert.Equal("undefined", result.GetString());
    }

    [Theory]
    [InlineData("nope.value")]
    [InlineData("({ Date: missingName })")]
    [InlineData("/* Math.random() */ missingName")]
    [InlineData("({ text: 'Date', value: missingName })")]
    [InlineData("(() => { typeof Date; return missingName; })()")]
    [InlineData("(() => { const Math = { random: () => missingName }; return Math.random(); })()")]
    [InlineData("((Math) => Math.random())({})")]
    [InlineData("((Date) => new Date())(undefined)")]
    [InlineData("(missingName, Math.random())")]
    [InlineData("(Math.random, missingName)")]
    [InlineData("(false && Math.random(), missingName)")]
    [InlineData("Math[\"ran\" + \"dom\"]()")]
    [InlineData("(null).Date")]
    [InlineData("({ Date: undefined }).Date()")]
    [InlineData("Math = {}, Math.random()")]
    [InlineData("globalThis.Math = {}, globalThis.Math.random()")]
    [InlineData("globalThis['Math'] = {}, globalThis.Math.random()")]
    [InlineData("globalThis = { Math: {} }, globalThis.Math.random()")]
    [InlineData("globalThis = { Date: {} }, globalThis.Date.now()")]
    public async Task An_unrelated_runtime_error_is_not_reframed_as_a_capability_error(string source)
    {
        // Unrelated JavaScript failures are author/runtime errors, not stripped-capability reaches; they must keep
        // surfacing as native errors rather than being mislabelled as sandbox/determinism problems.
        var exception = await Assert.ThrowsAnyAsync<Exception>(
            () => EvaluateAsync(source));

        Assert.IsNotType<JavaScriptBindingEvaluationException>(exception);
    }

    private static async Task<JsonElement> EvaluateAsync(string source)
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPortableExpressionEvaluator>().EvaluateAsync(Request(source));
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
