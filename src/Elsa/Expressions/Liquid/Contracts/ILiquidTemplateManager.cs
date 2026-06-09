using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.Liquid.Contracts;

public interface ILiquidTemplateManager
{
    Task<string?> RenderAsync(string template, IExpressionExecutionContext expressionExecutionContext, CancellationToken cancellationToken = default);
}