using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Workflows.Primitives.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.JavaScript.PreProcessors
{
    public sealed class WorkflowFunctionsPreProcessor(IWorkflowExecutionContext worklowExecution) : IScriptPreProcessor
    {
        public ValueTask PreProcess(string script, IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken)
        {
            // Add common functions.
            var result = new IJavaScriptFunction[]
            {
                new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowDefinitionId, () => worklowExecution.WorkflowDefinitionId),
                new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowDefinitionVersionId, () => worklowExecution.WorkflowDefinitionVersionId),
                new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowDefinitionVersion, () => worklowExecution.WorkflowDefinitionVersion),
                new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowInstanceId, () => worklowExecution.InstanceId),
                new JavaScriptFunction(WorkflowFunctionNames.GetCorrelationId, () => worklowExecution.CorrelationId),
                new JavaScriptFunction<string>(WorkflowFunctionNames.SetCorrelationId, (string id) =>
                {
                    worklowExecution.CorrelationId = id;
                    return null;
                }),
                new JavaScriptFunction<string>(WorkflowFunctionNames.SetWorkflowInstanceName, name =>
                {
                    worklowExecution.Name = name;
                    return null;
                }),
                new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowInstanceName, () => worklowExecution.Name),

                new JavaScriptFunction<string>(WorkflowFunctionNames.GetInput, worklowExecution.GetInput),

                new JavaScriptFunction<string, string>(WorkflowFunctionNames.GetOutputFrom, worklowExecution.GetOutput),
                new JavaScriptFunction(WorkflowFunctionNames.GetLastResult, () => worklowExecution.GetLastActivityResult()),
            };

            result.ToList().ForEach(executionContext.RegisterFunction);

            return ValueTask.CompletedTask;
        }
    }
}
