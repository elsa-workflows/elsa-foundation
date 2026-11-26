using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Elsa.Primitives.Entities;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfWorkflowLibraryStore
{
    private const int MaximumDeletionWorkflows = 10_000;

    public Task<FolderDeletionPreview> PreviewFolderDeletionAsync(string folderId, CancellationToken cancellationToken = default) =>
        EfDesignSupport.ReadAsync("previewing workflow folder deletion", async () =>
            (await ReadDeletionAsync(folderId, cancellationToken)).Preview);

    private async Task<DeletionContents> ReadDeletionAsync(string folderId, CancellationToken token)
    {
        ValidateStoredId(folderId, "folder-");
        var folder = await FolderRows.AsNoTracking().SingleOrDefaultAsync(row => row.Id == folderId, token)
            ?? Missing<WorkflowFolderEntity>(folderId);
        var parent = folder.ParentId is null ? null : await FolderRows.AsNoTracking().SingleAsync(row => row.Id == folder.ParentId, token);
        var descendants = await FolderRows.AsNoTracking().Where(row => row.Id != folderId && row.Path.StartsWith(folder.Path))
            .OrderBy(row => row.Id).Take(MaximumFolders + 1).ToArrayAsync(token);
        if (descendants.Length > MaximumFolders) throw new ArgumentException("This folder exceeds the supported folder limit.");
        var memberships = await OrganizationRows.AsNoTracking().Where(row => row.FolderId == folderId)
            .OrderBy(row => row.DefinitionHash).Take(MaximumDeletionWorkflows + 1).ToArrayAsync(token);
        if (memberships.Length > MaximumDeletionWorkflows)
            throw new ArgumentException($"Move workflows out of this folder before deleting it; at most {MaximumDeletionWorkflows} direct workflows are supported.");
        // Join rather than a large IN clause: all providers support the same bounded target set.
        var definitions = await (from definition in DefinitionRows.AsNoTracking()
            join membership in OrganizationRows.AsNoTracking() on definition.IdLookupHash equals membership.DefinitionHash
            where membership.FolderId == folderId
            orderby definition.IdLookupHash
            select definition).Take(MaximumDeletionWorkflows + 1).ToArrayAsync(token);
        if (definitions.Length != memberships.Length || definitions.Where((row, index) =>
                row.IdLookupHash != memberships[index].DefinitionHash).Any())
            Conflict("Folder contents changed. Reload the deletion preview before trying again.");
        for (var index = 0; index < definitions.Length; index++)
            EfDesignSupport.EnsureDefinitionIdentity(memberships[index].DefinitionId, definitions[index].Id, "folder deletion lookup");
        var revision = Binding("folder-deletion-v1", new
        {
            folder = Folder(folder), parent = parent is null ? null : Folder(parent),
            descendants = descendants.Select(Folder).ToArray(),
            memberships = memberships.Select(row => new { row.DefinitionId, row.Revision }).ToArray(),
            definitions = definitions.Select(row => new { row.Id, row.LastModifiedAt, row.DeletedAt, row.DeletedReason }).ToArray()
        });
        return new(new(Folder(folder), parent is null ? null : Folder(parent), descendants.Select(Folder).ToArray(),
            definitions.Select(row => new FolderDeletionWorkflow(row.Id, row.DeletedAt is not null)).ToArray(), revision),
            folder, descendants, memberships, definitions);
    }

    public async Task DeleteFolderAsync(string folderId, string previewRevision, bool deleteWorkflows, string actorId,
        string operationKey, CancellationToken cancellationToken = default)
    {
        ValidateStoredId(folderId, "folder-"); ValidateActor(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(previewRevision);
        if (previewRevision.Length != 64) throw new ArgumentException("A current folder deletion preview is required.");
        await WriteAsync(operationKey, "workflow.folder.delete-contents.v1",
            new { folderId, previewRevision, deleteWorkflows, actorId }, async token =>
        {
            var contents = await ReadDeletionAsync(folderId, token);
            if (!StringComparer.Ordinal.Equals(previewRevision, contents.Preview.Revision))
                Conflict("Folder contents changed. Reload the deletion preview and confirm again.");
            var folder = contents.Folder;
            var children = contents.Descendants.Where(row => row.ParentId == folderId).ToArray();
            if (children.Length > 0 && await (from child in FolderRows
                    join sibling in FolderRows on child.NameHash equals sibling.NameHash
                    where child.ParentId == folderId && sibling.Id != folderId && sibling.ParentKey == folder.ParentKey
                    select sibling).AnyAsync(token))
                Conflict("A child folder has the same name as a folder at the destination. Rename or move it before deleting this folder.");

            var now = DateTimeOffset.UtcNow;
            TrackDeletionRow(folder);
            if (children.Any(child => child.NameHash == folder.NameHash))
            {
                // Vacate the selected folder's sibling name before promoting a same-named child.
                // Both saves remain in the atomic writer's transaction; a failure rolls back this
                // temporary reservation along with all moves and audit facts.
                folder.NameHash = EfDesignSupport.LookupHash(NewId("deleting-folder-"));
                await db.SaveChangesAsync(token);
            }
            foreach (var descendant in contents.Descendants)
            {
                TrackDeletionRow(descendant);
                var before = Folder(descendant);
                descendant.Path = folder.Path[..^(folderId.Length + 1)] + descendant.Path[folder.Path.Length..];
                descendant.Depth--;
                if (descendant.ParentId == folderId)
                {
                    descendant.ParentId = folder.ParentId;
                    descendant.ParentKey = folder.ParentKey;
                }
                descendant.Revision = Revision(); descendant.LastModifiedAt = now;
                Audit(descendant.Id, actorId, "folder.move", operationKey, before, Folder(descendant));
            }
            for (var index = 0; index < contents.Memberships.Length; index++)
            {
                var membership = contents.Memberships[index];
                TrackDeletionRow(membership);
                membership.FolderId = folder.ParentId; membership.Revision = Revision(); membership.LastModifiedAt = now;
                Audit(membership.DefinitionId, actorId, "workflow.move", operationKey,
                    new { folderId }, new { folderId = folder.ParentId });
                var definition = contents.Definitions[index];
                // A reused scope can already track a definition. Reload to preserve concurrency originals.
                var tracked = db.Definitions.Local.FirstOrDefault(row => row.IdLookupHash == definition.IdLookupHash && row.TenantId == definition.TenantId);
                if (tracked is not null)
                {
                    await db.Entry(tracked).ReloadAsync(token);
                    if (db.Entry(tracked).State == EntityState.Detached || tracked.LastModifiedAt != definition.LastModifiedAt ||
                        tracked.DeletedAt != definition.DeletedAt || tracked.DeletedReason != definition.DeletedReason)
                        Conflict("A workflow changed. Reload the deletion preview and confirm again.");
                    definition = tracked;
                }
                else TrackDeletionRow(definition);
                if (deleteWorkflows && definition.DeletedAt is null)
                {
                    definition.DeletedAt = now; definition.DeletedReason = "Folder deleted with its workflows.";
                    Audit(definition.Id, actorId, "workflow.delete", operationKey, new { deletedAt = (DateTimeOffset?)null },
                        new { definition.DeletedAt, definition.DeletedReason });
                }
                definition.LastModifiedAt = now;
            }
            Audit(folderId, actorId, "folder.delete", operationKey, contents.Preview, null);
            db.Folders.Remove(folder);
            return true;
        }, cancellationToken);
    }

    private sealed record DeletionContents(FolderDeletionPreview Preview, WorkflowFolderEntity Folder,
        WorkflowFolderEntity[] Descendants, WorkflowOrganizationEntity[] Memberships, WorkflowDefinition[] Definitions);

    private void TrackDeletionRow<T>(T row) where T : TenantEntity
    {
        // No-tracking previews do not retain shadow composite keys. Supply the known physical
        // scope before attaching, otherwise EF interprets the generated key as a new row.
        var entry = db.Entry(row);
        entry.Property<string>(EfDesignSupport.ScopeKeyProperty).CurrentValue = EfDesignSupport.ScopeKey(Tenant);
        entry.State = EntityState.Unchanged;
    }
}
