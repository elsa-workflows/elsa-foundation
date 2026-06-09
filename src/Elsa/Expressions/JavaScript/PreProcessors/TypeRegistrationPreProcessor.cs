using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Expressions.JavaScript.PreProcessors;

public sealed class TypeRegistrationPreProcessor(IOptions<JavaScriptOptions> options) : IScriptPreProcessor
{
    public ValueTask PreProcess(string script, IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? evaluatorOptions, CancellationToken cancellationToken)
    {
        options.Value.TypeDescriptors
            .ToList()
            .ForEach(d => executionContext.RegisterType(d.GetDescriptorType()));

        return ValueTask.CompletedTask;
    }
}