using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Primitives.Extensions;
using System.Dynamic;

namespace Elsa.Expressions.JavaScript.Providers
{
    internal sealed class ArgsObjectProvider : IJavaScriptObjectProvider
    {
        private const string ArgsObjectName = "args";

        public ValueTask<IEnumerable<IJavaScriptObject>> GetObjects(IJavaScriptExecutionContext context, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken = default)
        {
            var argsObject = new JavaScriptObject(
                 ArgsObjectName,
                 GetArgsFromOptions(context, options)
             );

            return new([argsObject]);
        }

        static IDictionary<string, object?> GetArgsFromOptions(IJavaScriptExecutionContext context, IExpressionEvaluatorOptions? options)
        {
            var args = new ExpandoObject() as IDictionary<string, object?>;

            if (options is null)
            {
                return args;
            }

            foreach (var (name, value) in options.Arguments)
            {
                args[name.Camelize()] = context.NormalizeValue(value);
            }

            return args;
        }
    }
}
