using System.Text.Json;
using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.JavaScript.Core.Contracts;

/// <summary>
/// Evaluates the isolated JavaScript binding surface from one immutable, explicitly parameterized request.
/// </summary>
public interface IPortableJavaScriptEvaluator
{
    /// <summary>Evaluates the expression in <paramref name="request"/> and returns its result as JSON.</summary>
    ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request);
}
