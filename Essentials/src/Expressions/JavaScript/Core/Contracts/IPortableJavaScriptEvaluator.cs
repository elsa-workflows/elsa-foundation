using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Exceptions;

namespace Elsa.Expressions.JavaScript.Core.Contracts;

/// <summary>
/// Evaluates the isolated JavaScript binding surface from one immutable, explicitly parameterized request.
/// </summary>
public interface IPortableJavaScriptEvaluator
{
    /// <summary>Evaluates the expression in <paramref name="request"/> and returns its result as JSON.</summary>
    /// <exception cref="JavaScriptBindingEvaluationException">
    /// The expression failed after reaching for an ambient capability the deterministic binding sandbox withholds, such
    /// as <c>Date</c> or <c>Math.random</c>. The evaluator's own error is the inner exception.
    /// </exception>
    ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request);
}
