using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Exceptions;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

/// <summary>
/// Proves the Groundwork (document) <see cref="GroundworkActivityDefinitionStore"/> reproduces the relational
/// adapter's behaviour for the simple activity-definition aggregate: id / natural-key / filter reads over the
/// closed query spec, executed against the document store.
/// </summary>
public class GroundworkActivityDefinitionStoreTests
{
    private const string SchemaVersion = ActivitiesDesignStorageManifest.SchemaVersion;

    private static async Task<GroundworkActivityDefinitionStore> SeededAsync(params ActivityDefinition[] definitions)
    {
        var raw = await SeedRawAsync(definitions);
        return new GroundworkActivityDefinitionStore(raw);
    }

    private static async Task<InMemoryDocumentStore> SeedRawAsync(
        IEnumerable<ActivityDefinition> definitions)
    {
        var raw = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        foreach (var definition in definitions)
        {
            var envelope = new GroundworkDocument<ActivityDefinition>(
                ActivitiesDesignStorageManifest.ActivityDefinitionCollection, definition);
            var content = JsonSerializer.Serialize(envelope, GroundworkActivitiesDesignJson.Options);
            await raw.SaveAsync(new SaveDocumentRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, definition.Id, SchemaVersion, content));
        }

        return raw;
    }

    private static ActivityDefinition Definition(string id, string typeKey, string category = "General", string? search = null) =>
        new()
        {
            Id = id,
            ActivityTypeKey = typeKey,
            Category = category,
            DisplayName = search ?? typeKey,
            Description = search,
        };

    [Fact]
    public async Task Provider_and_corrupt_payload_reads_surface_domain_scoped_failures()
    {
        var providerFailure = new IOException("activity-provider-read");
        var raw = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var store = new GroundworkActivityDefinitionStore(
            new ThrowingDocumentStore(raw, GroundworkDocumentStoreOperation.Load, providerFailure));

        var providerException = await Assert.ThrowsAsync<DesignPersistenceException>(() => store.GetAsync("activity-1"));
        Assert.Equal(DesignPersistenceDomain.Activity, providerException.Domain);
        Assert.Equal(DesignPersistenceFailureKind.Provider, providerException.FailureKind);
        Assert.Same(providerFailure, providerException.InnerException);

        await raw.SaveAsync(new SaveDocumentRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "corrupt-activity",
            SchemaVersion,
            "null"));
        var corruptStore = new GroundworkActivityDefinitionStore(raw);

        var corruptException = await Assert.ThrowsAsync<DesignPersistenceException>(() => corruptStore.GetAsync("corrupt-activity"));
        Assert.Equal(DesignPersistenceDomain.Activity, corruptException.Domain);
        Assert.Equal(DesignPersistenceFailureKind.Serialization, corruptException.FailureKind);
    }

    [Fact]
    public async Task Get_returns_definition_by_id()
    {
        var store = await SeededAsync(Definition("a1", "Acme.Send"), Definition("a2", "Acme.Wait"));
        var result = await store.GetAsync("a1");
        Assert.Equal("Acme.Send", result.ActivityTypeKey);
    }

    [Fact]
    public async Task Get_throws_when_absent()
    {
        var store = await SeededAsync(Definition("a1", "Acme.Send"));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => store.GetAsync("missing"));
    }

    [Fact]
    public async Task FindByIdOrActivityTypeKey_matches_either_field()
    {
        var store = await SeededAsync(Definition("a1", "Acme.Send"), Definition("a2", "Acme.Wait"));

        Assert.Equal("a1", (await store.FindByIdOrActivityTypeKeyAsync("a1", "none"))!.Id);
        Assert.Equal("a2", (await store.FindByIdOrActivityTypeKeyAsync("none", "Acme.Wait"))!.Id);
        Assert.Null(await store.FindByIdOrActivityTypeKeyAsync("none", "none"));
    }

    [Fact]
    public async Task FindByIdOrActivityTypeKey_prefers_the_id_match_over_the_natural_key_match()
    {
        var store = await SeededAsync(
            Definition("requested-id", "Acme.ById"),
            Definition("other-id", "Acme.ByTypeKey"));

        var result = await store.FindByIdOrActivityTypeKeyAsync("requested-id", "Acme.ByTypeKey");

        Assert.NotNull(result);
        Assert.Equal("requested-id", result!.Id);
    }

    [Fact]
    public async Task ExistsByActivityTypeKey_reflects_presence()
    {
        var store = await SeededAsync(Definition("a1", "Acme.Send"));
        Assert.True(await store.ExistsByActivityTypeKeyAsync("Acme.Send"));
        Assert.False(await store.ExistsByActivityTypeKeyAsync("Acme.Missing"));
    }

    [Fact]
    public async Task Find_by_filter_search_term_matches_substring()
    {
        var store = await SeededAsync(
            Definition("a1", "Acme.Send", search: "Sends an email"),
            Definition("a2", "Acme.Wait", search: "Waits a while"));

        var result = await store.FindAsync(new ActivityDefinitionFilter { SearchTerm = "email" });

        Assert.NotNull(result);
        Assert.Equal("a1", result!.Id);
    }

    [Fact]
    public async Task Find_by_filter_category_matches_exact()
    {
        var store = await SeededAsync(
            Definition("a1", "Acme.Send", category: "Mail"),
            Definition("a2", "Acme.Wait", category: "Timing"));

        var result = await store.FindAsync(new ActivityDefinitionFilter { Category = "Timing" });

        Assert.NotNull(result);
        Assert.Equal("a2", result!.Id);
    }

    [Fact]
    public async Task Find_by_filter_display_name_matches_exact()
    {
        var store = await SeededAsync(
            Definition("a1", "Acme.Send", search: "Send Email"),
            Definition("a2", "Acme.Wait", search: "Wait"));

        var result = await store.FindAsync(new ActivityDefinitionFilter { DisplayName = "Send Email" });

        Assert.NotNull(result);
        Assert.Equal("a1", result!.Id);
    }

    [Fact]
    public async Task Definition_reads_use_the_selected_named_route_and_result_operation()
    {
        var raw = await SeedRawAsync(
        [
            Definition("a1", "Acme.Send", category: "Mail", search: "Send Email"),
            Definition("a2", "Acme.Wait", category: "Timing", search: "Wait")
        ]);
        var bounded = new RecordingBoundedDocumentStore(raw);
        var store = new GroundworkActivityDefinitionStore(raw, bounded);
        var filter = new ActivityDefinitionFilter { Category = "Mail", DisplayName = "Send Email" };

        await store.ListAsync(filter);
        Assert.All(
            bounded.Queries,
            query =>
            {
                Assert.Equal(
                    ActivitiesDesignStorageManifest.ListActivityDefinitionsByCategoryQuery,
                    query.QueryIdentity);
                Assert.Equal(BoundedQueryResultOperation.Documents, query.ResultOperation);
                Assert.Equal(ActivitiesDesignStorageManifest.ActivityDefinitionCategoryOrder, query.Order);
            });

        bounded.Queries.Clear();
        await store.FindAsync(filter);
        var first = Assert.Single(bounded.Queries);
        Assert.Equal(
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByCategoryQuery,
            first.QueryIdentity);
        Assert.Equal(BoundedQueryResultOperation.First, first.ResultOperation);
        Assert.Equal(ActivitiesDesignStorageManifest.ActivityDefinitionCategoryOrder, first.Order);

        bounded.Queries.Clear();
        await store.ExistsByActivityTypeKeyAsync("Acme.Send");
        var any = Assert.Single(bounded.Queries);
        Assert.Equal(
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByTypeKeyQuery,
            any.QueryIdentity);
        Assert.Equal(BoundedQueryResultOperation.Any, any.ResultOperation);
        Assert.NotEqual(ActivitiesDesignStorageManifest.ListAllQuery, any.QueryIdentity);
    }
}
