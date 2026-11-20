using System.Text.Json;

namespace Elsa.Expressions.JavaScript.Core.Contracts;

/// <summary>
/// Executes a stateful JavaScript program with one explicit immutable argument document. This is an
/// activity service, not an input-binding expression context, and exposes no workflow value writer.
/// </summary>
public interface IJavaScriptScriptEvaluator
{
    ValueTask<JsonElement?> EvaluateAsync(JavaScriptScriptEvaluationRequest request);
}

/// <summary>A complete, immutable request for one transient script-activity attempt.</summary>
public sealed class JavaScriptScriptEvaluationRequest
{
    public JavaScriptScriptEvaluationRequest(
        string source,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("JavaScript activity arguments must be one JSON object.", nameof(arguments));

        Source = source;
        Arguments = arguments.Clone();
        CancellationToken = cancellationToken;
    }

    public string Source { get; }
    public JsonElement Arguments { get; }
    public CancellationToken CancellationToken { get; }
}
