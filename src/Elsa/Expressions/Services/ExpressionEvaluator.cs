using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Expressions.Services;

/// <inheritdoc />
public sealed class ExpressionEvaluator(IExpressionDescriptorRegistry registry, IServiceProvider serviceProvider, IOptions<ExpressionEvaluatorOptions> evaluatorOptions)
    : IExpressionEvaluator
{
    /// <inheritdoc />
    public async ValueTask<T?> EvaluateAsync<T>(IExpression expression, IExpressionExecutionContext context, IExpressionEvaluatorOptions? options = null)
    {
        return (T?)await EvaluateAsync(expression, typeof(T), context, options);
    }

    /// <inheritdoc />
    public async ValueTask<object?> EvaluateAsync(IExpression expression, Type returnType, IExpressionExecutionContext context, IExpressionEvaluatorOptions? options = null)
    {
        var expressionType = expression.Type;
        var expressionDescriptor = registry.Find(expressionType)
                                   ?? throw new($"Could not find a descriptor for expression type \"{expressionType}\".");

        var handler = expressionDescriptor.HandlerFactory(serviceProvider);
        options ??= evaluatorOptions.Value;
        return await handler.EvaluateAsync(expression, returnType, context, options);
    }
}