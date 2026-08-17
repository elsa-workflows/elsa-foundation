using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Exceptions;
using Groundwork.Kernel;
using Groundwork.Store;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkActivityDefinitionStoreTests
{
    [Fact]
    public async Task Provider_and_corrupt_payload_reads_surface_domain_scoped_failures()
    {
        using var harness = ActivityDesignV2TestHarness.Create();
        var exception = await Assert.ThrowsAsync<DesignPersistenceException>(() => harness.Store.SaveAsync(new ActivityDesignSaveRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            "corrupt-activity",
            ActivitiesDesignStorageManifest.SchemaVersion,
            "null")));

        Assert.Equal(DesignPersistenceFailureKind.Serialization, exception.FailureKind);

        var unit = ActivitiesDesignStorageManifest.Require(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind);
        harness.Connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope("tenant-a"))).Upsert(
            new StorageValues(new Dictionary<string, object?>
            {
                [ActivitiesDesignStorageManifest.IdField] = "corrupt-read",
                [ActivitiesDesignStorageManifest.SchemaVersionField] = ActivitiesDesignStorageManifest.SchemaVersion,
                [ActivitiesDesignStorageManifest.ContentField] = "{not-json",
                [ActivitiesDesignStorageManifest.RevisionField] = 1L,
                [ActivitiesDesignStorageManifest.UpdatedAtField] = DateTimeOffset.UtcNow
            }),
            WriteOptions.Unconditional);
        var corrupt = await Assert.ThrowsAsync<DesignPersistenceException>(() =>
            new GroundworkActivityDefinitionStore(harness.Store).GetAsync("corrupt-read"));
        Assert.Equal(DesignPersistenceDomain.Activity, corrupt.Domain);
        Assert.Equal(DesignPersistenceFailureKind.Serialization, corrupt.FailureKind);
    }

    [Fact]
    public async Task Get_returns_definition_by_id()
    {
        using var harness = await SeededAsync(Definition("a1", "Acme.Send"), Definition("a2", "Acme.Wait"));
        var result = await new GroundworkActivityDefinitionStore(harness.Store).GetAsync("a1");
        Assert.Equal("Acme.Send", result.ActivityTypeKey);
    }

    [Fact]
    public async Task Get_throws_when_absent()
    {
        using var harness = await SeededAsync(Definition("a1", "Acme.Send"));
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            new GroundworkActivityDefinitionStore(harness.Store).GetAsync("missing"));
    }

    [Fact]
    public async Task FindByIdOrActivityTypeKey_matches_either_field()
    {
        using var harness = await SeededAsync(Definition("a1", "Acme.Send"), Definition("a2", "Acme.Wait"));
        var store = new GroundworkActivityDefinitionStore(harness.Store);

        Assert.Equal("a1", (await store.FindByIdOrActivityTypeKeyAsync("a1", "none"))!.Id);
        Assert.Equal("a2", (await store.FindByIdOrActivityTypeKeyAsync("none", "Acme.Wait"))!.Id);
        Assert.Null(await store.FindByIdOrActivityTypeKeyAsync("none", "none"));
    }

    [Fact]
    public async Task FindByIdOrActivityTypeKey_prefers_the_id_match_over_the_natural_key_match()
    {
        using var harness = await SeededAsync(
            Definition("requested-id", "Acme.ById"),
            Definition("other-id", "Acme.ByTypeKey"));

        var result = await new GroundworkActivityDefinitionStore(harness.Store)
            .FindByIdOrActivityTypeKeyAsync("requested-id", "Acme.ByTypeKey");

        Assert.NotNull(result);
        Assert.Equal("requested-id", result!.Id);
    }

    [Fact]
    public async Task ExistsByActivityTypeKey_reflects_presence()
    {
        using var harness = await SeededAsync(Definition("a1", "Acme.Send"));
        var store = new GroundworkActivityDefinitionStore(harness.Store);
        Assert.True(await store.ExistsByActivityTypeKeyAsync("Acme.Send"));
        Assert.False(await store.ExistsByActivityTypeKeyAsync("Acme.Missing"));
    }

    [Fact]
    public async Task Find_by_filter_search_term_matches_substring()
    {
        using var harness = await SeededAsync(
            Definition("a1", "Acme.Send", search: "Sends an email"),
            Definition("a2", "Acme.Wait", search: "Waits a while"));

        var result = await new GroundworkActivityDefinitionStore(harness.Store)
            .FindAsync(new ActivityDefinitionFilter { SearchTerm = "email" });

        Assert.NotNull(result);
        Assert.Equal("a1", result!.Id);
    }

    [Fact]
    public async Task Find_by_filter_category_matches_exact()
    {
        using var harness = await SeededAsync(
            Definition("a1", "Acme.Send", category: "Mail"),
            Definition("a2", "Acme.Wait", category: "Timing"));

        var result = await new GroundworkActivityDefinitionStore(harness.Store)
            .FindAsync(new ActivityDefinitionFilter { Category = "Timing" });

        Assert.NotNull(result);
        Assert.Equal("a2", result!.Id);
    }

    [Fact]
    public async Task Find_by_filter_display_name_matches_exact()
    {
        using var harness = await SeededAsync(
            Definition("a1", "Acme.Send", search: "Send Email"),
            Definition("a2", "Acme.Wait", search: "Wait"));

        var result = await new GroundworkActivityDefinitionStore(harness.Store)
            .FindAsync(new ActivityDefinitionFilter { DisplayName = "Send Email" });

        Assert.NotNull(result);
        Assert.Equal("a1", result!.Id);
    }

    [Fact]
    public async Task Definition_reads_use_the_selected_named_route_and_result_operation()
    {
        using var harness = await SeededAsync(
            Definition("a2", "Acme.Send", category: "Mail", search: "Send Email"),
            Definition("a1", "Acme.Send", category: "Mail", search: "Send Email"));
        var store = new GroundworkActivityDefinitionStore(harness.Store);

        var listed = await store.ListAsync(new ActivityDefinitionFilter { Category = "Mail" });
        Assert.Equal(["a1", "a2"], listed.Select(x => x.Id));
        Assert.Equal("a1", (await store.FindAsync(new ActivityDefinitionFilter { Category = "Mail" }))!.Id);
    }

    private static async Task<ActivityDesignV2TestHarness> SeededAsync(params ActivityDefinition[] definitions)
    {
        var harness = ActivityDesignV2TestHarness.Create();
        foreach (var definition in definitions)
            await harness.SaveAsync(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
                definition,
                GroundworkActivitiesDesignJson.Options);
        return harness;
    }

    private static ActivityDefinition Definition(
        string id,
        string typeKey,
        string category = "General",
        string? search = null) =>
        new()
        {
            Id = id,
            ActivityTypeKey = typeKey,
            Category = category,
            DisplayName = search ?? typeKey,
            Description = search,
            TenantId = "tenant-a"
        };
}
