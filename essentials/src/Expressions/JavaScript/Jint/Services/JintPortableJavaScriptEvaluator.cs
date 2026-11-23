using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Exceptions;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;
using Jint;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// Executes portable binding expressions in a fresh, closed Jint engine under <c>binding-readonly-v1</c>: the native
/// intrinsics on the host clock in UTC, plus the registered .NET-implemented functions (spec 1223 FR-003). It does not use
/// the legacy engine configurator, event, preprocessor or CLR-access pipelines.
/// </summary>
internal sealed class JintPortableJavaScriptEvaluator(
    IOptions<FeatureOptions> featureOptions,
    IScriptValueBridge valueBridge,
    IJavaScriptPrecisionPolicy precisionPolicy,
    JavaScriptHostFunctions hostFunctions,
    TimeProvider? timeProvider = null,
    ILogger<JintPortableJavaScriptEvaluator>? logger = null) : IPortableJavaScriptEvaluator
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

        // Issue #4901: run on a large-stack evaluation thread, so deep calls in an expression cannot overflow the host's stack.
        return ValueTask.FromResult(JintEvaluationThreads.Run(() => Evaluate(request, prepared), featureOptions.Value));
    }

    private JsonElement Evaluate(ExpressionEvaluationRequest request, Prepared<Acornima.Ast.Script> prepared)
    {
        var definition = request.Definition;
        var engine = IsolatedJintEngine.Create(featureOptions.Value, request.CancellationToken, timeProvider);
        using var values = valueBridge.Begin(engine);
        var functions = new JavaScriptHostFunctionContext(values, request.VariableReader, logger);
        hostFunctions.Install(engine, featureOptions.Value, functions, request.CancellationToken);
        IsolatedJintEngine.SetArgs(engine, values, request.ArgumentValues(), featureOptions.Value);
        var variables = IsolatedJintEngine.SetVariables(engine, values, request.PinnedVariableValues(), request.VariableReader, featureOptions.Value);

        EvaluationLimits.Start(engine);
        var result = engine.Evaluate(prepared);
        // Writing the result and checking the variables runs the script's own toJSON and getters: a phase of its own.
        EvaluationLimits.Start(engine);

        // Spec 1228 FR-006a, ADR 0083 binding-readonly-v1: an input expression only reads. Any change the bridge finds
        // fails the evaluation, so nothing is written and nothing is dropped silently.
        if (variables is not null)
        {
            foreach (var (name, change) in variables.CollectForRefusal())
            {
                if (change.Kind != ScriptValueChangeKind.Unchanged)
                    throw new InputExpressionWriteException(name, change.ChangedPaths.Count > 0 ? change.ChangedPaths[0] : change.Key, variables.IsDeclared(name));
            }
        }

        if (result.IsUndefined())
            throw new InvalidOperationException("A JavaScript binding must produce a value. Use an expression or return a value from the statement block; undefined is not supported.");

        foreach (var warning in functions.Warnings)
            request.Warnings?.Report(warning);
        var written = JavaScriptValueWrites.Apply(values.Write(result, "result"), "result", precisionPolicy);
        JavaScriptValueWrites.WarnLossyReads(values, written.Losses, request.Warnings, logger);
        return written.Value!.Value;
    }
}
