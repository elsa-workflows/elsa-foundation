using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Api.Handlers;

internal static class WorkflowFolderViewMapper
{
    public static WorkflowFolderView ToView(this WorkflowFolder folder) => new(
        folder.Id,
        folder.ParentFolderId,
        folder.Name,
        folder.NormalizedName,
        folder.CreatedAt,
        folder.LastModifiedAt);
}
