using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Microsoft.Extensions.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// Executes portable binding expressions in a fresh, closed Jint engine. It intentionally does not use
/// the legacy engine configurator, event, preprocessor, host-function, or CLR-access pipelines.
/// </summary>
internal sealed class JintPortableJavaScriptEvaluator(IOptions<FeatureOptions> featureOptions) : IPortableJavaScriptEvaluator
{
    /// <summary>
    /// Ambient capabilities that <see cref="IsolatedJintEngine.DisableAmbientCapabilities"/> strips from the
    /// deterministic binding sandbox. Referencing any of them (e.g. <c>new Date()</c>, <c>Math.random()</c>)
    /// makes the engine throw an opaque native <see cref="JavaScriptException"/> such as
    /// "Date is not a constructor". Naming them lets the evaluator turn that into an actionable authoring error.
    /// </summary>
    private static readonly string[] StrippedAmbientCapabilities =
        ["Date", "Temporal", "Intl", "Math.random", "crypto", "performance", "process"];

    public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.CancellationToken.ThrowIfCancellationRequested();

        var definition = request.Definition;
        if (!request.Capabilities.IsBindingPure ||
            !StringComparer.Ordinal.Equals(request.Capabilities.Profile, ExpressionCapabilityProfiles.BindingPureV1))
            throw new InvalidOperationException($"JavaScript binding evaluation requires capability profile '{ExpressionCapabilityProfiles.BindingPureV1}'.");
        if (definition.Options.EnumerateObject().Any())
            throw new InvalidOperationException("The JavaScript binding-pure-v1 evaluator does not support evaluator options.");

        var engine = IsolatedJintEngine.Create(featureOptions.Value, request.CancellationToken);
        IsolatedJintEngine.SetReadOnlyArgs(engine, request.ParameterValues, featureOptions.Value);
        IsolatedJintEngine.SetReadOnlyVariables(engine, request.AmbientVariables, featureOptions.Value);

        JsValue result;
        try
        {
            result = engine.Evaluate($"\"use strict\"; ({definition.Source})");
        }
        catch (JavaScriptException exception)
        {
            // A raw Jint runtime error (e.g. "Date is not a constructor") is opaque: it poisons the scheduler
            // work item with no indication of why. When the expression reached for an ambient capability the
            // deterministic sandbox deliberately withholds, rethrow with an actionable message naming it. The
            // original error is preserved as the inner exception; the failure is not swallowed.
            var reached = ReferencedStrippedCapabilities(definition.Source);
            if (reached.Length == 0)
                throw;

            throw new JavaScriptBindingEvaluationException(
                $"JavaScript binding expression '{definition.Source}' referenced ambient capability " +
                $"[{string.Join(", ", reached)}], which is unavailable in the deterministic binding sandbox " +
                $"(profile '{ExpressionCapabilityProfiles.BindingPureV1}'). Time, randomness, locale, and " +
                "environment data must be supplied as workflow inputs or variables instead of read from ambient " +
                $"JavaScript globals. Original evaluator error: {exception.Message}",
                exception);
        }

        if (result.IsUndefined())
            throw new InvalidOperationException("A portable JavaScript expression cannot return undefined.");

        return ValueTask.FromResult(JintResultMaterializer.Materialize(engine, result, featureOptions.Value));
    }

    private static string[] ReferencedStrippedCapabilities(string source) =>
        StrippedAmbientCapabilities
            .Where(capability => IdentifierReference(capability).IsMatch(source))
            .ToArray();

    // Matches the capability as a whole JS member/identifier token (so "Date" matches `new Date()` and
    // `Math.random` matches `Math.random()` but neither matches a substring like "myDate" or "updated").
    private static Regex IdentifierReference(string capability) =>
        new($@"(?<![\w$.]){Regex.Escape(capability)}(?![\w$])", RegexOptions.CultureInvariant);
}

/// <summary>
/// Raised when a portable JavaScript binding expression fails because it reached for an ambient capability the
/// deterministic binding sandbox withholds. Carries the actionable message; the original Jint error is the inner
/// exception.
/// </summary>
public sealed class JavaScriptBindingEvaluationException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);
