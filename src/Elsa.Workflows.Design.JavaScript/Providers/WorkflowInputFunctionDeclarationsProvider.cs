using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Core.Constants;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Design.Core;

namespace Elsa.Workflows.Design.JavaScript.Providers
{
    internal sealed class WorkflowInputFunctionDeclarationsProvider(IWorkflowDesignContext designContext) : IJavaScriptFunctionDeclarationProvider
    {
        public async ValueTask<IEnumerable<JavaScriptFunctionDeclaration>> GetDeclarations(CancellationToken cancellationToken = default)
        {
            var result = new List<JavaScriptFunctionDeclaration>();

            var inputs = designContext.Graph.Inputs.Where(x => VariableNameValidator.IsValidVariableName(x.Name));
            foreach (var input in inputs)
            {
                var pascalizedInputName = input.Name.Pascalize();
                var getInputValue = new JavaScriptFunctionDeclaration(
                    $"get{pascalizedInputName}",
                    WellKnownTypeNames.Any
                );

                result.Add(getInputValue);
            }

            return result;
        }
    }
}
