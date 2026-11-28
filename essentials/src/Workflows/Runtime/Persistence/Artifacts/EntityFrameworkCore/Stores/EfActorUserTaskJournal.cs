using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed class EfActorUserTaskJournal(RuntimeDbContext context, IPersistenceAccessContextAccessor access)
    : EfUserTaskArchiveStore(context, access), IActorUserTaskJournal
{
    protected override bool ReadsActorTasks => true;

    public override async ValueTask<bool> TryWriteAsync(UserTaskRecord record, long? expectedRevision, CancellationToken cancellationToken = default)
    {
        if (!IsActorTaskId(record.Id) || record.ActorRecipient is not { } recipient || recipient.ActorId != record.WorkflowExecutionId ||
            recipient.FrameId != record.InvocationId || string.IsNullOrWhiteSpace(recipient.WaitId) || record.Recipient is not null ||
            record.OperationKey != record.Id || record.Status == UserTaskStatus.Staged)
            throw new UserTaskException("unavailable", "This journal writes only its own Actor tasks.");
        if (record.Revision != (expectedRevision ?? 0) + 1)
            throw new ArgumentException("A task write must advance one revision.", nameof(record));
        if (expectedRevision is null && record is not { Status: UserTaskStatus.Open, Decision: null })
            throw new ArgumentException("An Actor task must open before it accepts a response.", nameof(record));

        EnsureClean();
        access.Current.EnsureTenantScope(record.TenantId);
        if (expectedRevision is not null)
        {
            var prior = await FindAsync(record.TenantId, record.Id, cancellationToken);
            if (prior?.Revision != expectedRevision) return false;
            if (prior!.ActorRecipient != recipient || prior.ExecutableNodeId != record.ExecutableNodeId ||
                prior.AssigneeId != record.AssigneeId || prior.GroupId != record.GroupId || !AdmitsTransition(prior, record))
                throw new UserTaskException("conflict", "The Actor task no longer accepts this transition.");
        }

        var row = ToRow(record, Scope, actorJournal: true);
        context.UserTasks.Attach(row);
        var entry = context.Entry(row);
        entry.State = expectedRevision is null ? EntityState.Added : EntityState.Modified;
        if (expectedRevision is { } revision) entry.Property(x => x.Revision).OriginalValue = revision;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey | EfWriteConflict.Concurrency))
        {
            return false;
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }

    private static bool AdmitsTransition(UserTaskRecord prior, UserTaskRecord next) => prior.Status switch
    {
        UserTaskStatus.Open or UserTaskStatus.Claimed when next.Status == UserTaskStatus.CompletionPending =>
            prior.Decision is null && next.Decision?.Response is { } response && response.TaskId == prior.Id &&
                prior.Actions.Contains(response.Action, StringComparer.Ordinal) && response.Data.ValueKind == JsonValueKind.Object,
        UserTaskStatus.Open or UserTaskStatus.Claimed => next.Decision is null &&
            next.Status is UserTaskStatus.Open or UserTaskStatus.Claimed or UserTaskStatus.Cancelled,
        UserTaskStatus.CompletionPending => next.Status is UserTaskStatus.Completed or UserTaskStatus.Cancelled &&
            prior.Decision is { } accepted && next.Decision is { } reply &&
            accepted.RequestId == reply.RequestId && accepted.ActorId == reply.ActorId && accepted.AcceptedAt == reply.AcceptedAt &&
            accepted.Response.TaskId == reply.Response.TaskId && accepted.Response.CompletionId == reply.Response.CompletionId &&
            accepted.Response.Action == reply.Response.Action && JsonElement.DeepEquals(accepted.Response.Data, reply.Response.Data),
        _ => false
    };

    public async ValueTask<UserTaskPage> RecoverOwnedAsync(string tenantId, int limit, string? after, CancellationToken cancellationToken = default)
    {
        ValidateLimit(limit);
        access.Current.EnsureTenantScope(tenantId);
        var scope = Scope;
        var scopeKey = ActorScopeKey(scope);
        var scopeHash = Hash(scopeKey);
        var tenantKey = Encode(tenantId);
        var tenantHash = Hash(tenantId);
        var source = context.UserTasks.AsNoTracking().Where(row => row.ScopeHash == scopeHash && row.ScopeKey == scopeKey &&
            row.TenantHash == tenantHash && row.TenantKey == tenantKey && row.Status < (int)UserTaskStatus.Completed);
        if (after is not null) source = source.Where(row => string.Compare(row.TaskId, after) > 0);
        var rows = await source.OrderBy(row => row.TaskId).Take(limit + 1).ToArrayAsync(cancellationToken);
        return new(rows.Take(limit).Select(row => Read(row, scope, actorJournal: true)).ToArray(),
            rows.Length > limit ? rows[limit - 1].TaskId : null);
    }
}
