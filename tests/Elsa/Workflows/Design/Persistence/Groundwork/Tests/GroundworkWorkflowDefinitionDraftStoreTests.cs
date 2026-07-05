using System.Text.Json;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

/// <summary>
/// Proves the Groundwork (document) <see cref="GroundworkWorkflowDefinitionDraftStore"/> round-trips the rich
/// draft (authored <c>State</c> via the payload serializer, navigation/shadow members excluded) and resolves a
/// draft by its owning definition — the same behaviour as the relational adapter.
/// </summary>
public class GroundworkWorkflowDefinitionDraftStoreTests
{
    private const string SchemaVersion = WorkflowsDesignStorageManifest.SchemaVersion;
    private static readonly FakePayloadSerializer Payloads = new();

    private static async Task<(GroundworkWorkflowDefinitionDraftStore Store, InMemoryDocumentStore Raw)> SeededAsync(
        params WorkflowDefinitionDraft[] drafts)
    {
        var raw = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var options = GroundworkDesignDocumentSerialization.Create(Payloads);

        foreach (var draft in drafts)
        {
            var envelope = new GroundworkDocument<WorkflowDefinitionDraft>(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection, draft);
            var content = JsonSerializer.Serialize(envelope, options);
            await raw.SaveAsync(new SaveDocumentRequest(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, draft.Id, SchemaVersion, content));
        }

        return (new GroundworkWorkflowDefinitionDraftStore(raw, Payloads), raw);
    }

    private static WorkflowDefinitionDraft Draft(string id, string definitionId) =>
        new()
        {
            Id = id,
            WorkflowDefinitionId = definitionId,
            State = new WorkflowDefinitionState([], null, [], [], null, null),
        };

    [Fact]
    public async Task FindByWorkflowDefinitionId_round_trips_state()
    {
        var (store, _) = await SeededAsync(Draft("d1", "def1"), Draft("d2", "def2"));

        var result = await store.FindByWorkflowDefinitionIdAsync("def1");

        Assert.NotNull(result);
        Assert.Equal("d1", result!.Id);
        Assert.NotNull(result.State);
        Assert.Null(result.WorkflowDefinition);
    }

    [Fact]
    public async Task FindByWorkflowDefinitionId_returns_null_when_absent()
    {
        var (store, _) = await SeededAsync(Draft("d1", "def1"));
        Assert.Null(await store.FindByWorkflowDefinitionIdAsync("other"));
    }

    [Fact]
    public async Task Legacy_document_with_extra_Errors_property_deserializes_via_the_primary_path_and_retains_layout()
    {
        // Documents written before validation errors became derived state may still carry an
        // "errors" property. The primary-path deserializer must accept the unknown member (STJ's
        // default unmapped-member handling ignores it) rather than failing over to the legacy path
        // and losing the Layout — this pins the behaviour the draft-document code comment relies on.
        var raw = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var options = GroundworkDesignDocumentSerialization.Create(Payloads);

        var layout = new[] { new DesignMetadataRecord("root", 1, 2, 3, 4) };
        // Primary-shape document (collection/entity/layout) plus an extra top-level "errors" member,
        // serialized through the same options the store uses so the entity/State project correctly.
        var content = JsonSerializer.Serialize(new
        {
            collection = WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection,
            entity = Draft("d1", "def1"),
            layout,
            errors = new[] { new { path = "$workflow", type = "Legacy/Error", message = "stale" } },
        }, options);
        await raw.SaveAsync(new SaveDocumentRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, "d1", SchemaVersion, content));

        var store = new GroundworkWorkflowDefinitionDraftStore(raw, Payloads);
        var draft = await store.FindByIdAsync("d1");
        var readLayout = await store.FindLayoutByDraftIdAsync("d1");

        Assert.NotNull(draft);
        Assert.Equal("def1", draft!.WorkflowDefinitionId);
        Assert.Equal(layout.Single(), readLayout.Single());
    }

    [Fact]
    public async Task Stored_document_omits_persistence_artifacts()
    {
        var (_, raw) = await SeededAsync(Draft("d1", "def1"));

        var json = (await raw.LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, "d1"))!.ContentJson;

        Assert.Contains("\"state\"", json);
        Assert.DoesNotContain("stateSource", json);
        Assert.DoesNotContain("rowNumber", json);
        Assert.DoesNotContain("workflowDefinition\"", json); // navigation excluded (distinct from workflowDefinitionId)
    }
}
