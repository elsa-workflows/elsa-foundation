using System.Runtime.ExceptionServices;

namespace Elsa.Workflows.Design.Persistence.Core.Atomic;

/// <summary>Shared retry, replay, rollback, and commit-classification state machine.</summary>
public static class DesignAtomicWriteProtocol
{
    public static async Task<TResult> ExecuteAsync<TScope, TMarker, TStage, TResult>(
        DesignAtomicWriteLane<TScope, TMarker, TStage, TResult> lane,
        Func<TScope, CancellationToken, Task<TStage>> stage,
        Func<CancellationToken, Task>? beforeAttempt,
        CancellationToken cancellationToken)
        where TScope : IDisposable
        where TMarker : class
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(lane);
        ArgumentNullException.ThrowIfNull(stage);
        var existing = await lane.LoadMarker(cancellationToken);
        if (existing is not null)
            return lane.OnReplay(existing);
        if (beforeAttempt is not null)
            await beforeAttempt(cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            if (lane.ThrowIfCancellationRequestedEachAttempt)
                cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await ExecuteAttemptAsync(lane, stage, cancellationToken);
            }
            catch (Exception exception) when (lane.ClassifyMarkerRace(exception))
            {
                var replayed = await HandleMarkerRaceAsync(lane, attempt, exception, cancellationToken);
                if (replayed is not null)
                    return replayed;
            }
            catch (Exception exception) when (lane.ClassifyUncertainCommit(exception))
            {
                return await lane.OnUncertainCommit(exception, cancellationToken);
            }
        }
    }

    private static async Task<TResult> ExecuteAttemptAsync<TScope, TMarker, TStage, TResult>(
        DesignAtomicWriteLane<TScope, TMarker, TStage, TResult> lane,
        Func<TScope, CancellationToken, Task<TStage>> stage,
        CancellationToken cancellationToken)
        where TScope : IDisposable
        where TMarker : class
        where TResult : class
    {
        using var scope = lane.BeginScope();
        try
        {
            var staged = await stage(scope, cancellationToken);
            ArgumentNullException.ThrowIfNull(staged);
            if (!lane.IsAccepted(staged))
            {
                lane.Rollback(scope);
                return lane.OnRejected();
            }
            await lane.SaveMarker(scope, staged, cancellationToken);
            DesignAtomicCommitDisposition disposition;
            try
            {
                disposition = await lane.Commit(scope, cancellationToken);
            }
            catch (Exception exception) when (lane.ClassifyMarkerRace(exception))
            {
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var reconciled = await lane.TryReconcileAfterCommit(exception, cancellationToken);
                if (reconciled is not null)
                    return reconciled;
                throw;
            }
            return disposition == DesignAtomicCommitDisposition.Rejected
                ? lane.OnRejected()
                : lane.OnCommitted(staged);
        }
        catch (Exception exception) when (lane.ClassifyMarkerRace(exception))
        {
            TryRollback(lane, scope);
            throw;
        }
        catch (Exception exception) when (lane.ClassifyUncertainCommit(exception))
        {
            throw;
        }
        catch
        {
            TryRollback(lane, scope);
            throw;
        }
    }

    private static async Task<TResult?> HandleMarkerRaceAsync<TScope, TMarker, TStage, TResult>(
        DesignAtomicWriteLane<TScope, TMarker, TStage, TResult> lane,
        int attempt,
        Exception exception,
        CancellationToken cancellationToken)
        where TScope : IDisposable
        where TMarker : class
        where TResult : class
    {
        if (lane.DelayBeforeMarkerReload && attempt < lane.MarkerRaceAttemptBudget)
            await lane.Delay(attempt, cancellationToken);
        var winner = await lane.LoadMarker(cancellationToken);
        if (winner is not null)
            return lane.OnReplay(winner);
        if (attempt >= lane.MarkerRaceAttemptBudget)
        {
            if (lane.CreateExhaustedMarkerRaceException is not null)
                throw lane.CreateExhaustedMarkerRaceException(exception, lane.MarkerId);
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
        if (!lane.DelayBeforeMarkerReload)
            await lane.Delay(attempt, cancellationToken);
        return default;
    }

    private static void TryRollback<TScope, TMarker, TStage, TResult>(
        DesignAtomicWriteLane<TScope, TMarker, TStage, TResult> lane,
        TScope scope)
        where TScope : IDisposable
        where TMarker : class
        where TResult : class
    {
        if (!lane.RollbackOnAttemptFailure)
            return;
        try { lane.Rollback(scope); }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) { }
    }
}
