namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>Atomically changes the server-owned folder placement of logical workflow definitions.</summary>
public interface IMoveWorkflowDefinitionsCommand
{
    Task Execute(IReadOnlyCollection<string> definitionIds, string? folderId, CancellationToken cancellationToken = default);
}
