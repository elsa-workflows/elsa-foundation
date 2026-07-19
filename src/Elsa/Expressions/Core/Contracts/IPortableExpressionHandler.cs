using System.Text.Json;
using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Core.Contracts;

/// <summary>A language-specific evaluator for portable, explicitly parameterized expressions.</summary>
public interface IPortableExpressionHandler
{
    string Language { get; }
    ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request);
}
