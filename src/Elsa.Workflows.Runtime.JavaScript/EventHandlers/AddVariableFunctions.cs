using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Primitives.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.JavaScript.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.JavaScript.EventHandlers
{
    public sealed class AddVariableFunctions(IOptions<FeatureOptions> options, IWorkflowExecutionContext workflowExecution)
        : IDomainEventHandler<OnEvaluatingScript>
    {
        public ValueTask Handle(OnEvaluatingScript domainEvent, CancellationToken cancellationToken)
        {
            var workflowVariableFunctions = BuildWorkflowVariableFunctions(domainEvent.ExecutionContext);
            workflowVariableFunctions.ToList().ForEach(domainEvent.ExecutionContext.RegisterFunction);

            domainEvent.ExecutionContext.RegisterFunction(
                new JavaScriptFunction<string, object>(
                     WorkflowFunctionNames.SetVariableFunctionName,
                     (name, value) => SetVariable(domainEvent.ExecutionContext, name, value)
                 )
            );

            domainEvent.ExecutionContext.RegisterFunction(
                new JavaScriptFunction<string>(
                    WorkflowFunctionNames.GetVariableFunctionName,
                    (name) => workflowExecution.GetVariable(name)
                )
            );

            return ValueTask.CompletedTask;
        }

        private IEnumerable<IJavaScriptFunction> BuildWorkflowVariableFunctions(IJavaScriptExecutionContext context)
        {
            foreach (var variable in workflowExecution.GetVariables())
            {
                var pascalName = variable.Name.Pascalize();
                var variableType = variable.GetVariableType();

                var setVariable = new JavaScriptFunction<object>(
                    string.Format(WorkflowFunctionNames.SetNamedVariableFunctionFormat, pascalName),
                    (value) => SetVariable(context, variable.Name, value)
                );
                var getVariable = new JavaScriptFunction(
                    string.Format(WorkflowFunctionNames.GetNamedVariableFunctionFormat, pascalName),
                    () => workflowExecution.GetVariable(variable.Name)
                );

                yield return setVariable;
                yield return getVariable;
            }
        }


        private void SetVariable(IJavaScriptExecutionContext context, string name, object? value)
        {
            if (options.Value.DisableVariableCopying)
                return;

            // To ensure both variable accessor syntaxes work, we need to update the variables container in the engine as well as the context
            // to keep them in sync.

            // Variables Container
            var variablesContainer = (IDictionary<string, object?>?)context.GetValue(
                VariableNames.VariableContainer
            );
            variablesContainer ??= new Dictionary<string, object?>();

            // Set value in JavaScript Execution Context
            variablesContainer[name] = context.NormalizeValue(value);
            context.SetValue(
                VariableNames.VariableContainer,
                variablesContainer
            );

            // Set value in Workflow Context
            workflowExecution.SetVariable(name, value);
        }
    }
}
