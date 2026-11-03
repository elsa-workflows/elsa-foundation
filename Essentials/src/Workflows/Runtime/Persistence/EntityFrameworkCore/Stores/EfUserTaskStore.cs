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
        if (user is not null) source = source.Where(x => x.GroupKey != null
            ? groupHashes.Contains(x.GroupHash!) && groups.Contains(x.GroupKey) && (x.ClaimantKey == null || (x.ClaimantHash == userHash && x.ClaimantKey == user)) : x.AssigneeHash == userHash && x.AssigneeKey == user);
        source = query.View switch
        {
            "mine" => source.Where(x => x.Status < (int)UserTaskStatus.Completed && ((x.AssigneeHash == userHash && x.AssigneeKey == user) || (x.ClaimantHash == userHash && x.ClaimantKey == user))),
            "queue" => source.Where(x => x.Status == (int)UserTaskStatus.Open && x.GroupKey != null && groups.Contains(x.GroupKey) && x.ClaimantKey == null),
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
        var source = context.UserTaskForms.AsNoTracking().Where(x => x.ScopeHash == sh && x.ScopeKey == sk && x.TenantHash == th && x.TenantKey == tk);
        if (after is not null) source = source.Where(x => string.Compare(x.Id, after) > 0);
        var rows = await source.OrderBy(x => x.Id).Take(limit + 1).ToArrayAsync(cancellationToken);
        return new(rows.Take(limit).Select(x => ReadForm(x, scope)).ToArray(), rows.Length > limit ? rows[limit - 1].Id : null);
    }

    public async ValueTask<bool> TryCreateFormAsync(UserTaskFormVersion version, CancellationToken cancellationToken = default)
    {
        EnsureClean(); access.Current.EnsureTenantScope(version.TenantId);
        var scope = Scope;
        var row = new UserTaskFormEntity { Id = CompositeId(scope, version.TenantId, version.Form.Id, version.Form.Version),
            ScopeKey = Encode(scope), ScopeHash = Hash(scope), TenantKey = Encode(version.TenantId), TenantHash = Hash(version.TenantId), FormKey = Encode(version.Form.Id),
            VersionKey = Encode(version.Form.Version), ContentJson = JsonSerializer.Serialize(version) };
        context.UserTaskForms.Add(row);
        try { await context.SaveChangesAsync(cancellationToken); return true; }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey)) { return false; }
        finally { context.Entry(row).State = EntityState.Detached; }
    }

    private static UserTaskEntity ToRow(UserTaskRecord task, string scope) => new()
    {
        Id = CompositeId(scope, task.TenantId, task.Id), ScopeKey = Encode(scope), ScopeHash = Hash(scope), TenantKey = Encode(task.TenantId), TenantHash = Hash(task.TenantId),
        ClaimantHash = task.ClaimantId is null ? null : Hash(task.ClaimantId), TaskId = task.Id, ExecutionHash = Hash(task.WorkflowExecutionId), AssigneeHash = task.AssigneeId is null ? null : Hash(task.AssigneeId), GroupHash = task.GroupId is null ? null : Hash(task.GroupId), ExecutionKey = Encode(task.WorkflowExecutionId), AssigneeKey = task.AssigneeId is null ? null : Encode(task.AssigneeId),
        GroupKey = task.GroupId is null ? null : Encode(task.GroupId), ClaimantKey = task.ClaimantId is null ? null : Encode(task.ClaimantId),
        SearchTitle = task.Title.ToUpperInvariant(), Status = (int)task.Status, Revision = task.Revision, ContentJson = JsonSerializer.Serialize(task)
    };
    private static UserTaskRecord Read(UserTaskEntity row, string scope)
    {
        var content = UserTasksEfModule.Chain.Upcast<UserTaskEntity>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]!;
        var value = JsonSerializer.Deserialize<UserTaskRecord>(content) ?? throw new InvalidDataException("The task journal is invalid.");
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
        var value = JsonSerializer.Deserialize<UserTaskFormVersion>(content) ?? throw new InvalidDataException("The form version is invalid.");
        if (row.ScopeKey != Encode(scope) || row.ScopeHash != Hash(scope) || row.TenantKey != Encode(value.TenantId) || row.TenantHash != Hash(value.TenantId) ||
            row.FormKey != Encode(value.Form.Id) || row.VersionKey != Encode(value.Form.Version) || row.Id != CompositeId(scope, value.TenantId, value.Form.Id, value.Form.Version))
            throw new InvalidDataException("The form identity or projection differs.");
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
