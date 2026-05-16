using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Core.Constants;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Core.Options;
using Elsa.Workflows.Constants;
using Elsa.Workflows.Design.Core;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.JavaScript.Providers
{
    internal sealed class WorkflowVariablesDeclarationProvider(IOptions<JavaScriptProviderOptions> options, IWorkflowGraph workflowGraph)
        : IJavaScriptTypeDeclarationProvider, IJavaScriptVariableDeclarationProvider
    {
        private const string TypeName = "WorkflowVariables";

        ValueTask<IEnumerable<JavaScriptVariableDeclaration>> IJavaScriptVariableDeclarationProvider.GetDeclarations(CancellationToken cancellationToken)
        {
            if (options.Value.DisableWrappers)
                return new([]);

            var result = new JavaScriptVariableDeclaration(
                VariableNames.VariableContainer, 
                TypeName
            );

            return new([result]);
        }

        ValueTask<IEnumerable<JavaScriptTypeDeclaration>> IJavaScriptTypeDeclarationProvider.GetDeclarations(CancellationToken cancellationTokenToken)
        {
            if (options.Value.DisableWrappers)
                return new([]);

            var variables = workflowGraph.Variables;

            var result = new JavaScriptTypeDeclaration
            {
                Name = TypeName,
                DeclarationKeyword = DeclarationKeywords.Class
            };

            foreach (var variable in variables.Where(x => VariableNameValidator.IsValidVariableName(x.Name)))
            {
                var variableType = variable.GetVariableType();
                result.Properties.Add(new JavaScriptPropertyDeclaration
                {
                    Name = variable.Name,
                    Type = variableType.Name
                });
            }

            return new([result]);
        }
    }
}
