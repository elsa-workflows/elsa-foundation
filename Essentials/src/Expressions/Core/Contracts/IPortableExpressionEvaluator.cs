using System.Text.Json;
using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Core.Contracts;

/// <summary>
/// Evaluates a serialized pure expression using only the immutable values declared by its definition.
/// Evaluator infrastructure is supplied through constructor injection, never through the request.
/// </summary>
public interface IPortableExpressionEvaluator
{
    ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request);
}
