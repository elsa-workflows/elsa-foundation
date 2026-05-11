using Elsa.Expressions.Core;

namespace Elsa.Expressions.Liquid.Contracts
{
    public interface ILiquidTemplateManager
    {
        Task<string?> RenderAsync(string template, IExpressionExecutionContext expressionExecutionContext, CancellationToken cancellationToken = default);
    }
}
