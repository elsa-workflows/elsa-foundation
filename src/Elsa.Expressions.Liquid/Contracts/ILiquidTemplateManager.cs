using Elsa.Expressions.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Expressions.Liquid.Contracts
{
    public interface ILiquidTemplateManager
    {
        Task<string?> RenderAsync(string template, IExpressionExecutionContext expressionExecutionContext, CancellationToken cancellationToken = default);
    }
}
