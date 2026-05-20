using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Primitives.Constants;
using Elsa.Expressions.JavaScript.Rendering.Core.Events;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;
using Elsa.Expressions.JavaScript.Rendering.Core.Options;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Constants;
using Elsa.Workflows.Design.Core;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.JavaScript.EventHandlers
{
    internal sealed class AddWorkflowVariableFunctionDeclarations(IOptions<JavaScriptDeclarationOptions> options, IWorkflowDesignContext designContext)
        : IDomainEventHandler<OnDeclarationsDocumentGenerating>
    {
        public ValueTask Handle(OnDeclarationsDocumentGenerating domainEvent, CancellationToken cancellationToken)
        {
            var workflowVariableDeclarations = BuildNamedVariableFunctionDeclarations();
            var genericVariableFunctionDeclarations = BuildGenericVariableFunctionDeclarations();

            workflowVariableDeclarations
                .Concat(genericVariableFunctionDeclarations)
                .ToList()
                .ForEach(domainEvent.Context.AddFunction);

            return ValueTask.CompletedTask;
        }

        private static IEnumerable<JavaScriptFunctionDeclaration> BuildGenericVariableFunctionDeclarations()
        {
            yield return new JavaScriptFunctionDeclaration(
               WorkflowFunctionNames.SetVariableFunctionName,
               returnType: WellKnownTypeNames.Void,
               parameters:
               [
                   new JavaScriptParameterDeclaration("name", WellKnownTypeNames.String),
                    new JavaScriptParameterDeclaration("value", WellKnownTypeNames.Any)
               ]
            );

            yield return new JavaScriptFunctionDeclaration(
                WorkflowFunctionNames.GetVariableFunctionName,
                returnType: WellKnownTypeNames.Any,
                parameters:
                [
                    new JavaScriptParameterDeclaration("name", WellKnownTypeNames.String)
                ]
            );
        }

        private IEnumerable<JavaScriptFunctionDeclaration> BuildNamedVariableFunctionDeclarations()
        {
            if (options.Value.DisableWrappers)
                yield break;

            foreach (var variable in designContext.Variables.Where(x => x.Name.IsValidVariableName()))
            {
                var pascalName = variable.Name.Pascalize();
                var variableType = variable.GetVariableType();
                var typeAlias = options.Value.GetTypeAliasOrDefault(variableType.Name);

                var setVariableFunctionName = string.Format(WorkflowFunctionNames.SetNamedVariableFunctionFormat, pascalName);
                var setVariableDeclaration = new JavaScriptFunctionDeclaration(
                    setVariableFunctionName,
                    parameters:
                    [
                        new JavaScriptParameterDeclaration("value", typeAlias)
                    ]
                );

                var getVariableFunctionName = string.Format(WorkflowFunctionNames.GetNamedVariableFunctionFormat, pascalName);
                var getVariableDeclaration = new JavaScriptFunctionDeclaration(
                    getVariableFunctionName,
                    typeAlias
                );

                yield return setVariableDeclaration;
                yield return getVariableDeclaration;
            }
        }
    }
}
