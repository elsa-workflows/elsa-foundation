using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Handlers;

internal static class WorkflowDefinitionViewMapper
{
    public static async Task<WorkflowDefinitionView[]> CreatePageAsync(
        IReadOnlyCollection<WorkflowDefinition> definitions,
        IWorkflowDefinitionListProjectionStore projectionStore,
        IReadOnlyDictionary<string, WorkflowFolderDetails> folderDetailsById,
        CancellationToken cancellationToken)
    {
        var views = await CreateAsync(definitions, projectionStore, cancellationToken);
        return views.Select(view =>
        {
            if (view.FolderId is null)
                return view with { FolderBreadcrumb = [] };
            if (!folderDetailsById.TryGetValue(view.FolderId, out var details))
            {
                throw new InvalidOperationException(
                    $"Workflow definition '{view.Id}' references unavailable workflow folder '{view.FolderId}'.");
            }
            return view with
            {
                FolderBreadcrumb = details.Ancestors
                    .Append(details.Folder)
                    .Select(folder => new WorkflowFolderBreadcrumbItemView(folder.Id, folder.Name))
                    .ToArray()
            };
        }).ToArray();
    }

    public static async Task<WorkflowDefinitionView[]> CreateAsync(
        IReadOnlyCollection<WorkflowDefinition> definitions,
        IWorkflowDefinitionListProjectionStore projectionStore,
        CancellationToken cancellationToken)
    {
        var projections = await projectionStore.ListByDefinitionIdsAsync(
            definitions.Select(definition => definition.Id).ToArray(),
            cancellationToken);
        var projectionsByDefinitionId = projections.ToDictionary(
            projection => projection.WorkflowDefinitionId,
            StringComparer.Ordinal);

        return definitions.Select(definition =>
        {
            projectionsByDefinitionId.TryGetValue(definition.Id, out var projection);
            return new WorkflowDefinitionView(
                definition.Id,
                definition.Name,
                definition.Description,
                definition.CreatedAt,
                definition.LastModifiedAt,
                definition.DeletedAt,
                projection?.DraftId,
                projection?.LatestVersionId,
                projection?.LatestVersion,
                projection?.VersionCount ?? 0,
                definition.FolderId);
        }).ToArray();
    }
}
