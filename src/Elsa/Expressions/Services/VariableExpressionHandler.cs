using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Services;

public sealed class VariableExpressionHandler : IExpressionHandler
{
    public ValueTask<object?> EvaluateAsync(IExpression expression, Type returnType, IExpressionExecutionContext context, IExpressionEvaluatorOptions options)
    {
        if (!VariableReference.TryParse(expression.Value, out var reference) || reference is null)
            return new ValueTask<object?>((object?)null);

        // Container-scoped references resolve through the context's visible scope chain when present;
        // this honours nearest-scope visibility and shadowing (ADR 0027).
        if (context is IScopedVariableProvider scopedProvider && scopedProvider.TryGetScopedVariableValue(reference, out var scopedValue))
            return new(scopedValue);

        if (!reference.IsWorkflowScope)
            return new ValueTask<object?>((object?)null);

        return new(context.GetVariableValueOrDefault(reference.ReferenceKey));
    }
}
