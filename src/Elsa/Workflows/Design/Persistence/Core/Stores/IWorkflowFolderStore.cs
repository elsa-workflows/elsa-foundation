using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Stores;

/// <summary>Optional durable folder capability selected by the active design persistence provider.</summary>
public interface IWorkflowFolderStore
{
    bool IsAvailable { get; }
    Task<WorkflowFolderPage> ListDirectChildrenAsync(WorkflowFolderPageRequest request, CancellationToken cancellationToken = default);
    Task<WorkflowFolderDetails?> FindWithAncestorsAsync(string folderId, CancellationToken cancellationToken = default);
    async Task<IReadOnlyDictionary<string, WorkflowFolderDetails>> FindManyWithAncestorsAsync(
        IReadOnlyCollection<string> folderIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folderIds);
        var result = new Dictionary<string, WorkflowFolderDetails>(StringComparer.Ordinal);
        foreach (var folderId in folderIds.Distinct(StringComparer.Ordinal))
        {
            var details = await FindWithAncestorsAsync(folderId, cancellationToken);
            if (details is not null)
                result.Add(folderId, details);
        }
        return result;
    }
    Task<WorkflowFolder> CreateAsync(WorkflowFolder folder, CancellationToken cancellationToken = default);
}
