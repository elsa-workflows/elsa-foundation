namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Preflight veto point evaluated by <see cref="IDeleteWorkflowDefinitionPermanentlyCommand"/> implementations
/// before any row is removed. Other verticals contribute implementations for state the design store cannot see;
/// e.g. publishing refuses deletion while the definition still holds an active publication slot or a live
/// Published executable reference, because hard-deleting the design rows would strand that runtime state.
/// Contributing a veto here does not by itself make permanent deletion available: that needs the publication
/// check, <see cref="IWorkflowDefinitionPublicationDeletionGuard"/>.
/// </summary>
public interface IWorkflowDefinitionPermanentDeletionGuard
{
    /// <summary>Throws <see cref="InvalidOperationException"/> when the definition must not be permanently deleted yet.</summary>
    Task EnsureCanDeleteAsync(string definitionId, CancellationToken cancellationToken = default);
}
