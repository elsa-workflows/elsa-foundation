using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Groundwork.Querying;
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
        var raw = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());

        foreach (var definition in definitions)
        {
            var envelope = new GroundworkDocument<ActivityDefinition>(
                ActivitiesDesignStorageManifest.ActivityDefinitionCollection, definition);
            var content = JsonSerializer.Serialize(envelope, GroundworkActivitiesDesignJson.Options);
            await raw.SaveAsync(new SaveDocumentRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, definition.Id, SchemaVersion, content));
        }

        return new GroundworkActivityDefinitionStore(raw);
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetAsync("missing"));
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
}
