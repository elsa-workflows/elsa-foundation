using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Mediator.Core;
using Elsa.Workflows.Activities.Core;
using Elsa.Workflows.Constants;
using Elsa.Workflows.Runtime.Core;

namespace Elsa.Workflows.Runtime.JavaScript.EventHandlers
{
    /// <summary>
    /// Copies variables from the JavaScript engine back into the expression execution context after evaluation, excluding variables that are defined as inputs to any workflow activity in the current execution context. 
    /// This allows JavaScript expressions to modify existing variables or create new ones without overwriting activity inputs.
    /// </summary>
    public sealed class CopyVariablesToWorkflowContext(IWorkflowExecutionContext context) : IDomainEventHandler<OnScriptEvaluated>
    {
        public ValueTask Handle(OnScriptEvaluated domainEvent, CancellationToken cancellationToken)
        {
            var context = domainEvent.ExpressionContext;
            var variablesContainer = domainEvent.EvaluationContext.GetValue<IDictionary<string, object?>>(
                VariableNames.VariableContainer
            );

            if (variablesContainer is null)
            {
                return ValueTask.CompletedTask;
            }

            var inputNames = GetInputNames(context)
                .FilterInvalidVariableNames()
                .Distinct()
                .ToList();

            foreach (var (variableName, variableValue) in variablesContainer)
            {
                if (inputNames.Contains(variableName))
                    continue;

                var processedValue = variableValue ?? context.GetVariableInScope(variableName);
                _ = context.SetVariable(variableName, processedValue);
            }

            return ValueTask.CompletedTask;
        }
  
        private IEnumerable<string> GetInputNames(IExpressionExecutionContext expressionContext)
        {
            var activityExecutionContext = context.GetActivityContextForExpression(expressionContext);

            while (activityExecutionContext != null)
            {
                if (activityExecutionContext.Activity is IWorkflowActivity workflow)
                {
                    var inputDefinitions = workflow.Inputs;

                    foreach (var inputDefinition in inputDefinitions)
                        yield return inputDefinition.Name;
                }

                foreach (var syntheticProperty in activityExecutionContext.Activity.SyntheticProperties)
                {
                    yield return syntheticProperty.Key;
                }

                activityExecutionContext = activityExecutionContext.ParentActivityExecutionContext;
            }
        }
    }
}
