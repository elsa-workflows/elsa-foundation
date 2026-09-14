namespace Elsa.Workflows.Design.Persistence.Core.Atomic;

/// <summary>Provider-neutral callbacks for the design atomic-write state machine.</summary>
public sealed class DesignAtomicWriteLane<TScope, TMarker, TStage, TResult>
    where TScope : IDisposable
    where TMarker : class
    where TResult : class
{
    public required string MarkerId { get; init; }
    public required Func<CancellationToken, Task<TMarker?>> LoadMarker { get; init; }
    public required Func<TScope> BeginScope { get; init; }
    public required Func<TScope, TStage, CancellationToken, Task> SaveMarker { get; init; }
    public required Func<TScope, CancellationToken, Task<DesignAtomicCommitDisposition>> Commit { get; init; }
    public required Action<TScope> Rollback { get; init; }
    public required Func<Exception, bool> ClassifyMarkerRace { get; init; }
    public required Func<Exception, bool> ClassifyUncertainCommit { get; init; }
    public required Func<Exception, CancellationToken, Task<TResult>> OnUncertainCommit { get; init; }
    public required Func<Exception, CancellationToken, Task<TResult?>> TryReconcileAfterCommit { get; init; }
    public Func<TScope, Task>? DisposeBeforeReconcile { get; init; }
    public required Func<int, CancellationToken, Task> Delay { get; init; }
    public required Func<TStage, bool> IsAccepted { get; init; }
    public required Func<TStage, TResult> OnCommitted { get; init; }
    public required Func<TMarker, TResult> OnReplay { get; init; }
    public required Func<TResult> OnRejected { get; init; }
    public int MarkerRaceAttemptBudget { get; init; } = 4;
    public bool DelayBeforeMarkerReload { get; init; }
    public bool ThrowIfCancellationRequestedEachAttempt { get; init; } = true;
    public bool RollbackOnAttemptFailure { get; init; } = true;
    public Func<Exception, string, Exception>? CreateExhaustedMarkerRaceException { get; init; }
}

public enum DesignAtomicCommitDisposition
{
    Committed,
    Rejected
}
