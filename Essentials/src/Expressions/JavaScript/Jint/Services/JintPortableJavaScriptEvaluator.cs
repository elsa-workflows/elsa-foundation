using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;
using Jint;
using Microsoft.Extensions.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// Executes portable binding expressions in a fresh, closed Jint engine under <c>binding-readonly-v1</c>: the native
/// intrinsics on the host clock in UTC, plus the registered .NET-implemented functions (spec 1223 FR-003). It does not use
/// the legacy engine configurator, event, preprocessor or CLR-access pipelines.
/// </summary>
internal sealed class JintPortableJavaScriptEvaluator(
    IOptions<FeatureOptions> featureOptions,
    JavaScriptHostFunctions hostFunctions,
    TimeProvider? timeProvider = null) : IPortableJavaScriptEvaluator
{
    public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.CancellationToken.ThrowIfCancellationRequested();

        var definition = request.Definition;
        if (!request.Capabilities.IsBindingReadonly ||
            !StringComparer.Ordinal.Equals(request.Capabilities.Profile, ExpressionCapabilityProfiles.BindingReadonlyV1))
            throw new InvalidOperationException($"JavaScript binding evaluation requires capability profile '{ExpressionCapabilityProfiles.BindingReadonlyV1}'.");
        if (definition.Options.EnumerateObject().Any())
            throw new InvalidOperationException($"The JavaScript {ExpressionCapabilityProfiles.BindingReadonlyV1} evaluator does not support evaluator options.");

        var prepared = PreparedJavaScript.Binding(definition.Source);

        var engine = IsolatedJintEngine.Create(featureOptions.Value, request.CancellationToken, timeProvider);
        hostFunctions.Install(engine, featureOptions.Value, request.VariableReader, request.CancellationToken);
        IsolatedJintEngine.SetReadOnlyArgs(engine, request.ArgumentValues(), featureOptions.Value);
        IsolatedJintEngine.SetReadOnlyVariables(engine, request.PinnedVariableValues(), request.VariableReader, featureOptions.Value);

        var result = engine.Evaluate(prepared);
        if (result.IsUndefined())
            throw new InvalidOperationException("A JavaScript binding must produce a value. Use an expression or return a value from the statement block; undefined is not supported.");

        return ValueTask.FromResult(JintResultMaterializer.Materialize(engine, result, featureOptions.Value));
    }
}
