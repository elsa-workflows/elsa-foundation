using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Primitives.Extensions;

namespace Elsa.Expressions.JavaScript.PreProcessors;

public sealed class ArgsObjectPreProcessor : IScriptPreProcessor
{
    private const string ArgsObjectName = "args";

    public ValueTask PreProcess(string script, IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken)
    {
        // Add args object
        executionContext.SetValue(ArgsObjectName, GetArgsFromOptions(options));
        return ValueTask.CompletedTask;
    }

    private static IDictionary<string, object?> GetArgsFromOptions(IExpressionEvaluatorOptions? options)
    {
        var args = new Dictionary<string, object?>();

        if (options is null)
        {
            return args;
        }

        foreach (var (name, value) in options.Arguments)
        {
            args[name.Camelize()] = value;
        }

        return args;
    }
}