using Elsa.Expressions.Core.Extensions;
using Elsa.Expressions.JavaScript.Core.Constants;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.JavaScript.Core.Options;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Constants;
using Elsa.Workflows.Design.Core;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.JavaScript.Providers
{
    internal sealed class WorkflowVariableFunctionDeclarationsProvider(IOptions<JavaScriptProviderOptions> options, IWorkflowGraph workflowGraph, IJavaScriptTypeAliasRegistry typeAliasRegistry) 
        : IJavaScriptFunctionDeclarationProvider
    {
        public ValueTask<IEnumerable<JavaScriptFunctionDeclaration>> GetDeclarations(CancellationToken cancellationToken = default)
        {
            var workflowVariableDeclarations = BuildNamedVariableFunctionDeclarations();
            var genericVariableFunctionDeclarations = BuildGenericVariableFunctionDeclarations();
            var result = workflowVariableDeclarations.Concat(genericVariableFunctionDeclarations);

            return new(result);
        }

        static IEnumerable<JavaScriptFunctionDeclaration> BuildGenericVariableFunctionDeclarations()
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

        IEnumerable<JavaScriptFunctionDeclaration> BuildNamedVariableFunctionDeclarations()
        {
            if (options.Value.DisableWrappers)
                yield break;

            foreach (var variable in workflowGraph.Variables.Where(x => x.Name.IsValidVariableName()))
            {
                var pascalName = variable.Name.Pascalize();
                var variableType = variable.GetVariableType();
                var typeAlias = typeAliasRegistry.GetAliasOrDefault(variableType);

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
