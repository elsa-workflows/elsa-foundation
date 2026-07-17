using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;
using Jint;
using Jint.Native;
using Microsoft.Extensions.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// Executes portable binding expressions in a fresh, closed Jint engine. It intentionally does not use
/// the legacy engine configurator, event, preprocessor, host-function, or CLR-access pipelines.
/// </summary>
internal sealed class JintPortableJavaScriptEvaluator(IOptions<FeatureOptions> featureOptions) : IPortableJavaScriptEvaluator
{
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
        IsolatedJintEngine.SetReadOnlyArgs(engine, request.ParameterValues);

        var result = engine.Evaluate($"\"use strict\"; ({definition.Source})");
        if (result.IsUndefined())
            throw new InvalidOperationException("A portable JavaScript expression cannot return undefined.");

        return ValueTask.FromResult(JintResultMaterializer.Materialize(engine, result, featureOptions.Value));
    }
}
