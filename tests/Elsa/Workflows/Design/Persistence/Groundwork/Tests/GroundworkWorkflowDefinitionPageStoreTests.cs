using System.Text.Json;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Groundwork.Core.PhysicalStorage;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowDefinitionPageStoreTests
{
    [Fact]
    public void Admits_a_scale_bearing_cursor_route_with_the_canonical_keyset_order()
    {
        var unit = WorkflowsDesignStorageManifest.CreatePhysicalized().StorageUnits.Single(unit =>
            unit.Identity.Value == WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind);
        var route = Assert.Single(unit.PhysicalStorage!.BoundedQueries, query =>
            query.Identity == WorkflowsDesignStorageManifest.PageWorkflowDefinitionsQuery);

        Assert.Equal(QueryPagingSupport.Cursor, route.PagingSupport);
        Assert.Equal(BoundedQueryExecutionClass.ScaleBearing, route.ExecutionClass);
        Assert.Equal(
            [
                WorkflowsDesignStorageManifest.WorkflowDefinitionLastModifiedAtField,
                WorkflowsDesignStorageManifest.WorkflowDefinitionIdField
            ],
            route.SortFields!.Select(field => field.Path));
        var search = Assert.Single(unit.PhysicalStorage.BoundedQueries, query =>
            query.Identity == WorkflowsDesignStorageManifest.SearchWorkflowDefinitionsQuery);
        Assert.Equal(BoundedQueryExecutionClass.Ordinary, search.ExecutionClass);
        Assert.Equal(QueryPagingSupport.Cursor, search.PagingSupport);
    }

    [Fact]
    public async Task Pages_workflow_definitions_in_last_modified_then_id_order_without_duplicates_at_an_equal_timestamp()
    {
        var store = await SeededStoreAsync(
            Definition("d", 0),
            Definition("b", 0),
            Definition("a", 0),
            Definition("c", -1));

        var first = await store.QueryPageAsync(new WorkflowDefinitionPageQuery(2));

        Assert.Equal(["a", "b"], first.Items.Select(item => item.Id));
        Assert.Equal(2, first.Items.Count);
    }

    [Fact]
    public async Task Rejects_blank_and_oversized_page_inputs_before_the_provider_is_called()
    {
        var store = await SeededStoreAsync(Definition("a", 0), Definition("b", -1));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.QueryPageAsync(new WorkflowDefinitionPageQuery(1, ContinuationToken: " ")));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.QueryPageAsync(new WorkflowDefinitionPageQuery(101)));
    }

    [Fact]
    public async Task Paged_search_matches_definition_ids_as_well_as_names_and_descriptions()
    {
        var store = await SeededStoreAsync(Definition("order-123", 0), Definition("other", -1));

        var page = await store.QueryPageAsync(new WorkflowDefinitionPageQuery(10, SearchTerm: "123"));

        Assert.Equal(["order-123"], page.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Sqlite_continuations_resume_at_the_keyset_boundary_and_reject_a_reused_search_context()
    {
        await using var database = new TemporarySqliteDatabase();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGroundworkWorkflowsDesignStores();
        services.AddSqliteGroundworkDocumentStore(database.ConnectionString);
        await using var provider = services.BuildServiceProvider();
        await provider.ApplySqliteGroundworkSchemaAsync(database.ConnectionString);
        await provider.InitializeGroundworkStoreAsync();
        await using var scope = provider.CreateAsyncScope();
        var documentStore = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        var store = new GroundworkWorkflowDefinitionStore(documentStore, (IBoundedDocumentStore)documentStore);

        foreach (var definition in new[] { Definition("d", 0), Definition("b", 0), Definition("a", 0), Definition("c", -1) })
            await SaveAsync(documentStore, definition);

        var first = await store.QueryPageAsync(new WorkflowDefinitionPageQuery(2));
        var second = await store.QueryPageAsync(new WorkflowDefinitionPageQuery(2, ContinuationToken: first.NextContinuationToken));

        Assert.Equal(["a", "b"], first.Items.Select(item => item.Id));
        Assert.Equal(["d", "c"], second.Items.Select(item => item.Id));
        Assert.NotNull(first.NextContinuationToken);
        Assert.Null(second.NextContinuationToken);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.QueryPageAsync(new WorkflowDefinitionPageQuery(2, SearchTerm: "other", ContinuationToken: first.NextContinuationToken)));
        Assert.Equal("ContinuationToken", exception.ParamName);

        var malformed = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.QueryPageAsync(new WorkflowDefinitionPageQuery(2, ContinuationToken: "not-a-groundwork-continuation")));
        Assert.Equal("ContinuationToken", malformed.ParamName);
    }

    private static async Task<IWorkflowDefinitionPageStore> SeededStoreAsync(params WorkflowDefinition[] definitions)
    {
        var documentStore = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.CreatePhysicalized());
        foreach (var definition in definitions)
            await SaveAsync(documentStore, definition);

        return new GroundworkWorkflowDefinitionStore(documentStore, documentStore);
    }

    private static WorkflowDefinition Definition(string id, int minuteOffset) => new()
    {
        Id = id,
        Name = id,
        LastModifiedAt = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero).AddMinutes(minuteOffset)
    };

    private static Task SaveAsync(IDocumentStore documentStore, WorkflowDefinition definition)
    {
        var document = new GroundworkDocument<WorkflowDefinition>(
            WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
            definition);
        return documentStore.SaveAsync(new SaveDocumentRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            definition.Id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(document, GroundworkDesignJson.Options)));
    }

    private sealed class TemporarySqliteDatabase : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"elsa-workflow-definition-page-{Guid.NewGuid():N}.db");

        public string ConnectionString => $"Data Source={_path}";

        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            return ValueTask.CompletedTask;
        }
    }
}
