using System.Text.Json;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Groundwork.Query.Model;
using Groundwork.Store;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowDefinitionDraftStoreTests
{
    private static readonly FakePayloadSerializer Payloads = new();

    private static WorkflowDefinitionDraft Draft(string id, string definitionId, DateTimeOffset? timestamp = null) => new()
    {
        Id = id,
        WorkflowDefinitionId = definitionId,
        CreatedAt = timestamp ?? default,
        LastModifiedAt = timestamp ?? default,
        State = new WorkflowDefinitionState([], null, [], [], null)
    };

    private static (GroundworkWorkflowDefinitionDraftStore Store, DesignGroundworkTestPersistence Raw) Seeded(
        params (WorkflowDefinitionDraft Draft, IReadOnlyCollection<DesignMetadataRecord>? Layout, IReadOnlyCollection<ActivityPresentationRecord>? Presentation)[] values)
    {
        var raw = new DesignGroundworkTestPersistence();
        raw.RecordQueries = true;
        foreach (var value in values)
            raw.SeedDraft(value.Draft, value.Layout, value.Presentation);
        return (new GroundworkWorkflowDefinitionDraftStore(
            new GroundworkDesignStorage(raw, DesignGroundworkTestAccess.DefaultAccessContextAccessor),
            Payloads,
            DesignGroundworkTestAccess.DefaultAccessContextAccessor), raw);
    }

    [Fact]
    public async Task FindByWorkflowDefinitionId_round_trips_state()
    {
        var (store, raw) = Seeded((Draft("d1", "def1"), null, null), (Draft("d2", "def2"), null, null));
        using (raw)
        {
            var result = await store.FindByWorkflowDefinitionIdAsync("def1");
            Assert.NotNull(result);
            Assert.Equal("d1", result!.Id);
            Assert.NotNull(result.State);
            Assert.Null(result.WorkflowDefinition);
            var query = Assert.Single(raw.Queries);
            Assert.Equal(WorkflowsDesignStorageManifest.DraftByDefinitionIndex, query.IndexName);
            Assert.Equal(
                [
                    WorkflowsDesignStorageManifest.DraftLastModifiedAtField,
                    WorkflowsDesignStorageManifest.DraftCreatedAtField,
                    WorkflowsDesignStorageManifest.DraftIdField
                ],
                query.Request.Order.Select(term => term.Column.Name));
            var predicate = Assert.IsType<Predicate.Equal>(query.Request.Where);
            Assert.Equal(WorkflowsDesignStorageManifest.DraftDefinitionIdField, predicate.Column.Name);
            Assert.Equal("def1", predicate.Value.Value);
        }
    }

    [Fact]
    public async Task FindByWorkflowDefinitionId_returns_null_when_absent()
    {
        var (store, raw) = Seeded((Draft("d1", "def1"), null, null));
        using (raw) Assert.Null(await store.FindByWorkflowDefinitionIdAsync("other"));
    }

    [Fact]
    public async Task ListByWorkflowDefinitionId_uses_the_declared_bounded_route()
    {
        var (store, raw) = Seeded((Draft("d1", "def1"), null, null), (Draft("d2", "def2"), null, null));
        using (raw)
        {
            Assert.Equal(["d1"], (await store.ListByWorkflowDefinitionIdAsync("def1")).Select(x => x.Id));
            var query = Assert.Single(raw.Queries);
            Assert.Equal(WorkflowsDesignStorageManifest.DraftByDefinitionIndex, query.IndexName);
            Assert.Equal(
                [
                    WorkflowsDesignStorageManifest.DraftDefinitionIdField,
                    WorkflowsDesignStorageManifest.DraftLastModifiedAtField,
                    WorkflowsDesignStorageManifest.DraftCreatedAtField,
                    WorkflowsDesignStorageManifest.DraftIdField
                ],
                query.Request.Order.Select(term => term.Column.Name));
            var predicate = Assert.IsType<Predicate.Equal>(query.Request.Where);
            Assert.Equal(WorkflowsDesignStorageManifest.DraftDefinitionIdField, predicate.Column.Name);
            Assert.Equal("def1", predicate.Value.Value);
        }
    }

    [Fact]
    public async Task Current_draft_uses_the_declared_identity_tie_break()
    {
        var timestamp = DateTimeOffset.UnixEpoch.AddDays(1);
        var (store, raw) = Seeded((Draft("draft-a", "def1", timestamp), null, null), (Draft("draft-b", "def1", timestamp), null, null));
        using (raw) Assert.Equal("draft-b", (await store.FindByWorkflowDefinitionIdAsync("def1"))?.Id);
    }

    [Fact]
    public async Task Legacy_document_with_extra_Errors_property_deserializes_via_the_primary_path_and_retains_layout()
    {
        var layout = new[] { new DesignMetadataRecord("root", 1, 2, 3, 4) };
        var raw = new DesignGroundworkTestPersistence();
        var draft = Draft("d1", "def1");
        var options = GroundworkDesignDocumentSerialization.Create(Payloads);
        var content = JsonSerializer.SerializeToElement(new
        {
            collection = WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection,
            entity = draft,
            layout,
            errors = new[] { new { path = "$workflow", type = "Legacy/Error", message = "stale" } }
        }, options);
        var values = GroundworkDesignStorage.Values(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
            draft,
            options,
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection,
            layout);
        var row = values.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        row[WorkflowsDesignStorageManifest.ContentField] = content;
        raw.InsertRaw(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, new StorageValues(row));
        var store = new GroundworkWorkflowDefinitionDraftStore(
            new GroundworkDesignStorage(raw, DesignGroundworkTestAccess.DefaultAccessContextAccessor),
            Payloads,
            DesignGroundworkTestAccess.DefaultAccessContextAccessor);
        using (raw)
        {
            Assert.Equal(layout, await store.FindLayoutByDraftIdAsync("d1"));
        }
    }

    [Fact]
    public async Task FindWithLayout_returns_the_single_rich_document_projection()
    {
        var layout = new[] { new DesignMetadataRecord("root", 1, 2, 3, 4) };
        var (store, raw) = Seeded((Draft("d1", "def1"), layout, null));
        using (raw)
        {
            var result = await store.FindWithLayoutByIdAsync("d1");
            Assert.NotNull(result);
            Assert.Equal("d1", result!.Draft.Id);
            Assert.Equal(layout, result.Layout);
        }
    }

    [Fact]
    public void Stored_document_omits_persistence_artifacts()
    {
        var (_, raw) = Seeded((Draft("d1", "def1"), null, null));
        using (raw)
        {
            var values = Assert.Single(raw.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind));
            var json = ((JsonElement)values.Values[WorkflowsDesignStorageManifest.ContentField]!).GetRawText();
            Assert.Contains("\"state\"", json);
            Assert.DoesNotContain("stateSource", json);
            Assert.DoesNotContain("rowNumber", json);
            Assert.DoesNotContain("workflowDefinition\"", json);
        }
    }
}
