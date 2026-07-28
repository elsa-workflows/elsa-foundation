using System.Text.Json;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
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

    private static async Task<(GroundworkWorkflowDefinitionDraftStore Store, InMemoryDocumentStore Raw, RecordingBoundedDocumentStore Bounded)> SeededAsync(
        params WorkflowDefinitionDraft[] drafts)
    {
        var raw = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var bounded = new RecordingBoundedDocumentStore(raw);
        var options = GroundworkDesignDocumentSerialization.Create(Payloads);

        foreach (var draft in drafts)
        {
            var envelope = new GroundworkDocument<WorkflowDefinitionDraft>(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection, draft);
            var content = JsonSerializer.Serialize(envelope, options);
            await raw.SaveAsync(new SaveDocumentRequest(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, draft.Id, SchemaVersion, content));
        }

        return (new GroundworkWorkflowDefinitionDraftStore(
            raw,
            bounded,
            Payloads,
            GroundworkTestAccess.DefaultAccessContextAccessor), raw, bounded);
    }

    private static WorkflowDefinitionDraft Draft(string id, string definitionId, DateTimeOffset? timestamp = null) =>
        new()
        {
            Id = id,
            WorkflowDefinitionId = definitionId,
            CreatedAt = timestamp ?? default,
            LastModifiedAt = timestamp ?? default,
            State = new WorkflowDefinitionState([], null, [], [], null),
        };

    [Fact]
    public async Task FindByWorkflowDefinitionId_round_trips_state()
    {
        var (store, _, bounded) = await SeededAsync(Draft("d1", "def1"), Draft("d2", "def2"));

        var result = await store.FindByWorkflowDefinitionIdAsync("def1");

        Assert.NotNull(result);
        Assert.Equal("d1", result!.Id);
        Assert.NotNull(result.State);
        Assert.Null(result.WorkflowDefinition);
        var query = Assert.Single(bounded.Queries);
        Assert.Equal(WorkflowsDesignStorageManifest.FindCurrentDraftByDefinitionQuery, query.QueryIdentity);
        Assert.Equal(BoundedQueryResultOperation.First, query.ResultOperation);
        AssertComparison(query, WorkflowsDesignStorageManifest.DraftDefinitionIdField, QueryComparisonOperator.Equal, ["def1"]);
        Assert.Equal(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftOrder,
            query.Order);
    }

    [Fact]
    public async Task FindByWorkflowDefinitionId_returns_null_when_absent()
    {
        var (store, _, _) = await SeededAsync(Draft("d1", "def1"));
        Assert.Null(await store.FindByWorkflowDefinitionIdAsync("other"));
    }

    [Fact]
    public async Task ListByWorkflowDefinitionId_uses_the_declared_bounded_route()
    {
        var (store, _, bounded) = await SeededAsync(Draft("d1", "def1"), Draft("d2", "def2"));

        var result = await store.ListByWorkflowDefinitionIdAsync("def1");

        Assert.Equal(["d1"], result.Select(x => x.Id));
        var query = Assert.Single(bounded.Queries);
        Assert.Equal(WorkflowsDesignStorageManifest.ListDraftsByDefinitionQuery, query.QueryIdentity);
        Assert.Equal(BoundedQueryResultOperation.Documents, query.ResultOperation);
        AssertComparison(query, WorkflowsDesignStorageManifest.DraftDefinitionIdField, QueryComparisonOperator.Equal, ["def1"]);
    }

    [Fact]
    public async Task Current_draft_uses_the_declared_identity_tie_break()
    {
        var timestamp = DateTimeOffset.UnixEpoch.AddDays(1);
        var first = Draft("draft-a", "def1", timestamp);
        var second = Draft("draft-b", "def1", timestamp);
        var (store, _, _) = await SeededAsync(first, second);

        var result = await store.FindByWorkflowDefinitionIdAsync("def1");

        Assert.Equal("draft-b", result?.Id);
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

        var store = new GroundworkWorkflowDefinitionDraftStore(
            raw,
            Payloads,
            GroundworkTestAccess.DefaultAccessContextAccessor);
        var draftWithLayout = await store.FindWithLayoutByIdAsync("d1");

        Assert.NotNull(draftWithLayout);
        Assert.Equal("def1", draftWithLayout!.Draft.WorkflowDefinitionId);
        Assert.Equal(layout.Single(), draftWithLayout.Layout.Single());
    }

    [Fact]
    public async Task Stored_document_omits_persistence_artifacts()
    {
        var (_, raw, _) = await SeededAsync(Draft("d1", "def1"));

        var json = (await raw.LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, "d1"))!.ContentJson;

        Assert.Contains("\"state\"", json);
        Assert.DoesNotContain("stateSource", json);
        Assert.DoesNotContain("rowNumber", json);
        Assert.DoesNotContain("workflowDefinition\"", json); // navigation excluded (distinct from workflowDefinitionId)
    }

    private static void AssertComparison(
        DocumentQuery query,
        string path,
        QueryComparisonOperator operation,
        IReadOnlyCollection<string> values)
    {
        var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
        Assert.Equal(path, comparison.Path);
        Assert.Equal(operation, comparison.Operator);
        Assert.Equal(values, comparison.Values.Select(value => value!).ToArray());
    }
}
