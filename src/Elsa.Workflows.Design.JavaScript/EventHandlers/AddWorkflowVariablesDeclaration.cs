using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Primitives.Constants;
using Elsa.Expressions.JavaScript.Rendering.Core.Events;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;
using Elsa.Expressions.JavaScript.Rendering.Core.Options;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Primitives.Constants;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.JavaScript.EventHandlers
{
    public sealed class AddWorkflowVariablesDeclaration(IOptions<JavaScriptDeclarationOptions> options, IWorkflowDesignContext designContext) : IDomainEventHandler<OnDeclarationsDocumentGenerating>
    {
        private const string TypeName = "WorkflowVariables";

        public ValueTask Handle(OnDeclarationsDocumentGenerating domainEvent, CancellationToken cancellationToken)
        {
            if (options.Value.DisableWrappers)
                return ValueTask.CompletedTask;

            domainEvent.Context.AddVariable(
                new JavaScriptVariableDeclaration(
                    VariableNames.VariableContainer,
                    TypeName
                )
            );

            domainEvent.Context.AddType(
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
