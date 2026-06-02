using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.PreProcessors
{
    internal sealed class ArgumentFunctionsPreProcessor : IScriptPreProcessor
    {
        public ValueTask PreProcess(string script, IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken)
        {
            if (options is null)
                return ValueTask.CompletedTask;

            options.Arguments.ToList().ForEach(arg =>
            {
                var function = new JavaScriptFunction($"get{arg.Key}", () => arg.Value);
                executionContext.RegisterFunction(function);
            });

            return ValueTask.CompletedTask;
        }
    }
}
