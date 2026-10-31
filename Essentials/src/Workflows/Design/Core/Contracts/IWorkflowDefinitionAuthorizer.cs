namespace Elsa.Workflows.Design.Core.Contracts;

/// <summary>Current owning-resource authority for one workflow definition, independent of its authored revision.</summary>
/// <remarks>
/// This is a single-implementation replacement contract in the current application context.
/// Consumers must fail closed when no implementation or multiple implementations are installed.
/// A management decision includes permission to read the definition and its owning organization.
/// </remarks>
public interface IWorkflowDefinitionAuthorizer
{
    ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default);

    ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken = default);
}
