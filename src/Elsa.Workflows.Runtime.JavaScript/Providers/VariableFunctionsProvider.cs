using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Constants;
using Elsa.Workflows.Runtime.Core;
using Elsa.Workflows.Runtime.JavaScript.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.JavaScript.Providers
{
    internal sealed class VariableFunctionsProvider(IOptions<ProviderOptions> options, IWorkflowExecutionContext workflowExecution)
        : IJavaScriptFunctionProvider
    {
        public ValueTask<IEnumerable<IJavaScriptFunction>> GetFunctions(IExpressionExecutionContext expressionExecutionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken = default)
        {
            var result = new List<IJavaScriptFunction>();

            var workflowVariableFunctions = BuildWorkflowVariableFunctions();
            result.AddRange(workflowVariableFunctions);

            var setVariable = new JavaScriptFunction<string, object>(
                 WorkflowFunctionNames.SetVariableFunctionName,
                 (ctx, name, value) => SetVariable(ctx, name, value)
             );
            result.Add(setVariable);

            var getVariable = new JavaScriptFunction<string>(
                WorkflowFunctionNames.GetVariableFunctionName,
                (ctx, name) => workflowExecution.GetVariable(name)
            );
            result.Add(getVariable);

            return new(result);
        }

        IEnumerable<IJavaScriptFunction> BuildWorkflowVariableFunctions()
        {
            foreach (var variable in workflowExecution.GetVariables())
            {
                var pascalName = variable.Name.Pascalize();
                var variableType = variable.GetVariableType();
                
                var setVariable = new JavaScriptFunction<object>(
                    string.Format(WorkflowFunctionNames.SetNamedVariableFunctionFormat, pascalName),
                    (ctx, value) => SetVariable(ctx, variable.Name, value)
                );
                var getVariable = new JavaScriptFunction(
                    string.Format(WorkflowFunctionNames.GetNamedVariableFunctionFormat, pascalName),
                    (ctx) => workflowExecution.GetVariable(variable.Name)
                );

                yield return setVariable;
                yield return getVariable;
            }
        }


        private void SetVariable(IJavaScriptExecutionContext context, string name, object? value)
        {
            if (options.Value.DisableWrappers || options.Value.DisableVariableCopying)
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
