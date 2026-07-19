using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>Atomically changes the hierarchy or lifetime of durable workflow folders.</summary>
public interface IRestructureWorkflowFoldersCommand
{
    Task<WorkflowFolder> RenameAsync(string folderId, string name, CancellationToken cancellationToken = default);
    Task<WorkflowFolder> MoveAsync(string folderId, string? parentId, CancellationToken cancellationToken = default);
    Task DeleteEmptyAsync(string folderId, CancellationToken cancellationToken = default);
}
