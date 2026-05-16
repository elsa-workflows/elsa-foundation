using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Workflows.Constants;
using Elsa.Workflows.Runtime.Core;

namespace Elsa.Workflows.Runtime.JavaScript.Providers
{
    /// <summary>  
    /// </summary>
    /// <param name="worklowExecution"></param>
    internal sealed class CommonFunctionsProvider(IWorkflowExecution worklowExecution)

        : IJavaScriptFunctionProvider
    {
        public ValueTask<IEnumerable<IJavaScriptFunction>> GetFunctions(IExpressionExecutionContext expressionExecutionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken = default)
        {
            // Add common functions.
            var result = new IJavaScriptFunction[]
            {
                new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowDefinitionId, (_) => worklowExecution.DefinitionId),
                new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowDefinitionVersionId, (_) => worklowExecution.VersionId),
                new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowDefinitionVersion, (_) => worklowExecution.Version),
                new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowInstanceId, (_) => worklowExecution.ExecutionContext.InstanceId),
                new JavaScriptFunction(WorkflowFunctionNames.GetCorrelationId, (_) => worklowExecution.ExecutionContext.CorrelationId),
                new JavaScriptFunction(WorkflowFunctionNames.SetCorrelationId, parameters =>
                {
                    var value = parameters[0] as string;
                    worklowExecution.ExecutionContext.CorrelationId = value;
                    return null;
                }),
                new JavaScriptFunction(WorkflowFunctionNames.SetWorkflowInstanceName, parameters =>
                {
                    var value = parameters[0] as string;
                    worklowExecution.ExecutionContext.Name = value;
                    return null;
                }),
                new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowInstanceName, (_) => worklowExecution.ExecutionContext.Name),               
                
                new JavaScriptFunction<string>(WorkflowFunctionNames.GetInput, worklowExecution.ExecutionContext.GetInput),

                new JavaScriptFunction<string, string>(WorkflowFunctionNames.GetOutputFrom, worklowExecution.ExecutionContext.GetOutput),
                new JavaScriptFunction(WorkflowFunctionNames.GetLastResult, (_) => worklowExecution.ExecutionContext.GetLastActivityResult()),
            };

            return new(result);
        }
    }
}
