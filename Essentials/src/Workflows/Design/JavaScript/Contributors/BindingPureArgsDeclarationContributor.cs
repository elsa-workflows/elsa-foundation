using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;

namespace Elsa.Workflows.Design.JavaScript.Contributors;

/// <summary>
/// Declares the immutable <c>args</c> object of a canonical <c>binding-readonly-v1</c> JavaScript expression. The
/// built-in and host-registered functions and the variables read by name are completed by the JavaScript expression
/// tooling (spec 1223); host services, configuration and mutation helpers do not exist on this surface.
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
