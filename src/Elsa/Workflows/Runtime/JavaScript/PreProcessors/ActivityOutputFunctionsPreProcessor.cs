using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.JavaScript.PreProcessors;

public sealed class ActivityOutputFunctionsPreProcessor(IWorkflowExecutionContext workflowExecutionContext) : IScriptPreProcessor
{
    public async ValueTask PreProcess(string script, IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken)
    {
        var activityExecutionContext = workflowExecutionContext.GetActivityContextForExpression(expressionContext);
        var activityOutputs = activityExecutionContext.GetActivityOutputs();

        if (activityOutputs is null)
        {
            return;
        }

        await foreach (var activityOutput in activityOutputs)
        {
            foreach (var outputName in activityOutput.OutputNames.FilterInvalidVariableNames())
            {
                var getOutputFunction = new JavaScriptFunction(
                    $"get{outputName}From{activityOutput.ActivityName.Pascalize()}",
                    () => workflowExecutionContext.GetOutput(activityOutput.ActivityId, outputName)
                );

                executionContext.RegisterFunction(getOutputFunction);
            }
        }
    }
}