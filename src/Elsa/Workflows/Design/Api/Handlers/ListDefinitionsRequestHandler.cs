using Elsa.Mediator.Core.Contracts;
using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class ListDefinitionsRequestHandler(
    IWorkflowDefinitionStore store,
    IWorkflowDefinitionListProjectionStore projectionStore,
    IWorkflowDefinitionTagStore? tagStore = null,
    ITagDefinitionStore? tagDefinitionStore = null,
    ITagDefinitionCatalogPersistence? tagCatalogPersistence = null)

    : IRequestHandler<ListDefinitions, WorkflowDefinitionListView>
{
    public async Task<WorkflowDefinitionListView> Handle(ListDefinitions request, CancellationToken cancellationToken)
    {
        var markerTagClauses = ParseMarkerTagClauses(request.MarkerTagClauses);
        var taggingAvailable =
            tagStore is not null
            && tagDefinitionStore is not null
            && tagCatalogPersistence is not null;
        if (markerTagClauses.Count > 0 && !taggingAvailable)
            throw new ArgumentException(
                "Marker tag filters are unavailable because workflow-definition tagging is not active in this shell.",
                nameof(request.MarkerTagClauses));

        var filter = new WorkflowDefinitionFilter
        {
            Id = request.Id,
            Description = request.Description,
            Name = request.Name,
            SearchTerm = request.SearchTerm,
            MarkerTagClauses = markerTagClauses
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
        var tagSets = !taggingAvailable
            ? []
            : await tagStore!.ListByDefinitionIdsAsync(
                definitions.Select(definition => definition.Id).ToArray(),
                cancellationToken);
        var tagDefinitionIds = tagSets
            .SelectMany(tagSet => tagSet.Assertions)
            .Select(assertion => assertion.TagDefinitionId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var tagDefinitions = !taggingAvailable
            ? []
            : await ResolveMarkerTagsAsync(tagDefinitionStore!, tagDefinitionIds, cancellationToken);
        var projectionsByDefinitionId = projections.ToDictionary(
            projection => projection.WorkflowDefinitionId,
            StringComparer.Ordinal);
        var tagSetsByDefinitionId = tagSets.ToDictionary(
            tagSet => tagSet.WorkflowDefinitionId,
            StringComparer.Ordinal);
        var tagDefinitionsById = tagDefinitions.ToDictionary(
            definition => definition.Id,
            StringComparer.Ordinal);

        var items = definitions.Select(definition =>
        {
            projectionsByDefinitionId.TryGetValue(definition.Id, out var projection);
            tagSetsByDefinitionId.TryGetValue(definition.Id, out var tagSet);
            var markerTags = (tagSet?.Assertions ?? [])
                .Select(assertion => tagDefinitionsById.GetValueOrDefault(assertion.TagDefinitionId))
                .Where(tagDefinition => tagDefinition is not null)
                .Select(tagDefinition => new WorkflowDefinitionMarkerTagView(
                    tagDefinition!.Id,
                    tagDefinition.CanonicalKey,
                    tagDefinition.DisplayName,
                    tagDefinition.Description,
                    tagDefinition.Color,
                    tagDefinition.Status.ToString()))
                .OrderBy(tag => tag.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(tag => tag.TagDefinitionId, StringComparer.Ordinal)
                .ToArray();
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
                markerTags);
        }).ToArray();
        return new WorkflowDefinitionListView(items, request.Page, request.PageSize, page.TotalCount);
    }

    private static async Task<IReadOnlyList<TagDefinition>> ResolveMarkerTagsAsync(
        ITagDefinitionStore store,
        IReadOnlyCollection<string> tagDefinitionIds,
        CancellationToken cancellationToken)
    {
        var definitions = new List<TagDefinition>(tagDefinitionIds.Count);
        foreach (var batch in tagDefinitionIds.Chunk(100))
            definitions.AddRange(await store.ListByIdsAsync(batch, cancellationToken));
        return definitions;
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

    private static IReadOnlyCollection<WorkflowDefinitionMarkerTagClause> ParseMarkerTagClauses(
        IReadOnlyCollection<string>? values)
    {
        if (values is null)
            return [];

        return values.Select(value =>
        {
            var parts = value.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
                throw new ArgumentException(
                    "Each markerTagClauses value must use '<tagDefinitionId>:exists' or '<tagDefinitionId>:missing'.",
                    nameof(values));
            var operation = parts[1].ToLowerInvariant() switch
            {
                "exists" => WorkflowDefinitionMarkerTagOperator.Exists,
                "missing" => WorkflowDefinitionMarkerTagOperator.Missing,
                _ => throw new ArgumentException(
                    "Marker tag operators must be either 'exists' or 'missing'.",
                    nameof(values))
            };
            return new WorkflowDefinitionMarkerTagClause(parts[0], operation);
        }).ToArray();
    }
}
