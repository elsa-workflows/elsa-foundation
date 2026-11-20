namespace Elsa.Expressions.JavaScript.Rendering.Core.Contracts;

/// <summary>
/// Contributes JavaScript declarations (variables, types, functions) to the declarations
/// document. Implementations receive the <see cref="IJavaScriptDeclarationsContributionContext"/>
/// and add their declarations to it; the single <c>BuildDeclarationsDocument</c> handler iterates
/// every registered contributor. Implement and register via DI to extend the generated document.
/// </summary>
public interface IJavaScriptDeclarationContributor
{
    ValueTask Contribute(IJavaScriptDeclarationsContributionContext context, CancellationToken cancellationToken);
}