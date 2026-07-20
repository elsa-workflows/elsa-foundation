using System.Text.Json;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

/// <summary>
/// Proves the Groundwork (document) <see cref="GroundworkWorkflowDefinitionVersionLayoutStore"/> round-trips
/// the layout's plain-JSON <c>Records</c> and resolves a layout by its owning version — the same behaviour as
/// the relational adapter. The layout needs no payload serializer: its records are plain DTOs.
/// </summary>
public class GroundworkWorkflowDefinitionVersionLayoutStoreTests
{
    private const string SchemaVersion = WorkflowsDesignStorageManifest.SchemaVersion;

    // Plain-projection options mirroring the adapter's own (no payload delegation, navigation excluded).
    private static readonly JsonSerializerOptions Options =
        GroundworkDocumentSerialization.Create(["RowNumber", "WorkflowDefinitionVersion"]);

    private static async Task<(GroundworkWorkflowDefinitionVersionLayoutStore Store, InMemoryDocumentStore Raw, RecordingBoundedDocumentStore Bounded)> SeededAsync(
        params WorkflowDefinitionVersionLayout[] layouts)
    {
        var raw = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var bounded = new RecordingBoundedDocumentStore(raw);

        foreach (var layout in layouts)
        {
            var envelope = new GroundworkDocument<WorkflowDefinitionVersionLayout>(
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutCollection, layout);
            var content = JsonSerializer.Serialize(envelope, Options);
            await raw.SaveAsync(new SaveDocumentRequest(
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind, layout.Id, SchemaVersion, content));
        }

        return (new GroundworkWorkflowDefinitionVersionLayoutStore(raw, bounded), raw, bounded);
    }

    private static WorkflowDefinitionVersionLayout Layout(string id, string versionId, params DesignMetadataRecord[] records) =>
        new() { Id = id, WorkflowDefinitionVersionId = versionId, Records = records };

    [Fact]
    public async Task FindByVersionId_round_trips_records()
    {
        var (store, _, bounded) = await SeededAsync(
            Layout("l1", "v1", new DesignMetadataRecord("node-a", 10, 20, 100, 50)),
            Layout("l2", "v2"));

        var result = await store.FindByVersionIdAsync("v1");

        Assert.NotNull(result);
        Assert.Equal("l1", result!.Id);
        var record = Assert.Single(result.Records);
        Assert.Equal("node-a", record.NodeId);
        Assert.Equal(10, record.X);
        Assert.Equal(100, record.Width);
        Assert.Null(result.WorkflowDefinitionVersion);
        var query = Assert.Single(bounded.Queries);
        Assert.Equal(WorkflowsDesignStorageManifest.FindLayoutByVersionQuery, query.QueryIdentity);
        Assert.Equal(BoundedQueryResultOperation.First, query.ResultOperation);
        var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
        Assert.Equal(WorkflowsDesignStorageManifest.LayoutVersionIdField, comparison.Path);
        Assert.Equal(QueryComparisonOperator.Equal, comparison.Operator);
        Assert.Equal(new[] { "v1" }, comparison.Values.Select(value => value!).ToArray());
    }

    [Fact]
    public async Task FindByVersionId_returns_null_when_absent()
    {
        var (store, _, _) = await SeededAsync(Layout("l1", "v1"));
        Assert.Null(await store.FindByVersionIdAsync("other"));
    }

    [Fact]
    public async Task FindByVersionId_classifies_corrupt_json_with_document_context()
    {
        var raw = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var envelope = new DocumentEnvelope(
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind,
            "bad-layout",
            SchemaVersion,
            1,
            "{",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
        var store = new GroundworkWorkflowDefinitionVersionLayoutStore(
            raw,
            new FirstResultBoundedDocumentStore(envelope));

        var exception = await Assert.ThrowsAsync<GroundworkCorruptPayloadException>(
            () => store.FindByVersionIdAsync("bad-version"));

        Assert.Equal(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind, exception.DocumentKind);
        Assert.Equal("bad-layout", exception.DocumentId);
        Assert.Equal(typeof(WorkflowDefinitionVersionLayout), exception.EntityType);
        Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public async Task Stored_document_omits_navigation()
    {
        var (_, raw, _) = await SeededAsync(Layout("l1", "v1"));

        var json = (await raw.LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind, "l1"))!.ContentJson;

        Assert.Contains("\"records\"", json);
        Assert.DoesNotContain("rowNumber", json);
        Assert.DoesNotContain("workflowDefinitionVersion\"", json); // nav excluded (distinct from ...VersionId)
    }

    private sealed class FirstResultBoundedDocumentStore(DocumentEnvelope result) : IBoundedDocumentStore
    {
        public Task<DocumentQueryResult> QueryAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long> CountAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<DocumentEnvelope?>(result);

        public Task<bool> AnyAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
