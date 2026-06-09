using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Serialization.Core;

namespace Elsa.Expressions.JavaScript.Services;

/// <summary>
/// Evaluates JavaScript expressions.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="JavaScriptExpressionHandler"/> class.
/// </remarks>
public sealed class JavaScriptExpressionHandler(IJavaScriptEvaluator javaScriptEvaluator, IObjectConverter objectConverter)
    : IExpressionHandler
{
    /// <inheritdoc />
    public async ValueTask<object?> EvaluateAsync(IExpression expression, Type returnType, IExpressionExecutionContext context, IExpressionEvaluatorOptions options)
    {
        var javaScriptExpression = objectConverter.ConvertTo<string>(expression.Value) ?? "";

        return await javaScriptEvaluator.EvaluateAsync(
            javaScriptExpression,
            returnType,
            context,
            options,
            additionalFunctions: [],
            context.CancellationToken
        );
    }
}