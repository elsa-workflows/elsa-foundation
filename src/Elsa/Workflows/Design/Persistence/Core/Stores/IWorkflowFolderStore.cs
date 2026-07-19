using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Stores;

/// <summary>Optional durable folder capability selected by the active design persistence provider.</summary>
public interface IWorkflowFolderStore
{
    bool IsAvailable { get; }
    Task<WorkflowFolderPage> ListDirectChildrenAsync(WorkflowFolderPageRequest request, CancellationToken cancellationToken = default);
    Task<WorkflowFolderDetails?> FindWithAncestorsAsync(string folderId, CancellationToken cancellationToken = default);
    Task<WorkflowFolder> CreateAsync(WorkflowFolder folder, CancellationToken cancellationToken = default);
}
