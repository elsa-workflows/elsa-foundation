using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Primitives.Constants;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;
using Elsa.Expressions.JavaScript.Rendering.Core.Options;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Primitives.Constants;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.JavaScript.Contributors
{
    public sealed class WorkflowVariablesDeclarationContributor(IOptions<JavaScriptDeclarationOptions> options, IWorkflowDesignContext designContext) : IJavaScriptDeclarationContributor
    {
        private const string TypeName = "WorkflowVariables";

        public ValueTask Contribute(IJavaScriptDeclarationsContributionContext context, CancellationToken cancellationToken)
        {
            if (options.Value.DisableWrappers)
                return ValueTask.CompletedTask;

            context.AddVariable(
                new JavaScriptVariableDeclaration(
                    VariableNames.VariableContainer,
                    TypeName
                )
            );

            context.AddType(
                GetTypeDeclaration()
            );

            return ValueTask.CompletedTask;
        }


        private JavaScriptTypeDeclaration GetTypeDeclaration()
        {
            var variables = designContext.GetVariableDefinitions();
            var result = new JavaScriptTypeDeclaration
            {
                Name = TypeName,
                DeclarationKeyword = DeclarationKeywords.Class
            };

            foreach (var variable in variables.Where(x => VariableNameValidator.IsValidVariableName(x.Name)))
            {
                var variableType = variable.TypeInformation;
                result.Properties.Add(new JavaScriptPropertyDeclaration
                {
                    Name = variable.Name,
                    Type = variableType.TypeName
                });
            }

            return result;
        }
    }
}
