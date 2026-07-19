using Elsa.Persistence.Core;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
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

    private static WorkflowFolder Folder(string id, string name, string? parentId = null)
    {
        var normalized = WorkflowFolderNames.Normalize(name);
        return new WorkflowFolder { Id = id, ParentFolderId = parentId, Name = normalized.Name, NormalizedName = normalized.NormalizedName };
    }

    private sealed class Clock : ISystemClock { public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch; }
}
