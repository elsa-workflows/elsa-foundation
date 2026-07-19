using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Core.Indexing;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

/// <summary>
/// Proves the Groundwork (document) <see cref="IWorkflowDefinitionStore"/> adapter behaves exactly like the
/// relational EF Core adapter: both translate the named read operations into the closed query spec, so a host
/// that selects a Groundwork provider gets the same results as a host on EF Core. The in-memory document store
/// reproduces the real provider surface (equality on a manifest-declared index) from
/// <see cref="WorkflowsDesignStorageManifest"/>, so this is evidence the design lane runs on a document
/// database, not just a relational one.
/// </summary>
public class GroundworkWorkflowDefinitionStoreTests
{
    private const string SchemaVersion = WorkflowsDesignStorageManifest.SchemaVersion;

    private static async Task<IWorkflowDefinitionStore> SeededStoreAsync(params WorkflowDefinition[] definitions)
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());

        foreach (var definition in definitions)
        {
            var envelope = new GroundworkDocument<WorkflowDefinition>(
                WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
                definition);
            var content = JsonSerializer.Serialize(envelope, GroundworkDesignJson.Options);
            await store.SaveAsync(new SaveDocumentRequest(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                definition.Id,
                SchemaVersion,
                content));
        }

        return new GroundworkWorkflowDefinitionStore(store);
    }

    private static WorkflowDefinition[] Sample() =>
    [
        new() { Id = "a", Name = "Order Processing", Description = "Handles orders" },
        new() { Id = "b", Name = "Invoice Generator", Description = null },
        new() { Id = "c", Name = "Shipping", Description = "ORDER fulfilment" },
    ];

    [Fact]
    public async Task FindById_returns_match_via_point_read()
    {
        var store = await SeededStoreAsync(Sample());
        var result = await store.FindByIdAsync("b");
        Assert.NotNull(result);
        Assert.Equal("Invoice Generator", result!.Name);
    }

    [Fact]
    public async Task FindById_returns_null_when_absent()
    {
        var store = await SeededStoreAsync(Sample());
        Assert.Null(await store.FindByIdAsync("missing"));
    }

    [Fact]
    public async Task GetAsync_throws_when_absent()
    {
        var store = await SeededStoreAsync(Sample());
        await Assert.ThrowsAsync<EntityNotFoundException>(() => store.GetAsync("missing"));
    }

    [Fact]
    public async Task List_by_id_matches_single()
    {
        var store = await SeededStoreAsync(Sample());
        var result = await store.ListAsync(new WorkflowDefinitionFilter { Id = "c" });
        Assert.Equal(["c"], result.Select(x => x.Id));
    }

    [Fact]
    public async Task List_by_ids_matches_set_membership()
    {
        var store = await SeededStoreAsync(Sample());
        var result = await store.ListAsync(new WorkflowDefinitionFilter { Ids = ["a", "c"] });
        Assert.Equal(["a", "c"], result.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task List_by_name_matches_exact()
    {
        var store = await SeededStoreAsync(Sample());
        var result = await store.ListAsync(new WorkflowDefinitionFilter { Name = "Shipping" });
        Assert.Equal(["c"], result.Select(x => x.Id));
    }

    [Fact]
    public async Task List_by_names_matches_set_membership()
    {
        var store = await SeededStoreAsync(Sample());
        var result = await store.ListAsync(new WorkflowDefinitionFilter { Names = ["Order Processing", "Shipping"] });
        Assert.Equal(["a", "c"], result.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task List_by_search_term_fans_out_over_name_description_and_id()
    {
        var store = await SeededStoreAsync(Sample());
        // "order" matches name "Order Processing" (a) and description "ORDER fulfilment" (c), case-insensitively.
        var result = await store.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "order" });
        Assert.Equal(["a", "c"], result.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task List_with_no_filter_returns_all()
    {
        var store = await SeededStoreAsync(Sample());
        var result = await store.ListAsync(new WorkflowDefinitionFilter());
        Assert.Equal(["a", "b", "c"], result.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task List_page_uses_the_declared_bounded_route_with_stable_id_tie_breaking()
    {
        var documents = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var queries = new RecordingBoundedDocumentStore();
        var store = new GroundworkWorkflowDefinitionStore(documents, queries);

        await store.ListPageAsync(new WorkflowDefinitionListQuery(
            new WorkflowDefinitionFilter(),
            WorkflowDefinitionLifecycleScope.All,
            WorkflowDefinitionSortBy.CreatedAt,
            WorkflowDefinitionSortDirection.Desc,
            Page: 2,
            PageSize: 10));

        var query = Assert.Single(queries.Observed);
        Assert.Equal(WorkflowsDesignStorageManifest.PageByCreatedAtDescendingQuery, query.QueryIdentity);
        Assert.Equal(10, query.Skip);
        Assert.Equal(10, query.Take);
        Assert.Collection(
            query.Order,
            order =>
            {
                Assert.Equal(WorkflowsDesignStorageManifest.WorkflowDefinitionCreatedAtField, order.Path);
                Assert.Equal(PhysicalSortDirection.Descending, order.Direction);
            },
            order =>
            {
                Assert.Equal(WorkflowsDesignStorageManifest.WorkflowDefinitionIdField, order.Path);
                Assert.Equal(PhysicalSortDirection.Ascending, order.Direction);
            });
    }

    [Fact]
    public async Task List_page_returns_a_bounded_deterministic_window_and_authoritative_total_count()
    {
        var store = await SeededStoreAsync(
            new WorkflowDefinition { Id = "b", Name = "alpha" },
            new WorkflowDefinition { Id = "a", Name = "alpha" },
            new WorkflowDefinition { Id = "c", Name = "zeta" });

        var page = await store.ListPageAsync(new WorkflowDefinitionListQuery(
            new WorkflowDefinitionFilter(),
            WorkflowDefinitionLifecycleScope.All,
            WorkflowDefinitionSortBy.Name,
            WorkflowDefinitionSortDirection.Desc,
            Page: 1,
            PageSize: 2));

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(["c", "a"], page.Items.Select(definition => definition.Id));
    }

    [Theory]
    [InlineData(WorkflowDefinitionMarkerTagOperator.Exists, "tagged")]
    [InlineData(WorkflowDefinitionMarkerTagOperator.Missing, "untagged")]
    public async Task List_page_filters_marker_projection_with_authoritative_total(
        WorkflowDefinitionMarkerTagOperator operation,
        string expectedId)
    {
        var documents = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        await documents.SaveAsync(MarkerDefinitionSave(
            new WorkflowDefinition { Id = "tagged", Name = "Tagged" },
            ["tag-priority"]));
        await documents.SaveAsync(MarkerDefinitionSave(
            new WorkflowDefinition { Id = "untagged", Name = "Untagged" },
            []));
        var store = new GroundworkWorkflowDefinitionStore(documents, documents);

        var page = await store.ListPageAsync(new WorkflowDefinitionListQuery(
            new WorkflowDefinitionFilter
            {
                MarkerTagClauses =
                [
                    new WorkflowDefinitionMarkerTagClause("tag-priority", operation)
                ]
            },
            WorkflowDefinitionLifecycleScope.All));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(expectedId, Assert.Single(page.Items).Id);
    }

    [Theory]
    [InlineData(WorkflowDefinitionControlledTagOperator.Exists, "value-production", "production")]
    [InlineData(WorkflowDefinitionControlledTagOperator.Missing, "value-production", "untagged")]
    [InlineData(WorkflowDefinitionControlledTagOperator.AnyOf, "value-production", "production")]
    [InlineData(WorkflowDefinitionControlledTagOperator.NoneOf, "value-production", "untagged")]
    public async Task List_page_filters_controlled_projection_with_authoritative_total(
        WorkflowDefinitionControlledTagOperator operation,
        string controlledValueId,
        string expectedId)
    {
        var documents = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        await documents.SaveAsync(ControlledDefinitionSave(
            new WorkflowDefinition { Id = "production", Name = "Production" },
            [new("tag-environment", "value-production")]));
        await documents.SaveAsync(ControlledDefinitionSave(
            new WorkflowDefinition { Id = "untagged", Name = "Untagged" },
            []));
        var store = new GroundworkWorkflowDefinitionStore(documents, documents);
        var values = operation is WorkflowDefinitionControlledTagOperator.AnyOf or WorkflowDefinitionControlledTagOperator.NoneOf
            ? new[] { controlledValueId }
            : null;

        var page = await store.ListPageAsync(new WorkflowDefinitionListQuery(
            new WorkflowDefinitionFilter
            {
                ControlledTagClauses = [new("tag-environment", operation, values)]
            },
            WorkflowDefinitionLifecycleScope.All));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(expectedId, Assert.Single(page.Items).Id);
    }

    private static SaveDocumentRequest MarkerDefinitionSave(
        WorkflowDefinition definition,
        IReadOnlyCollection<string> tagDefinitionIds) =>
        new(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            definition.Id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(
                new
                {
                    collection = WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
                    entity = definition,
                    markerProjection = GroundworkWorkflowDefinitionTagStore.MarkerProjection(tagDefinitionIds)
                },
                GroundworkDesignJson.Options));

    private static SaveDocumentRequest ControlledDefinitionSave(
        WorkflowDefinition definition,
        IReadOnlyCollection<WorkflowDefinitionControlledTagValue> controlledValues) =>
        new(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            definition.Id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(
                new
                {
                    collection = WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
                    entity = definition,
                    markerProjection = GroundworkWorkflowDefinitionTagStore.MarkerProjection([]),
                    controlledProjection = GroundworkWorkflowDefinitionTagStore.ControlledProjection(controlledValues)
                },
                GroundworkDesignJson.Options));

    [Fact]
    public void Paging_manifest_declares_bounded_server_routes_and_typed_sort_indexes()
    {
        var unit = Assert.Single(
            WorkflowsDesignStorageManifest.Create().StorageUnits,
            candidate => candidate.Identity.Value == WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind);
        var storage = Assert.IsType<StorageUnitPhysicalStorage>(unit.PhysicalStorage);
        var routes = storage.BoundedQueries;

        Assert.Equal(13, routes.Count);
        Assert.Contains(routes, route => route.Identity == WorkflowsDesignStorageManifest.ListAllQuery);
        var createdAtRoute = Assert.Single(routes, route => route.Identity == WorkflowsDesignStorageManifest.PageByCreatedAtQuery);
        Assert.Equal(BoundedQueryExecutionClass.ScaleBearing, createdAtRoute.ExecutionClass);
        Assert.Equal(QueryPagingSupport.Offset, createdAtRoute.PagingSupport);
        Assert.Equal(QuerySortSupport.Ascending, createdAtRoute.SortSupport);
        Assert.True(createdAtRoute.SupportsTotalCount);
        Assert.Equal(
            [
                new BoundedQuerySortField(WorkflowsDesignStorageManifest.WorkflowDefinitionCreatedAtField, PhysicalSortDirection.Ascending),
                new BoundedQuerySortField(WorkflowsDesignStorageManifest.WorkflowDefinitionIdField, PhysicalSortDirection.Ascending)
            ],
            createdAtRoute.SortFields);
        Assert.Contains(
            createdAtRoute.ResidualPredicateFields,
            field => field.Path == WorkflowsDesignStorageManifest.WorkflowDefinitionDeletedAtField
                && field.ValueKind == IndexValueKind.DateTime);

        var searchRoute = Assert.Single(routes, route => route.Identity == WorkflowsDesignStorageManifest.SearchPageByNameQuery);
        Assert.Equal(BoundedQueryExecutionClass.Ordinary, searchRoute.ExecutionClass);
        Assert.True(searchRoute.SupportsDisjunction);
        Assert.Contains(
            searchRoute.PredicateFields,
            field => field.Path == WorkflowsDesignStorageManifest.WorkflowDefinitionNameField
                && field.Operations.Contains(PortableQueryOperation.Contains));
        var markerProjection = Assert.Single(
            searchRoute.ResidualPredicateFields,
            field => field.Path == WorkflowsDesignStorageManifest.WorkflowDefinitionMarkerProjectionField);
        Assert.Contains(PortableQueryOperation.Contains, markerProjection.Operations);
        Assert.Contains(PortableQueryOperation.NotContains, markerProjection.Operations);

        Assert.Equal(
            IndexValueKind.DateTime,
            Assert.Single(storage.LogicalIndexes, index => index.Identity == WorkflowsDesignStorageManifest.WorkflowDefinitionByCreatedAtIndex)
                .Fields[0].ValueKind);

        var descendingRoute = Assert.Single(routes, route => route.Identity == WorkflowsDesignStorageManifest.PageByCreatedAtDescendingQuery);
        Assert.Equal(
            [
                new BoundedQuerySortField(WorkflowsDesignStorageManifest.WorkflowDefinitionCreatedAtField, PhysicalSortDirection.Descending),
                new BoundedQuerySortField(WorkflowsDesignStorageManifest.WorkflowDefinitionIdField, PhysicalSortDirection.Ascending)
            ],
            descendingRoute.SortFields);
        Assert.Equal(QuerySortSupport.Descending, descendingRoute.SortSupport);
    }

    [Fact]
    public async Task Empty_store_returns_nothing()
    {
        var store = await SeededStoreAsync();
        Assert.Empty(await store.ListAsync(new WorkflowDefinitionFilter()));
    }

    [Fact]
    public async Task Tenant_agnostic_query_requires_explicit_across_scope_privilege_and_records_outcome()
    {
        var manifest = WorkflowsDesignStorageManifest.Create();
        var ordinaryStore = new InMemoryDocumentStore(manifest);
        var accessor = new MutableAccessContextAccessor
        {
            Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))
        };
        var source = new SeededSessionSource(Sample()[..2]);
        var auditSink = new GroundworkPrivilegedAccessSink();
        var sessions = new GroundworkStoreSessionFactory(
            accessor,
            source,
            new GroundworkPrivilegedAccessRecorder(auditSink));
        var store = new GroundworkWorkflowDefinitionStore(ordinaryStore, ordinaryStore, sessions);
        var filter = new WorkflowDefinitionFilter { TenantAgnostic = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ListAsync(filter));
        Assert.Equal(0, source.OpenCount);

        accessor.Current = PersistenceAccessContext.PrivilegedScoped(
            new PersistenceScope("tenant-a"),
            new PersistenceAccessPurpose("list-workflow-definitions-across-tenants"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ListAsync(filter));
        Assert.Equal(0, source.OpenCount);

        accessor.Current = PersistenceAccessContext.PrivilegedAcrossScopes(
            new PersistenceAccessPurpose("list-workflow-definitions-across-tenants"));
        var results = await store.ListAsync(filter);

        Assert.Equal(["a", "b"], results.Select(definition => definition.Id).Order(StringComparer.Ordinal));
        Assert.Equal(1, source.OpenCount);
        var records = auditSink.Snapshot();
        Assert.Equal(2, records.Count);
        Assert.Equal(GroundworkPrivilegedAccessEventKind.Acquisition, records[0].EventKind);
        Assert.Equal(GroundworkPrivilegedAccessEventKind.Outcome, records[1].EventKind);
        Assert.Equal(records[0].AuditId, records[1].AuditId);
        Assert.Equal(GroundworkPrivilegedAccessOutcome.Succeeded, records[1].Outcome);
    }

    private sealed class MutableAccessContextAccessor : IPersistenceAccessContextAccessor
    {
        public required PersistenceAccessContext Current { get; set; }
    }

    private sealed class SeededSessionSource(IReadOnlyList<WorkflowDefinition> definitions)
        : IGroundworkStoreSessionSource
    {
        public int OpenCount { get; private set; }

        public async ValueTask<GroundworkStoreSessionResources> OpenAsync(
            global::Groundwork.Documents.Scoping.DocumentStoreAccess access,
            CancellationToken cancellationToken = default)
        {
            OpenCount++;
            var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create(), access);
            foreach (var definition in definitions)
            {
                var document = new GroundworkDocument<WorkflowDefinition>(
                    WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
                    definition);
                await store.SaveAsync(
                    new SaveDocumentRequest(
                        WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                        definition.Id,
                        SchemaVersion,
                        JsonSerializer.Serialize(document, GroundworkDesignJson.Options)),
                    cancellationToken);
            }

            return new GroundworkStoreSessionResources(store, store);
        }
    }

    private sealed class RecordingBoundedDocumentStore : IBoundedDocumentStore
    {
        public List<DocumentQuery> Observed { get; } = [];

        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            Observed.Add(query);
            return Task.FromResult(DocumentQueryResult.Empty);
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
