using System.Diagnostics;
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

        for (var attempt = 1; ; attempt++)
        {
            if (lane.ThrowIfCancellationRequestedEachAttempt)
                cancellationToken.ThrowIfCancellationRequested();
            // Attempt setup can acquire locks and resolve provider state. Re-run it for every
            // retry so a failed attempt never carries a disposed/stale handle or stale snapshot
            // into the next stage execution.
            if (beforeAttempt is not null)
                await beforeAttempt(cancellationToken);
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
        var scopeLifetime = new ScopeLifetime<TScope>(lane.BeginScope());
        var scope = scopeLifetime.Value;
        Exception? primaryException = null;
        try
        {
            var staged = await stage(scope, cancellationToken);
            ArgumentNullException.ThrowIfNull(staged);
            if (!lane.IsAccepted(staged))
            {
                // Rejection is authoritative; provider cleanup failures must not replace it.
                TryRollback(lane, scope);
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
                if (!lane.ShouldReconcileAfterCommitFailure(exception))
                    throw;
                if (lane.DisposeBeforeReconcile is not null)
                    await scopeLifetime.DisposeBeforeReconcileAsync(
                        lane.DisposeBeforeReconcile,
                        exception,
                        lane.MarkerId);
                var reconciled = await lane.TryReconcileAfterCommit(exception, cancellationToken);
                if (reconciled is not null)
                    return reconciled;
                throw;
            }
            if (disposition == DesignAtomicCommitDisposition.Rejected)
            {
                TryRollback(lane, scope);
                return lane.OnRejected();
            }
            return lane.OnCommitted(staged);
        }
        catch (Exception exception) when (lane.ClassifyMarkerRace(exception))
        {
            primaryException = exception;
            if (!scopeLifetime.IsDisposed)
                TryRollback(lane, scope);
            throw;
        }
        catch (Exception exception) when (lane.ClassifyUncertainCommit(exception))
        {
            primaryException = exception;
            throw;
        }
        catch (Exception exception)
        {
            primaryException = exception;
            if (!scopeLifetime.IsDisposed)
                TryRollback(lane, scope);
            throw;
        }
        finally
        {
            scopeLifetime.DisposePreservingOutcome(primaryException, lane.MarkerId);
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
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            Trace.TraceWarning("Workflow design atomic rollback failed for marker '{0}': {1}", lane.MarkerId, exception);
        }
    }

    private sealed class ScopeLifetime<TScope>(TScope value)
        where TScope : IDisposable
    {
        private int disposed;

        public TScope Value { get; } = value;
        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public async Task DisposeBeforeReconcileAsync(
            Func<TScope, Task> disposeBeforeReconcile,
            Exception primaryException,
            string markerId)
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            try
            {
                await disposeBeforeReconcile(Value);
            }
            catch (Exception exception) when (IsNonFatalCleanupFailure(exception))
            {
                RecordCleanupFailure(primaryException, exception, markerId, "pre-reconcile scope disposal");
            }
        }

        public void DisposePreservingOutcome(Exception? primaryException, string markerId)
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            try
            {
                Value.Dispose();
            }
            catch (Exception exception) when (IsNonFatalCleanupFailure(exception))
            {
                if (primaryException is { } primary)
                    primary.Data[$"Elsa.Design.Atomic.ScopeCleanupFailure.{markerId}"] = exception;
                Trace.TraceWarning(
                    "Workflow design atomic scope disposal failed for marker '{0}': {1}",
                    markerId,
                    exception);
            }
        }
    }

    private static void RecordCleanupFailure(
        Exception primaryException,
        Exception cleanupException,
        string markerId,
        string phase)
    {
        primaryException.Data[$"Elsa.Design.Atomic.ScopeCleanupFailure.{markerId}.{phase}"] = cleanupException;
        Trace.TraceWarning(
            "Workflow design atomic {0} failed for marker '{1}': {2}",
            phase,
            markerId,
            cleanupException);
    }

    private static bool IsNonFatalCleanupFailure(Exception exception) =>
        exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException);
}
