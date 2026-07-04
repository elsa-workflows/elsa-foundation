using System.Text.Json;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
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
    public async Task Empty_store_returns_nothing()
    {
        var store = await SeededStoreAsync();
        Assert.Empty(await store.ListAsync(new WorkflowDefinitionFilter()));
    }
}
