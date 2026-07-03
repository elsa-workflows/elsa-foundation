using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Primitives.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.JavaScript.PostProcessors;

/// <summary>
/// Copies variables from the JavaScript engine back into the expression execution context after evaluation,
/// excluding variables that are defined as inputs to any workflow activity in the current execution context.
/// This allows JavaScript expressions to modify existing variables or create new ones (via direct
/// <c>variables.x = …</c> assignment) without overwriting activity inputs. Re-pointed onto the live
/// execution-time carrier per ADR 0030: the input-name exclusion is derived from the passed activity execution
/// context rather than a DI-registered live workflow execution context. The copy-back routes through
/// <see cref="IExpressionExecutionContext"/>'s scope-aware <c>SetVariable</c>, so the mutation lands in the
/// workflow/container scope and folds into the checkpoint-commit durable-value write-back. No-op for any
/// non-execution context, so it is safe to register globally.
/// </summary>
public sealed class CopyVariablesToWorkflowContext : IScriptPostProcessor
{
    public ValueTask PostProcess(IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken)
    {
        if (expressionContext is not IExecutionExpressionState)
            return ValueTask.CompletedTask;

        var variablesContainer = executionContext.GetValue<IDictionary<string, object?>>(
            VariableNames.VariableContainer
        );

        if (variablesContainer is null)
        {
            return ValueTask.CompletedTask;
        }

        var inputNames = GetInputNames(expressionContext as IActivityExecutionContext)
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

    private static IEnumerable<string> GetInputNames(IActivityExecutionContext? activityExecutionContext)
    {
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
