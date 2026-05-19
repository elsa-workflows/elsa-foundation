using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Mediator.Core;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Constants;
using Elsa.Workflows.Runtime.Core;
using Elsa.Workflows.Runtime.JavaScript.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.JavaScript.EventHandlers
{
    public sealed class AddVariableFunctions(IOptions<FeatureOptions> options, IWorkflowExecutionContext workflowExecution)
        : IDomainEventHandler<OnEvaluatingScript>
    {
        public ValueTask Handle(OnEvaluatingScript domainEvent, CancellationToken cancellationToken)
        {            
            var workflowVariableFunctions = BuildWorkflowVariableFunctions(domainEvent.EvaluationContext);
            workflowVariableFunctions.ToList().ForEach(domainEvent.EvaluationContext.AddFunction);
            
            domainEvent.EvaluationContext.AddFunction(
                new JavaScriptFunction<string, object>(
                     WorkflowFunctionNames.SetVariableFunctionName,
                     (name, value) => SetVariable(domainEvent.EvaluationContext, name, value)
                 )
            );

            domainEvent.EvaluationContext.AddFunction(
                new JavaScriptFunction<string>(
                    WorkflowFunctionNames.GetVariableFunctionName,
                    (name) => workflowExecution.GetVariable(name)
                )
            );

            return ValueTask.CompletedTask;
        }

        IEnumerable<IJavaScriptFunction> BuildWorkflowVariableFunctions(IJavaScriptEvaluationContext context)
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


        private void SetVariable(IJavaScriptEvaluationContext context, string name, object? value)
        {
            // Variables Container
            var variablesContainer = (IDictionary<string, object?>?)context.GetValue(
                VariableNames.VariableContainer
            );
            variablesContainer ??= new Dictionary<string, object?>();

            // Set value in JavaScript Evaluation Context
            variablesContainer[name] = value;

            context.SetValue(
                VariableNames.VariableContainer, 
                variablesContainer
            );

            if (options.Value.DisableVariableCopying)
                return;

            // To ensure both variable accessor syntaxes work, we need to update the variables container in the engine as well as the context to keep them in sync.            
            workflowExecution.SetVariable(name, value);
        }
    }
}
