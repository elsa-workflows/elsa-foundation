using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Constants;
using Elsa.Workflows.Runtime.Core;


namespace Elsa.Workflows.Runtime.JavaScript.Providers
{
    internal class WorkflowInputFunctionsProvider(IWorkflowExecution workflowExecution)
        : IJavaScriptFunctionProvider
    {
        public async ValueTask<IEnumerable<IJavaScriptFunction>> GetFunctions(IExpressionExecutionContext expressionExecutionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken = default)
        {
            // Create workflow input accessors - only if the current activity is not part of a composite activity definition.
            // Otherwise, the workflow input accessors will hide the composite activity input accessors which rely on variable accessors.
            if (expressionExecutionContext.IsContainedWithinCompositeActivity())
                return [];

            var result = new List<IJavaScriptFunction>();

            var inputs = workflowExecution.ExecutionContext
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

                result.Add(getInputValue);
            }

            return result;
        }
    }
}

