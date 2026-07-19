using Elsa.Primitives.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkRestructureWorkflowFoldersCommandTests
{
    [Fact]
    public async Task Rename_and_move_preserve_descendant_identity_and_definition_placement()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.CreatePhysicalized());
        var clock = new Clock();
        var access = GroundworkTestAccess.AccessContext("tenant-a");
        var folders = new GroundworkWorkflowFolderStore(store, access, clock);
        await folders.CreateAsync(Folder("parent", "Parent"));
        await folders.CreateAsync(Folder("moving", "Moving"));
        await folders.CreateAsync(Folder("child", "Child", "moving"));
        var definition = new WorkflowDefinition { Id = "definition", Name = "Keep", FolderId = "child", TenantId = "tenant-a", DeletedAt = DateTimeOffset.UnixEpoch };
        await new GroundworkSaveWorkflowDefinitionCommand(store, clock, access).Execute(definition);
        clock.Advance();

        var command = new GroundworkRestructureWorkflowFoldersCommand(store, access, clock);
        var renamed = await command.RenameAsync("moving", "Renamed");
        var moved = await command.MoveAsync("moving", "parent");

        Assert.Equal("Renamed", renamed.Name);
        Assert.Equal("RENAMED", renamed.NormalizedName);
        Assert.Equal("parent", moved.ParentFolderId);
        var details = await folders.FindWithAncestorsAsync("child");
        Assert.Equal(["parent", "moving"], details!.Ancestors.Select(folder => folder.Id));
        var persisted = await new GroundworkWorkflowDefinitionStore(store).GetAsync("definition");
        Assert.Equal("child", persisted.FolderId);
        Assert.Equal(DateTimeOffset.UnixEpoch, persisted.DeletedAt);
    }

    [Fact]
    public async Task Rejects_cycles_depth_overflow_and_non_empty_delete_without_cascading()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.CreatePhysicalized());
        var clock = new Clock();
        var access = GroundworkTestAccess.AccessContext("tenant-a");
        var folders = new GroundworkWorkflowFolderStore(store, access, clock);
        await folders.CreateAsync(Folder("root", "Root"));
        await folders.CreateAsync(Folder("child", "Child", "root"));
        var command = new GroundworkRestructureWorkflowFoldersCommand(store, access, clock);

        await Assert.ThrowsAsync<ArgumentException>(() => command.MoveAsync("root", "child"));
        await Assert.ThrowsAsync<WorkflowFolderRestructureConflictException>(() => command.DeleteEmptyAsync("root"));
        Assert.NotNull(await folders.FindWithAncestorsAsync("root"));
        Assert.NotNull(await folders.FindWithAncestorsAsync("child"));

        string? parent = null;
        for (var depth = 0; depth < WorkflowFolderNames.MaximumDepth; depth++)
        {
            var created = await folders.CreateAsync(Folder($"depth-{depth}", $"Depth {depth}", parent));
            parent = created.Id;
        }
        await folders.CreateAsync(Folder("subtree", "Subtree"));
        await folders.CreateAsync(Folder("subtree-child", "Subtree child", "subtree"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => command.MoveAsync("subtree", parent));
        Assert.Null((await folders.FindWithAncestorsAsync("subtree"))!.Folder.ParentFolderId);

        var empty = await folders.CreateAsync(Folder("empty", "Empty"));
        await command.DeleteEmptyAsync(empty.Id);
        Assert.Null(await folders.FindWithAncestorsAsync(empty.Id));
    }

    [Fact]
    public async Task Delete_counts_soft_deleted_direct_definitions_and_foreign_folders_are_hidden()
    {
        var store = new InMemoryDocumentStore(WorkflowsDesignStorageManifest.CreatePhysicalized());
        var clock = new Clock();
        var tenantA = GroundworkTestAccess.AccessContext("tenant-a");
        var tenantB = GroundworkTestAccess.AccessContext("tenant-b");
        await new GroundworkWorkflowFolderStore(store, tenantA, clock).CreateAsync(Folder("occupied", "Occupied"));
        await new GroundworkSaveWorkflowDefinitionCommand(store, clock, tenantA).Execute(new WorkflowDefinition
        {
            Id = "deleted", Name = "Deleted", FolderId = "occupied", TenantId = "tenant-a", DeletedAt = DateTimeOffset.UnixEpoch
        });
        await new GroundworkWorkflowFolderStore(store, tenantB, clock).CreateAsync(Folder("foreign", "Foreign"));
        var local = new GroundworkRestructureWorkflowFoldersCommand(store, tenantA, clock);

        await Assert.ThrowsAsync<WorkflowFolderRestructureConflictException>(() => local.DeleteEmptyAsync("occupied"));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => local.RenameAsync("foreign", "Hidden"));
    }

    private static WorkflowFolder Folder(string id, string name, string? parentId = null)
    {
        var normalized = WorkflowFolderNames.Normalize(name);
        return new WorkflowFolder { Id = id, ParentFolderId = parentId, Name = normalized.Name, NormalizedName = normalized.NormalizedName };
    }

    private sealed class Clock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public void Advance() => UtcNow = UtcNow.AddMinutes(1);
    }
}
