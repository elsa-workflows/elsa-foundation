using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Query.Model;
using Groundwork.Store;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowDefinitionListProjectionStoreTests
{
    private static WorkflowDefinitionState EmptyState() => WorkflowDefinitionState.Empty;

    [Fact]
    public async Task Lists_current_draft_latest_version_and_version_count_for_every_requested_definition()
    {
        using var raw = new DesignGroundworkTestPersistence();
        raw.RecordQueries = true;
        raw.SeedDraft(Draft("draft-old", "definition-1", 1));
        raw.SeedDraft(Draft("draft-current", "definition-1", 2));
        raw.SeedVersion(Version("version-1", "definition-1", "1.0.0"));
        raw.SeedVersion(Version("version-2", "definition-1", "2.0.0"));
        var projections = await new GroundworkWorkflowDefinitionListProjectionStore(
            raw,
            new FakePayloadSerializer(),
            DesignGroundworkTestAccess.DefaultAccessContextAccessor)
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

        Assert.Equal(2, raw.Queries.Count);
        AssertBatchQuery(
            raw.Queries.Single(query => query.IndexName == WorkflowsDesignStorageManifest.DraftByDefinitionIndex),
            WorkflowsDesignStorageManifest.DraftByDefinitionIndex,
            WorkflowsDesignStorageManifest.DraftDefinitionIdLookupHashField,
            ["definition-1", "definition-2"],
            [
                WorkflowsDesignStorageManifest.DraftDefinitionIdLookupHashField,
                WorkflowsDesignStorageManifest.DraftLastModifiedAtField,
                WorkflowsDesignStorageManifest.DraftCreatedAtField,
                WorkflowsDesignStorageManifest.DraftIdField
            ]);
        AssertBatchQuery(
            raw.Queries.Single(query => query.IndexName == WorkflowsDesignStorageManifest.VersionByDefinitionIndex),
            WorkflowsDesignStorageManifest.VersionByDefinitionIndex,
            WorkflowsDesignStorageManifest.VersionDefinitionIdLookupHashField,
            ["definition-1", "definition-2"],
            [
                WorkflowsDesignStorageManifest.VersionDefinitionIdLookupHashField,
                WorkflowsDesignStorageManifest.VersionSemVerSortKeyField,
                WorkflowsDesignStorageManifest.VersionIdField
            ]);
    }

    [Fact]
    public async Task Oversized_definition_sets_are_partitioned_into_deterministic_bounded_batches()
    {
        using var raw = new DesignGroundworkTestPersistence();
        raw.RecordQueries = true;
        var requested = Enumerable.Range(0, 450).Select(index => $"definition-{index:D3}").Reverse().ToList();
        requested.AddRange(requested.Take(10));
        var rows = await new GroundworkWorkflowDefinitionListProjectionStore(
            raw,
            new FakePayloadSerializer(),
            DesignGroundworkTestAccess.DefaultAccessContextAccessor)
            .ListByDefinitionIdsAsync(requested);
        Assert.Equal(450, rows.Count);
        Assert.Equal(requested.Distinct(StringComparer.Ordinal), rows.Select(row => row.WorkflowDefinitionId));
        Assert.Equal(58, raw.Queries.Count);
        Assert.All(raw.Queries, query => Assert.Contains(
            query.IndexName,
            new[] { WorkflowsDesignStorageManifest.DraftByDefinitionIndex, WorkflowsDesignStorageManifest.VersionByDefinitionIndex }));
        foreach (var index in new[]
                 {
                     WorkflowsDesignStorageManifest.DraftByDefinitionIndex,
                     WorkflowsDesignStorageManifest.VersionByDefinitionIndex
                 })
        {
            var batches = raw.Queries
                .Where(query => query.IndexName == index)
                .Select(query => Assert.IsType<Predicate.In>(query.Request.Where).Values.Select(value => value.Value?.ToString() ?? string.Empty).ToArray())
                .ToArray();
            Assert.Equal(29, batches.Length);
            Assert.All(batches.Take(28), batch => Assert.Equal(16, batch.Length));
            Assert.Equal(2, batches[^1].Length);
            var expected = requested.Distinct(StringComparer.Ordinal)
                .Select(LookupHash)
                .Chunk(16)
                .Select(batch => batch.Order(StringComparer.Ordinal).ToArray())
                .SelectMany(batch => batch)
                .ToArray();
            Assert.Equal(expected, batches.SelectMany(batch => batch));
        }
    }

    [Fact]
    public async Task Empty_definition_set_returns_without_provider_io()
    {
        using var raw = new DesignGroundworkTestPersistence();
        var rows = await new GroundworkWorkflowDefinitionListProjectionStore(
            raw,
            new FakePayloadSerializer(),
            DesignGroundworkTestAccess.DefaultAccessContextAccessor)
            .ListByDefinitionIdsAsync([]);
        Assert.Empty(rows);
        Assert.Equal(0, raw.LoadCount);
        Assert.Empty(raw.Queries);
    }

    [Fact]
    public async Task Groups_projection_rows_by_folded_definition_identity()
    {
        using var raw = new DesignGroundworkTestPersistence();
        raw.SeedDraft(Draft("draft", "Stored-Definition", 1));
        raw.SeedVersion(Version("version", "STORED-DEFINITION", "1.0.0"));

        var rows = await new GroundworkWorkflowDefinitionListProjectionStore(
            raw,
            new FakePayloadSerializer(),
            DesignGroundworkTestAccess.DefaultAccessContextAccessor)
            .ListByDefinitionIdsAsync(["stored-definition", "STORED-DEFINITION"]);

        var row = Assert.Single(rows);
        Assert.Equal("stored-definition", row.WorkflowDefinitionId);
        Assert.Equal("draft", row.DraftId);
        Assert.Equal("version", row.LatestVersionId);
        Assert.Equal(1, row.VersionCount);
    }

    [Fact]
    public async Task List_projection_rejects_a_stale_draft_relationship_hash()
    {
        using var raw = new DesignGroundworkTestPersistence();
        var draft = Draft("draft", "actual-definition", 1);
        var options = GroundworkDesignDocumentSerialization.Create(new FakePayloadSerializer());
        var values = GroundworkDesignStorage.Values(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
            draft,
            options,
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection);
        var row = values.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        row[WorkflowsDesignStorageManifest.DraftDefinitionIdLookupHashField] = LookupHash("requested-definition");
        raw.InsertRaw(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
            new StorageValues(row));

        var store = new GroundworkWorkflowDefinitionListProjectionStore(
            raw,
            new FakePayloadSerializer(),
            DesignGroundworkTestAccess.DefaultAccessContextAccessor);

        await Assert.ThrowsAsync<GroundworkQueryReadinessException>(() =>
            store.ListByDefinitionIdsAsync(["requested-definition"]));
    }

    [Fact]
    public async Task List_projection_rejects_a_stale_version_relationship_hash()
    {
        using var raw = new DesignGroundworkTestPersistence();
        var version = Version("version", "actual-definition", "1.0.0");
        var options = GroundworkDesignDocumentSerialization.Create(new FakePayloadSerializer());
        var values = GroundworkDesignStorage.Values(
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
            version,
            options,
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionCollection);
        var row = values.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        row[WorkflowsDesignStorageManifest.VersionDefinitionIdLookupHashField] = LookupHash("requested-definition");
        raw.InsertRaw(
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind,
            new StorageValues(row));

        var store = new GroundworkWorkflowDefinitionListProjectionStore(
            raw,
            new FakePayloadSerializer(),
            DesignGroundworkTestAccess.DefaultAccessContextAccessor);

        await Assert.ThrowsAsync<GroundworkQueryReadinessException>(() =>
            store.ListByDefinitionIdsAsync(["requested-definition"]));
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

    private static void AssertBatchQuery(
        DesignGroundworkTestPersistence.RecordedQuery query,
        string index,
        string predicateColumn,
        IReadOnlyList<string> values,
        IReadOnlyList<string> order)
    {
        Assert.Equal(index, query.IndexName);
        var predicate = Assert.IsType<Predicate.In>(query.Request.Where);
        Assert.Equal(predicateColumn, predicate.Column.Name);
        Assert.Equal(
            values.Select(LookupHash).Order(StringComparer.Ordinal),
            predicate.Values.Select(value => value.Value?.ToString() ?? string.Empty));
        Assert.Equal(order, query.Request.Order.Select(term => term.Column.Name));
    }

    private static string LookupHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(QuerySearchKeys.Encode(value, QuerySearchKeyPolicy.UnicodeOrdinalIgnoreCase)))).ToLowerInvariant();
}
