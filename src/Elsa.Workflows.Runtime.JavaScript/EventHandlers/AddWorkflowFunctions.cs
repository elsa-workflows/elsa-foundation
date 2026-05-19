using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Mediator.Core;
using Elsa.Workflows.Constants;
using Elsa.Workflows.Runtime.Core;

namespace Elsa.Workflows.Runtime.JavaScript.EventHandlers
{
    /// <summary>  
    /// </summary>
    /// <param name="worklowExecution"></param>
    public sealed class AddWorkflowFunctions(IWorkflowExecutionContext worklowExecution) : IDomainEventHandler<OnEvaluatingScript>
    {
        public ValueTask Handle(OnEvaluatingScript domainEvent, CancellationToken cancellationToken)
        {
            // Add common functions.
            var result = new IJavaScriptFunction[]
            {
                new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowDefinitionId, (_) => worklowExecution.WorkflowDefinitionId),
                new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowDefinitionVersionId, (_) => worklowExecution.WorkflowDefinitionVersionId),
                new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowDefinitionVersion, (_) => worklowExecution.WorkflowDefinitionVersion),
                new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowInstanceId, (_) => worklowExecution.InstanceId),
                new JavaScriptFunction(WorkflowFunctionNames.GetCorrelationId, (_) => worklowExecution.CorrelationId),
                new JavaScriptFunction(WorkflowFunctionNames.SetCorrelationId, parameters =>
                {
                    var value = parameters[0] as string;
                    worklowExecution.CorrelationId = value;
                    return null;
                }),
                new JavaScriptFunction(WorkflowFunctionNames.SetWorkflowInstanceName, parameters =>
                {
                    var value = parameters[0] as string;
                    worklowExecution.Name = value;
                    return null;
                }),
                new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowInstanceName, (_) => worklowExecution.Name),

                new JavaScriptFunction<string>(WorkflowFunctionNames.GetInput, worklowExecution.GetInput),

                new JavaScriptFunction<string, string>(WorkflowFunctionNames.GetOutputFrom, worklowExecution.GetOutput),
                new JavaScriptFunction(WorkflowFunctionNames.GetLastResult, (_) => worklowExecution.GetLastActivityResult()),
            };

            result.ToList().ForEach(domainEvent.EvaluationContext.AddFunction);

            return ValueTask.CompletedTask;
        }
    }
}
