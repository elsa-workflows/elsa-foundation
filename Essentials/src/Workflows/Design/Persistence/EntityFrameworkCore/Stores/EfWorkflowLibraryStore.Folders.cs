using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfWorkflowLibraryStore
{
    public async Task<WorkflowFolderPage> ListFoldersAsync(string? parentId, int pageSize = 50,
        string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        ValidatePage(pageSize);
        if (parentId is not null) await FolderAsync(parentId, cancellationToken);
        var binding = Binding("folders", new { parentId, pageSize });
        var cursor = Decode(continuationToken, binding);
        var query = FolderRows.AsNoTracking().Where(row => row.ParentKey == (parentId ?? ""));
        if (cursor is not null)
            query = query.Where(row => row.NormalizedName.CompareTo(cursor.Key) > 0 || row.NormalizedName == cursor.Key && row.Id.CompareTo(cursor.Id) > 0);
        var rows = await query.OrderBy(row => row.NormalizedName).ThenBy(row => row.Id).Take(pageSize + 1).ToArrayAsync(cancellationToken);
        var page = rows.Take(pageSize).ToArray();
        return new(page.Select(Folder).ToArray(), rows.Length > pageSize
            ? Encode(new(binding, page[^1].NormalizedName, page[^1].Id)) : null);
    }

    public async Task<WorkflowFolderDetail?> GetFolderAsync(string id, CancellationToken cancellationToken = default)
    {
        ValidateStoredId(id, "folder-");
        var row = await FolderRows.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (row is null) return null;
        var path = await BreadcrumbAsync(id, cancellationToken);
        return new(Folder(row), path.Take(path.Count - 1).ToArray());
    }

    public Task<WorkflowFolder> CreateFolderAsync(string name, string? parentId, string actorId, CancellationToken cancellationToken = default)
    {
        name = Text(name, "Folder name"); ValidateActor(actorId);
        var normalized = Normalized(name); var hash = EfDesignSupport.LookupHash(normalized);
        var key = NewId("create-folder-");
        return WriteAsync(key, "workflow.folder.create.v1", new { name, parentId, actorId }, async token =>
        {
            var parent = parentId is null ? null : await FolderAsync(parentId, token);
            if (parent?.Depth >= 16) throw new ArgumentException("Workflow folders support at most 16 levels.");
            if (await FolderRows.CountAsync(token) >= MaximumFolders) throw new ArgumentException($"The folder limit is {MaximumFolders}.");
            if (await FolderRows.AnyAsync(row => row.ParentKey == (parentId ?? "") && row.NameHash == hash, token))
                Conflict("A folder with this name already exists under the selected parent.");
            var now = DateTimeOffset.UtcNow; var id = NewId("folder-");
            var folder = new WorkflowFolderEntity { Id = id, TenantId = Tenant, Name = name, NormalizedName = normalized,
                NameHash = hash, ParentId = parentId, ParentKey = parentId ?? "", Depth = (parent?.Depth ?? 0) + 1,
                Path = (parent?.Path ?? "/") + id + "/", Revision = Revision(), CreatedAt = now, LastModifiedAt = now };
            db.Folders.Add(folder);
            var result = Folder(folder); Audit(id, actorId, "folder.create", key, null, result);
            return result;
        }, cancellationToken);
    }

    public async Task RenameFolderAsync(string id, string name, string? revision, string actorId, CancellationToken cancellationToken = default)
    {
        ValidateStoredId(id, "folder-"); name = Text(name, "Folder name"); ValidateActor(actorId);
        var key = NewId("rename-folder-"); var normalized = Normalized(name); var hash = EfDesignSupport.LookupHash(normalized);
        await WriteAsync(key, "workflow.folder.rename.v1", new { id, name, revision, actorId }, async token =>
        {
            var row = await FolderAsync(id, token); CheckRevision(revision, row.Revision); var before = Folder(row);
            if (await FolderRows.AnyAsync(candidate => candidate.Id != id && candidate.ParentKey == row.ParentKey && candidate.NameHash == hash, token))
                Conflict("A folder with this name already exists under the selected parent.");
            row.Name = name; row.NormalizedName = normalized; row.NameHash = hash;
            row.Revision = Revision(); row.LastModifiedAt = DateTimeOffset.UtcNow;
            Audit(id, actorId, "folder.rename", key, before, Folder(row)); return id;
        }, cancellationToken);
    }

    public async Task MoveFolderAsync(string id, string? parentId, string? revision, string actorId, CancellationToken cancellationToken = default)
    {
        ValidateStoredId(id, "folder-"); ValidateActor(actorId); var key = NewId("move-folder-");
        await WriteAsync(key, "workflow.folder.move.v1", new { id, parentId, revision, actorId }, async token =>
        {
            var folder = await FolderAsync(id, token); CheckRevision(revision, folder.Revision); var before = Folder(folder);
            var parent = parentId is null ? null : await FolderAsync(parentId, token);
            if (parent is not null && parent.Path.StartsWith(folder.Path, StringComparison.Ordinal))
                throw new ArgumentException("A folder cannot be moved into itself or one of its descendants.");
            if (await FolderRows.AnyAsync(candidate => candidate.Id != id && candidate.ParentKey == (parentId ?? "") && candidate.NameHash == folder.NameHash, token))
                Conflict("A folder with this name already exists under the selected parent.");
            var oldPath = folder.Path;
            var descendants = await FolderRows.Where(candidate => candidate.Path.StartsWith(oldPath))
                .OrderBy(candidate => candidate.Id).Take(MaximumFolders + 1).ToArrayAsync(token);
            if (descendants.Length > MaximumFolders) throw new InvalidOperationException("The folder subtree exceeds its supported bound.");
            var depthDelta = (parent?.Depth ?? 0) + 1 - folder.Depth;
            if (descendants.Any(candidate => candidate.Depth + depthDelta > 16))
                throw new ArgumentException("Moving this folder would exceed the 16-level folder limit.");
            var path = (parent?.Path ?? "/") + folder.Id + "/";
            foreach (var descendant in descendants)
            {
                descendant.Path = path + descendant.Path[oldPath.Length..]; descendant.Depth += depthDelta;
                descendant.LastModifiedAt = DateTimeOffset.UtcNow; descendant.Revision = Revision();
            }
            folder.ParentId = parentId; folder.ParentKey = parentId ?? "";
            Audit(id, actorId, "folder.move", key, before, Folder(folder)); return id;
        }, cancellationToken);
    }

    public async Task DeleteEmptyFolderAsync(string id, string? revision, string actorId, CancellationToken cancellationToken = default)
    {
        ValidateStoredId(id, "folder-"); ValidateActor(actorId); var key = NewId("delete-folder-");
        await WriteAsync(key, "workflow.folder.delete.v1", new { id, revision, actorId }, async token =>
        {
            var folder = await FolderAsync(id, token); CheckRevision(revision, folder.Revision);
            if (await FolderRows.AnyAsync(candidate => candidate.ParentId == id, token) || await OrganizationRows.AnyAsync(candidate => candidate.FolderId == id, token))
                Conflict("This folder contains folders or workflow definitions, including deleted definitions. Move them before deleting it.");
            Audit(id, actorId, "folder.delete", key, Folder(folder), null); db.Folders.Remove(folder); return id;
        }, cancellationToken);
    }

    public async Task MoveDefinitionsAsync(IReadOnlyList<string> definitionIds, string? folderId, string actorId,
        string operationKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitionIds); ValidateActor(actorId);
        if (definitionIds.Count is < 1 or > 100) throw new ArgumentException("Select between 1 and 100 workflows.");
        var hashes = definitionIds.Select(DefinitionHash).ToArray();
        if (hashes.Distinct(StringComparer.Ordinal).Count() != hashes.Length) throw new ArgumentException("A workflow can occur only once in a move.");
        await WriteAsync(operationKey, "workflow.folder.place.v1", new { definitionIds, folderId, actorId }, async token =>
        {
            if (folderId is not null) await FolderAsync(folderId, token);
            var definitions = await DefinitionRows.Where(row => hashes.Contains(row.IdLookupHash)).ToArrayAsync(token);
            if (definitions.Length != hashes.Length) Missing<WorkflowDefinition>("selected workflow");
            foreach (var definition in definitions)
            {
                EfDesignSupport.EnsureDefinitionIdentityInSet(definitionIds, definition.Id, "workflow placement lookup");
                if (definition.DeletedAt is not null) Conflict("Restore deleted workflows before moving them.");
            }
            var rows = await OrganizationRows.Where(row => hashes.Contains(row.DefinitionHash)).ToArrayAsync(token);
            foreach (var definition in definitions)
            {
                var row = rows.SingleOrDefault(candidate => candidate.DefinitionHash == definition.IdLookupHash) ?? NewOrganization(definition);
                var previous = row.FolderId; row.FolderId = folderId; row.Revision = Revision(); row.LastModifiedAt = DateTimeOffset.UtcNow;
                definition.LastModifiedAt = row.LastModifiedAt;
                Audit(definition.Id, actorId, "workflow.move", operationKey, new { folderId = previous }, new { folderId });
            }
            return true;
        }, cancellationToken);
    }
}
