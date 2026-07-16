using System.Text.Json;
using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.JavaScript.Core.Contracts;

/// <summary>
/// Evaluates the isolated JavaScript binding surface. Unlike <see cref="IJavaScriptEvaluator"/>, this
/// contract accepts no workflow execution context or extension callbacks.
/// </summary>
public interface IPortableJavaScriptEvaluator
{
    ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request);
}
