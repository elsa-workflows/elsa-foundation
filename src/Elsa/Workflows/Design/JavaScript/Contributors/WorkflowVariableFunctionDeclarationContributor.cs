using Elsa.Expressions.JavaScript.Primitives.Constants;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;
using Elsa.Expressions.JavaScript.Rendering.Core.Options;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Primitives.Constants;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.JavaScript.Contributors;

internal sealed class WorkflowVariableFunctionDeclarationContributor(IOptions<JavaScriptDeclarationOptions> options, IWorkflowDesignContext designContext)
    : IJavaScriptDeclarationContributor
{
    public ValueTask Contribute(IJavaScriptDeclarationsContributionContext context, CancellationToken cancellationToken)
    {
        var workflowVariableDeclarations = BuildNamedVariableFunctionDeclarations();
        var genericVariableFunctionDeclarations = BuildGenericVariableFunctionDeclarations();

        workflowVariableDeclarations
            .Concat(genericVariableFunctionDeclarations)
            .ToList()
            .ForEach(context.AddFunction);

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

        foreach (var variable in designContext.GetVariableDefinitions())
        {
            var pascalName = variable.Name.Pascalize();
            var typeAlias = options.Value.GetTypeAliasOrDefault(variable.Type.Alias);

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