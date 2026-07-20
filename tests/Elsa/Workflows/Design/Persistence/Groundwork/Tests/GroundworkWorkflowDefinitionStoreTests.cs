using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
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
        var store = await SeedRawAsync(definitions);
        return new GroundworkWorkflowDefinitionStore(store);
    }

    private static async Task<InMemoryDocumentStore> SeedRawAsync(
        IEnumerable<WorkflowDefinition> definitions)
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

        return store;
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
    public async Task Point_read_does_not_require_a_bounded_surface_but_named_queries_do()
    {
        var raw = await SeedRawAsync(Sample());
        var store = new GroundworkWorkflowDefinitionStore(new DocumentStoreOnlyAdapter(raw));

        Assert.Equal("b", (await store.FindByIdAsync("b"))?.Id);

        var exception = await Assert.ThrowsAsync<GroundworkQueryReadinessException>(() =>
            store.ListAsync(new WorkflowDefinitionFilter { Id = "b" }));
        Assert.Equal(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, exception.DocumentKind);
        Assert.Equal(WorkflowsDesignStorageManifest.ListDefinitionsByIdQuery, exception.QueryIdentity);
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
    public async Task List_uses_the_selected_named_route_with_its_complete_declared_order()
    {
        var raw = await SeedRawAsync(Sample());
        var bounded = new RecordingBoundedDocumentStore(raw);
        var store = new GroundworkWorkflowDefinitionStore(raw, bounded);

        var result = await store.ListAsync(
            new WorkflowDefinitionFilter
            {
                SearchTerm = "order",
                Description = "Handles orders"
            });

        Assert.Equal(["a"], result.Select(x => x.Id));
        Assert.NotEmpty(bounded.Queries);
        Assert.All(
            bounded.Queries,
            query =>
            {
                Assert.Equal(WorkflowsDesignStorageManifest.SearchDefinitionsQuery, query.QueryIdentity);
                Assert.Equal(BoundedQueryResultOperation.Documents, query.ResultOperation);
                Assert.Equal(WorkflowsDesignStorageManifest.WorkflowDefinitionSearchOrder, query.Order);
                Assert.NotEqual(WorkflowsDesignStorageManifest.ListAllQuery, query.QueryIdentity);
            });
    }

    [Fact]
    public async Task List_with_no_filter_returns_all()
    {
        var store = await SeededStoreAsync(Sample());
        var result = await store.ListAsync(new WorkflowDefinitionFilter());
        Assert.Equal(["a", "b", "c"], result.Select(x => x.Id).OrderBy(x => x));
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
}
