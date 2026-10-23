namespace Elsa.Expressions.JavaScript.Core.Exceptions;

/// <summary>
/// Raised when a portable JavaScript binding expression fails because it reached for an ambient capability the
/// deterministic binding sandbox withholds. Carries the actionable message; the original evaluator error is the inner
/// exception.
/// </summary>
public sealed class JavaScriptBindingEvaluationException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);
