using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Primitives.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.JavaScript.PreProcessors;

public sealed class WorkflowInputFunctionsPreProcessor(IWorkflowExecutionContext workflowExecution)
    : IScriptPreProcessor
{
    public ValueTask PreProcess(string script, IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken)
    {
        // Create workflow input accessors - only if the current activity is not part of a composite activity definition.
        // Otherwise, the workflow input accessors will hide the composite activity input accessors which rely on variable accessors.
        if (expressionContext.IsContainedWithinCompositeActivity())
            return ValueTask.CompletedTask;

        var inputs = workflowExecution.GetWorkflowInputs();

        foreach (var input in inputs)
        {
            var name = string.Format(
                WorkflowFunctionNames.GetNamedInputFunctionFormat,
                input.Key.Pascalize()
            );
            var getInputValue = new JavaScriptFunction(
                name,
                () => expressionContext.Get(input.Value.MemoryBlockReference)
            );

            executionContext.RegisterFunction(getInputValue);
        }

        return ValueTask.CompletedTask;
    }
}