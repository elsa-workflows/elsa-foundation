using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Workflows.Primitives.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.JavaScript.PreProcessors;

/// <summary>
/// Registers the workflow-level identity and correlation/name functions (<c>getWorkflowInstanceId</c>,
/// <c>getCorrelationId</c>, <c>getWorkflowInstanceName</c>, the definition id/version getters, <c>getInput</c>,
/// and <c>getLastResult</c>) from the live execution-time expression carrier
/// (<see cref="IExecutionExpressionState"/>). Re-pointed onto the carrier per ADR 0030: no DI-registered live
/// workflow execution context. Correlation-id / instance-name assignment routes through the runtime activity
/// execution context's control-leaf intent surface, exactly as the non-script <c>Correlate</c>/<c>SetName</c>
/// leaves do. No-op for any non-execution context (e.g. input materialization), so it is safe to register globally.
/// </summary>
public sealed class WorkflowFunctionsPreProcessor : IScriptPreProcessor
{
    public ValueTask PreProcess(string script, IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken)
    {
        if (expressionContext is not IExecutionExpressionState state)
            return ValueTask.CompletedTask;

        var functions = new List<IJavaScriptFunction>
        {
            new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowDefinitionId, () => state.WorkflowDefinitionId),
            new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowDefinitionVersionId, () => state.WorkflowDefinitionVersionId),
            new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowDefinitionVersion, () => state.WorkflowDefinitionVersion),
            new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowInstanceId, () => state.WorkflowInstanceId),
            new JavaScriptFunction(WorkflowFunctionNames.GetCorrelationId, () => state.CorrelationId),
            new JavaScriptFunction(WorkflowFunctionNames.GetWorkflowInstanceName, () => state.WorkflowName),
            new JavaScriptFunction<string>(WorkflowFunctionNames.GetInput, name => state.WorkflowInputs.GetValueOrDefault(name)),
            // No live "last activity result" exists on the execution-time carrier; kept defined (it is declared to
            // the editor) so scripts calling it degrade to null rather than a "not defined" reference error.
            new JavaScriptFunction(WorkflowFunctionNames.GetLastResult, () => (object?)null),
        };

        // Correlation-id / instance-name mutation from script funnels through the same control-leaf intent path
        // the Correlate/SetName leaves use, so the change folds into the activity-completed workflow-execution
        // state change rather than the carrier persisting it directly.
        if (expressionContext is IRuntimeActivityExecutionContext runtimeContext)
        {
            functions.Add(new JavaScriptFunction<string>(WorkflowFunctionNames.SetCorrelationId, id =>
            {
                runtimeContext.SetCorrelationId(id);
                return null;
            }));
            functions.Add(new JavaScriptFunction<string>(WorkflowFunctionNames.SetWorkflowInstanceName, name =>
            {
                runtimeContext.SetInstanceName(name);
                return null;
            }));
        }

        functions.ForEach(executionContext.RegisterFunction);

        return ValueTask.CompletedTask;
    }
}
