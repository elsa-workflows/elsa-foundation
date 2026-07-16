using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Closes test scopes and reconciles only their detached DispatchWorkflow children.</summary>
public sealed class WorkflowTestScopeCleaner(
    IWorkflowTestScopeStore scopeStore,
    IWorkflowTestScopeCleanupStore cleanupStore,
    IWorkflowDispatchQueryStore dispatchStore) : IWorkflowTestScopeCleaner
{
    private const int PageSize = WorkflowDispatchQuery.MaximumTake;

    public async ValueTask<WorkflowTestScopeCleanupResult> CloseAsync(
        string scopeId,
        WorkflowTestScopeCloseReason reason,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        var close = await scopeStore.CloseAsync(
            new WorkflowTestScopeCloseRequest(scopeId, reason, requestedAt),
            cancellationToken);
        if (close.Disposition == WorkflowTestScopeCloseDisposition.NotFound || close.Record is null)
            return new WorkflowTestScopeCleanupResult(0, 0, 0, 0, 0);
        if (close.Record.State == WorkflowTestScopeState.Closed)
            return new WorkflowTestScopeCleanupResult(0, 0, 0, 0, 0);

        var scope = close.Record.Scope;
        var responsibilityAt = close.Record.ClosingAt
            ?? throw new InvalidOperationException("A closing workflow test scope requires a stable close timestamp.");
        var pending = await QueryActionableAsync(scope, WorkflowDispatchStatus.Pending, cancellationToken);
        var started = await QueryActionableAsync(scope, WorkflowDispatchStatus.Started, cancellationToken);
        var actionable = pending
            .Concat(started)
            .OrderBy(record => record.CreatedAt)
            .ThenBy(record => record.DispatchId, StringComparer.Ordinal)
            .Take(PageSize)
            .ToArray();
        var intents = actionable
            .ToDictionary(
                record => record.DispatchId,
                record => WorkflowDispatchCancelIntentFactory.Create(record, responsibilityAt),
                StringComparer.Ordinal);
        var result = await cleanupStore.CleanupAsync(
            scope,
            responsibilityAt,
            PageSize,
            intents,
            cancellationToken: cancellationToken);
        if (result.RemainingLive == 0 && result.ContinuationToken is null)
            await scopeStore.CompleteAsync(scope.ScopeId, responsibilityAt, cancellationToken);
        return result;
    }

    private async ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> QueryActionableAsync(
        WorkflowTestScope scope,
        WorkflowDispatchStatus status,
        CancellationToken cancellationToken)
    {
        var actionable = new List<WorkflowDispatchRecord>(PageSize);
        DateTimeOffset? afterCreatedAt = null;
        string? afterDispatchId = null;
        while (actionable.Count < PageSize)
        {
            var page = await dispatchStore.QueryAsync(
                new WorkflowDispatchQuery(
                    status: status,
                    take: PageSize,
                    afterCreatedAt: afterCreatedAt,
                    afterDispatchId: afterDispatchId,
                    testScopeId: scope.ScopeId),
                cancellationToken);
            if (page.Count == 0)
                break;
            actionable.AddRange(page.Where(record =>
                record.Mode == WorkflowDispatchMode.FireAndForget &&
                (status != WorkflowDispatchStatus.Started ||
                 !WorkflowDispatchLifecycle.IsTestScopeCancellationRequested(record))));
            if (page.Count < PageSize)
                break;
            var last = page.Last();
            afterCreatedAt = last.CreatedAt;
            afterDispatchId = last.DispatchId;
        }

        return actionable.Take(PageSize).ToArray();
    }

    public async ValueTask<int> SweepAsync(
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        if (observedAt == default)
            throw new ArgumentOutOfRangeException(nameof(observedAt));
        var processed = 0;
        string? continuation = null;
        do
        {
            var page = await scopeStore.QueryAsync(
                new WorkflowTestScopePageQuery(observedAt, PageSize, ContinuationToken: continuation),
                cancellationToken);
            foreach (var record in page.Items)
            {
                var reason = record.State == WorkflowTestScopeState.Open
                    ? WorkflowTestScopeCloseReason.Expired
                    : record.CloseReason!.Value;
                await CloseAsync(record.Scope.ScopeId, reason, observedAt, cancellationToken);
                processed++;
            }
            continuation = page.ContinuationToken;
        } while (continuation is not null);
        return processed;
    }
}
