namespace Elsa.Persistence.Groundwork.DesignAtomic;

/// <summary>
/// Lane-owned callbacks for <see cref="DesignAtomicWriteProtocol"/>. The protocol classifies
/// retry, replay, and commit only; each design catalog supplies its own document kind, marker
/// identity, persistence operations, and uncertain-commit policy.
/// </summary>
/// <remarks>
/// Dual-lane hosts must keep two public <c>IDesignAtomicWriter</c> contracts. Unifying them into
/// one DI service type would let <c>TryAddScoped</c> keep a single implementation.
/// </remarks>
public sealed class DesignAtomicWriteLane<TScope, TMarker, TStage, TResult>
    where TScope : IDisposable
{
    /// <summary>The catalog's operation-ledger unit id (for example <c>workflowDesignOperation</c>).</summary>
    public required string DocumentKind { get; init; }

    /// <summary>The durable marker id for this operation. Formulas stay in the calling lane.</summary>
    public required string MarkerId { get; init; }

    public required Func<CancellationToken, Task<TMarker?>> LoadMarker { get; init; }

    public required Func<TScope> BeginScope { get; init; }

    public required Func<TScope, TStage, CancellationToken, Task> SaveMarker { get; init; }

    public required Func<TScope, CancellationToken, Task<DesignAtomicCommitDisposition>> Commit { get; init; }

    public required Action<TScope> Rollback { get; init; }

    public required Func<Exception, bool> ClassifyMarkerRace { get; init; }

    /// <summary>
    /// True when the exception is an uncertain-commit signal for the outer retry loop
    /// (workflow lane). The activity lane leaves this false and reconciles inside
    /// <see cref="TryReconcileAfterCommit"/> instead.
    /// </summary>
    public required Func<Exception, bool> ClassifyUncertainCommit { get; init; }

    /// <summary>
    /// Outer-loop uncertain-commit handler. The workflow lane waits for the marker to become
    /// visible; the activity lane never classifies loop-level uncertain commit.
    /// </summary>
    public required Func<Exception, CancellationToken, Task<TResult>> OnUncertainCommit { get; init; }

    /// <summary>
    /// Called after <see cref="Commit"/> throws a non-race, non-cancellation exception.
    /// Return a result to treat the commit as reconciled; return null to rethrow.
    /// </summary>
    public required Func<Exception, CancellationToken, Task<TResult?>> TryReconcileAfterCommit { get; init; }

    public required Func<int, CancellationToken, Task> Delay { get; init; }

    public required Func<TStage, bool> IsAccepted { get; init; }

    public required Func<TStage, TResult> OnCommitted { get; init; }

    public required Func<TMarker, TResult> OnReplay { get; init; }

    public required Func<TResult> OnRejected { get; init; }

    public int MarkerRaceAttemptBudget { get; init; } = 4;

    /// <summary>
    /// When true (activity lane), a marker-race retry delays before reloading the winner.
    /// When false (workflow lane), the winner is reloaded first.
    /// </summary>
    public bool DelayBeforeMarkerReload { get; init; }

    /// <summary>
    /// When true (workflow lane), each retry iteration observes cancellation before staging.
    /// </summary>
    public bool ThrowIfCancellationRequestedEachAttempt { get; init; }

    /// <summary>
    /// When true (workflow lane), attempt failures other than loop-level uncertain commit
    /// call <see cref="Rollback"/> inside a best-effort wrapper.
    /// </summary>
    public bool RollbackOnAttemptFailure { get; init; }

    /// <summary>
    /// Builds the exception thrown after the marker-race budget is exhausted and no winner
    /// is visible. Null (activity lane) rethrows the original conflict exception.
    /// </summary>
    public Func<Exception, string, Exception>? CreateExhaustedMarkerRaceException { get; init; }
}

/// <summary>Classification of a lane commit that did not throw.</summary>
public enum DesignAtomicCommitDisposition
{
    Committed,
    Rejected
}
