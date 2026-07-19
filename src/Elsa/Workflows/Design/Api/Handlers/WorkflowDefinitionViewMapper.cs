using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Handlers;

internal static class WorkflowDefinitionViewMapper
{
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
