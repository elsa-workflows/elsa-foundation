using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class ListDefinitionsRequestHandler(
    IWorkflowDefinitionStore store,
    IWorkflowDefinitionListProjectionStore projectionStore)

    : IRequestHandler<ListDefinitions, WorkflowDefinitionListView>
{
    public async Task<WorkflowDefinitionListView> Handle(ListDefinitions request, CancellationToken cancellationToken)
    {
        var filter = new WorkflowDefinitionFilter
        {
            Id = request.Id,
            Description = request.Description,
            Name = request.Name,
            SearchTerm = request.SearchTerm
        };

        var listQuery = new WorkflowDefinitionListQuery(
            filter,
            ParseScope(request.State),
            ParseSortBy(request.SortBy),
            ParseSortDirection(request.SortDirection),
            request.Page,
            request.PageSize);
        listQuery.Validate();
        var page = await store.ListPageAsync(listQuery, cancellationToken);
        var definitions = page.Items;
        var projections = await projectionStore.ListByDefinitionIdsAsync(
            definitions.Select(definition => definition.Id).ToArray(),
            cancellationToken);
        var projectionsByDefinitionId = projections.ToDictionary(
            projection => projection.WorkflowDefinitionId,
            StringComparer.Ordinal);

        var items = definitions.Select(definition =>
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
                projection?.VersionCount ?? 0);
        }).ToArray();
        return new WorkflowDefinitionListView(items, request.Page, request.PageSize, page.TotalCount);
    }

    private static WorkflowDefinitionLifecycleScope ParseScope(string? state) => state?.ToLowerInvariant() switch
    {
        "deleted" => WorkflowDefinitionLifecycleScope.Deleted,
        "all" => WorkflowDefinitionLifecycleScope.All,
        _ => WorkflowDefinitionLifecycleScope.Active
    };

    private static WorkflowDefinitionSortBy ParseSortBy(string? sortBy)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
            return WorkflowDefinitionSortBy.Name;

        return sortBy.ToLowerInvariant() switch
        {
        "name" => WorkflowDefinitionSortBy.Name,
        "lastmodifiedat" => WorkflowDefinitionSortBy.LastModifiedAt,
        "createdat" => WorkflowDefinitionSortBy.CreatedAt,
        _ => throw new ArgumentException("sortBy must be one of: name, lastModifiedAt, createdAt.", nameof(sortBy))
        };
    }

    private static WorkflowDefinitionSortDirection ParseSortDirection(string? sortDirection)
    {
        if (string.IsNullOrWhiteSpace(sortDirection))
            return WorkflowDefinitionSortDirection.Asc;

        return sortDirection.ToLowerInvariant() switch
        {
        "asc" => WorkflowDefinitionSortDirection.Asc,
        "desc" => WorkflowDefinitionSortDirection.Desc,
        _ => throw new ArgumentException("sortDirection must be either asc or desc.", nameof(sortDirection))
        };
    }
}
