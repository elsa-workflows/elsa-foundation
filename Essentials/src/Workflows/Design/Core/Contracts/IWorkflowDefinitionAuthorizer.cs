using Elsa.Workflows.Design.Core.Exceptions;

namespace Elsa.Workflows.Design.Core.Contracts;

/// <summary>Current owning-resource authority for one workflow definition, independent of its authored revision.</summary>
/// <remarks>
/// This is a single-implementation replacement contract in the current application context.
/// Consumers must fail closed when no implementation or multiple implementations are installed.
/// A management decision includes permission to read the definition and its owning organization.
/// </remarks>
public interface IWorkflowDefinitionAuthorizer
{
    /// <summary>Checks current read authority for the definition and its owning organization.</summary>
    /// <returns>Whether access is allowed; known authorization refusals and unavailable resources return false.</returns>
    /// <exception cref="WorkflowDefinitionAuthorizationException">An infrastructure failure prevented the decision; the original cause is retained as InnerException.</exception>
    /// <exception cref="OperationCanceledException">The operation was cancelled.</exception>
    ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default);

    /// <summary>Checks current management authority, including the definition's required read visibility.</summary>
    /// <returns>Whether access is allowed; known authorization refusals and unavailable resources return false.</returns>
    /// <exception cref="WorkflowDefinitionAuthorizationException">An infrastructure failure prevented the decision; the original cause is retained as InnerException.</exception>
    /// <exception cref="OperationCanceledException">The operation was cancelled.</exception>
    ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken = default);
}
