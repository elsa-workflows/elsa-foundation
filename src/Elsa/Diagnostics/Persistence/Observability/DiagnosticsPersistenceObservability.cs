namespace Elsa.Diagnostics.Persistence.Observability;

public enum DiagnosticsDrainState
{
    Created,
    Running,
    Closing,
    Draining,
    Stopped,
    TimedOut
}

public enum DiagnosticsPersistenceOperation
{
    Commit,
    Retention
}

public enum DiagnosticsPersistenceLossReason
{
    QueueOverflow,
    RetryExhausted,
    ShutdownTimeout,
    WriteAfterClosure,
    DurableRetentionDeletion,
    SubscriberDelivery
}

/// <summary>
/// Low-cardinality pull-observability seam. It deliberately accepts no diagnostic payloads, identifiers,
/// tenant values, or free-form labels, preventing the drain from recursively emitting captured signals.
/// </summary>
public interface IDiagnosticsPersistenceObserver
{
    void RecordState(DiagnosticsDrainState state);
    void RecordRetry(DiagnosticsPersistenceOperation operation, int attempt, int maxAttempts);
    void RecordOperationFailure(DiagnosticsPersistenceOperation operation);
    void RecordLoss(DiagnosticsPersistenceLossReason reason, int count);
}

public sealed record DiagnosticsPersistenceSnapshot(
    DiagnosticsDrainState State,
    long CommitRetries,
    long RetentionRetries,
    long CommitFailures,
    long RetentionFailures,
    IReadOnlyDictionary<DiagnosticsPersistenceLossReason, long> Losses);

/// <summary>Thread-safe, pull-only lifecycle and loss counters shared by diagnostics adapters.</summary>
public sealed class DiagnosticsPersistenceCounters : IDiagnosticsPersistenceObserver
{
    private readonly long[] _losses = new long[Enum.GetValues<DiagnosticsPersistenceLossReason>().Length];
    private long _commitRetries;
    private long _retentionRetries;
    private long _commitFailures;
    private long _retentionFailures;
    private int _state = (int)DiagnosticsDrainState.Created;

    public void RecordState(DiagnosticsDrainState state) => Volatile.Write(ref _state, (int)state);

    public void RecordRetry(DiagnosticsPersistenceOperation operation, int attempt, int maxAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(attempt, maxAttempts);
        Interlocked.Increment(ref operation == DiagnosticsPersistenceOperation.Commit
            ? ref _commitRetries
            : ref _retentionRetries);
    }

    public void RecordOperationFailure(DiagnosticsPersistenceOperation operation) =>
        Interlocked.Increment(ref operation == DiagnosticsPersistenceOperation.Commit
            ? ref _commitFailures
            : ref _retentionFailures);

    public void RecordLoss(DiagnosticsPersistenceLossReason reason, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        Interlocked.Add(ref _losses[(int)reason], count);
    }

    public DiagnosticsPersistenceSnapshot Snapshot()
    {
        var losses = Enum.GetValues<DiagnosticsPersistenceLossReason>()
            .ToDictionary(reason => reason, reason => Interlocked.Read(ref _losses[(int)reason]));
        return new(
            (DiagnosticsDrainState)Volatile.Read(ref _state),
            Interlocked.Read(ref _commitRetries),
            Interlocked.Read(ref _retentionRetries),
            Interlocked.Read(ref _commitFailures),
            Interlocked.Read(ref _retentionFailures),
            losses);
    }
}

internal sealed class NullDiagnosticsPersistenceObserver : IDiagnosticsPersistenceObserver
{
    public static NullDiagnosticsPersistenceObserver Instance { get; } = new();
    public void RecordState(DiagnosticsDrainState state) { }
    public void RecordRetry(DiagnosticsPersistenceOperation operation, int attempt, int maxAttempts) { }
    public void RecordOperationFailure(DiagnosticsPersistenceOperation operation) { }
    public void RecordLoss(DiagnosticsPersistenceLossReason reason, int count) { }
}
