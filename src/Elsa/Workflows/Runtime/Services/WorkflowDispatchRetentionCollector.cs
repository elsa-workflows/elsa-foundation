using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Bounded, fail-closed retention sweep for terminal detached-dispatch history.</summary>
public sealed class WorkflowDispatchRetentionCollector(
    IWorkflowDispatchQueryStore queryStore,
    IWorkflowDispatchDeleteStore deleteStore,
    IWorkflowExecutionStateStore executionStateStore,
    ILogger<WorkflowDispatchRetentionCollector> logger,
    WorkflowDispatchRetentionCursor? cursor = null) : IWorkflowDispatchRetentionCollector
{
    private readonly WorkflowDispatchRetentionCursor _cursor = cursor ?? new WorkflowDispatchRetentionCursor();
    private static readonly WorkflowDispatchStatus[] TerminalStatuses =
    [
        WorkflowDispatchStatus.Completed,
        WorkflowDispatchStatus.Faulted,
        WorkflowDispatchStatus.Cancelled,
        WorkflowDispatchStatus.DispatchFailed
    ];

    public async ValueTask<WorkflowDispatchRetentionSweepResult> SweepAsync(
        CancellationToken cancellationToken = default)
    {
        var candidates = new Dictionary<string, WorkflowDispatchRecord>(StringComparer.Ordinal);
        try
        {
            foreach (var status in TerminalStatuses)
            {
                var continuation = _cursor.Get(status);
                var records = await QueryPageAsync(status, continuation, cancellationToken);
                if (records.Count == 0 && continuation is not null)
                {
                    _cursor.Reset(status);
                    records = await QueryPageAsync(status, null, cancellationToken);
                }
                foreach (var record in records)
                    candidates[record.DispatchId] = record;
                if (records.Count == WorkflowDispatchQuery.MaximumTake)
                {
                    var last = records.Last();
                    _cursor.Set(status, new WorkflowDispatchRetentionContinuation(last.CreatedAt, last.DispatchId));
                }
                else
                    _cursor.Reset(status);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Workflow dispatch retention retained all records because candidate enumeration failed");
            return new(0, 0, 0, 1);
        }

        var deleted = 0;
        var retained = 0;
        var uncertain = 0;
        foreach (var candidate in candidates.Values)
        {
            try
            {
                if (!await BothExecutionsAreAbsentAsync(candidate, cancellationToken))
                {
                    retained++;
                    continue;
                }

                // Candidate reads are only advisory. Re-read both links immediately before deletion so a retained
                // execution observed in either position always wins over collection.
                if (!await BothExecutionsAreAbsentAsync(candidate, cancellationToken))
                {
                    retained++;
                    continue;
                }

                if (await deleteStore.TryDeleteAsync(candidate, cancellationToken))
                    deleted++;
                else
                    retained++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                uncertain++;
                logger.LogWarning(
                    exception,
                    "Workflow dispatch retention retained {DispatchId} because linked execution state was uncertain",
                    candidate.DispatchId);
            }
        }

        return new(candidates.Count, deleted, retained, uncertain);
    }

    private async ValueTask<bool> BothExecutionsAreAbsentAsync(
        WorkflowDispatchRecord record,
        CancellationToken cancellationToken) =>
        await executionStateStore.FindAsync(record.ParentWorkflowExecutionId, cancellationToken) is null &&
        await executionStateStore.FindAsync(record.ChildWorkflowExecutionId, cancellationToken) is null;

    private ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> QueryPageAsync(
        WorkflowDispatchStatus status,
        WorkflowDispatchRetentionContinuation? continuation,
        CancellationToken cancellationToken) =>
        queryStore.QueryAsync(
            new WorkflowDispatchQuery(
                status: status,
                afterCreatedAt: continuation?.CreatedAt,
                afterDispatchId: continuation?.DispatchId),
            cancellationToken);
}

public sealed record WorkflowDispatchRetentionContinuation(DateTimeOffset CreatedAt, string DispatchId);

/// <summary>Process-stable continuation state prevents retained prefix records from starving later cleanup pages.</summary>
public sealed class WorkflowDispatchRetentionCursor
{
    private readonly object _gate = new();
    private readonly Dictionary<WorkflowDispatchStatus, WorkflowDispatchRetentionContinuation> _continuations = new();

    public WorkflowDispatchRetentionContinuation? Get(WorkflowDispatchStatus status)
    {
        lock (_gate)
            return _continuations.GetValueOrDefault(status);
    }

    public void Set(WorkflowDispatchStatus status, WorkflowDispatchRetentionContinuation continuation)
    {
        lock (_gate)
            _continuations[status] = continuation;
    }

    public void Reset(WorkflowDispatchStatus status)
    {
        lock (_gate)
            _continuations.Remove(status);
    }
}
