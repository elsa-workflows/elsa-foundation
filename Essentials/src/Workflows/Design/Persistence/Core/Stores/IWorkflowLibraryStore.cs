using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Stores;

/// <summary>Tenant-scoped workflow catalog organization. Authored content and runtime are outside this port.</summary>
public interface IWorkflowLibraryStore
{
    Task<WorkflowFolderPage> ListFoldersAsync(string? parentId, int pageSize = 50, string? continuationToken = null, CancellationToken cancellationToken = default);
    Task<WorkflowFolderDetail?> GetFolderAsync(string id, CancellationToken cancellationToken = default);
    Task<WorkflowFolder> CreateFolderAsync(string name, string? parentId, string actorId, CancellationToken cancellationToken = default);
    Task RenameFolderAsync(string id, string name, string? revision, string actorId, CancellationToken cancellationToken = default);
    Task MoveFolderAsync(string id, string? parentId, string? revision, string actorId, CancellationToken cancellationToken = default);
    Task DeleteEmptyFolderAsync(string id, string? revision, string actorId, CancellationToken cancellationToken = default);
    Task MoveDefinitionsAsync(IReadOnlyList<string> definitionIds, string? folderId, string actorId, string operationKey, CancellationToken cancellationToken = default);

    Task<WorkflowLibraryPage> ListDefinitionsAsync(WorkflowLibraryQuery query, string? subjectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetVersionLabelsAsync(IReadOnlyList<string> versionIds, CancellationToken cancellationToken = default);
    Task<WorkflowOrganization?> GetOrganizationAsync(string definitionId, CancellationToken cancellationToken = default);
    Task<WorkflowOrganization> ReplaceTagsAsync(string definitionId, string revision, IReadOnlyList<WorkflowTagAssignment> tags,
        string actorId, string operationKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowTagEditOutcome>> EditTagsAsync(WorkflowTagBulkEdit edit, string actorId, CancellationToken cancellationToken = default);

    Task<WorkflowTagPage> ListTagsAsync(string? search = null, int pageSize = 50, string? continuationToken = null, CancellationToken cancellationToken = default);
    Task<WorkflowTag?> GetTagAsync(string id, CancellationToken cancellationToken = default);
    Task<WorkflowTag> CreateTagAsync(WorkflowTagInput input, string actorId, CancellationToken cancellationToken = default);
    Task<WorkflowTag> UpdateTagAsync(string id, WorkflowTagInput input, string actorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowLibraryView>> ListViewsAsync(string subjectId, CancellationToken cancellationToken = default);
    Task<WorkflowLibraryView> SaveViewAsync(string? id, WorkflowLibraryViewInput input, string subjectId, bool canShare, CancellationToken cancellationToken = default);
    Task DeleteViewAsync(string id, string revision, string subjectId, bool canShare, CancellationToken cancellationToken = default);
    Task SetFavoriteAsync(string definitionId, bool favorite, string subjectId, CancellationToken cancellationToken = default);
}
