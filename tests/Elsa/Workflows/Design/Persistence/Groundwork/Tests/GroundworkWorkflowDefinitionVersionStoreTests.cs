using System.Text.Json;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

/// <summary>
/// Proves the Groundwork (document) <see cref="GroundworkWorkflowDefinitionVersionStore"/> behaves like the
/// relational EF Core adapter for the first <b>rich</b> design aggregate: authored <c>State</c> survives a
/// document round-trip (delegated to the payload serializer), the EF shadow / navigation members are excluded
/// from the stored document, the owning definition is loaded via an explicit second read, and SemVer ordering
/// resolves the latest version. This is the concrete evidence that a stateful design entity runs on a document
/// database, host's choice.
/// </summary>
public class GroundworkWorkflowDefinitionVersionStoreTests
{
    private const string SchemaVersion = WorkflowsDesignStorageManifest.SchemaVersion;

    private static readonly FakePayloadSerializer Payloads = new();

    private sealed record Fixture(
        GroundworkWorkflowDefinitionVersionStore Versions,
        InMemoryDocumentStore Store);

    private static async Task<Fixture> SeededAsync(
        IEnumerable<WorkflowDefinitionVersion> versions,
        IEnumerable<WorkflowDefinition>? definitions = null)
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var richOptions = GroundworkDesignDocumentSerialization.Create(Payloads);

        foreach (var version in versions)
        {
            var envelope = new GroundworkDocument<WorkflowDefinitionVersion>(
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionCollection, version);
            var content = JsonSerializer.Serialize(envelope, richOptions);
            await store.SaveAsync(new SaveDocumentRequest(
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind, version.Id, SchemaVersion, content));
        }

        foreach (var definition in definitions ?? [])
        {
            var envelope = new GroundworkDocument<WorkflowDefinition>(
                WorkflowsDesignStorageManifest.WorkflowDefinitionCollection, definition);
            var content = JsonSerializer.Serialize(envelope, GroundworkDesignJson.Options);
            await store.SaveAsync(new SaveDocumentRequest(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind, definition.Id, SchemaVersion, content));
        }

        var definitionStore = new GroundworkWorkflowDefinitionStore(store);
        return new Fixture(new GroundworkWorkflowDefinitionVersionStore(store, definitionStore, Payloads), store);
    }

    private static WorkflowDefinitionState EmptyState() => new([], null, [], [], null, null);

    private static WorkflowDefinitionVersion Version(string id, string definitionId, string version) =>
        new(definitionId, version) { Id = id, State = EmptyState() };

    [Fact]
    public async Task FindById_round_trips_state_and_excludes_navigation()
    {
        var fixture = await SeededAsync([Version("v1", "def1", "1.0.0")]);

        var result = await fixture.Versions.FindByIdAsync("v1");

        Assert.NotNull(result);
        Assert.Equal("def1", result!.DefinitionId);
        Assert.Equal("1.0.0", result.Version);
        Assert.NotNull(result.State);          // authored state survived the document round-trip
        Assert.Empty(result.State.Variables);
        Assert.Null(result.Definition);        // navigation is not embedded in the document
    }

    [Fact]
    public async Task Stored_document_omits_persistence_artifacts()
    {
        var fixture = await SeededAsync([Version("v1", "def1", "1.0.0")]);

        var envelope = await fixture.Store.LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind, "v1");
        var json = envelope!.ContentJson;

        Assert.Contains("\"state\"", json);            // logical state is persisted directly
        Assert.DoesNotContain("stateSource", json);    // EF shadow excluded
        Assert.DoesNotContain("rowNumber", json);      // relational identity excluded
        Assert.DoesNotContain("\"definition\":", json); // navigation excluded (distinct from "definitionId")
    }

    [Fact]
    public async Task GetWithDefinition_loads_owner_via_second_read()
    {
        var fixture = await SeededAsync(
            [Version("v1", "def1", "1.0.0")],
            [new WorkflowDefinition { Id = "def1", Name = "Order Processing" }]);

        var result = await fixture.Versions.GetWithDefinitionAsync("v1");

        Assert.NotNull(result.Definition);
        Assert.Equal("Order Processing", result.Definition!.Name);
    }

    [Fact]
    public async Task GetWithDefinition_throws_when_version_absent()
    {
        var fixture = await SeededAsync([]);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Versions.GetWithDefinitionAsync("missing"));
    }

    [Fact]
    public async Task FindLatestVersion_resolves_highest_semver()
    {
        var fixture = await SeededAsync(
        [
            Version("v1", "def1", "1.0.0"),
            Version("v2", "def1", "2.0.0"),
            Version("v3", "def1", "1.5.0"),
        ]);

        var latest = await fixture.Versions.FindLatestVersionAsync("def1");

        Assert.Equal("v2", latest!.Id);
        Assert.Equal("2.0.0", latest.Version);
    }

    [Fact]
    public async Task FindLatestVersion_returns_null_for_unknown_definition()
    {
        var fixture = await SeededAsync([Version("v1", "def1", "1.0.0")]);
        Assert.Null(await fixture.Versions.FindLatestVersionAsync("other"));
    }

    [Fact]
    public async Task ListByDefinition_returns_only_that_definitions_versions()
    {
        var fixture = await SeededAsync(
        [
            Version("v1", "def1", "1.0.0"),
            Version("v2", "def1", "2.0.0"),
            Version("v3", "def2", "1.0.0"),
        ]);

        var result = await fixture.Versions.ListByDefinitionAsync("def1");

        Assert.Equal(["v1", "v2"], result.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task Exists_is_true_for_known_sort_key()
    {
        var version = Version("v1", "def1", "1.0.0");
        var fixture = await SeededAsync([version]);

        Assert.True(await fixture.Versions.ExistsAsync("def1", version.SemVerSortKey));
    }

    [Fact]
    public async Task Exists_is_false_for_unknown_sort_key()
    {
        var fixture = await SeededAsync([Version("v1", "def1", "1.0.0")]);
        Assert.False(await fixture.Versions.ExistsAsync("def1", "9999999999"));
    }

    [Fact]
    public async Task GetAsync_throws_when_absent()
    {
        var fixture = await SeededAsync([]);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => fixture.Versions.GetAsync("missing"));
    }
}
