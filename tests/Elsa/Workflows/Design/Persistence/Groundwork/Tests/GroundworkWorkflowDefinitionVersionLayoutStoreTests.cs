using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Query.Model;
using Groundwork.Store;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowDefinitionVersionLayoutStoreTests
{
    private static WorkflowDefinitionVersionLayout Layout(string id, string versionId, params DesignMetadataRecord[] records) =>
        new() { Id = id, WorkflowDefinitionVersionId = versionId, Records = records };

    private static (GroundworkWorkflowDefinitionVersionLayoutStore Store, DesignGroundworkTestPersistence Raw) Seeded(
        params WorkflowDefinitionVersionLayout[] layouts)
    {
        var raw = new DesignGroundworkTestPersistence();
        foreach (var layout in layouts) raw.SeedLayout(layout);
        return (new GroundworkWorkflowDefinitionVersionLayoutStore(raw, DesignGroundworkTestAccess.DefaultAccessContextAccessor), raw);
    }

    [Fact]
    public async Task FindByVersionId_round_trips_records()
    {
        var (store, raw) = Seeded(Layout("l1", "v1", new DesignMetadataRecord("node-a", 10, 20, 100, 50)), Layout("l2", "v2"));
        raw.RecordQueries = true;
        using (raw)
        {
            var result = await store.FindByVersionIdAsync("v1");
            Assert.NotNull(result);
            Assert.Equal("l1", result!.Id);
            var record = Assert.Single(result.Records);
            Assert.Equal("node-a", record.NodeId);
            Assert.Equal(10, record.X);
            Assert.Equal(20, record.Y);
            Assert.Equal(100, record.Width);
            Assert.Equal(50, record.Height);
            Assert.Null(result.WorkflowDefinitionVersion);
            var query = Assert.Single(raw.Queries);
            Assert.Equal(WorkflowsDesignStorageManifest.LayoutByVersionIndex, query.IndexName);
            Assert.Equal([WorkflowsDesignStorageManifest.IdField], query.Request.Order.Select(term => term.Column.Name));
            var predicate = Assert.IsType<Predicate.Equal>(query.Request.Where);
            Assert.Equal(WorkflowsDesignStorageManifest.LayoutVersionIdField, predicate.Column.Name);
            Assert.Equal("v1", predicate.Value.Value);
        }
    }

    [Fact]
    public async Task FindByVersionId_returns_null_when_absent()
    {
        var (store, raw) = Seeded(Layout("l1", "v1"));
        using (raw) Assert.Null(await store.FindByVersionIdAsync("other"));
    }

    [Fact]
    public async Task FindByVersionId_classifies_corrupt_json_with_document_context()
    {
        using var raw = new DesignGroundworkTestPersistence();
        raw.InsertRaw(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind,
            new StorageValues(new Dictionary<string, object?>
            {
                [WorkflowsDesignStorageManifest.IdField] = "bad-layout",
                [WorkflowsDesignStorageManifest.TenantIdField] = DesignGroundworkTestAccess.DefaultScopeValue,
                [WorkflowsDesignStorageManifest.SchemaVersionField] = WorkflowsDesignStorageManifest.SchemaVersion,
                [WorkflowsDesignStorageManifest.ContentField] = "{",
                [WorkflowsDesignStorageManifest.LayoutVersionIdField] = "bad-version",
                ["createdAt"] = DateTimeOffset.UtcNow,
                ["lastModifiedAt"] = DateTimeOffset.UtcNow
            }));
        var exception = await Assert.ThrowsAsync<GroundworkCorruptPayloadException>(() =>
            new GroundworkWorkflowDefinitionVersionLayoutStore(raw, DesignGroundworkTestAccess.DefaultAccessContextAccessor)
                .FindByVersionIdAsync("bad-version"));
        Assert.Contains("deserialized", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stored_document_omits_navigation()
    {
        var (_, raw) = Seeded(Layout("l1", "v1"));
        using (raw)
        {
            var values = Assert.Single(raw.Snapshot(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind));
            Assert.Equal("l1", values.Values[WorkflowsDesignStorageManifest.IdField]);
            Assert.Equal("v1", values.Values[WorkflowsDesignStorageManifest.LayoutVersionIdField]);
            Assert.Equal(WorkflowsDesignStorageManifest.SchemaVersion, values.Values[WorkflowsDesignStorageManifest.SchemaVersionField]);
            var json = ((System.Text.Json.JsonElement)values.Values[WorkflowsDesignStorageManifest.ContentField]!).GetRawText();
            Assert.Contains("\"records\"", json);
            Assert.DoesNotContain("rowNumber", json);
            Assert.DoesNotContain("workflowDefinitionVersion\"", json);
        }
    }
}
