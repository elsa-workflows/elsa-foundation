using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Mediator.Core;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Constants;
using Elsa.Workflows.Runtime.Core;

namespace Elsa.Workflows.Runtime.JavaScript.EventHandlers
{
    public sealed class AddWorkflowInputFunctions(IWorkflowExecutionContext workflowExecution)
        : IDomainEventHandler<OnEvaluatingScript>
    {
        public ValueTask Handle(OnEvaluatingScript domainEvent, CancellationToken cancellationToken)
        {
            // Create workflow input accessors - only if the current activity is not part of a composite activity definition.
            // Otherwise, the workflow input accessors will hide the composite activity input accessors which rely on variable accessors.
            if (domainEvent.ExpressionContext.IsContainedWithinCompositeActivity())
                return ValueTask.CompletedTask;

            var inputs = workflowExecution
                .GetWorkflowInputs()
                .Where(x => VariableNameValidator.IsValidVariableName(x.Name));

            foreach (var input in inputs)
            {
                var name = string.Format(
                    WorkflowFunctionNames.GetNamedInputFunctionFormat,
                    input.Name.Pascalize()
                );
                var getInputValue = new JavaScriptFunction(
                    name,
                    (_) => input?.Value
                );

                domainEvent.EvaluationContext.AddFunction(getInputValue);
            }

            return ValueTask.CompletedTask;
        }
    }
}

