using Elsa.Activities.Core.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Runtime.Core;

namespace Elsa.Workflows.Runtime.JavaScript.Providers
{
    internal sealed class ActivityOutputFunctionsProvider(IWorkflowExecution workflowExecution)
        : IJavaScriptFunctionProvider
    {       
        public async ValueTask<IEnumerable<IJavaScriptFunction>> GetFunctions(IExpressionExecutionContext expressionExecutionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken = default)
        {
            var result = new List<IJavaScriptFunction>();

            var activityOutputs = expressionExecutionContext.TryGetTransientProperty<IActivityExecutionContext>(IActivityExecutionContext.ContextKey, out var activityExecutionContext)
                ? activityExecutionContext.GetActivityOutputs()
                : null;

            if (activityOutputs is null)
            {
                return result;
            }

            await foreach (var activityOutput in activityOutputs)
            {
                foreach (var outputName in activityOutput.OutputNames.FilterInvalidVariableNames())
                {
                    var getOutputFunction = new JavaScriptFunction(
                        $"get{outputName}From{activityOutput.ActivityName.Pascalize()}",
                        () => workflowExecution.ExecutionContext.GetOutput(activityOutput.ActivityId, outputName)
                    );

                    result.Add(getOutputFunction);
                }
            }

            return result;
        }
    }
}
