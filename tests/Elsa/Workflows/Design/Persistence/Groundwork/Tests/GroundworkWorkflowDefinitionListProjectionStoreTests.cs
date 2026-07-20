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

public sealed class GroundworkWorkflowDefinitionListProjectionStoreTests
{
    [Fact]
    public async Task Lists_current_draft_latest_version_and_version_count_for_every_requested_definition()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        await SaveAsync(store, WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection,
            Draft("draft-old", "definition-1", 1));
        await SaveAsync(store, WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection,
            Draft("draft-current", "definition-1", 2));
        await SaveAsync(store, WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionCollection,
            Version("version-1", "definition-1", "1.0.0"));
        await SaveAsync(store, WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionCollection,
            Version("version-2", "definition-1", "2.0.0"));
        var bounded = new RecordingBoundedDocumentStore(store);

        var projections = await new GroundworkWorkflowDefinitionListProjectionStore(
                store,
                bounded,
                new FakePayloadSerializer(),
                GroundworkTestAccess.DefaultAccessContextAccessor)
            .ListByDefinitionIdsAsync(["definition-1", "definition-2", "definition-1"]);

        var populated = Assert.Single(projections, x => x.WorkflowDefinitionId == "definition-1");
        Assert.Equal("draft-current", populated.DraftId);
        Assert.Equal("version-2", populated.LatestVersionId);
        Assert.Equal("2.0.0", populated.LatestVersion);
        Assert.Equal(2, populated.VersionCount);

        var empty = Assert.Single(projections, x => x.WorkflowDefinitionId == "definition-2");
        Assert.Null(empty.DraftId);
        Assert.Null(empty.LatestVersionId);
        Assert.Null(empty.LatestVersion);
        Assert.Equal(0, empty.VersionCount);

        var queries = bounded.Queries.OrderBy(query => query.QueryIdentity, StringComparer.Ordinal).ToArray();
        Assert.Equal(2, queries.Length);
        var drafts = queries.Single(query => query.QueryIdentity == WorkflowsDesignStorageManifest.ListDraftsByDefinitionQuery);
        AssertBatchQuery(
            drafts,
            WorkflowsDesignStorageManifest.DraftDefinitionIdField,
            ["definition-1", "definition-2"],
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftOrder);
        var versions = queries.Single(query => query.QueryIdentity == WorkflowsDesignStorageManifest.ListVersionsByDefinitionQuery);
        AssertBatchQuery(
            versions,
            WorkflowsDesignStorageManifest.VersionDefinitionIdField,
            ["definition-1", "definition-2"],
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionOrder);
    }

    [Fact]
    public async Task Empty_definition_set_returns_without_provider_io()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var bounded = new RecordingBoundedDocumentStore(store);
        var projections = await new GroundworkWorkflowDefinitionListProjectionStore(
                store,
                bounded,
                new FakePayloadSerializer(),
                GroundworkTestAccess.DefaultAccessContextAccessor)
            .ListByDefinitionIdsAsync([]);

        Assert.Empty(projections);
        Assert.Empty(bounded.Queries);
    }

    private static async Task SaveAsync<TEntity>(
        IDocumentStore store,
        string documentKind,
        string collection,
        TEntity entity) where TEntity : Elsa.Primitives.Entities.Entity
    {
        var document = new GroundworkDocument<TEntity>(collection, entity);
        var content = JsonSerializer.Serialize(document, GroundworkDesignDocumentSerialization.Create(new FakePayloadSerializer()));
        await store.SaveAsync(new SaveDocumentRequest(documentKind, entity.Id, WorkflowsDesignStorageManifest.SchemaVersion, content));
    }

    private static WorkflowDefinitionDraft Draft(string id, string definitionId, int day) => new()
    {
        Id = id,
        WorkflowDefinitionId = definitionId,
        CreatedAt = DateTimeOffset.UnixEpoch.AddDays(day),
        LastModifiedAt = DateTimeOffset.UnixEpoch.AddDays(day),
        State = EmptyState()
    };

    private static WorkflowDefinitionVersion Version(string id, string definitionId, string version) =>
        new(definitionId, version) { Id = id, State = EmptyState() };

    private static WorkflowDefinitionState EmptyState() => WorkflowDefinitionState.Empty;

    private static void AssertBatchQuery(
        DocumentQuery query,
        string path,
        IReadOnlyCollection<string> values,
        IReadOnlyList<DocumentQueryOrder> order)
    {
        Assert.Equal(BoundedQueryResultOperation.Documents, query.ResultOperation);
        var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
        Assert.Equal(path, comparison.Path);
        Assert.Equal(QueryComparisonOperator.In, comparison.Operator);
        Assert.Equal(values, comparison.Values.Select(value => value!).ToArray());
        Assert.Equal(order, query.Order);
    }
}
