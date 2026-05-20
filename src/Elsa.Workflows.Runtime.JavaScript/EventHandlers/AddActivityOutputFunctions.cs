using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Runtime.Core;

namespace Elsa.Workflows.Runtime.JavaScript.EventHandlers
{
    public sealed class AddActivityOutputFunctions(IWorkflowExecutionContext workflowExecutionContext) : IDomainEventHandler<OnEvaluatingScript>
    {
        public async ValueTask Handle(OnEvaluatingScript domainEvent, CancellationToken cancellationToken)
        {
            var activityExecutionContext = workflowExecutionContext.GetActivityContextForExpression(domainEvent.ExpressionContext);
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

                    domainEvent.ExecutionContext.RegisterFunction(getOutputFunction);
                }
            }


        }
    }
}
