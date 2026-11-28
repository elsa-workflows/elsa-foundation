using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed class EfOutboxWorkflowDispatchCompletion(RuntimeDbContext context, IPersistenceAccessContextAccessor accessContextAccessor)
    : IEfOutboxWorkflowDispatchCompletion
{
    public async ValueTask<EfOutboxWorkflowDispatchFacts> ReadAsync(string scope, WorkflowDispatchRecord projectedDispatch, CancellationToken token)
    {
        var dispatchRow = await context.WorkflowDispatches.SingleOrDefaultAsync(
            candidate => candidate.Id == WorkflowDispatchEfSupport.RowId(scope, projectedDispatch.DispatchId) &&
                         candidate.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
                         candidate.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope),
            token)
            ?? throw new InvalidOperationException(
                $"Workflow dispatch '{projectedDispatch.DispatchId}' was not found in the atomic completion context.");
        var existingDispatch = WorkflowDispatchEfSupport.ReadChecked(dispatchRow, scope, projectedDispatch.DispatchId);
        accessContextAccessor.Current.EnsureTenantScope(existingDispatch.TenantId);

        var childId = existingDispatch.ChildWorkflowExecutionId;
        var childRow = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == EfWorkflowExecutionStateStore.CreateIdForAtomicParticipant(scope, childId) &&
                         candidate.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
                         candidate.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope),
            token);
        var childExecution = childRow is null
            ? null
            : EfWorkflowExecutionStateStore.ReadChecked(childRow, scope, childId);
        return new(dispatchRow, existingDispatch, childExecution);
    }

    public void Stage(string scope, EfOutboxWorkflowDispatchFacts existing, WorkflowDispatchRecord projected) =>
        WorkflowDispatchEfSupport.Copy(existing.Row, projected, scope, checked(existing.Row.Revision + 1));

    public async ValueTask<bool> MatchesAsync(string scope, WorkflowDispatchRecord dispatch, CancellationToken token)
    {
        var dispatchEntry = await context.WorkflowDispatches.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.Id == WorkflowDispatchEfSupport.RowId(scope, dispatch.DispatchId), token);
        return dispatchEntry is not null &&
            WorkflowDispatchLifecycle.RecordsEqual(WorkflowDispatchEfSupport.ReadChecked(dispatchEntry, scope, dispatch.DispatchId), dispatch);
    }
}
