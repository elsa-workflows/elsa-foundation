using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowFolderStoreTests
{
    [Fact]
    public async Task Creates_root_children_in_normalized_name_order_and_declares_a_non_null_unique_sibling_key()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.CreatePhysicalized());
        var folders = new GroundworkWorkflowFolderStore(store, GroundworkTestAccess.AccessContext("tenant-a"), new Clock());

        await folders.CreateAsync(Folder("b", "Beta"));
        await folders.CreateAsync(Folder("a", "alpha"));

        var page = await folders.ListDirectChildrenAsync(new WorkflowFolderPageRequest(null, 100));
        Assert.Equal(["a", "b"], page.Items.Select(folder => folder.Id));
        var unit = WorkflowsDesignStorageManifest.CreatePhysicalized().StorageUnits.Single(unit =>
            unit.Identity.Value == WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind);
        var physical = Assert.IsType<global::Groundwork.Core.PhysicalStorage.PhysicalStoragePolicy.ExplicitPolicy>(unit.PhysicalStorage!.Policy).Definition;
        Assert.Contains(physical.Indexes, index => index.LogicalName == "by-parent-and-normalized-name" && index.IsUnique);
        Assert.Equal(
            WorkflowFolderNames.MaximumIdentifierLength,
            physical.ProjectedColumns.Single(column => column.LogicalName == "parent_key").Length);
        Assert.Equal(
            WorkflowFolderNames.MaximumNameLength,
            physical.ProjectedColumns.Single(column => column.LogicalName == "normalized_name").Length);
        Assert.Equal(
            WorkflowFolderNames.MaximumIdentifierLength,
            physical.ProjectedColumns.Single(column => column.LogicalName == "folder_id").Length);
    }

    [Fact]
    public async Task Rejects_a_seventeenth_level_and_unknown_parent_without_writing_a_folder()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.CreatePhysicalized());
        var folders = new GroundworkWorkflowFolderStore(store, GroundworkTestAccess.AccessContext("tenant-a"), new Clock());
        string? parent = null;
        for (var depth = 0; depth < WorkflowFolderNames.MaximumDepth; depth++)
        {
            var created = await folders.CreateAsync(Folder($"f-{depth}", $"Folder {depth}", parent));
            parent = created.Id;
        }

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => folders.CreateAsync(Folder("too-deep", "Too deep", parent)));
        await Assert.ThrowsAsync<Elsa.Primitives.Exceptions.EntityNotFoundException>(() => folders.CreateAsync(Folder("missing", "Missing", "nope")));
    }

    [Fact]
    public async Task Rejects_names_and_identifiers_that_cannot_be_represented_by_the_physical_contract()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.CreatePhysicalized());
        var folders = new GroundworkWorkflowFolderStore(store, GroundworkTestAccess.AccessContext("tenant-a"), new Clock());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorkflowFolderNames.Normalize(new string('n', WorkflowFolderNames.MaximumNameLength + 1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            folders.CreateAsync(Folder(new string('i', WorkflowFolderNames.MaximumIdentifierLength + 1), "Folder")));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            folders.CreateAsync(Folder("child", "Child", new string('p', WorkflowFolderNames.MaximumIdentifierLength + 1))));
        Assert.Empty(store.Snapshot(WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind));
    }

    [Fact]
    public async Task Batch_details_load_each_requested_folder_and_shared_ancestor_once()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.CreatePhysicalized());
        IWorkflowFolderStore folders = new GroundworkWorkflowFolderStore(
            store,
            GroundworkTestAccess.AccessContext("tenant-a"),
            new Clock());
        await folders.CreateAsync(Folder("root", "Root"));
        await folders.CreateAsync(Folder("a", "A", "root"));
        await folders.CreateAsync(Folder("b", "B", "root"));
        var readsBeforeBatch = store.LoadCount;

        var details = await folders.FindManyWithAncestorsAsync(["a", "a", "b"]);

        Assert.Equal(["root"], details["a"].Ancestors.Select(folder => folder.Id));
        Assert.Equal(["root"], details["b"].Ancestors.Select(folder => folder.Id));
        Assert.Equal(3, store.LoadCount - readsBeforeBatch);
    }

    [Fact]
    public async Task Folder_details_hide_documents_from_a_foreign_ambient_tenant()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.CreatePhysicalized());
        var tenantAFolders = new GroundworkWorkflowFolderStore(
            store,
            GroundworkTestAccess.AccessContext("tenant-a"),
            new Clock());
        await tenantAFolders.CreateAsync(Folder("folder", "Folder"));
        IWorkflowFolderStore tenantBFolders = new GroundworkWorkflowFolderStore(
            store,
            GroundworkTestAccess.AccessContext("tenant-b"),
            new Clock());

        Assert.Null(await tenantBFolders.FindWithAncestorsAsync("folder"));
        Assert.Empty(await tenantBFolders.FindManyWithAncestorsAsync(["folder"]));
    }

    private static WorkflowFolder Folder(string id, string name, string? parentId = null)
    {
        var normalized = WorkflowFolderNames.Normalize(name);
        return new WorkflowFolder { Id = id, ParentFolderId = parentId, Name = normalized.Name, NormalizedName = normalized.NormalizedName };
    }

    private sealed class Clock : ISystemClock { public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch; }
}
