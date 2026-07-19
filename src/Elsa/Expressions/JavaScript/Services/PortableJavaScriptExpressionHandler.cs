using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Services;

/// <summary>Routes canonical JavaScript definitions to the isolated engine implementation.</summary>
public sealed class PortableJavaScriptExpressionHandler(IPortableJavaScriptEvaluator evaluator) : IPortableExpressionHandler
{
    public string Language => "JavaScript";

    public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!StringComparer.Ordinal.Equals(request.Definition.Language, Language))
            throw new ArgumentException($"Expected a '{Language}' expression definition.", nameof(request));

        return evaluator.EvaluateAsync(request);
    }
}
