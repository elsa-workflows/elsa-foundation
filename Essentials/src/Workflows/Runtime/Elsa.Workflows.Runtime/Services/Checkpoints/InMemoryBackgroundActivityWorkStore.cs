using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Checkpoints;

/// <summary>Process-local worker store sharing the checkpoint store's atomic participant state.</summary>
public sealed class InMemoryBackgroundActivityWorkStore(
    InMemoryRuntimeCheckpointStoreState state,
    IPersistenceAccessContextAccessor accessContextAccessor) : IBackgroundActivityWorkStore
{
    public ValueTask<BackgroundActivityWorkItem?> FindAsync(
        string workflowExecutionId,
        string workId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workId);
        cancellationToken.ThrowIfCancellationRequested();
        var key = Key(workflowExecutionId, workId);
        lock (state.SyncRoot)
            return ValueTask.FromResult(state.BackgroundActivityWorkItems.GetValueOrDefault(key));
    }

    public async ValueTask<BackgroundActivityWorkClaim?> ClaimNextAsync(
        string ownerId,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout));
        var scope = Scope();
        await state.WriteGate.WaitAsync(cancellationToken);
        try
        {
            lock (state.SyncRoot)
            {
                foreach (var pair in state.BackgroundActivityWorkItems
                             .Where(pair => StringComparer.Ordinal.Equals(pair.Key.Scope, scope) &&
                                            (pair.Value.Status == BackgroundActivityWorkStatus.Pending ||
                                             pair.Value.Status == BackgroundActivityWorkStatus.Claimed && pair.Value.VisibleUntil <= now))
                             .OrderBy(pair => pair.Value.Request.WorkflowExecutionId, StringComparer.Ordinal)
                             .ThenBy(pair => pair.Value.RecordedAt)
                             .ThenBy(pair => pair.Key.WorkId, StringComparer.Ordinal))
                {
                    var updated = BackgroundActivityWorkLifecycle.TryClaim(pair.Value, ownerId, now, visibilityTimeout, out var claim);
                    if (updated is null)
                        continue;
                    state.BackgroundActivityWorkItems[pair.Key] = updated;
                    return claim;
                }
            }

            return null;
        }
        finally
        {
            state.WriteGate.Release();
        }
    }

    public async ValueTask<BackgroundActivityWorkClaim?> RenewClaimAsync(
        BackgroundActivityWorkClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        var key = Key(claim.WorkflowExecutionId, claim.WorkId);
        await state.WriteGate.WaitAsync(cancellationToken);
        try
        {
            lock (state.SyncRoot)
            {
                if (!state.BackgroundActivityWorkItems.TryGetValue(key, out var current))
                    return null;
                var updated = BackgroundActivityWorkLifecycle.TryRenewClaim(current, claim, now, visibilityTimeout, out var renewed);
                if (updated is null)
                    return null;
                state.BackgroundActivityWorkItems[key] = updated;
                return renewed;
            }
        }
        finally
        {
            state.WriteGate.Release();
        }
    }

    public async ValueTask<BackgroundActivityWorkItem> RecordResultAsync(
        BackgroundActivityWorkClaim claim,
        BackgroundActivityWorkTerminalResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);
        var key = Key(claim.WorkflowExecutionId, claim.WorkId);
        await state.WriteGate.WaitAsync(cancellationToken);
        try
        {
            lock (state.SyncRoot)
            {
                if (!state.BackgroundActivityWorkItems.TryGetValue(key, out var current))
                    throw new BackgroundActivityWorkConflictException(claim.WorkflowExecutionId, claim.WorkId, "the work item was not found in this persistence scope");
                var updated = BackgroundActivityWorkLifecycle.RecordResult(current, claim, result, now);
                state.BackgroundActivityWorkItems[key] = updated;
                return updated;
            }
        }
        finally
        {
            state.WriteGate.Release();
        }
    }

    public ValueTask<IReadOnlyCollection<BackgroundActivityWorkItem>> ListResultReadyAsync(
        int limit,
        string? workflowExecutionId = null,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit));
        if (workflowExecutionId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = Scope();
        lock (state.SyncRoot)
        {
            IReadOnlyCollection<BackgroundActivityWorkItem> items = state.BackgroundActivityWorkItems
                .Where(pair => StringComparer.Ordinal.Equals(pair.Key.Scope, scope) &&
                               pair.Value.Status == BackgroundActivityWorkStatus.ResultReady &&
                               (workflowExecutionId is null || StringComparer.Ordinal.Equals(pair.Key.WorkflowExecutionId, workflowExecutionId)))
                .OrderBy(pair => pair.Value.UpdatedAt)
                .ThenBy(pair => pair.Key.WorkflowExecutionId, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.WorkId, StringComparer.Ordinal)
                .Take(limit)
                .Select(pair => pair.Value)
                .ToArray();
            return ValueTask.FromResult(items);
        }
    }

    private string Scope() => accessContextAccessor.Current.RequireScope().Value;

    private (string Scope, string WorkflowExecutionId, string WorkId) Key(string workflowExecutionId, string workId) =>
        (Scope(), workflowExecutionId, workId);
}
