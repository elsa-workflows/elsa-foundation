using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfWorkflowDesignPersistenceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Folder_deletion_preserves_hierarchy_and_recoverable_workflows_after_restart(bool nested, bool deleteWorkflows)
    {
        await using var database = new TemporarySqliteDatabase("folder-deletion");
        string? parentId; string childId; string grandchildId; string folderId;
        var deletedAt = DateTimeOffset.UnixEpoch;
        await using (var host = LibraryHost(database.ConnectionString))
        {
            using var scope = host.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.EnsureCreatedAsync();
            var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            var deletion = scope.ServiceProvider.GetRequiredService<IWorkflowFolderDeletionStore>();
            parentId = nested ? (await library.CreateFolderAsync("Parent", null, "alice")).Id : null;
            folderId = (await library.CreateFolderAsync("Demo", parentId, "alice")).Id;
            childId = (await library.CreateFolderAsync("Child", folderId, "alice")).Id;
            grandchildId = (await library.CreateFolderAsync("Grandchild", childId, "alice")).Id;
            db.Definitions.AddRange(new WorkflowDefinition { Id = "one", Name = "One", TenantId = "tenant-a" },
                new WorkflowDefinition { Id = "two", Name = "Two", TenantId = "tenant-a" },
                new WorkflowDefinition { Id = "already-deleted", Name = "Deleted", TenantId = "tenant-a" },
                new WorkflowDefinition { Id = "in-child", Name = "Child workflow", TenantId = "tenant-a" });
            await db.SaveChangesAsync();
            db.Drafts.Add(new WorkflowDefinitionDraft { Id = "draft-one", WorkflowDefinitionId = "one", TenantId = "tenant-a", State = WorkflowDefinitionState.Empty });
            db.Versions.Add(new WorkflowDefinitionVersion("one", "1.0.0") { Id = "version-one", TenantId = "tenant-a", State = WorkflowDefinitionState.Empty });
            await db.SaveChangesAsync();
            await library.MoveDefinitionsAsync(["one", "two", "already-deleted"], folderId, "alice", "place-direct");
            await library.MoveDefinitionsAsync(["in-child"], grandchildId, "alice", "place-child");
            var existing = await db.Definitions.SingleAsync(row => row.Id == "already-deleted");
            existing.DeletedAt = deletedAt; existing.DeletedReason = "Original reason"; await db.SaveChangesAsync();
            var tag = await library.CreateTagAsync(new("Important"), "alice");
            var organization = (await library.GetOrganizationAsync("one"))!;
            await library.ReplaceTagsAsync("one", organization.Revision, [new(tag.Id)], "alice", "tag");
            await library.SetFavoriteAsync("one", true, "alice");
            var preview = await deletion.PreviewFolderDeletionAsync(folderId);
            Assert.Equal(2, preview.Workflows.Count(row => !row.Deleted));
            Assert.Single(preview.Workflows.Where(row => row.Deleted));
            Assert.Equal(2, preview.Descendants.Count);
            await deletion.DeleteFolderAsync(folderId, preview.Revision, deleteWorkflows, "alice", "delete");
            Assert.Contains(await db.LibraryAudit.AsNoTracking().ToArrayAsync(), row => row.Operation == "folder.delete" && row.ActorId == "alice");
        }
        await using (var restart = LibraryHost(database.ConnectionString))
        {
            using var scope = restart.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
            var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>();
            Assert.Null(await library.GetFolderAsync(folderId));
            Assert.Equal(parentId, (await library.GetFolderAsync(childId))!.Folder.ParentId);
            Assert.Equal(childId, (await library.GetFolderAsync(grandchildId))!.Folder.ParentId);
            Assert.Equal(nested ? new[] { "Parent", "Child", "Grandchild" } : new[] { "Child", "Grandchild" },
                (await library.GetOrganizationAsync("in-child"))!.FolderBreadcrumb.Select(row => row.Name));
            Assert.Equal(4, await db.Definitions.CountAsync());
            Assert.Equal("draft-one", (await db.Drafts.SingleAsync()).Id);
            Assert.Equal("version-one", (await db.Versions.SingleAsync()).Id);
            Assert.Equal("1.0.0", (await db.Versions.SingleAsync()).Version);
            foreach (var id in new[] { "one", "two" })
            {
                Assert.Equal(parentId, (await library.GetOrganizationAsync(id))!.FolderId);
                Assert.Equal(deleteWorkflows, (await db.Definitions.SingleAsync(row => row.Id == id)).DeletedAt is not null);
            }
            Assert.Null((await db.Definitions.SingleAsync(row => row.Id == "in-child")).DeletedAt);
            var existing = await db.Definitions.SingleAsync(row => row.Id == "already-deleted");
            Assert.Equal(deletedAt, existing.DeletedAt); Assert.Equal("Original reason", existing.DeletedReason);
            Assert.Equal(parentId, (await library.GetOrganizationAsync(existing.Id))!.FolderId);
            Assert.Single((await library.GetOrganizationAsync("one"))!.Tags);
            Assert.Single((await library.ListDefinitionsAsync(new(State: "all", FavoritesOnly: true), "alice")).Items);
            if (deleteWorkflows)
            {
                var one = await db.Definitions.SingleAsync(row => row.Id == "one");
                one.DeletedAt = null; one.DeletedReason = null;
                await scope.ServiceProvider.GetRequiredService<ISaveWorkflowDefinitionCommand>().Execute(new("restore-one"), one);
                Assert.Equal(parentId, (await library.GetOrganizationAsync("one"))!.FolderId);
            }
        }
        await using var other = LibraryHost(database.ConnectionString, "tenant-b");
        using var otherScope = other.CreateScope();
        var otherDeletion = otherScope.ServiceProvider.GetRequiredService<IWorkflowFolderDeletionStore>();
        await Assert.ThrowsAsync<EntityNotFoundException>(() => otherDeletion.PreviewFolderDeletionAsync(childId));
    }

    [Theory]
    [InlineData("membership")]
    [InlineData("lifecycle")]
    [InlineData("child")]
    [InlineData("parent")]
    public async Task Folder_deletion_rejects_changed_preview_without_partial_mutation(string change)
    {
        await using var database = new TemporarySqliteDatabase("folder-deletion-stale");
        await using var host = LibraryHost(database.ConnectionString); using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.EnsureCreatedAsync();
        var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        var deletion = scope.ServiceProvider.GetRequiredService<IWorkflowFolderDeletionStore>();
        var parent = await library.CreateFolderAsync("Parent", null, "alice");
        var folder = await library.CreateFolderAsync("Demo", parent.Id, "alice");
        db.Definitions.AddRange(new WorkflowDefinition { Id = "one", Name = "One", TenantId = "tenant-a" },
            new WorkflowDefinition { Id = "two", Name = "Two", TenantId = "tenant-a" }); await db.SaveChangesAsync();
        await library.MoveDefinitionsAsync(["one"], folder.Id, "alice", "place");
        var preview = await deletion.PreviewFolderDeletionAsync(folder.Id);
        switch (change)
        {
            case "membership": await library.MoveDefinitionsAsync(["two"], folder.Id, "alice", "place-two"); break;
            case "child": await library.CreateFolderAsync("New child", folder.Id, "alice"); break;
            case "parent": await library.RenameFolderAsync(parent.Id, "Renamed parent", parent.Revision, "alice"); break;
            case "lifecycle":
                var row = await db.Definitions.SingleAsync(row => row.Id == "one");
                row.DeletedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); break;
        }
        var audits = await db.LibraryAudit.CountAsync();
        await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => deletion.DeleteFolderAsync(folder.Id, preview.Revision, true, "alice", "stale-delete"));
        Assert.NotNull(await library.GetFolderAsync(folder.Id));
        Assert.Equal(folder.Id, (await library.GetOrganizationAsync("one"))!.FolderId);
        Assert.Null((await db.Definitions.SingleAsync(row => row.Id == "two")).DeletedAt);
        Assert.Equal(audits, await db.LibraryAudit.CountAsync());
    }

    [Fact]
    public async Task Folder_deletion_rejects_child_name_collision_without_moving_or_deleting_any_workflow()
    {
        await using var database = new TemporarySqliteDatabase("folder-deletion-collision");
        await using var host = LibraryHost(database.ConnectionString); using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.EnsureCreatedAsync();
        var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        var deletion = scope.ServiceProvider.GetRequiredService<IWorkflowFolderDeletionStore>();
        var folder = await library.CreateFolderAsync("Demo", null, "alice");
        var child = await library.CreateFolderAsync("Invoices", folder.Id, "alice");
        await library.CreateFolderAsync("Invoices", null, "alice");
        db.Definitions.Add(new WorkflowDefinition { Id = "one", Name = "One", TenantId = "tenant-a" }); await db.SaveChangesAsync();
        await library.MoveDefinitionsAsync(["one"], folder.Id, "alice", "place");
        var preview = await deletion.PreviewFolderDeletionAsync(folder.Id);
        await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => deletion.DeleteFolderAsync(folder.Id, preview.Revision, true, "alice", "delete"));
        Assert.Equal(folder.Id, (await library.GetOrganizationAsync("one"))!.FolderId);
        Assert.Equal(folder.Id, (await library.GetFolderAsync(child.Id))!.Folder.ParentId);
        Assert.Null((await db.Definitions.SingleAsync()).DeletedAt);
    }

    [Fact]
    public async Task Folder_deletion_promotes_a_child_with_the_deleted_parents_name()
    {
        await using var database = new TemporarySqliteDatabase("folder-deletion-same-name");
        await using var host = LibraryHost(database.ConnectionString); using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.EnsureCreatedAsync();
        var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        var deletion = scope.ServiceProvider.GetRequiredService<IWorkflowFolderDeletionStore>();
        var folder = await library.CreateFolderAsync("Demo", null, "alice");
        var child = await library.CreateFolderAsync("Demo", folder.Id, "alice");
        var grandchild = await library.CreateFolderAsync("Grandchild", child.Id, "alice");
        var preview = await deletion.PreviewFolderDeletionAsync(folder.Id);
        await deletion.DeleteFolderAsync(folder.Id, preview.Revision, false, "alice", "delete");
        var promoted = Assert.Single((await library.ListFoldersAsync(null)).Items);
        Assert.Equal(child.Id, promoted.Id); Assert.Equal("Demo", promoted.Name);
        Assert.Equal(new[] { "Demo" }, (await library.GetFolderAsync(grandchild.Id))!.Ancestors.Select(row => row.Name));
    }

    [Fact]
    public async Task Folder_deletion_rolls_back_workflows_hierarchy_and_audits_on_provider_failure()
    {
        await using var database = new TemporarySqliteDatabase("folder-deletion-rollback");
        await using var host = LibraryHost(database.ConnectionString); using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(); await db.Database.EnsureCreatedAsync();
        var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        var deletion = scope.ServiceProvider.GetRequiredService<IWorkflowFolderDeletionStore>();
        var folder = await library.CreateFolderAsync("Demo", null, "alice");
        var child = await library.CreateFolderAsync("Demo", folder.Id, "alice");
        db.Definitions.Add(new WorkflowDefinition { Id = "one", Name = "One", TenantId = "tenant-a" }); await db.SaveChangesAsync();
        await library.MoveDefinitionsAsync(["one"], folder.Id, "alice", "place");
        var preview = await deletion.PreviewFolderDeletionAsync(folder.Id);
        var auditCount = await db.LibraryAudit.CountAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_folder_delete BEFORE DELETE ON elsa_workflow_folders BEGIN SELECT RAISE(ABORT, 'injected folder deletion failure'); END;");
        await Assert.ThrowsAsync<DesignPersistenceException>(() => deletion.DeleteFolderAsync(folder.Id, preview.Revision, true, "alice", "delete"));
        using var verify = host.CreateScope();
        var read = verify.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        var readDb = verify.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>();
        Assert.NotNull(await read.GetFolderAsync(folder.Id));
        Assert.Equal(folder.Id, (await read.GetFolderAsync(child.Id))!.Folder.ParentId);
        Assert.Equal(folder.Id, (await read.GetOrganizationAsync("one"))!.FolderId);
        Assert.Null((await readDb.Definitions.SingleAsync()).DeletedAt);
        Assert.Equal(auditCount, await readDb.LibraryAudit.CountAsync());
    }
}
