using Elsa.Mediator.Core.Contracts;
using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class ListDefinitionsRequestHandler(
    IWorkflowDefinitionStore store,
    IWorkflowDefinitionListProjectionStore projectionStore,
    IWorkflowDefinitionTagStore? tagStore = null,
    ITagDefinitionStore? tagDefinitionStore = null,
    ITagDefinitionCatalogPersistence? tagCatalogPersistence = null,
    IControlledTagValueStore? controlledTagValues = null,
    IControlledTagValueCatalogPersistence? controlledValuePersistence = null)

    : IRequestHandler<ListDefinitions, WorkflowDefinitionListView>
{
    public async Task<WorkflowDefinitionListView> Handle(ListDefinitions request, CancellationToken cancellationToken)
    {
        var markerTagClauses = ParseMarkerTagClauses(request.MarkerTagClauses);
        var controlledTagClauses = ParseControlledTagClauses(request.ControlledTagClauses);
        var controlledTagFacets = ParseControlledTagFacetIds(request.ControlledTagFacets);
        var groupByTagDefinitionId = NormalizeOptionalId(request.GroupByControlledTagDefinitionId);
        var taggingAvailable =
            tagStore is not null
            && tagDefinitionStore is not null
            && tagCatalogPersistence is not null;
        var controlledQueryRequested =
            controlledTagClauses.Count > 0
            || controlledTagFacets.Count > 0
            || groupByTagDefinitionId is not null;
        var controlledTaggingAvailable =
            controlledTagValues is not null
            && controlledValuePersistence is not null;
        if ((markerTagClauses.Count > 0 || controlledQueryRequested) && !taggingAvailable)
            throw new ArgumentException(
                "Tag filters are unavailable because workflow-definition tagging is not active in this shell.",
                nameof(request.ControlledTagClauses));
        if (controlledQueryRequested && !controlledTaggingAvailable)
            throw new WorkflowDefinitionTaggingUnavailableException();

        if (controlledQueryRequested)
        {
            await ValidateControlledQueryAsync(
                controlledTagClauses,
                controlledTagFacets,
                groupByTagDefinitionId,
                cancellationToken);
        }

        var filter = new WorkflowDefinitionFilter
        {
            Id = request.Id,
            Description = request.Description,
            Name = request.Name,
            SearchTerm = request.SearchTerm,
            MarkerTagClauses = markerTagClauses,
            ControlledTagClauses = controlledTagClauses
        };

        var listQuery = new WorkflowDefinitionListQuery(
            filter,
            ParseScope(request.State),
            ParseSortBy(request.SortBy),
            ParseSortDirection(request.SortDirection),
            request.Page,
            request.PageSize);
        listQuery.Validate();
        var grouped = groupByTagDefinitionId is null
            ? null
            : await ListGroupedAsync(
                listQuery,
                groupByTagDefinitionId,
                cancellationToken);
        var page = grouped?.Page
                   ?? await store.ListPageAsync(listQuery, cancellationToken);
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
        var controlledValueIds = tagSets
            .SelectMany(tagSet => tagSet.Assertions)
            .Select(assertion => assertion.ControlledValueId)
            .Where(valueId => !string.IsNullOrWhiteSpace(valueId))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var resolvedControlledValues = !taggingAvailable || !controlledTaggingAvailable
            ? []
            : await ResolveControlledTagValuesAsync(
                controlledTagValues!,
                controlledValueIds,
                cancellationToken);
        var controlledValuesById = resolvedControlledValues.ToDictionary(
            value => value.Id,
            StringComparer.Ordinal);

        var items = definitions.Select(definition =>
        {
            projectionsByDefinitionId.TryGetValue(definition.Id, out var projection);
            tagSetsByDefinitionId.TryGetValue(definition.Id, out var tagSet);
            var markerTags = (tagSet?.Assertions ?? [])
                .Where(assertion => assertion.ControlledValueId is null)
                .Select(assertion => tagDefinitionsById.GetValueOrDefault(assertion.TagDefinitionId))
                .Where(tagDefinition => tagDefinition is not null)
                .Where(tagDefinition => tagDefinition!.ValueMode == TagValueMode.Marker)
                .DistinctBy(tagDefinition => tagDefinition!.Id, StringComparer.Ordinal)
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
            var tagChips = BuildTagChips(
                tagSet?.Assertions ?? [],
                tagDefinitionsById,
                controlledValuesById);
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
                markerTags,
                tagChips,
                grouped?.ItemGroups.GetValueOrDefault(definition.Id));
        }).ToArray();
        var facets = controlledTagFacets.Count == 0
            ? []
            : await BuildControlledFacetsAsync(
                listQuery,
                controlledTagFacets,
                cancellationToken);
        return new WorkflowDefinitionListView(
            items,
            request.Page,
            request.PageSize,
            page.TotalCount,
            facets,
            grouped?.Groups ?? []);
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

    private static async Task<IReadOnlyList<ControlledTagValue>> ResolveControlledTagValuesAsync(
        IControlledTagValueStore store,
        IReadOnlyCollection<string> controlledValueIds,
        CancellationToken cancellationToken)
    {
        var values = new List<ControlledTagValue>(controlledValueIds.Count);
        foreach (var batch in controlledValueIds.Chunk(100))
            values.AddRange(await store.ListByIdsAsync(batch, cancellationToken));
        return values;
    }

    private async Task ValidateControlledQueryAsync(
        IReadOnlyCollection<WorkflowDefinitionControlledTagClause> clauses,
        IReadOnlyCollection<string> facetTagDefinitionIds,
        string? groupByTagDefinitionId,
        CancellationToken cancellationToken)
    {
        var definitionIds = clauses
            .Select(clause => clause.TagDefinitionId)
            .Concat(facetTagDefinitionIds)
            .Concat(groupByTagDefinitionId is null ? [] : [groupByTagDefinitionId])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var definitions = await TagDefinitionStore.ListByIdsAsync(definitionIds, cancellationToken);
        var definitionsById = definitions.ToDictionary(definition => definition.Id, StringComparer.Ordinal);
        foreach (var definitionId in definitionIds)
        {
            if (!definitionsById.TryGetValue(definitionId, out var definition)
                || definition.ValueMode != TagValueMode.Controlled
                || definition.Cardinality != TagCardinality.Single
                || !definition.Eligibility.HasFlag(TagDefinitionEligibility.WorkflowDefinition))
            {
                throw new ArgumentException(
                    "A requested controlled tag definition is unavailable or does not support this operation.",
                    nameof(definitionIds));
            }
        }

        var requestedValues = clauses
            .SelectMany(clause => (clause.ControlledValueIds ?? [])
                .Select(valueId => new WorkflowDefinitionControlledTagValue(
                    clause.TagDefinitionId,
                    valueId)))
            .Distinct()
            .ToArray();
        var values = await ResolveControlledTagValuesAsync(
            ControlledValueStore,
            requestedValues.Select(value => value.ControlledValueId).Distinct(StringComparer.Ordinal).ToArray(),
            cancellationToken);
        var valuesById = values.ToDictionary(value => value.Id, StringComparer.Ordinal);
        foreach (var requestedValue in requestedValues)
        {
            if (!valuesById.TryGetValue(requestedValue.ControlledValueId, out var value)
                || !StringComparer.Ordinal.Equals(
                    value.TagDefinitionId,
                    requestedValue.TagDefinitionId))
            {
                throw new ArgumentException(
                    "A requested controlled tag value is unavailable for its tag definition.",
                    nameof(clauses));
            }
        }
    }

    private async Task<GroupedDefinitionsResult> ListGroupedAsync(
        WorkflowDefinitionListQuery query,
        string tagDefinitionId,
        CancellationToken cancellationToken)
    {
        var definition = (await TagDefinitionStore.ListByIdsAsync(
            [tagDefinitionId],
            cancellationToken)).Single();
        var values = await ControlledValueStore.ListWithRevisionsAsync(
            new ControlledTagValueListRequest
            {
                TagDefinitionId = tagDefinitionId,
                ActiveOnly = false
            },
            cancellationToken);
        EnsureBoundedCatalog(values.Count, tagDefinitionId);

        var plans = values
            .Select(record => new ControlledGroupPlan(
                "Value",
                record.Value.Id,
                record.Value.DisplayName,
                record.Value.Color ?? definition.Color,
                [
                    new WorkflowDefinitionControlledTagClause(
                        tagDefinitionId,
                        WorkflowDefinitionControlledTagOperator.AnyOf,
                        [record.Value.Id]),
                    new WorkflowDefinitionControlledTagClause(
                        tagDefinitionId,
                        WorkflowDefinitionControlledTagOperator.NotConflicted)
                ]))
            .Concat(
            [
                new ControlledGroupPlan(
                    "Untagged",
                    null,
                    "Untagged",
                    null,
                    [
                        new WorkflowDefinitionControlledTagClause(
                            tagDefinitionId,
                            WorkflowDefinitionControlledTagOperator.Missing)
                    ]),
                new ControlledGroupPlan(
                    "Conflicted",
                    null,
                    "Conflicted",
                    null,
                    [
                        new WorkflowDefinitionControlledTagClause(
                            tagDefinitionId,
                            WorkflowDefinitionControlledTagOperator.Conflicted)
                    ])
            ])
            .ToArray();

        var counts = new int[plans.Length];
        for (var index = 0; index < plans.Length; index++)
        {
            var countPage = await store.ListPageAsync(
                WithControlledClauses(query, plans[index].Clauses, offset: 0, pageSize: 1),
                cancellationToken);
            counts[index] = countPage.TotalCount;
        }

        var items = new List<WorkflowDefinition>(query.PageSize);
        var itemGroups = new Dictionary<string, WorkflowDefinitionControlledTagGroupKeyView>(
            StringComparer.Ordinal);
        var remainingSkip = query.Skip;
        var remainingTake = query.PageSize;
        for (var index = 0; index < plans.Length && remainingTake > 0; index++)
        {
            var count = counts[index];
            if (remainingSkip >= count)
            {
                remainingSkip -= count;
                continue;
            }

            var take = Math.Min(remainingTake, count - remainingSkip);
            var groupPage = await store.ListPageAsync(
                WithControlledClauses(
                    query,
                    plans[index].Clauses,
                    remainingSkip,
                    take),
                cancellationToken);
            items.AddRange(groupPage.Items);
            foreach (var item in groupPage.Items)
            {
                itemGroups[item.Id] = new WorkflowDefinitionControlledTagGroupKeyView(
                    plans[index].Kind,
                    plans[index].ControlledValueId);
            }
            remainingTake -= groupPage.Items.Count;
            remainingSkip = 0;
        }

        var groups = plans.Select((plan, index) =>
            new WorkflowDefinitionControlledTagGroupView(
                plan.Kind,
                plan.ControlledValueId,
                plan.Label,
                plan.Color,
                counts[index])).ToArray();
        return new(
            new WorkflowDefinitionPage(items, counts.Sum()),
            groups,
            itemGroups);
    }

    private async Task<IReadOnlyCollection<WorkflowDefinitionControlledTagFacetView>> BuildControlledFacetsAsync(
        WorkflowDefinitionListQuery query,
        IReadOnlyCollection<string> tagDefinitionIds,
        CancellationToken cancellationToken)
    {
        var result = new List<WorkflowDefinitionControlledTagFacetView>(tagDefinitionIds.Count);
        foreach (var tagDefinitionId in tagDefinitionIds)
        {
            var values = await ControlledValueStore.ListWithRevisionsAsync(
                new ControlledTagValueListRequest
                {
                    TagDefinitionId = tagDefinitionId,
                    ActiveOnly = false
                },
                cancellationToken);
            EnsureBoundedCatalog(values.Count, tagDefinitionId);
            var facetValues = new List<WorkflowDefinitionControlledTagFacetValueView>(values.Count);
            foreach (var record in values)
            {
                var facetQuery = WithControlledClauses(
                    query,
                    [
                        new WorkflowDefinitionControlledTagClause(
                            tagDefinitionId,
                            WorkflowDefinitionControlledTagOperator.AnyOf,
                            [record.Value.Id])
                    ],
                    offset: 0,
                    pageSize: 1,
                    removeTagDefinitionId: tagDefinitionId);
                var count = (await store.ListPageAsync(facetQuery, cancellationToken)).TotalCount;
                facetValues.Add(new(
                    record.Value.Id,
                    record.Value.CanonicalKey,
                    record.Value.DisplayName,
                    record.Value.Description,
                    record.Value.Color,
                    record.Value.Status.ToString(),
                    record.Value.SortOrder,
                    count));
            }
            result.Add(new(tagDefinitionId, facetValues));
        }
        return result;
    }

    private static WorkflowDefinitionListQuery WithControlledClauses(
        WorkflowDefinitionListQuery query,
        IReadOnlyCollection<WorkflowDefinitionControlledTagClause> additionalClauses,
        int offset,
        int pageSize,
        string? removeTagDefinitionId = null)
    {
        var existingClauses = (query.Filter.ControlledTagClauses ?? [])
            .Where(clause => removeTagDefinitionId is null
                             || !StringComparer.Ordinal.Equals(
                                 clause.TagDefinitionId,
                                 removeTagDefinitionId));
        var filter = new WorkflowDefinitionFilter
        {
            Id = query.Filter.Id,
            Ids = query.Filter.Ids,
            Name = query.Filter.Name,
            Names = query.Filter.Names,
            Description = query.Filter.Description,
            SearchTerm = query.Filter.SearchTerm,
            TenantAgnostic = query.Filter.TenantAgnostic,
            MarkerTagClauses = query.Filter.MarkerTagClauses,
            ControlledTagClauses = existingClauses.Concat(additionalClauses).ToArray()
        };
        return query with
        {
            Filter = filter,
            Page = 1,
            PageSize = pageSize,
            Offset = offset
        };
    }

    private static IReadOnlyCollection<WorkflowDefinitionTagChipView> BuildTagChips(
        IReadOnlyCollection<WorkflowDefinitionTagAssertion> assertions,
        IReadOnlyDictionary<string, TagDefinition> definitions,
        IReadOnlyDictionary<string, ControlledTagValue> controlledValues)
    {
        return assertions
            .GroupBy(assertion => assertion.TagDefinitionId, StringComparer.Ordinal)
            .Select(group =>
            {
                if (!definitions.TryGetValue(group.Key, out var definition))
                    return null;

                var valueIds = group
                    .Select(assertion => assertion.ControlledValueId)
                    .Where(valueId => !string.IsNullOrWhiteSpace(valueId))
                    .Cast<string>()
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                if (valueIds.Length == 0)
                {
                    return new WorkflowDefinitionTagChipView(
                        definition.Id,
                        definition.CanonicalKey,
                        definition.DisplayName,
                        definition.Description,
                        definition.Color,
                        definition.Status.ToString());
                }

                if (valueIds.Length > 1)
                {
                    return new WorkflowDefinitionTagChipView(
                        definition.Id,
                        definition.CanonicalKey,
                        definition.DisplayName,
                        definition.Description,
                        definition.Color,
                        definition.Status.ToString(),
                        Conflict: true,
                        ConflictControlledValueIds: valueIds);
                }

                controlledValues.TryGetValue(valueIds[0], out var value);
                return new WorkflowDefinitionTagChipView(
                    definition.Id,
                    definition.CanonicalKey,
                    definition.DisplayName,
                    definition.Description,
                    definition.Color,
                    definition.Status.ToString(),
                    valueIds[0],
                    value?.CanonicalKey,
                    value?.DisplayName ?? "Unavailable value",
                    value?.Description,
                    value?.Color,
                    value?.Status.ToString() ?? "Unavailable");
            })
            .Where(chip => chip is not null)
            .Cast<WorkflowDefinitionTagChipView>()
            .OrderBy(chip => chip.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(chip => chip.TagDefinitionId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyCollection<string> ParseControlledTagFacetIds(
        IReadOnlyCollection<string>? values)
    {
        var ids = (values ?? [])
            .Select(value => value?.Trim() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length > 4)
            throw new ArgumentOutOfRangeException(
                nameof(values),
                "At most four controlled tag facets can be requested.");
        if (ids.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Controlled tag facet identities cannot be blank.", nameof(values));
        return ids;
    }

    private static string? NormalizeOptionalId(string? value)
    {
        if (value is null)
            return null;
        var normalized = value.Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("A group-by tag definition identity cannot be blank.", nameof(value));
        return normalized;
    }

    private static void EnsureBoundedCatalog(int count, string tagDefinitionId)
    {
        if (count > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tagDefinitionId),
                "Faceting and grouping support at most 100 controlled values per tag definition.");
        }
    }

    private ITagDefinitionStore TagDefinitionStore =>
        tagDefinitionStore
        ?? throw new InvalidOperationException("Tag definition persistence is unavailable.");

    private IControlledTagValueStore ControlledValueStore =>
        controlledTagValues
        ?? throw new InvalidOperationException("Controlled tag value persistence is unavailable.");

    private sealed record ControlledGroupPlan(
        string Kind,
        string? ControlledValueId,
        string Label,
        string? Color,
        IReadOnlyCollection<WorkflowDefinitionControlledTagClause> Clauses);

    private sealed record GroupedDefinitionsResult(
        WorkflowDefinitionPage Page,
        IReadOnlyCollection<WorkflowDefinitionControlledTagGroupView> Groups,
        IReadOnlyDictionary<string, WorkflowDefinitionControlledTagGroupKeyView> ItemGroups);

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
        if (values.Count > 16)
            throw new ArgumentOutOfRangeException(
                nameof(values),
                "At most 16 marker tag clauses are supported.");

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

    private static IReadOnlyCollection<WorkflowDefinitionControlledTagClause> ParseControlledTagClauses(
        IReadOnlyCollection<string>? values)
    {
        if (values is null)
            return [];
        if (values.Count > 16)
            throw new ArgumentOutOfRangeException(
                nameof(values),
                "At most 16 controlled tag clauses are supported.");

        return values.Select(value =>
        {
            var parts = value.Split(':', 3, StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]))
                throw new ArgumentException(
                    "Each controlledTagClauses value must use '<tagDefinitionId>:exists', '<tagDefinitionId>:missing', '<tagDefinitionId>:anyOf:<valueId,...>', or '<tagDefinitionId>:noneOf:<valueId,...>'.",
                    nameof(values));
            var operation = parts[1].ToLowerInvariant() switch
            {
                "exists" => WorkflowDefinitionControlledTagOperator.Exists,
                "missing" => WorkflowDefinitionControlledTagOperator.Missing,
                "anyof" => WorkflowDefinitionControlledTagOperator.AnyOf,
                "noneof" => WorkflowDefinitionControlledTagOperator.NoneOf,
                _ => throw new ArgumentException("Controlled tag operators must be exists, missing, anyOf, or noneOf.", nameof(values))
            };
            var valueIds = parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[2])
                ? parts[2].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : [];
            var clause = new WorkflowDefinitionControlledTagClause(parts[0], operation, valueIds);
            clause.Validate();
            return clause;
        }).ToArray();
    }
}
