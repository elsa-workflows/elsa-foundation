using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.Services;

public sealed class VariableExpressionHandler : IExpressionHandler
{
    public ValueTask<object?> EvaluateAsync(IExpression expression, Type returnType, IExpressionExecutionContext context, IExpressionEvaluatorOptions options)
    {
        var variableId = $"{expression.Value}";
        var variable = context.GetVariable(variableId);
        var value = variable?.Get(context);

        return new(value);
    }
}
