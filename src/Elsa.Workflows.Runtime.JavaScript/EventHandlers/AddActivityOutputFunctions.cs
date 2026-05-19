using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Mediator.Core;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Runtime.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Workflows.Runtime.JavaScript.EventHandlers
{
    public sealed class AddActivityOutputFunctions(IWorkflowExecutionContext workflowExecutionContext) : IDomainEventHandler<OnEvaluatingScript>
    {
        public async ValueTask Handle(OnEvaluatingScript domainEvent, CancellationToken cancellationToken)
        {
            var result = new List<IJavaScriptFunction>();

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

                    result.Add(getOutputFunction);
                }
            }

            
        }
    }
}
