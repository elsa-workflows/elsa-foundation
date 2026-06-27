using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Services;

public sealed class VariableExpressionHandler : IExpressionHandler
{
    public ValueTask<object?> EvaluateAsync(IExpression expression, Type returnType, IExpressionExecutionContext context, IExpressionEvaluatorOptions options)
    {
        if (!VariableReference.TryParse(expression.Value, out var reference) || reference is null || !reference.IsWorkflowScope)
            return new ValueTask<object?>((object?)null);

        var variable = context.GetVariable(reference.ReferenceKey);
        var value = variable?.Get(context);

        return new(value);
    }
}
