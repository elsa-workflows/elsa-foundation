using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Exceptions;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

/// <summary>
/// Proves the Groundwork (document) <see cref="GroundworkActivityDefinitionVersionStore"/> — the most complex
/// rich design aggregate on the document path — round-trips the authored projection collections (via the
/// payload serializer) and the native <c>DescriptorPayload</c> JSON, resolves the owning definition with an
/// explicit second read, and excludes the EF shadow / navigation members; the same behaviour as the
/// relational adapter.
/// </summary>
public class GroundworkActivityDefinitionVersionStoreTests
{
    private const string SchemaVersion = ActivitiesDesignStorageManifest.SchemaVersion;
    private static readonly FakePayloadSerializer Payloads = new();

    private static async Task<(
        GroundworkActivityDefinitionVersionStore Store,
        InMemoryDocumentStore Raw,
        RecordingBoundedDocumentStore Bounded)> SeededAsync(
        ActivityDefinition[] definitions,
        params ActivityDefinitionVersion[] versions)
    {
        var raw = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var versionOptions = GroundworkActivitiesDesignDocumentSerialization.Create(Payloads);

        foreach (var definition in definitions)
        {
            var envelope = new GroundworkDocument<ActivityDefinition>(
                ActivitiesDesignStorageManifest.ActivityDefinitionCollection, definition);
            await raw.SaveAsync(new SaveDocumentRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, definition.Id, SchemaVersion,
                JsonSerializer.Serialize(envelope, GroundworkActivitiesDesignJson.Options)));
        }

        foreach (var version in versions)
        {
            var envelope = new GroundworkDocument<ActivityDefinitionVersion>(
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionCollection, version);
            await raw.SaveAsync(new SaveDocumentRequest(
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind, version.Id, SchemaVersion,
                JsonSerializer.Serialize(envelope, versionOptions)));
        }

        var definitionStore = new GroundworkActivityDefinitionStore(raw);
        var bounded = new RecordingBoundedDocumentStore(raw);
        return (
            new GroundworkActivityDefinitionVersionStore(raw, definitionStore, Payloads, bounded),
            raw,
            bounded);
    }

    private static ActivityDefinition Definition(string id, string typeKey = "Acme.Send") =>
        new() { Id = id, ActivityTypeKey = typeKey, Category = "General", DisplayName = typeKey };

    private static ActivityDefinitionVersion Version(string id, string definitionId, string version = "1.0.0", bool withFacet = false) =>
        new(version, definitionId)
        {
            Id = id,
            DescriptorType = "Acme.SendActivity",
            DescriptorPayload = JsonSerializer.SerializeToElement(new { kind = "send", retries = 3 }),
            SourceKind = "Json",
            SourceId = "asset-1",
            DesignFacets = withFacet
                ? [new ActivityDesignFacet("layout", "1.0", JsonSerializer.SerializeToElement(new { x = 1 }))]
                : [],
        };

    [Fact]
    public async Task Get_round_trips_descriptor_payload_and_facets()
    {
        var (store, _, _) = await SeededAsync([Definition("def1")], Version("v1", "def1", withFacet: true));

        var result = await store.GetAsync("v1");

        Assert.Equal("Acme.SendActivity", result.DescriptorType);
        Assert.Equal(3, result.DescriptorPayload.GetProperty("retries").GetInt32());
        var facet = Assert.Single(result.DesignFacets);
        Assert.Equal("layout", facet.Kind);
        Assert.Null(result.Definition); // navigation not embedded
    }

    [Fact]
    public async Task Get_throws_when_absent()
    {
        var (store, _, _) = await SeededAsync([Definition("def1")], Version("v1", "def1"));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => store.GetAsync("missing"));
    }

    [Fact]
    public async Task GetWithDefinition_loads_owning_definition_via_second_read()
    {
        var (store, _, _) = await SeededAsync([Definition("def1", "Acme.Send")], Version("v1", "def1"));

        var result = await store.GetWithDefinitionAsync("v1");

        Assert.NotNull(result.Definition);
        Assert.Equal("def1", result.Definition!.Id);
        Assert.Equal("Acme.Send", result.Definition.ActivityTypeKey);
    }

    [Fact]
    public async Task GetWithDefinition_throws_EntityNotFound_when_absent()
    {
        // GetWithDefinitionAsync must use the same not-found convention as GetAsync (and the
        // 404-mapping infrastructure / FindVersion assume) rather than ArgumentException (issue #417 item 8).
        var (store, _, _) = await SeededAsync([Definition("def1")], Version("v1", "def1"));

        await Assert.ThrowsAsync<EntityNotFoundException>(() => store.GetWithDefinitionAsync("missing"));
    }

    [Fact]
    public async Task FindByDefinitionAndSortKey_matches_precomputed_key()
    {
        var v1 = Version("v1", "def1", "1.0.0");
        var v2 = Version("v2", "def1", "2.0.0");
        var (store, _, _) = await SeededAsync([Definition("def1")], v1, v2);

        var result = await store.FindByDefinitionAndSortKeyAsync("def1", v2.SemVerSortKey);

        Assert.NotNull(result);
        Assert.Equal("v2", result!.Id);
    }

    [Fact]
    public async Task ListByDefinition_returns_only_matching_definition()
    {
        var (store, _, _) = await SeededAsync(
            [Definition("def1"), Definition("def2")],
            Version("v1", "def1"), Version("v2", "def1"), Version("v3", "def2"));

        var result = await store.ListByDefinitionAsync("def1");

        Assert.Equal(2, result.Count);
        Assert.All(result, v => Assert.Equal("def1", v.DefinitionId));
    }

    [Fact]
    public async Task List_returns_every_version()
    {
        var (store, _, _) = await SeededAsync(
            [Definition("def1"), Definition("def2")],
            Version("v1", "def1"), Version("v2", "def2"));

        var result = await store.ListAsync();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Stored_document_omits_persistence_artifacts()
    {
        var (_, raw, _) = await SeededAsync([Definition("def1")], Version("v1", "def1"));

        var json = (await raw.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind, "v1"))!.ContentJson;

        Assert.Contains("\"descriptorPayload\"", json);
        Assert.DoesNotContain("descriptorPayloadSource", json);
        Assert.DoesNotContain("inputsSource", json);
        Assert.DoesNotContain("outputsSource", json);
        Assert.DoesNotContain("designFacetsSource", json);
        Assert.DoesNotContain("rowNumber", json);
        Assert.DoesNotContain("\"definition\"", json); // navigation excluded
    }

    [Fact]
    public async Task Version_reads_use_the_compound_named_routes_and_empty_batches_do_no_io()
    {
        var version = Version("v1", "def1");
        var (store, _, bounded) = await SeededAsync([Definition("def1")], version);

        await store.FindByDefinitionAndSortKeyAsync("def1", version.SemVerSortKey);
        var first = Assert.Single(bounded.Queries);
        Assert.Equal(
            ActivitiesDesignStorageManifest.FindActivityDefinitionVersionByDefinitionAndSortKeyQuery,
            first.QueryIdentity);
        Assert.Equal(BoundedQueryResultOperation.First, first.ResultOperation);

        bounded.Queries.Clear();
        await store.ListByDefinitionIdsAsync(["def1", "def1"]);
        Assert.All(
            bounded.Queries,
            query =>
            {
                Assert.Equal(
                    ActivitiesDesignStorageManifest.ListActivityDefinitionVersionsByDefinitionQuery,
                    query.QueryIdentity);
                Assert.Equal(BoundedQueryResultOperation.Documents, query.ResultOperation);
                Assert.Equal(ActivitiesDesignStorageManifest.ActivityDefinitionVersionOrder, query.Order);
            });

        bounded.Queries.Clear();
        Assert.Empty(await store.ListByDefinitionIdsAsync([]));
        Assert.Empty(bounded.Queries);
    }
}
