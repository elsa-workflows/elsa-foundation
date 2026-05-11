using Elsa.Expressions.Core;
using Elsa.Expressions.Liquid.Contracts;
using Elsa.Serialization.Core;

namespace Elsa.Expressions.Liquid.Services
{
    /// <summary>
    /// Evaluates a Liquid expression.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="LiquidExpressionHandler"/> class.
    /// </remarks>
    public sealed class LiquidExpressionHandler(ILiquidTemplateManager liquidTemplateManager, IObjectConverter objectConverter)
        : IExpressionHandler
    {
        /// <inheritdoc />
        public async ValueTask<object?> EvaluateAsync(IExpression expression, Type returnType, IExpressionExecutionContext context, IExpressionEvaluatorOptions _)
        {
            var liquidExpression = expression.GetValue<string>() ?? "";
            var renderedString = await liquidTemplateManager.RenderAsync(liquidExpression, context);
            return objectConverter.ConvertTo(renderedString, returnType);
        }
    }
}
