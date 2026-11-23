using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed class EfUserTaskStore(RuntimeDbContext context, IPersistenceAccessContextAccessor access) : IUserTaskStore
{
    internal RuntimeDbContext Context => context;
    internal PersistenceAccessContext AccessContext => access.Current;
    public bool IsDurable => true;
    private string Scope => RequireScope(access);

    public async ValueTask<UserTaskRecord?> FindAsync(string tenantId, string id, CancellationToken cancellationToken = default)
    {
        var scope = Scope;
        var key = CompositeId(scope, tenantId, id);
        var row = await context.UserTasks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == key, cancellationToken);
        if (row is null) return null;
        var result = Read(row, scope);
        if (result.TenantId != tenantId || result.Id != id) throw new InvalidDataException("The task identity differs from its journal key.");
        return result;
    }

    public async ValueTask<bool> TryWriteAsync(UserTaskRecord record, long? expectedRevision, CancellationToken cancellationToken = default)
    {
        if (record.Revision != (expectedRevision ?? 0) + 1) throw new ArgumentException("A task write must advance one revision.");
        EnsureClean();
        var scope = Scope;
        access.Current.EnsureTenantScope(record.TenantId);
        // Match deletion's root lock. A task cannot appear after deletion erased its canonical evidence.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (await EfWorkflowExecutionStateStore.LockWorkflowExecutionForWriteAsync(context, scope, record.WorkflowExecutionId, cancellationToken) == 0)
            throw new UserTaskException("unavailable", "The task's owning execution is unavailable.");
        if (await EfExecutionLivenessStateStore.IsWorkflowExecutionDeletionTombstonedAsync(context, scope, record.WorkflowExecutionId, cancellationToken))
            throw new UserTaskException("conflict", "The owning execution was deleted.");
        var row = ToRow(record, scope);
        context.UserTasks.Attach(row);
        context.Entry(row).State = expectedRevision is null ? EntityState.Added : EntityState.Modified;
        if (expectedRevision is { } revision) context.Entry(row).Property(x => x.Revision).OriginalValue = revision;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey | EfWriteConflict.Concurrency))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        finally { context.Entry(row).State = EntityState.Detached; }
    }

    public async ValueTask<UserTaskPage> QueryAsync(UserTaskQuery query, CancellationToken cancellationToken = default)
    {
        ValidateLimit(query.Limit);
        var scope = Scope; var sh = Hash(scope); var sk = Encode(scope); var tenant = Encode(query.TenantId); var tenantHash = Hash(query.TenantId);
        var source = context.UserTasks.AsNoTracking().Where(x => x.ScopeHash == sh && x.ScopeKey == sk && x.TenantHash == tenantHash && x.TenantKey == tenant && x.Status != (int)UserTaskStatus.Staged);
        var user = query.UserId is null ? null : Encode(query.UserId);
        var userHash = query.UserId is null ? null : Hash(query.UserId);
        var groupHashes = query.GroupIds.Select(Hash).ToArray();
        var groups = query.GroupIds.Select(Encode).ToArray();
        if (user is not null) source = source.Where(x => x.GroupKey != null && x.GroupKey != ""
            ? groupHashes.Contains(x.GroupHash!) && groups.Contains(x.GroupKey) && (x.ClaimantKey == null || (x.ClaimantHash == userHash && x.ClaimantKey == user)) : x.AssigneeHash == userHash && x.AssigneeKey == user);
        source = query.View switch
        {
            "mine" => source.Where(x => x.Status < (int)UserTaskStatus.Completed && ((x.AssigneeHash == userHash && x.AssigneeKey == user) || (x.ClaimantHash == userHash && x.ClaimantKey == user))),
            "queue" => source.Where(x => x.Status == (int)UserTaskStatus.Open && x.GroupKey != null && x.GroupKey != "" && groups.Contains(x.GroupKey) && x.ClaimantKey == null),
            "history" => source.Where(x => x.Status >= (int)UserTaskStatus.Completed),
            "all" when user is null => source,
            _ => throw new UserTaskException("validation", "Invalid task query.")
        };
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.ToUpperInvariant();
            source = source.Where(x => x.SearchTitle.Contains(search));
        }
        if (query.After is { } after) source = source.Where(x => string.Compare(x.TaskId, after) > 0);
        var rows = await source.OrderBy(x => x.TaskId).Take(query.Limit + 1).ToArrayAsync(cancellationToken);
        return new(rows.Take(query.Limit).Select(x => Read(x, scope)).ToArray(), rows.Length > query.Limit ? rows[query.Limit - 1].TaskId : null);
    }

    public async ValueTask<UserTaskPage> RecoverAsync(int limit, string? after, CancellationToken cancellationToken = default)
    {
        ValidateLimit(limit);
        var scope = Scope; var sh = Hash(scope); var sk = Encode(scope);
        var source = context.UserTasks.AsNoTracking().Where(x => x.ScopeHash == sh && x.ScopeKey == sk && x.Status < (int)UserTaskStatus.Completed);
        if (after is not null) source = source.Where(x => string.Compare(x.TaskId, after) > 0);
        var rows = await source.OrderBy(x => x.TaskId).Take(limit + 1).ToArrayAsync(cancellationToken);
        return new(rows.Take(limit).Select(x => Read(x, scope)).ToArray(), rows.Length > limit ? rows[limit - 1].TaskId : null);
    }

    public async ValueTask<UserTaskFormVersion?> FindFormAsync(string tenantId, string id, string version, CancellationToken cancellationToken = default)
    {
        if (version == UserTaskProtocol.FormDraftVersion) return null;
        var scope = Scope; var key = CompositeId(scope, tenantId, id, version);
        var row = await context.UserTaskForms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == key, cancellationToken);
        if (row is null) return null;
        var value = ReadForm(row, scope);
        if (value.TenantId != tenantId || value.Form.Id != id || value.Form.Version != version) throw new InvalidDataException("The form identity differs.");
        return value;
    }

    public async ValueTask<UserTaskFormPage> ListFormsAsync(string tenantId, int limit, string? after = null, CancellationToken cancellationToken = default)
    {
        ValidateLimit(limit);
        var scope = Scope; var sh = Hash(scope); var sk = Encode(scope); var tk = Encode(tenantId); var th = Hash(tenantId);
        var tombstones = await context.UserTaskForms.AsNoTracking().Where(x => x.ScopeHash == sh && x.ScopeKey == sk && x.TenantHash == th &&
            x.TenantKey == tk && x.VersionKey == Encode(UserTaskProtocol.FormDraftVersion)).ToArrayAsync(cancellationToken);
        var deleted = tombstones.Where(x => ReadDraft(x, scope).Deleted).Select(x => x.FormKey).ToHashSet(StringComparer.Ordinal);
        var source = context.UserTaskForms.AsNoTracking().Where(x => x.ScopeHash == sh && x.ScopeKey == sk && x.TenantHash == th && x.TenantKey == tk && x.VersionKey != Encode(UserTaskProtocol.FormDraftVersion));
        var rows = new List<UserTaskFormEntity>();
        var cursor = after;
        while (rows.Count <= limit)
        {
            var page = source;
            if (cursor is not null) page = page.Where(x => string.Compare(x.Id, cursor) > 0);
            var batch = await page.OrderBy(x => x.Id).Take(limit + 1).ToArrayAsync(cancellationToken);
            if (batch.Length == 0) break;
            foreach (var row in batch)
            {
                cursor = row.Id;
                if (!deleted.Contains(row.FormKey)) rows.Add(row);
                if (rows.Count > limit) break;
            }
            if (batch.Length < limit + 1) break;
        }
        return new(rows.Take(limit).Select(x => ReadForm(x, scope)).ToArray(), rows.Count > limit ? rows[limit - 1].Id : null);
    }

    public async ValueTask<UserTaskFormPage> ListFormVersionsAsync(string tenantId, string id, int limit, string? after = null, CancellationToken cancellationToken = default)
    {
        ValidateLimit(limit); access.Current.EnsureTenantScope(tenantId);
        var scope = Scope; var key = Encode(id);
        var draft = await context.UserTaskForms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == CompositeId(scope, tenantId, id, UserTaskProtocol.FormDraftVersion), cancellationToken);
        if (draft is not null && ReadDraft(draft, scope).Deleted) return new UserTaskFormPage([], null);
        var source = context.UserTaskForms.AsNoTracking().Where(x => x.ScopeHash == Hash(scope) && x.ScopeKey == Encode(scope) &&
            x.TenantHash == Hash(tenantId) && x.TenantKey == Encode(tenantId) && x.FormKey == key && x.VersionKey != Encode(UserTaskProtocol.FormDraftVersion));
        if (after is not null) source = source.Where(x => string.Compare(x.Id, after) > 0);
        var rows = await source.OrderBy(x => x.Id).Take(limit + 1).ToArrayAsync(cancellationToken);
        return new UserTaskFormPage(rows.Take(limit).Select(x => ReadForm(x, scope)).ToArray(), rows.Length > limit ? rows[limit - 1].Id : null);
    }

    public async ValueTask<bool> TryCreateFormAsync(UserTaskFormVersion version, CancellationToken cancellationToken = default)
    {
        if (version.Form.Version == UserTaskProtocol.FormDraftVersion) return false;
        EnsureClean(); access.Current.EnsureTenantScope(version.TenantId);
        var scope = Scope;
        var draft = await context.UserTaskForms.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == CompositeId(scope, version.TenantId, version.Form.Id, UserTaskProtocol.FormDraftVersion), cancellationToken);
        if (draft is not null && ReadDraft(draft, scope).Deleted) return false;
        var row = new UserTaskFormEntity { Id = CompositeId(scope, version.TenantId, version.Form.Id, version.Form.Version),
            ScopeKey = Encode(scope), ScopeHash = Hash(scope), TenantKey = Encode(version.TenantId), TenantHash = Hash(version.TenantId), FormKey = Encode(version.Form.Id),
            VersionKey = Encode(version.Form.Version), ContentJson = JsonSerializer.Serialize(version, Elsa.Serialization.Core.JsonBaseline.Options) };
        context.UserTaskForms.Add(row);
        try { await context.SaveChangesAsync(cancellationToken); return true; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey)) { return false; }
        finally { context.Entry(row).State = EntityState.Detached; }
    }

    public async ValueTask<UserTaskFormDefinition?> FindFormDefinitionAsync(string tenantId, string id, CancellationToken cancellationToken = default)
    {
        var scope = Scope; access.Current.EnsureTenantScope(tenantId);
        var rows = await context.UserTaskForms.AsNoTracking().Where(x => x.ScopeHash == Hash(scope) && x.ScopeKey == Encode(scope) &&
            x.TenantHash == Hash(tenantId) && x.TenantKey == Encode(tenantId) && x.FormKey == Encode(id)).ToArrayAsync(cancellationToken);
        return rows.Length == 0 ? null : Definition(tenantId, id, rows, scope);
    }

    public async ValueTask<UserTaskFormDefinitionPage> ListFormDefinitionsAsync(string tenantId, int limit, string? after = null, CancellationToken cancellationToken = default)
    {
        ValidateLimit(limit); access.Current.EnsureTenantScope(tenantId);
        var scope = Scope; var source = context.UserTaskForms.AsNoTracking().Where(x => x.ScopeHash == Hash(scope) && x.ScopeKey == Encode(scope) &&
            x.TenantHash == Hash(tenantId) && x.TenantKey == Encode(tenantId));
        var publishedIds = await source.Where(x => x.VersionKey != Encode(UserTaskProtocol.FormDraftVersion)).Select(x => x.FormKey).Distinct().ToArrayAsync(cancellationToken);
        var activeDraftRows = await source.Where(x => x.VersionKey == Encode(UserTaskProtocol.FormDraftVersion)).ToArrayAsync(cancellationToken);
        var deletedIds = activeDraftRows.Where(x => ReadDraft(x, scope).Deleted).Select(x => x.FormKey).ToHashSet(StringComparer.Ordinal);
        var draftIds = activeDraftRows.Where(x => !ReadDraft(x, scope).Deleted && ReadDraft(x, scope).Form is not null).Select(x => x.FormKey).ToArray();
        var ids = publishedIds.Where(x => !deletedIds.Contains(x)).Concat(draftIds).Distinct(StringComparer.Ordinal).Select(Decode)
            .Where(x => after is null || StringComparer.Ordinal.Compare(x, after) > 0).OrderBy(x => x, StringComparer.Ordinal).Take(limit + 1).ToArray();
        var selectedIds = ids.Take(limit).ToArray();
        if (selectedIds.Length == 0) return new UserTaskFormDefinitionPage([], null);
        var keys = selectedIds.Select(Encode).ToArray();
        var rows = await source.Where(x => keys.Contains(x.FormKey)).ToArrayAsync(cancellationToken);
        var items = selectedIds.Select(id => Definition(tenantId, id, rows.Where(x => x.FormKey == Encode(id)).ToArray(), scope)).ToArray();
        return new UserTaskFormDefinitionPage(items, ids.Length > limit ? ids[limit - 1] : null);
    }

    public async ValueTask<bool> TryWriteFormDraftAsync(UserTaskFormDraft draft, long expectedRevision, CancellationToken cancellationToken = default)
    {
        if (draft.Revision != expectedRevision + 1 || draft.Form is { } form && form.Id != draft.Id)
            throw new ArgumentException("A form draft write must advance one revision and retain its form identity.");
        EnsureClean(); access.Current.EnsureTenantScope(draft.TenantId);
        var scope = Scope; var key = CompositeId(scope, draft.TenantId, draft.Id, UserTaskProtocol.FormDraftVersion);
        var existing = await context.UserTaskForms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == key, cancellationToken);
        if (existing is not null && (ReadDraft(existing, scope).Revision != expectedRevision || ReadDraft(existing, scope).Deleted)) return false;
        var row = ToDraftRow(draft, scope);
        if (existing is null)
        {
            context.UserTaskForms.Add(row);
            try { await context.SaveChangesAsync(cancellationToken); return true; }
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey)) { return false; }
            finally { context.Entry(row).State = EntityState.Detached; }
        }
        try
        {
            var changed = await context.UserTaskForms.Where(x => x.Id == key && x.ContentJson == existing.ContentJson)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ContentJson, row.ContentJson), cancellationToken);
            return changed == 1;
        }
        finally { context.Entry(row).State = EntityState.Detached; }
    }

    public async ValueTask<bool> TryPublishFormAsync(UserTaskFormVersion version, UserTaskFormDraft definitionState, long expectedRevision, CancellationToken cancellationToken = default)
    {
        if (definitionState.TenantId != version.TenantId || definitionState.Id != version.Form.Id || definitionState.Form is not null ||
            definitionState.Revision != expectedRevision + 1 || version.Form.Version == UserTaskProtocol.FormDraftVersion)
            throw new ArgumentException("A form publication must atomically advance the matching definition revision.");
        EnsureClean(); access.Current.EnsureTenantScope(version.TenantId);
        var scope = Scope; var draftId = CompositeId(scope, version.TenantId, version.Form.Id, UserTaskProtocol.FormDraftVersion);
        var existing = default(UserTaskFormEntity);
        var draftRow = ToDraftRow(definitionState, scope);
        var publishedRow = ToFormRow(version, scope);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            existing = await context.UserTaskForms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == draftId, cancellationToken);
            if (existing is not null && (ReadDraft(existing, scope).Revision != expectedRevision || ReadDraft(existing, scope).Deleted))
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
            if (existing is null)
                context.UserTaskForms.Add(draftRow);
            else
            {
                var changed = await context.UserTaskForms.Where(x => x.Id == draftId && x.ContentJson == existing.ContentJson)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ContentJson, draftRow.ContentJson), cancellationToken);
                if (changed != 1)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }
            }
            context.UserTaskForms.Add(publishedRow);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey | EfWriteConflict.Concurrency))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        finally
        {
            context.Entry(draftRow).State = EntityState.Detached;
            context.Entry(publishedRow).State = EntityState.Detached;
        }
    }

    private static UserTaskFormEntity ToFormRow(UserTaskFormVersion version, string scope) => new()
    {
        Id = CompositeId(scope, version.TenantId, version.Form.Id, version.Form.Version), ScopeKey = Encode(scope), ScopeHash = Hash(scope),
        TenantKey = Encode(version.TenantId), TenantHash = Hash(version.TenantId), FormKey = Encode(version.Form.Id),
        VersionKey = Encode(version.Form.Version), ContentJson = JsonSerializer.Serialize(version)
    };

    private static UserTaskFormEntity ToDraftRow(UserTaskFormDraft draft, string scope) => new()
    {
        Id = CompositeId(scope, draft.TenantId, draft.Id, UserTaskProtocol.FormDraftVersion), ScopeKey = Encode(scope), ScopeHash = Hash(scope),
        TenantKey = Encode(draft.TenantId), TenantHash = Hash(draft.TenantId), FormKey = Encode(draft.Id),
        VersionKey = Encode(UserTaskProtocol.FormDraftVersion), ContentJson = JsonSerializer.Serialize(draft)
    };

    private static UserTaskFormDefinition Definition(string tenantId, string id, IReadOnlyCollection<UserTaskFormEntity> rows, string scope)
    {
        var versions = rows.Where(x => x.VersionKey != Encode(UserTaskProtocol.FormDraftVersion)).Select(x => ReadForm(x, scope)).ToArray();
        var latestNumeric = versions.Where(x => int.TryParse(x.Form.Version, out _)).OrderByDescending(x => int.Parse(x.Form.Version, System.Globalization.CultureInfo.InvariantCulture))
            .ThenByDescending(x => x.CreatedAt).FirstOrDefault();
        var latest = latestNumeric ?? versions.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Form.Version, StringComparer.Ordinal).FirstOrDefault();
        var draftRow = rows.FirstOrDefault(x => x.VersionKey == Encode(UserTaskProtocol.FormDraftVersion));
        var state = draftRow is null ? null : ReadDraft(draftRow, scope);
        if (state is not null && (state.TenantId != tenantId || state.Id != id)) throw new InvalidDataException("The form draft identity differs.");
        return new UserTaskFormDefinition(tenantId, id, latest, state?.Form is null ? null : state, state?.Revision ?? 0)
        {
            Deleted = state?.Deleted == true
        };
    }

    private static UserTaskEntity ToRow(UserTaskRecord task, string scope) => new()
    {
        Id = CompositeId(scope, task.TenantId, task.Id), ScopeKey = Encode(scope), ScopeHash = Hash(scope), TenantKey = Encode(task.TenantId), TenantHash = Hash(task.TenantId),
        ClaimantHash = task.ClaimantId is null ? null : Hash(task.ClaimantId), TaskId = task.Id, ExecutionHash = Hash(task.WorkflowExecutionId), AssigneeHash = task.AssigneeId is null ? null : Hash(task.AssigneeId), GroupHash = task.GroupId is null ? null : Hash(task.GroupId), ExecutionKey = Encode(task.WorkflowExecutionId), AssigneeKey = task.AssigneeId is null ? null : Encode(task.AssigneeId),
        GroupKey = task.GroupId is null ? null : Encode(task.GroupId), ClaimantKey = task.ClaimantId is null ? null : Encode(task.ClaimantId),
        SearchTitle = task.Title.ToUpperInvariant(), Status = (int)task.Status, Revision = task.Revision, ContentJson = JsonSerializer.Serialize(task, Elsa.Serialization.Core.JsonBaseline.Options)
    };
    private static UserTaskRecord Read(UserTaskEntity row, string scope)
    {
        var content = UserTasksEfModule.Chain.Upcast<UserTaskEntity>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]!;
        var value = JsonSerializer.Deserialize<UserTaskRecord>(content, Elsa.Serialization.Core.JsonBaseline.Options) ?? throw new InvalidDataException("The task journal is invalid.");
        var expected = ToRow(value, scope);
        if (row.Id != expected.Id || row.ScopeKey != expected.ScopeKey || row.ScopeHash != expected.ScopeHash ||
            row.TenantKey != expected.TenantKey || row.TenantHash != expected.TenantHash || row.ClaimantHash != expected.ClaimantHash || row.ExecutionHash != expected.ExecutionHash || row.AssigneeHash != expected.AssigneeHash || row.GroupHash != expected.GroupHash || row.TaskId != expected.TaskId || row.ExecutionKey != expected.ExecutionKey ||
            row.AssigneeKey != expected.AssigneeKey || row.GroupKey != expected.GroupKey || row.ClaimantKey != expected.ClaimantKey ||
            row.Status != expected.Status || row.Revision != expected.Revision || row.SearchTitle != expected.SearchTitle)
            throw new InvalidDataException("The task journal's identity or projection differs.");
        return value;
    }
    private static UserTaskFormVersion ReadForm(UserTaskFormEntity row, string scope)
    {
        var content = UserTasksEfModule.Chain.Upcast<UserTaskFormEntity>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]!;
        var value = JsonSerializer.Deserialize<UserTaskFormVersion>(content, Elsa.Serialization.Core.JsonBaseline.Options) ?? throw new InvalidDataException("The form version is invalid.");
        if (row.ScopeKey != Encode(scope) || row.ScopeHash != Hash(scope) || row.TenantKey != Encode(value.TenantId) || row.TenantHash != Hash(value.TenantId) ||
            row.FormKey != Encode(value.Form.Id) || row.VersionKey != Encode(value.Form.Version) || row.Id != CompositeId(scope, value.TenantId, value.Form.Id, value.Form.Version))
            throw new InvalidDataException("The form identity or projection differs.");
        return value;
    }
    private static UserTaskFormDraft ReadDraft(UserTaskFormEntity row, string scope)
    {
        var content = UserTasksEfModule.Chain.Upcast<UserTaskFormEntity>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]!;
        var value = JsonSerializer.Deserialize<UserTaskFormDraft>(content) ?? throw new InvalidDataException("The form draft is invalid.");
        if (row.ScopeKey != Encode(scope) || row.ScopeHash != Hash(scope) || row.TenantKey != Encode(value.TenantId) || row.TenantHash != Hash(value.TenantId) ||
            row.FormKey != Encode(value.Id) || row.VersionKey != Encode(UserTaskProtocol.FormDraftVersion) || row.Id != CompositeId(scope, value.TenantId, value.Id, UserTaskProtocol.FormDraftVersion) ||
            value.Revision < 1 || value.Form is { } form && form.Id != value.Id)
            throw new InvalidDataException("The form draft identity or revision differs.");
        return value;
    }
    private void EnsureClean()
    {
        if (context.ChangeTracker.HasChanges() || context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Task journal mutations require a clean independent persistence operation.");
    }
    private static void ValidateLimit(int limit) { if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit)); }
}

internal sealed class EfUserTaskPersistenceBoundary : IUserTaskPersistenceBoundary
{
    public bool CanRetainCompletionEvidence(IUserTaskStore journal, IWorkflowExecutionStateStore executions)
    {
        if (journal is not EfUserTaskStore tasks || executions is not EfWorkflowExecutionStateStore native ||
            !ReferenceEquals(tasks.Context, native.Context)) return false;
        try { return tasks.AccessContext.RequireScope() == native.AccessContext.RequireScope(); }
        catch (InvalidOperationException) { return false; }
    }
}
