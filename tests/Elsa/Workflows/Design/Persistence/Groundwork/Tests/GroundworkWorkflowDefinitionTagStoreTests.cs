using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowDefinitionTagStoreTests
{
    [Fact]
    public async Task Missing_tag_set_reads_as_empty_revision_zero()
    {
        var documents = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var store = CreateStore(documents);

        var result = await store.GetAsync("workflow-1");

        Assert.Equal("workflow-1", result.WorkflowDefinitionId);
        Assert.Equal(WorkflowDefinitionTagRevision.Initial, result.Revision);
        Assert.Empty(result.Assertions);
    }

    [Fact]
    public async Task Batch_read_uses_one_bounded_query_and_returns_empty_tag_sets_for_missing_definitions()
    {
        var (documents, store) = await CreateSeededStoreAsync();
        await store.ReplaceManualAsync(new(
            "workflow-1",
            "tenant-a",
            WorkflowDefinitionTagRevision.Initial,
            ["tag-a"],
            "author-1",
            "correlation-1"));

        var queryCountBefore = documents.DocumentQueryCount;
        var result = await store.ListByDefinitionIdsAsync(["workflow-1", "workflow-missing"]);

        Assert.Equal(1, documents.DocumentQueryCount - queryCountBefore);
        Assert.Collection(
            result.OrderBy(x => x.WorkflowDefinitionId, StringComparer.Ordinal),
            existing =>
            {
                Assert.Equal("workflow-1", existing.WorkflowDefinitionId);
                Assert.Equal(["tag-a"], existing.Assertions.Select(x => x.TagDefinitionId));
            },
            missing =>
            {
                Assert.Equal("workflow-missing", missing.WorkflowDefinitionId);
                Assert.Equal(WorkflowDefinitionTagRevision.Initial, missing.Revision);
                Assert.Empty(missing.Assertions);
            });
    }

    [Fact]
    public async Task Replace_manual_slice_commits_assertions_revision_and_audit_together()
    {
        var (documents, store) = await CreateSeededStoreAsync();

        var result = await store.ReplaceManualAsync(new(
            "workflow-1",
            "tenant-a",
            WorkflowDefinitionTagRevision.Initial,
            ["tag-b", "tag-a", "tag-a"],
            "author-1",
            "correlation-1"));

        Assert.Equal(WorkflowDefinitionTagReplaceStatus.Saved, result.Status);
        Assert.Equal("wft:00000000000000000001", result.TagSet!.Revision);
        Assert.Equal(["tag-a", "tag-b"], result.TagSet.Assertions.Select(x => x.TagDefinitionId));
        Assert.All(result.TagSet.Assertions, assertion =>
        {
            Assert.Equal(WorkflowDefinitionTagOriginKinds.Manual, assertion.OriginKind);
            Assert.Equal(WorkflowDefinitionTagOriginKinds.Manual, assertion.OriginKey);
        });

        var envelope = await documents.LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowDefinitionTagSetDocumentKind,
            "workflow-1");
        Assert.NotNull(envelope);

        var audits = await documents.QueryAsync(new DocumentQuery(
            WorkflowsDesignStorageManifest.WorkflowDefinitionTagAuditDocumentKind,
            WorkflowsDesignStorageManifest.ListAllQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                WorkflowsDesignStorageManifest.CollectionField,
                WorkflowsDesignStorageManifest.WorkflowDefinitionTagAuditCollection))]));
        var audit = Assert.Single(audits.Documents);
        Assert.Contains("\"actorId\":\"author-1\"", audit.ContentJson);
        Assert.Contains("\"correlationId\":\"correlation-1\"", audit.ContentJson);
        Assert.Contains("\"addedTagDefinitionIds\":[\"tag-a\",\"tag-b\"]", audit.ContentJson);
    }

    [Fact]
    public async Task Stale_revision_returns_conflict_without_mutation_or_audit()
    {
        var (documents, store) = await CreateSeededStoreAsync();
        var first = await store.ReplaceManualAsync(new(
            "workflow-1",
            "tenant-a",
            WorkflowDefinitionTagRevision.Initial,
            ["tag-a"],
            "author-1",
            "correlation-1"));

        var stale = await store.ReplaceManualAsync(new(
            "workflow-1",
            "tenant-a",
            WorkflowDefinitionTagRevision.Initial,
            ["tag-b"],
            "author-2",
            "correlation-2"));

        Assert.Equal(WorkflowDefinitionTagReplaceStatus.Conflict, stale.Status);
        Assert.Equal(first.TagSet!.Revision, stale.CurrentRevision);
        var current = await store.GetAsync("workflow-1");
        Assert.Equal(["tag-a"], current.Assertions.Select(x => x.TagDefinitionId));

        var audits = await documents.QueryAsync(new DocumentQuery(
            WorkflowsDesignStorageManifest.WorkflowDefinitionTagAuditDocumentKind,
            WorkflowsDesignStorageManifest.ListAllQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                WorkflowsDesignStorageManifest.CollectionField,
                WorkflowsDesignStorageManifest.WorkflowDefinitionTagAuditCollection))]));
        Assert.Single(audits.Documents);
    }

    [Fact]
    public async Task Tenant_scope_is_enforced_before_write()
    {
        var documents = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        var store = CreateStore(documents);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReplaceManualAsync(new(
            "workflow-1",
            "tenant-b",
            WorkflowDefinitionTagRevision.Initial,
            ["tag-a"],
            "author-1",
            "correlation-1")));
    }

    [Fact]
    public async Task Marker_projection_is_stable_and_delimiter_safe()
    {
        var (documents, store) = await CreateSeededStoreAsync();

        await store.ReplaceManualAsync(new(
            "workflow-1",
            "tenant-a",
            WorkflowDefinitionTagRevision.Initial,
            ["tag-b", "tag-a"],
            "author-1",
            "correlation-1"));

        var envelope = await documents.LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowDefinitionTagSetDocumentKind,
            "workflow-1");
        Assert.NotNull(envelope);
        Assert.Contains("\"markerProjection\":\"|tag-a|tag-b|\"", envelope.ContentJson);
        var definition = await documents.LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            "workflow-1");
        Assert.NotNull(definition);
        Assert.Contains("\"markerProjection\":\"|tag-a|tag-b|\"", definition.ContentJson);
    }

    [Fact]
    public async Task Replace_manual_slice_persists_controlled_value_without_putting_it_in_the_marker_projection()
    {
        var (documents, store) = await CreateSeededStoreAsync();

        var result = await store.ReplaceManualAsync(new(
            "workflow-1",
            "tenant-a",
            WorkflowDefinitionTagRevision.Initial,
            ["tag-priority"],
            "author-1",
            "correlation-1",
            ControlledValues: [new("tag-environment", "value-production")]));

        var controlled = Assert.Single(result.TagSet!.Assertions.Where(assertion => assertion.ControlledValueId is not null));
        Assert.Equal("tag-environment", controlled.TagDefinitionId);
        Assert.Equal("value-production", controlled.ControlledValueId);
        var definition = await documents.LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            "workflow-1");
        Assert.NotNull(definition);
        Assert.Contains("\"markerProjection\":\"|tag-priority|\"", definition.ContentJson);
        Assert.Contains("\"controlledProjection\":\"|tag-environment=value-production|\"", definition.ContentJson);
    }

    private static async Task<(InMemoryDocumentStore Documents, GroundworkWorkflowDefinitionTagStore Store)>
        CreateSeededStoreAsync()
    {
        var documents = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.Create());
        await documents.SaveAsync(GroundworkDocumentWriter.ToTenantScopedSaveRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            WorkflowsDesignStorageManifest.WorkflowDefinitionCollection,
            WorkflowsDesignStorageManifest.SchemaVersion,
            new WorkflowDefinition { Id = "workflow-1", Name = "Workflow 1", TenantId = "tenant-a" },
            GroundworkDesignJson.Options,
            PersistenceAccessContext.Scoped(new("tenant-a"))));
        return (documents, CreateStore(documents));
    }

    private static GroundworkWorkflowDefinitionTagStore CreateStore(IDocumentStore documents) =>
        new(
            documents,
            new AccessContextAccessor(PersistenceAccessContext.Scoped(new("tenant-a"))),
            TimeProvider.System);

    private sealed record AccessContextAccessor(PersistenceAccessContext Current) : IPersistenceAccessContextAccessor;
}
