using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Query.Model;
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
            WorkflowsDesignStorageManifest.DraftDefinitionIdField,
            ["definition-1", "definition-2"],
            [
                WorkflowsDesignStorageManifest.DraftDefinitionIdField,
                WorkflowsDesignStorageManifest.DraftLastModifiedAtField,
                WorkflowsDesignStorageManifest.DraftCreatedAtField,
                WorkflowsDesignStorageManifest.DraftIdField
            ]);
        AssertBatchQuery(
            raw.Queries.Single(query => query.IndexName == WorkflowsDesignStorageManifest.VersionByDefinitionIndex),
            WorkflowsDesignStorageManifest.VersionByDefinitionIndex,
            WorkflowsDesignStorageManifest.VersionDefinitionIdField,
            ["definition-1", "definition-2"],
            [
                WorkflowsDesignStorageManifest.VersionDefinitionIdField,
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
        Assert.Equal(6, raw.Queries.Count);
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
            Assert.Equal(3, batches.Length);
            Assert.Equal([200, 200, 50], batches.Select(batch => batch.Length));
            Assert.Equal("definition-000", batches.SelectMany(batch => batch).Order(StringComparer.Ordinal).First());
            Assert.Equal("definition-449", batches.SelectMany(batch => batch).Order(StringComparer.Ordinal).Last());
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
        Assert.Equal(values, predicate.Values.Select(value => value.Value?.ToString() ?? string.Empty).ToArray());
        Assert.Equal(order, query.Request.Order.Select(term => term.Column.Name));
    }
}
