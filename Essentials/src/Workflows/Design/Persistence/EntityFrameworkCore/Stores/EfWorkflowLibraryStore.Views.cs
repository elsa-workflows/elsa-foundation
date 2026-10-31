using System.Text.Json;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfWorkflowLibraryStore
{
    private sealed record ViewContent(WorkflowLibraryQuery Query, IReadOnlyList<string> Columns, int SchemaVersion = 1);
    private static WorkflowLibraryView View(WorkflowLibraryViewEntity row)
    {
        var content = JsonSerializer.Deserialize<ViewContent>(row.ContentJson, Json) ?? throw new InvalidDataException("The stored workflow view is invalid.");
        if (content.SchemaVersion != 1) throw new InvalidDataException("This workflow view requires a newer schema reader.");
        return new(row.Id, row.Name, row.Shared, row.OwnerId, content.Query, content.Columns, row.Revision, content.SchemaVersion);
    }

    public Task<IReadOnlyList<WorkflowLibraryView>> ListViewsAsync(string subjectId, CancellationToken cancellationToken = default) =>
        EfDesignSupport.ReadAsync("listing workflow views", () => ListViewsCoreAsync(subjectId, cancellationToken));

    private async Task<IReadOnlyList<WorkflowLibraryView>> ListViewsCoreAsync(string subjectId, CancellationToken cancellationToken)
    {
        ValidateActor(subjectId); var hash = EfDesignSupport.LookupHash(subjectId);
        var rows = await Scoped(db.LibraryViews).Where(row => row.Shared || row.OwnerHash == hash && row.OwnerId == subjectId)
            .OrderBy(row => row.Name).ThenBy(row => row.Id).Take(1001).ToArrayAsync(cancellationToken);
        if (rows.Length > 1000) throw new InvalidOperationException("The visible workflow view catalog exceeds 1000 entries.");
        return rows.Select(View).ToArray();
    }

    public Task<WorkflowLibraryView> SaveViewAsync(string? id, WorkflowLibraryViewInput input, string subjectId,
        bool canShare, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input); ValidateActor(subjectId);
        ArgumentNullException.ThrowIfNull(input.Query);
        var name = Text(input.Name, "View name");
        if (id is not null) { ValidateStoredId(id, "view-"); ArgumentException.ThrowIfNullOrWhiteSpace(input.Revision); }
        if (input.Shared && !canShare) throw new UnauthorizedAccessException("Sharing workflow views requires the share permission.");
        var query = NormalizeQuery(input.Query with { ContinuationToken = null });
        var columns = input.Columns ?? [];
        if (columns.Count > 16 || columns.Any(column => column is not ("name" or "id" or "state" or "folder" or "location" or "tags" or "modified" or "version" or "favorite")))
            throw new ArgumentException("Workflow views contain at most 16 supported columns.");
        var content = JsonSerializer.Serialize(new ViewContent(query, columns.Distinct(StringComparer.Ordinal).ToArray()), Json);
        if (content.Length > 32768) throw new ArgumentException("The saved view exceeds 32 KiB.");
        var key = NewId("save-view-");
        return WriteAsync(key, "workflow.view.save.v1", new { id, name, content, input.Shared, input.Revision, subjectId }, async token =>
        {
            await ValidateQueryReferencesAsync(query, token);
            var row = id is null ? null : await Scoped(db.LibraryViews).SingleOrDefaultAsync(candidate => candidate.Id == id, token)
                ?? Missing<WorkflowLibraryViewEntity>(id!);
            if (row is not null && !StringComparer.Ordinal.Equals(row.OwnerId, subjectId) && !(row.Shared && canShare)) Missing<WorkflowLibraryView>(id!);
            if (row is not null && row.Shared && !canShare) throw new UnauthorizedAccessException("Changing a shared view requires the share permission.");
            if (row is not null) CheckRevision(input.Revision, row.Revision);
            var before = row is null ? null : View(row);
            if (row is null)
            {
                var subjectHash = EfDesignSupport.LookupHash(subjectId);
                if (await Scoped(db.LibraryViews).CountAsync(candidate => candidate.OwnerHash == subjectHash && candidate.OwnerId == subjectId, token) >= 100)
                    throw new ArgumentException("Each user can create at most 100 workflow views.");
                if (input.Shared && await Scoped(db.LibraryViews).CountAsync(candidate => candidate.Shared, token) >= 500)
                    throw new ArgumentException("The tenant supports at most 500 shared workflow views.");
                row = new WorkflowLibraryViewEntity { Id = NewId("view-"), TenantId = Tenant, OwnerId = subjectId,
                    OwnerHash = subjectHash, CreatedAt = DateTimeOffset.UtcNow };
                db.LibraryViews.Add(row);
            }
            else if (!row.Shared && input.Shared && await Scoped(db.LibraryViews).CountAsync(candidate => candidate.Shared, token) >= 500)
                throw new ArgumentException("The tenant supports at most 500 shared workflow views.");
            row.Name = name; row.Shared = input.Shared; row.ContentJson = content; row.Revision = Revision(); row.LastModifiedAt = DateTimeOffset.UtcNow;
            var result = View(row); Audit(row.Id, subjectId, "view.save", key, before, result); return result;
        }, cancellationToken);
    }

    public async Task DeleteViewAsync(string id, string revision, string subjectId, bool canShare, CancellationToken cancellationToken = default)
    {
        ValidateStoredId(id, "view-"); ValidateActor(subjectId); ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        var key = NewId("delete-view-");
        await WriteAsync(key, "workflow.view.delete.v1", new { id, revision, subjectId }, async token =>
        {
            var row = await Scoped(db.LibraryViews).SingleOrDefaultAsync(candidate => candidate.Id == id, token) ?? Missing<WorkflowLibraryViewEntity>(id);
            if (!StringComparer.Ordinal.Equals(row.OwnerId, subjectId) && !(row.Shared && canShare)) Missing<WorkflowLibraryView>(id);
            if (row.Shared && !canShare) throw new UnauthorizedAccessException("Deleting a shared view requires the share permission.");
            CheckRevision(revision, row.Revision); Audit(id, subjectId, "view.delete", key, View(row), null); db.LibraryViews.Remove(row); return id;
        }, cancellationToken);
    }

    public async Task SetFavoriteAsync(string definitionId, bool favorite, string subjectId, CancellationToken cancellationToken = default)
    {
        ValidateActor(subjectId); var key = NewId("favorite-");
        await WriteAsync(key, "workflow.favorite.set.v1", new { definitionId, favorite, subjectId }, async token =>
        {
            var definition = await DefinitionAsync(definitionId, false, token); var hash = EfDesignSupport.LookupHash(subjectId);
            var row = await Scoped(db.Favorites).SingleOrDefaultAsync(candidate => candidate.SubjectHash == hash &&
                candidate.SubjectId == subjectId && candidate.DefinitionHash == definition.IdLookupHash, token);
            if (favorite && row is null)
                db.Favorites.Add(new WorkflowFavoriteEntity { Id = NewId("favorite-"), TenantId = Tenant,
                    SubjectId = subjectId, SubjectHash = hash, DefinitionHash = definition.IdLookupHash,
                    CreatedAt = DateTimeOffset.UtcNow, LastModifiedAt = DateTimeOffset.UtcNow });
            else if (!favorite && row is not null) db.Favorites.Remove(row);
            return true;
        }, cancellationToken);
    }
}
