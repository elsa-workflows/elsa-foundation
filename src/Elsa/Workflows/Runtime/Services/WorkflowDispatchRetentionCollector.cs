using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Bounded, fail-closed retention sweep for terminal detached-dispatch history.</summary>
public sealed class WorkflowDispatchRetentionCollector(
    IWorkflowDispatchQueryStore queryStore,
    IWorkflowDispatchDeleteStore deleteStore,
    IWorkflowExecutionStateStore executionStateStore,
    IPersistenceAccessContextAccessor accessContextAccessor,
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
        // Queries run against the ambient persistence scope, so the cursor position is only meaningful for that scope.
        // A host sweeps scope after scope; a shared position would apply one scope's continuation to the next and skip
        // that scope's older records.
        var partition = WorkflowDispatchRetentionCursor.PartitionOf(accessContextAccessor.Current);
        try
        {
            foreach (var status in TerminalStatuses)
            {
                var continuation = _cursor.Get(partition, status);
                var records = await QueryPageAsync(status, continuation, cancellationToken);
                if (records.Count == 0 && continuation is not null)
                {
                    _cursor.Reset(partition, status);
                    records = await QueryPageAsync(status, null, cancellationToken);
                }
                foreach (var record in records)
                    candidates[record.DispatchId] = record;
                if (records.Count == WorkflowDispatchQuery.MaximumTake)
                {
                    var last = records.Last();
                    _cursor.Set(partition, status, new WorkflowDispatchRetentionContinuation(last.CreatedAt, last.DispatchId));
                }
                else
                    _cursor.Reset(partition, status);
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

/// <summary>
/// Process-stable continuation state that prevents retained prefix records from starving later cleanup pages. Positions
/// are kept per persistence partition, because each partition queries a different set of records.
/// </summary>
public sealed class WorkflowDispatchRetentionCursor
{
    private readonly object _gate = new();
    private readonly Dictionary<(WorkflowDispatchRetentionPartition Partition, WorkflowDispatchStatus Status), WorkflowDispatchRetentionContinuation> _continuations = new();

    /// <summary>
    /// The partition a sweep reads from. A global sweep and an across-scopes sweep both have no scope but read different
    /// records, so they are kept apart.
    /// </summary>
    public static WorkflowDispatchRetentionPartition PartitionOf(PersistenceAccessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new(context.Scope, context.AcrossScopes);
    }

    public WorkflowDispatchRetentionContinuation? Get(WorkflowDispatchRetentionPartition partition, WorkflowDispatchStatus status)
    {
        lock (_gate)
            return _continuations.GetValueOrDefault((partition, status));
    }

    public void Set(WorkflowDispatchRetentionPartition partition, WorkflowDispatchStatus status, WorkflowDispatchRetentionContinuation continuation)
    {
        lock (_gate)
            _continuations[(partition, status)] = continuation;
    }

    public void Reset(WorkflowDispatchRetentionPartition partition, WorkflowDispatchStatus status)
    {
        lock (_gate)
            _continuations.Remove((partition, status));
    }
}

/// <summary>The persistence partition a retention sweep reads: one scope, the global partition, or all scopes.</summary>
public sealed record WorkflowDispatchRetentionPartition(PersistenceScope? Scope, bool AcrossScopes);
