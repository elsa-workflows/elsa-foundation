using Elsa.Expressions.JavaScript.Primitives.Constants;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;
using Elsa.Primitives.Extensions;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.JavaScript.Contributors;

internal sealed class WorkflowInputFunctionDeclarationContributor(IWorkflowDesignContext designContext) : IJavaScriptDeclarationContributor
{
    public ValueTask Contribute(IJavaScriptDeclarationsContributionContext context, CancellationToken cancellationToken)
    {
        var inputs = designContext.GetWorkflowInputs();
        foreach (var input in inputs)
        {
            var pascalizedInputName = input.Name.Pascalize();
            var getInputValue = new JavaScriptFunctionDeclaration(
                $"get{pascalizedInputName}",
                WellKnownTypeNames.Any
            );

            context.AddFunction(getInputValue);
        }
        return ValueTask.CompletedTask;
    }
}