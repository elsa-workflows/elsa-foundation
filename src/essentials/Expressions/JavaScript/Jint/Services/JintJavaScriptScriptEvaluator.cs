using System.Text.Json;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;
using Jint;
using Microsoft.Extensions.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>Executes transient script-activity programs in the same closed value world as pure bindings.</summary>
internal sealed class JintJavaScriptScriptEvaluator(IOptions<FeatureOptions> featureOptions) : IJavaScriptScriptEvaluator
{
    public ValueTask<JsonElement?> EvaluateAsync(JavaScriptScriptEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.CancellationToken.ThrowIfCancellationRequested();

        var engine = IsolatedJintEngine.Create(featureOptions.Value, request.CancellationToken);
        IsolatedJintEngine.SetReadOnlyArgs(engine, request.Arguments, featureOptions.Value);

        var result = engine.Evaluate($"\"use strict\"; (() => {{ {request.Source} }})()");
        if (result.IsUndefined())
            return ValueTask.FromResult<JsonElement?>(null);

        return ValueTask.FromResult<JsonElement?>(JintResultMaterializer.Materialize(engine, result, featureOptions.Value));
    }
}
