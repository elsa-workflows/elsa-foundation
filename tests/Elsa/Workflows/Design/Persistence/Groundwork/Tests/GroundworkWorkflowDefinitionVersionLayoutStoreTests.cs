using System.Text.Json;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
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

    private static async Task<(GroundworkWorkflowDefinitionVersionLayoutStore Store, InMemoryDocumentStore Raw)> SeededAsync(
        params WorkflowDefinitionVersionLayout[] layouts)
    {
        var raw = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());

        foreach (var layout in layouts)
        {
            var envelope = new GroundworkDocument<WorkflowDefinitionVersionLayout>(
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutCollection, layout);
            var content = JsonSerializer.Serialize(envelope, Options);
            await raw.SaveAsync(new SaveDocumentRequest(
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind, layout.Id, SchemaVersion, content));
        }

        return (new GroundworkWorkflowDefinitionVersionLayoutStore(raw), raw);
    }

    private static WorkflowDefinitionVersionLayout Layout(string id, string versionId, params DesignMetadataRecord[] records) =>
        new() { Id = id, WorkflowDefinitionVersionId = versionId, Records = records };

    [Fact]
    public async Task FindByVersionId_round_trips_records()
    {
        var (store, _) = await SeededAsync(
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
    }

    [Fact]
    public async Task FindByVersionId_returns_null_when_absent()
    {
        var (store, _) = await SeededAsync(Layout("l1", "v1"));
        Assert.Null(await store.FindByVersionIdAsync("other"));
    }

    [Fact]
    public async Task Stored_document_omits_navigation()
    {
        var (_, raw) = await SeededAsync(Layout("l1", "v1"));

        var json = (await raw.LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind, "l1"))!.ContentJson;

        Assert.Contains("\"records\"", json);
        Assert.DoesNotContain("rowNumber", json);
        Assert.DoesNotContain("workflowDefinitionVersion\"", json); // nav excluded (distinct from ...VersionId)
    }
}
