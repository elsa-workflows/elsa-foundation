using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed class EfUserTaskStore(RuntimeDbContext context, IPersistenceAccessContextAccessor access)
    : EfUserTaskArchiveStore(context, access)
{
    public override async ValueTask<bool> TryWriteAsync(UserTaskRecord record, long? expectedRevision, CancellationToken cancellationToken = default)
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
