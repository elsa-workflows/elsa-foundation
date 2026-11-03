using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Stores;

/// <summary>Optional tenant-scoped, atomic folder deletion with a confirmed contents preview.</summary>
public interface IWorkflowFolderDeletionStore
{
    Task<FolderDeletionPreview> PreviewFolderDeletionAsync(string folderId, CancellationToken cancellationToken = default);
    Task DeleteFolderAsync(string folderId, string previewRevision, bool deleteWorkflows, string actorId,
        string operationKey, CancellationToken cancellationToken = default);
}
