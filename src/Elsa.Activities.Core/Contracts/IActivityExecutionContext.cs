using Elsa.Expressions.Core;

namespace Elsa.Activities.Core.Contracts
{
    public interface IActivityExecutionContext
    {
        TService GetRequiredService<TService>();

        IExpressionExecutionContext ExpressionExecutionContext { get; }
    }
}
