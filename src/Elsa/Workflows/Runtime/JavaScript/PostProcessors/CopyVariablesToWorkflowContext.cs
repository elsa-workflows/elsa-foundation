using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Workflows.Primitives.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.JavaScript.PostProcessors;

/// <summary>
/// Copies variables from the JavaScript engine back into the expression execution context after evaluation, excluding variables that are defined as inputs to any workflow activity in the current execution context.
/// This allows JavaScript expressions to modify existing variables or create new ones without overwriting activity inputs.
/// </summary>
public sealed class CopyVariablesToWorkflowContext(IWorkflowExecutionContext workflowExecutionContext) : IScriptPostProcessor
{
    public ValueTask PostProcess(IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken)
    {
        var variablesContainer = executionContext.GetValue<IDictionary<string, object?>>(
            VariableNames.VariableContainer
        );

        if (variablesContainer is null)
        {
            return ValueTask.CompletedTask;
        }

        var inputNames = GetInputNames(expressionContext)
            .FilterInvalidVariableNames()
            .Distinct()
            .ToList();

        foreach (var (variableName, variableValue) in variablesContainer)
        {
            if (inputNames.Contains(variableName))
                continue;

            var processedValue = variableValue ?? expressionContext.GetVariableInScope(variableName);
            _ = expressionContext.SetVariable(variableName, processedValue);
        }

        return ValueTask.CompletedTask;
    }

    private IEnumerable<string> GetInputNames(IExpressionExecutionContext expressionContext)
    {
        var activityExecutionContext = workflowExecutionContext.GetActivityContextForExpression(expressionContext);

        while (activityExecutionContext != null)
        {
            if (activityExecutionContext.Activity is IWorkflowActivity workflow)
            {
                var inputDefinitions = workflow.Inputs;

                foreach (var inputDefinition in inputDefinitions)
                    yield return inputDefinition.Key;
            }

            foreach (var syntheticProperty in activityExecutionContext.Activity.SyntheticProperties)
            {
                yield return syntheticProperty.Key;
            }

            activityExecutionContext = activityExecutionContext.ParentActivityExecutionContext;
        }
    }
}