using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Primitives.Exceptions;
using Xunit;

#pragma warning disable CS0618

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkActivityDefinitionVersionStoreTests
{
    private static readonly FakePayloadSerializer Payloads = new();

    [Fact]
    public async Task Get_round_trips_descriptor_payload_and_facets()
    {
        using var harness = await SeededAsync([Definition("def1")], Version("v1", "def1", withFacet: true));
        var result = await VersionStore(harness).GetAsync("v1");

        Assert.Equal("Acme.SendActivity", result.DescriptorType);
        Assert.Equal(3, result.DescriptorPayload.GetProperty("retries").GetInt32());
        Assert.Equal("layout", Assert.Single(result.DesignFacets).Kind);
        Assert.Null(result.Definition);
    }

    [Fact]
    public async Task Get_throws_when_absent()
    {
        using var harness = await SeededAsync([Definition("def1")], Version("v1", "def1"));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => VersionStore(harness).GetAsync("missing"));
    }

    [Fact]
    public async Task GetWithDefinition_loads_owning_definition_via_second_read()
    {
        using var harness = await SeededAsync([Definition("def1", "Acme.Send")], Version("v1", "def1"));
        var result = await VersionStore(harness).GetWithDefinitionAsync("v1");

        Assert.NotNull(result.Definition);
        Assert.Equal("def1", result.Definition!.Id);
        Assert.Equal("Acme.Send", result.Definition.ActivityTypeKey);
    }

    [Fact]
    public async Task GetWithDefinition_throws_EntityNotFound_when_absent()
    {
        using var harness = await SeededAsync([Definition("def1")], Version("v1", "def1"));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => VersionStore(harness).GetWithDefinitionAsync("missing"));
    }

    [Fact]
    public async Task FindByDefinitionAndSortKey_matches_precomputed_key()
    {
        var v2 = Version("v2", "def1", "2.0.0");
        using var harness = await SeededAsync([Definition("def1")], Version("v1", "def1"), v2);

        var result = await VersionStore(harness).FindByDefinitionAndSortKeyAsync("def1", v2.SemVerSortKey);
        Assert.NotNull(result);
        Assert.Equal("v2", result!.Id);
    }

    [Fact]
    public async Task ListByDefinition_returns_only_matching_definition()
    {
        using var harness = await SeededAsync(
            [Definition("def1"), Definition("def2")],
            Version("v1", "def1"), Version("v2", "def1"), Version("v3", "def2"));

        var result = await VersionStore(harness).ListByDefinitionAsync("def1");
        Assert.Equal(2, result.Count);
        Assert.All(result, version => Assert.Equal("def1", version.DefinitionId));
    }

    [Fact]
    public async Task List_returns_every_version()
    {
        using var harness = await SeededAsync(
            [Definition("def1"), Definition("def2")], Version("v1", "def1"), Version("v2", "def2"));
        Assert.Equal(2, (await VersionStore(harness).ListAsync()).Count);
    }

    [Fact]
    public async Task Stored_document_omits_persistence_artifacts()
    {
        using var harness = await SeededAsync([Definition("def1")], Version("v1", "def1"));
        var json = (await harness.Store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind, "v1"))!.ContentJson;

        Assert.Contains("\"descriptorPayload\"", json);
        Assert.DoesNotContain("descriptorPayloadSource", json);
        Assert.DoesNotContain("inputsSource", json);
        Assert.DoesNotContain("outputsSource", json);
        Assert.DoesNotContain("designFacetsSource", json);
        Assert.DoesNotContain("rowNumber", json);
        Assert.DoesNotContain("\"definition\"", json);
    }

    [Fact]
    public async Task Version_reads_use_compound_public_queries_and_empty_batches_do_no_io()
    {
        using var harness = await SeededAsync([Definition("def1")], Version("v1", "def1"));
        var store = VersionStore(harness);
        Assert.NotNull(await store.FindByDefinitionAndSortKeyAsync("def1", Version("v1", "def1").SemVerSortKey));
        Assert.Empty(await store.ListByDefinitionIdsAsync([]));
    }

    private static GroundworkActivityDefinitionVersionStore VersionStore(ActivityDesignV2TestHarness harness) =>
        new(harness.Store, new GroundworkActivityDefinitionStore(harness.Store), Payloads);

    private static async Task<ActivityDesignV2TestHarness> SeededAsync(
        ActivityDefinition[] definitions,
        params ActivityDefinitionVersion[] versions)
    {
        var harness = ActivityDesignV2TestHarness.Create();
        foreach (var definition in definitions)
            await harness.SaveAsync(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
                definition,
                GroundworkActivitiesDesignJson.Options);
        var options = GroundworkActivitiesDesignDocumentSerialization.Create(Payloads);
        foreach (var version in versions)
            await harness.SaveAsync(
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionCollection,
                version,
                options);
        return harness;
    }

    private static ActivityDefinition Definition(string id, string typeKey = "Acme.Send") => new()
    {
        Id = id,
        ActivityTypeKey = typeKey,
        Category = "General",
        DisplayName = typeKey,
        TenantId = "tenant-a"
    };

    private static ActivityDefinitionVersion Version(
        string id,
        string definitionId,
        string version = "1.0.0",
        bool withFacet = false) => new(version, definitionId)
    {
        Id = id,
        DescriptorType = "Acme.SendActivity",
        DescriptorPayload = JsonSerializer.SerializeToElement(new { kind = "send", retries = 3 }),
        SourceKind = "Json",
        SourceId = "asset-1",
        DesignFacets = withFacet
            ? [new ActivityDesignFacet("layout", "1.0", JsonSerializer.SerializeToElement(new { x = 1 }))]
            : []
    };
}

#pragma warning restore CS0618
