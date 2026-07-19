using Elsa.Workflows.Design.Api.Handlers;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests;

/// <summary>
/// RED query-budget contract for FR-029. The list response must be a Studio-ready aggregate projection,
/// and the number of persistence reads must remain bounded as the number of definitions grows.
/// </summary>
public sealed class WorkflowDefinitionProjectionTests
{
    [Fact]
    public async Task Definition_list_returns_a_deterministic_server_page_with_authoritative_metadata()
    {
        var fixture = new ProjectionFixture(25);

        var result = await fixture.CreateHandler().Handle(
            new ListDefinitions(null, null, null, null, "all", Page: 2, PageSize: 3),
            CancellationToken.None);

        Assert.Equal(2, result.Page);
        Assert.Equal(3, result.PageSize);
        Assert.Equal(25, result.TotalCount);
        Assert.Equal(["definition-12", "definition-13", "definition-14"], result.Items.Select(item => item.Id));
        Assert.Equal(4, fixture.TotalReadCount);
    }

    [Theory]
    [InlineData("priority", null)]
    [InlineData(null, "sideways")]
    public async Task Definition_list_rejects_unknown_nonblank_sort_values(string? sortBy, string? sortDirection)
    {
        var handler = new ProjectionFixture(1).CreateHandler();

        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(
            new ListDefinitions(null, null, null, null, SortBy: sortBy, SortDirection: sortDirection),
            CancellationToken.None));
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Definition_list_rejects_unbounded_page_arguments(int page, int pageSize)
    {
        var handler = new ProjectionFixture(1).CreateHandler();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.Handle(
            new ListDefinitions(null, null, null, null, Page: page, PageSize: pageSize),
            CancellationToken.None));
    }

    [Fact]
    public void Paging_offset_accepts_the_last_representable_window_and_rejects_the_next()
    {
        var accepted = new WorkflowDefinitionListQuery(
            new WorkflowDefinitionFilter(),
            Page: 1_073_741_824,
            PageSize: 2);

        accepted.Validate();
        Assert.Equal(2_147_483_646, accepted.Skip);
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowDefinitionListQuery(
            new WorkflowDefinitionFilter(),
            Page: 1_073_741_825,
            PageSize: 2).Validate());
    }

    [Fact]
    public async Task Definition_list_applies_descending_sorts_with_an_ascending_id_tie_breaker()
    {
        var fixture = new ProjectionFixture(
        [
            new WorkflowDefinition { Id = "b", Name = "alpha" },
            new WorkflowDefinition { Id = "a", Name = "alpha" },
            new WorkflowDefinition { Id = "c", Name = "zeta" }
        ]);

        var result = await fixture.CreateHandler().Handle(
            new ListDefinitions(null, null, null, null, PageSize: 3, SortDirection: "desc"),
            CancellationToken.None);

        Assert.Equal(["c", "a", "b"], result.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Definition_list_keeps_authoritative_metadata_when_the_requested_page_is_empty()
    {
        var fixture = new ProjectionFixture(3);

        var result = await fixture.CreateHandler().Handle(
            new ListDefinitions(null, null, "needle", null, "all", Page: 9, PageSize: 2),
            CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(9, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal("needle", fixture.LastPageQuery!.Filter.SearchTerm);
    }

    [Fact]
    public async Task Definition_list_parses_marker_clauses_and_returns_catalog_backed_chips()
    {
        var fixture = new ProjectionFixture(1);

        var result = await fixture.CreateHandler().Handle(
            new ListDefinitions(
                null,
                null,
                null,
                null,
                MarkerTagClauses: ["tag-priority:exists", "tag-review:missing"]),
            CancellationToken.None);

        Assert.Collection(
            fixture.LastPageQuery!.Filter.MarkerTagClauses!,
            clause =>
            {
                Assert.Equal("tag-priority", clause.TagDefinitionId);
                Assert.Equal(WorkflowDefinitionMarkerTagOperator.Exists, clause.Operator);
            },
            clause =>
            {
                Assert.Equal("tag-review", clause.TagDefinitionId);
                Assert.Equal(WorkflowDefinitionMarkerTagOperator.Missing, clause.Operator);
            });
        var marker = Assert.Single(Assert.Single(result.Items).MarkerTags!);
        Assert.Equal("tag-priority", marker.TagDefinitionId);
        Assert.Equal("Priority", marker.DisplayName);
        Assert.Equal("#ef4444", marker.Color);
    }

    [Fact]
    public async Task Definition_list_parses_controlled_any_of_and_none_of_clauses()
    {
        var fixture = new ProjectionFixture(1);

        _ = await fixture.CreateHandler().Handle(
            new ListDefinitions(
                null,
                null,
                null,
                null,
                ControlledTagClauses:
                [
                    "tag-environment:anyOf:value-development,value-production",
                    "tag-region:noneOf:value-retired"
                ]),
            CancellationToken.None);

        Assert.Collection(
            fixture.LastPageQuery!.Filter.ControlledTagClauses!,
            anyOf =>
            {
                Assert.Equal("tag-environment", anyOf.TagDefinitionId);
                Assert.Equal(WorkflowDefinitionControlledTagOperator.AnyOf, anyOf.Operator);
                Assert.Equal(["value-development", "value-production"], anyOf.ControlledValueIds);
            },
            noneOf =>
            {
                Assert.Equal("tag-region", noneOf.TagDefinitionId);
                Assert.Equal(WorkflowDefinitionControlledTagOperator.NoneOf, noneOf.Operator);
                Assert.Equal(["value-retired"], noneOf.ControlledValueIds);
            });
    }

    [Fact]
    public async Task Controlled_queries_require_durable_controlled_value_persistence()
    {
        var handler = new ProjectionFixture(1).CreateHandlerWithoutControlledValuePersistence();

        await Assert.ThrowsAsync<WorkflowDefinitionTaggingUnavailableException>(() =>
            handler.Handle(
                new ListDefinitions(
                    null,
                    null,
                    null,
                    null,
                    ControlledTagClauses: ["tag-environment:exists"]),
                CancellationToken.None));
    }

    [Fact]
    public async Task Controlled_facets_remove_only_their_own_clause_and_grouping_pages_across_value_and_untagged_groups()
    {
        var definitions = new[]
        {
            new WorkflowDefinition { Id = "development", Name = "Development" },
            new WorkflowDefinition { Id = "production", Name = "Production" },
            new WorkflowDefinition { Id = "untagged", Name = "Untagged" },
            new WorkflowDefinition { Id = "conflicted", Name = "Conflicted" }
        };
        var tagSets = new Dictionary<string, WorkflowDefinitionTagSet>(StringComparer.Ordinal)
        {
            ["development"] = TagSet("development", Assertion("value-development", "manual")),
            ["production"] = TagSet("production", Assertion("value-production", "manual")),
            ["untagged"] = TagSet("untagged"),
            ["conflicted"] = TagSet(
                "conflicted",
                Assertion("value-development", "source"),
                Assertion("value-production", "manual"))
        };
        var services = new ServiceCollection()
            .AddSingleton<IWorkflowDefinitionStore>(new SemanticDefinitionStore(definitions, tagSets))
            .AddSingleton<IWorkflowDefinitionListProjectionStore>(new CountingProjectionStore())
            .AddSingleton<IWorkflowDefinitionTagStore>(new SemanticTagStore(tagSets))
            .AddSingleton<ITagDefinitionStore>(new CountingTagDefinitionStore())
            .AddSingleton<IControlledTagValueStore>(new CountingControlledTagValueStore())
            .AddSingleton<ITagDefinitionCatalogPersistence, DurableCatalogPersistence>()
            .AddSingleton<IControlledTagValueCatalogPersistence, DurableControlledValuePersistence>()
            .BuildServiceProvider();
        var handler = ActivatorUtilities.CreateInstance<ListDefinitionsRequestHandler>(services);

        var result = await handler.Handle(
            new ListDefinitions(
                null,
                null,
                null,
                null,
                PageSize: 2,
                ControlledTagClauses: ["tag-environment:noneOf:value-development"],
                ControlledTagFacets: ["tag-environment"],
                GroupByControlledTagDefinitionId: "tag-environment"),
            CancellationToken.None);

        var items = result.Items.ToArray();
        Assert.Equal(["production", "untagged"], items.Select(item => item.Id));
        Assert.Equal(
            [("Value", "value-production"), ("Untagged", null)],
            items.Select(item => (item.Group!.Kind, item.Group.ControlledValueId)));
        var productionChip = Assert.Single(items[0].TagChips!);
        Assert.Equal("value-production", productionChip.ControlledValueId);
        Assert.Equal("Production", productionChip.ControlledValueDisplayName);
        Assert.Equal("#22c55e", productionChip.ControlledValueColor);

        var facet = Assert.Single(result.ControlledTagFacets!);
        Assert.Equal(
            [("value-development", 2), ("value-production", 2)],
            facet.Values.Select(value => (value.ControlledValueId, value.Count)));
        Assert.Equal(
            [
                ("Value", "value-development", 0),
                ("Value", "value-production", 1),
                ("Untagged", null, 1),
                ("Conflicted", null, 0)
            ],
            result.ControlledTagGroups!.Select(group => (group.Kind, group.ControlledValueId, group.Count)));
    }

    [Theory]
    [InlineData("tag-priority")]
    [InlineData(":exists")]
    [InlineData("tag-priority:any")]
    public async Task Definition_list_rejects_invalid_marker_clauses(string value)
    {
        var handler = new ProjectionFixture(1).CreateHandler();

        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(
            new ListDefinitions(null, null, null, null, MarkerTagClauses: [value]),
            CancellationToken.None));
    }

    [Fact]
    public async Task Definition_list_rejects_marker_filters_when_tagging_is_not_active()
    {
        var fixture = new ProjectionFixture(1);
        var handler = fixture.CreateHandlerWithoutTags();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(
            new ListDefinitions(null, null, null, null, MarkerTagClauses: ["tag-priority:exists"]),
            CancellationToken.None));

        Assert.Contains("not active", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.DefinitionReadCount);
    }

    [Fact]
    public void Provider_neutral_query_fails_closed_for_marker_filters()
    {
        var filter = new WorkflowDefinitionFilter
        {
            MarkerTagClauses =
            [
                new WorkflowDefinitionMarkerTagClause(
                    "tag-priority",
                    WorkflowDefinitionMarkerTagOperator.Exists)
            ]
        };

        Assert.Throws<NotSupportedException>(() => filter.ToQuery());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    public async Task Definition_list_projects_draft_latest_version_count_and_deletion_with_a_bounded_read_budget(int definitionCount)
    {
        var fixture = new ProjectionFixture(definitionCount);
        var handler = fixture.CreateHandler();

        var result = (await handler.Handle(
            new ListDefinitions(null, null, null, null, "all"),
            CancellationToken.None)).Items.ToArray();

        Assert.Equal(definitionCount, result.Length);
        Assert.InRange(fixture.TotalReadCount, 1, 5);
        foreach (var view in result)
        {
            var ordinal = int.Parse(view.Id["definition-".Length..]);
            Assert.Equal($"draft-{ordinal}", RequiredProperty<string>(view, "DraftId"));
            Assert.Equal($"version-{ordinal}-2", RequiredProperty<string>(view, "LatestVersionId"));
            Assert.Equal("2.0.0", RequiredProperty<string>(view, "LatestVersion"));
            Assert.Equal(2, RequiredProperty<int>(view, "VersionCount"));
            Assert.Equal(ordinal % 2 == 0, Property(view, "DeletedAt") is not null);
        }
    }

    [Theory]
    [InlineData(null, 13)]
    [InlineData("active", 13)]
    [InlineData("deleted", 12)]
    [InlineData("all", 25)]
    public async Task Definition_list_honors_active_deleted_and_all_scopes(string? state, int expectedCount)
    {
        var fixture = new ProjectionFixture(25);
        var result = await fixture.CreateHandler().Handle(
            new ListDefinitions(null, null, null, null, state),
            CancellationToken.None);

        Assert.Equal(expectedCount, result.Items.Count);
        Assert.InRange(fixture.TotalReadCount, 1, 5);
    }

    private static T RequiredProperty<T>(WorkflowDefinitionView view, string name) =>
        Property(view, name) is T value
            ? value
            : throw new Xunit.Sdk.XunitException($"WorkflowDefinitionView must expose populated '{name}' in the aggregate list projection.");

    private static object? Property(WorkflowDefinitionView view, string name) =>
        view.GetType().GetProperty(name)?.GetValue(view);

    private sealed class ProjectionFixture
    {
        private readonly CountingDefinitionStore _definitions;
        private readonly CountingProjectionStore _projections;
        private readonly CountingTagStore _tags = new();
        private readonly CountingTagDefinitionStore _tagDefinitions = new();
        private readonly CountingControlledTagValueStore _controlledTagValues = new();

        public ProjectionFixture(int count)
        {
            var definitions = Enumerable.Range(1, count)
                .Select(ordinal => new WorkflowDefinition
                {
                    Id = $"definition-{ordinal}",
                    Name = $"Workflow {ordinal}",
                    DeletedAt = ordinal % 2 == 0 ? DateTimeOffset.UnixEpoch.AddDays(ordinal) : null
                })
                .ToArray();
            _definitions = new CountingDefinitionStore(definitions);
            _projections = new CountingProjectionStore();
        }

        public ProjectionFixture(IReadOnlyList<WorkflowDefinition> definitions)
        {
            _definitions = new CountingDefinitionStore(definitions);
            _projections = new CountingProjectionStore();
        }

        public int TotalReadCount =>
            _definitions.ReadCount + _projections.ReadCount + _tags.ReadCount + _tagDefinitions.ReadCount;
        public int DefinitionReadCount => _definitions.ReadCount;
        public WorkflowDefinitionListQuery? LastPageQuery => _definitions.LastPageQuery;

        public ListDefinitionsRequestHandler CreateHandler()
        {
            var services = new ServiceCollection()
                .AddSingleton<IWorkflowDefinitionStore>(_definitions)
                .AddSingleton<IWorkflowDefinitionListProjectionStore>(_projections)
                .AddSingleton<IWorkflowDefinitionTagStore>(_tags)
                .AddSingleton<ITagDefinitionStore>(_tagDefinitions)
                .AddSingleton<IControlledTagValueStore>(_controlledTagValues)
                .AddSingleton<ITagDefinitionCatalogPersistence, DurableCatalogPersistence>()
                .AddSingleton<IControlledTagValueCatalogPersistence, DurableControlledValuePersistence>()
                .BuildServiceProvider();
            return ActivatorUtilities.CreateInstance<ListDefinitionsRequestHandler>(services);
        }

        public ListDefinitionsRequestHandler CreateHandlerWithoutTags()
        {
            var services = new ServiceCollection()
                .AddSingleton<IWorkflowDefinitionStore>(_definitions)
                .AddSingleton<IWorkflowDefinitionListProjectionStore>(_projections)
                .BuildServiceProvider();
            return ActivatorUtilities.CreateInstance<ListDefinitionsRequestHandler>(services);
        }

        public ListDefinitionsRequestHandler CreateHandlerWithoutControlledValuePersistence()
        {
            var services = new ServiceCollection()
                .AddSingleton<IWorkflowDefinitionStore>(_definitions)
                .AddSingleton<IWorkflowDefinitionListProjectionStore>(_projections)
                .AddSingleton<IWorkflowDefinitionTagStore>(_tags)
                .AddSingleton<ITagDefinitionStore>(_tagDefinitions)
                .AddSingleton<IControlledTagValueStore>(_controlledTagValues)
                .AddSingleton<ITagDefinitionCatalogPersistence, DurableCatalogPersistence>()
                .BuildServiceProvider();
            return ActivatorUtilities.CreateInstance<ListDefinitionsRequestHandler>(services);
        }
    }

    private sealed class DurableCatalogPersistence : ITagDefinitionCatalogPersistence;
    private sealed class DurableControlledValuePersistence : IControlledTagValueCatalogPersistence;

    private sealed class CountingDefinitionStore(IReadOnlyList<WorkflowDefinition> definitions) : IWorkflowDefinitionStore
    {
        public int ReadCount { get; private set; }
        public WorkflowDefinitionListQuery? LastPageQuery { get; private set; }
        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(definitions);
        }

        public Task<WorkflowDefinitionPage> ListPageAsync(WorkflowDefinitionListQuery query, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            LastPageQuery = query;
            var matching = query.Scope switch
            {
                WorkflowDefinitionLifecycleScope.Deleted => definitions.Where(definition => definition.DeletedAt is not null),
                WorkflowDefinitionLifecycleScope.All => definitions,
                _ => definitions.Where(definition => definition.DeletedAt is null)
            };
            var ordered = query.SortDirection == WorkflowDefinitionSortDirection.Desc
                ? matching.OrderByDescending(definition => definition.Name, StringComparer.Ordinal)
                    .ThenBy(definition => definition.Id, StringComparer.Ordinal)
                    .ToArray()
                : matching.OrderBy(definition => definition.Name, StringComparer.Ordinal)
                    .ThenBy(definition => definition.Id, StringComparer.Ordinal)
                    .ToArray();
            return Task.FromResult(new WorkflowDefinitionPage(
                ordered.Skip(query.Skip).Take(query.PageSize).ToArray(),
                ordered.Length));
        }
    }

    private sealed class CountingProjectionStore : IWorkflowDefinitionListProjectionStore
    {
        public int ReadCount { get; private set; }

        public Task<IReadOnlyList<WorkflowDefinitionListProjection>> ListByDefinitionIdsAsync(
            IReadOnlyCollection<string> workflowDefinitionIds,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            IReadOnlyList<WorkflowDefinitionListProjection> projections = workflowDefinitionIds
                .Select(workflowDefinitionId =>
                {
                    var ordinal = workflowDefinitionId.StartsWith("definition-", StringComparison.Ordinal)
                        ? workflowDefinitionId["definition-".Length..]
                        : workflowDefinitionId;
                    return new WorkflowDefinitionListProjection(
                        workflowDefinitionId,
                        $"draft-{ordinal}",
                        $"version-{ordinal}-2",
                        "2.0.0",
                        2);
                })
                .ToArray();
            return Task.FromResult(projections);
        }
    }

    private sealed class CountingTagStore : IWorkflowDefinitionTagStore
    {
        public int ReadCount { get; private set; }

        public Task<WorkflowDefinitionTagSet> GetAsync(
            string workflowDefinitionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkflowDefinitionTagSet(
                workflowDefinitionId,
                null,
                WorkflowDefinitionTagRevision.Initial,
                [WorkflowDefinitionTagAssertion.Manual("tag-priority")]));

        public async Task<IReadOnlyCollection<WorkflowDefinitionTagSet>> ListByDefinitionIdsAsync(
            IReadOnlyCollection<string> workflowDefinitionIds,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            var results = new List<WorkflowDefinitionTagSet>();
            foreach (var id in workflowDefinitionIds)
                results.Add(await GetAsync(id, cancellationToken));
            return results;
        }

        public Task<WorkflowDefinitionTagReplaceResult> ReplaceManualAsync(
            ReplaceWorkflowDefinitionManualTags request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CountingTagDefinitionStore : ITagDefinitionStore
    {
        private readonly TagDefinition[] _definitions =
        [
            new()
            {
                Id = "tag-priority",
                CanonicalKey = "priority",
                DisplayName = "Priority",
                Color = "#ef4444"
            },
            Controlled("tag-environment"),
            Controlled("tag-region")
        ];

        public int ReadCount { get; private set; }

        public ValueTask<TagDefinition?> FindByCanonicalKeyAsync(
            string canonicalKey,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<TagDefinition?>(_definitions.SingleOrDefault(
                definition => definition.CanonicalKey == canonicalKey));

        public ValueTask<TagDefinitionRevisionedRecord?> FindWithRevisionAsync(
            string tagDefinitionId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                _definitions.SingleOrDefault(definition => definition.Id == tagDefinitionId) is { } definition
                    ? new TagDefinitionRevisionedRecord(definition, "tag:1")
                    : null);

        public ValueTask<IReadOnlyList<TagDefinition>> ListByIdsAsync(
            IReadOnlyCollection<string> tagDefinitionIds,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return ValueTask.FromResult<IReadOnlyList<TagDefinition>>(
                _definitions
                    .Where(definition => tagDefinitionIds.Contains(definition.Id, StringComparer.Ordinal))
                    .ToArray());
        }

        public ValueTask<IReadOnlyList<TagDefinition>> ListAsync(
            TagDefinitionListRequest request,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return ValueTask.FromResult<IReadOnlyList<TagDefinition>>(_definitions);
        }

        public ValueTask<bool> TryAddAsync(
            TagDefinition definition,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<TagDefinitionSaveResult> SaveWithRevisionAsync(
            TagDefinition definition,
            string expectedRevision,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static TagDefinition Controlled(string id) => new()
        {
            Id = id,
            CanonicalKey = id,
            DisplayName = id,
            ValueMode = TagValueMode.Controlled,
            Cardinality = TagCardinality.Single
        };
    }

    private sealed class CountingControlledTagValueStore : IControlledTagValueStore
    {
        private readonly ControlledTagValue[] _values =
        [
            Value("value-development", "tag-environment"),
            Value("value-production", "tag-environment"),
            Value("value-retired", "tag-region")
        ];

        public ValueTask<ControlledTagValue?> FindByCanonicalKeyAsync(string tagDefinitionId, string canonicalKey, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ControlledTagValue?>(_values.SingleOrDefault(
                value => value.TagDefinitionId == tagDefinitionId
                         && value.CanonicalKey == canonicalKey));

        public ValueTask<ControlledTagValueRevisionedRecord?> FindWithRevisionAsync(string controlledTagValueId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                _values.SingleOrDefault(value => value.Id == controlledTagValueId) is { } value
                    ? new ControlledTagValueRevisionedRecord(value, "value:1")
                    : null);

        public ValueTask<IReadOnlyList<ControlledTagValueRevisionedRecord>> ListWithRevisionsAsync(ControlledTagValueListRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ControlledTagValueRevisionedRecord>>(
                _values
                    .Where(value => value.TagDefinitionId == request.TagDefinitionId)
                    .OrderBy(value => value.SortOrder)
                    .ThenBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(value => new ControlledTagValueRevisionedRecord(value, "value:1"))
                    .ToArray());

        public ValueTask<bool> TryAddAsync(ControlledTagValue value, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<TagDefinitionSaveResult> SaveWithRevisionAsync(ControlledTagValue value, string expectedRevision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static ControlledTagValue Value(string id, string tagDefinitionId) => new()
        {
            Id = id,
            TagDefinitionId = tagDefinitionId,
            CanonicalKey = id,
            DisplayName = id == "value-development" ? "Development"
                : id == "value-production" ? "Production"
                : "Retired",
            Color = id == "value-production" ? "#22c55e" : null,
            SortOrder = id == "value-development" ? 10 : 20
        };
    }

    private sealed class SemanticTagStore(
        IReadOnlyDictionary<string, WorkflowDefinitionTagSet> tagSets) : IWorkflowDefinitionTagStore
    {
        public Task<WorkflowDefinitionTagSet> GetAsync(
            string workflowDefinitionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(tagSets.GetValueOrDefault(workflowDefinitionId) ?? TagSet(workflowDefinitionId));

        public Task<IReadOnlyCollection<WorkflowDefinitionTagSet>> ListByDefinitionIdsAsync(
            IReadOnlyCollection<string> workflowDefinitionIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<WorkflowDefinitionTagSet>>(
                workflowDefinitionIds.Select(id => tagSets.GetValueOrDefault(id) ?? TagSet(id)).ToArray());

        public Task<WorkflowDefinitionTagReplaceResult> ReplaceManualAsync(
            ReplaceWorkflowDefinitionManualTags request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SemanticDefinitionStore(
        IReadOnlyCollection<WorkflowDefinition> definitions,
        IReadOnlyDictionary<string, WorkflowDefinitionTagSet> tagSets) : IWorkflowDefinitionStore
    {
        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(definitions.Single(definition => definition.Id == id));

        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(definitions.SingleOrDefault(definition => definition.Id == id));

        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(
            WorkflowDefinitionFilter filter,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinition>>(definitions.ToArray());

        public Task<WorkflowDefinitionPage> ListPageAsync(
            WorkflowDefinitionListQuery query,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<WorkflowDefinition> matching = definitions;
            foreach (var clause in query.Filter.ControlledTagClauses ?? [])
            {
                matching = matching.Where(definition =>
                {
                    var tagSet = tagSets.GetValueOrDefault(definition.Id) ?? TagSet(definition.Id);
                    var effective = WorkflowDefinitionTagEffectiveReducer.ReduceControlled(
                        clause.TagDefinitionId,
                        tagSet.Assertions);
                    var requested = clause.ControlledValueIds ?? [];
                    return clause.Operator switch
                    {
                        WorkflowDefinitionControlledTagOperator.Exists =>
                            effective.State != WorkflowDefinitionTagEffectiveState.Untagged,
                        WorkflowDefinitionControlledTagOperator.Missing =>
                            effective.State == WorkflowDefinitionTagEffectiveState.Untagged,
                        WorkflowDefinitionControlledTagOperator.AnyOf =>
                            effective.ControlledValueIds.Intersect(requested, StringComparer.Ordinal).Any(),
                        WorkflowDefinitionControlledTagOperator.NoneOf =>
                            !effective.ControlledValueIds.Intersect(requested, StringComparer.Ordinal).Any(),
                        WorkflowDefinitionControlledTagOperator.Conflicted =>
                            effective.State == WorkflowDefinitionTagEffectiveState.Conflicted,
                        WorkflowDefinitionControlledTagOperator.NotConflicted =>
                            effective.State != WorkflowDefinitionTagEffectiveState.Conflicted,
                        _ => false
                    };
                });
            }

            var ordered = matching
                .OrderBy(definition => definition.Name, StringComparer.Ordinal)
                .ThenBy(definition => definition.Id, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult(new WorkflowDefinitionPage(
                ordered.Skip(query.Skip).Take(query.PageSize).ToArray(),
                ordered.Length));
        }
    }

    private static WorkflowDefinitionTagSet TagSet(
        string workflowDefinitionId,
        params WorkflowDefinitionTagAssertion[] assertions) =>
        new(
            workflowDefinitionId,
            null,
            WorkflowDefinitionTagRevision.Initial,
            assertions);

    private static WorkflowDefinitionTagAssertion Assertion(string valueId, string origin) =>
        new("tag-environment", origin, origin, valueId);
}
