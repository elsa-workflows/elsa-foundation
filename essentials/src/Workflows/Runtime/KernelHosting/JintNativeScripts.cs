using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHosting;

/// <summary>
/// RunJavaScript's engine on the kernel host: the isolated Jint evaluator, with its time, statement and recursion
/// limits. The program sees its arguments as frozen <c>args</c>; its value comes back with each number's JSON text kept, so a
/// number names the outcome it names on the old core.
/// </summary>
public sealed class JintNativeScripts(IJavaScriptScriptEvaluator evaluator) : INativeScripts
{
    /// <inheritdoc />
    public async ValueTask<JsonNode?> EvaluateAsync(string script, JsonObject arguments, CancellationToken cancellationToken)
    {
        var result = await evaluator.EvaluateAsync(new JavaScriptScriptEvaluationRequest(script, JsonSerializer.SerializeToElement(arguments), cancellationToken));
        return result is { } value ? JsonNode.Parse(value.GetRawText()) : null;
    }
}
