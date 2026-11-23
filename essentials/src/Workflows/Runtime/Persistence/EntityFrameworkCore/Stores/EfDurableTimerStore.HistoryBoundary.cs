using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfDurableTimerStore
{
    public ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken cancellationToken = default)
    {
        ValidateTimer(timer);
        return HistoryBoundary(timer.WorkflowExecutionId, "schedule-timer", () => SaveCoreAsyncHistory(timer, cancellationToken), cancellationToken, schedulerFailure: true);
    }

    public ValueTask<bool> HasPendingHistoryAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
        HistoryBoundary(workflowExecutionId, "has-pending-timer-history", () => HasPendingHistoryCoreAsync(workflowExecutionId, cancellationToken), cancellationToken);

    public ValueTask<RuntimeStorePage<DurableTimer>> ListPendingHistoryAsync(DurableTimerHistoryQuery query, CancellationToken cancellationToken = default) =>
        HistoryBoundary(null, "list-pending-timer-history", () => ListPendingHistoryCoreAsync(query, cancellationToken), cancellationToken);

    public ValueTask<bool> AcknowledgeHistoryAsync(string workflowExecutionId, string timerId, string historyEventId, CancellationToken cancellationToken = default) =>
        HistoryMutation(workflowExecutionId, "acknowledge-timer-history", () => AcknowledgeHistoryCoreAsync(workflowExecutionId, timerId, historyEventId, cancellationToken), cancellationToken);

    public ValueTask<RuntimeDurableTimerClaimTransitionResult> CompleteClaimWithHistoryAsync(RuntimeDurableTimerClaim claim,
        RuntimeHistoryObservation observation, CancellationToken cancellationToken = default) =>
        HistoryMutation(claim?.Timer.WorkflowExecutionId, "complete-timer-history-claim", () => CompleteClaimWithHistoryCoreAsync(claim!, observation, cancellationToken), cancellationToken);

    public async ValueTask RetireWithHistoryAsync(string workflowExecutionId, string timerId, RuntimeHistoryObservation observation, CancellationToken cancellationToken = default) =>
        _ = await HistoryMutation<object?>(workflowExecutionId, "retire-timer-history", async () =>
        { await RetireWithHistoryCoreAsync(workflowExecutionId, timerId, observation, cancellationToken); return null; }, cancellationToken);

    public ValueTask<bool> DischargeUnrecordedCompletionAsync(RuntimeCheckpointCommit completion, int limit = 256, CancellationToken cancellationToken = default) =>
        HistoryBoundary(completion?.WorkflowExecutionId, "discharge-unrecorded-timer-history", () => DischargeUnrecordedCompletionCoreAsync(completion!, limit, cancellationToken), cancellationToken, schedulerFailure: true);

    private ValueTask<T> HistoryMutation<T>(string? run, string operation, Func<ValueTask<T>> action, CancellationToken token) =>
        HistoryBoundary(run, operation, async () =>
        {
            RequireCleanHistoryMutation();
            var captured = accessContextAccessor.Current; CheckHistoryAccess(captured, token);
            await using var transaction = await context.Database.BeginTransactionAsync(token); CheckHistoryAccess(captured, token);
            var result = await action(); CheckHistoryAccess(captured, token);
            await transaction.CommitAsync(token); CheckHistoryAccess(captured, token);
            return result;
        }, token, schedulerFailure: true);

    private static async ValueTask<T> HistoryBoundary<T>(string? run, string operation, Func<ValueTask<T>> action, CancellationToken token,
        bool schedulerFailure = false)
    {
        token.ThrowIfCancellationRequested();
        try { return await action(); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { token.ThrowIfCancellationRequested(); throw; }
        catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException)
        {
            token.ThrowIfCancellationRequested();
            throw new RuntimeHistoryIntegrityException(run ?? "timer-sources", "timer source evidence could not be interpreted", error);
        }
        catch (Exception error) when (EfRuntimeHistoryCheckpoint.IsProviderUnavailable(error, token))
        {
            token.ThrowIfCancellationRequested();
            var contextual = run is null ? error : new RuntimeHistoryUnavailableException(run, operation, error);
            if (!schedulerFailure && contextual is RuntimeHistoryUnavailableException unavailable) throw unavailable;
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable",
                $"Timer history operation '{operation}' could not be acknowledged; responsibility may remain owed.", contextual);
        }
    }
}
