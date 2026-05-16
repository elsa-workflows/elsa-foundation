using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.Providers
{
    internal sealed class GetArgumentFunctionsProvider : IJavaScriptFunctionProvider
    {
        public ValueTask<IEnumerable<IJavaScriptFunction>> GetFunctions(IExpressionExecutionContext expressionExecutionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken = default)
        {
            var result = new List<JavaScriptFunction>();

            if (options is null)
                return new(result);

            foreach (var argument in options.Arguments)
            {
                result.Add(
                    new JavaScriptFunction($"get{argument.Key}", (_) => argument.Value)
                );
            }

            return new(result);
        }
    }
}
