using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;

namespace Elsa.Workflows.Design.JavaScript.Contributors;

/// <summary>
/// Describes the complete global surface of a canonical <c>binding-pure-v1</c> JavaScript
/// expression. Parameters are supplied only through the immutable <c>args</c> object; workflow
/// state, host services, configuration, and mutation helpers are deliberately not declared.
/// </summary>
public sealed class BindingPureArgsDeclarationContributor : IJavaScriptDeclarationContributor
{
    public ValueTask Contribute(IJavaScriptDeclarationsContributionContext context, CancellationToken cancellationToken)
    {
        context.AddVariable(new JavaScriptVariableDeclaration(
            "args",
            "Readonly<Record<string, any>>",
            "const"));
        return ValueTask.CompletedTask;
    }
}
