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
/// Replacement contract: exactly one low-cardinality pull observer is composed per drain. It deliberately
/// accepts no diagnostic payloads, identifiers, tenant values, or free-form labels, preventing the drain
/// from recursively emitting captured signals.
/// </summary>
public interface IDiagnosticsPersistenceObserver
{
    void RecordState(DiagnosticsDrainState state);
    /// <summary>Records one or more captures accepted by the bounded drain, without their contents or identifiers.</summary>
    void RecordAccepted(long count) { }
    /// <summary>Records the instantaneous bounded-queue depth. Implementations retain its high-water mark.</summary>
    void RecordQueueDepth(long depth) { }
    /// <summary>
    /// Records an ordered bounded-queue depth sample. Drains emit monotonically increasing sequences so
    /// asynchronous observers can discard stale callbacks without receiving captured data.
    /// </summary>
    void RecordQueueDepth(long depth, long sequence) => RecordQueueDepth(depth);
    void RecordRetry(DiagnosticsPersistenceOperation operation, int attempt, int maxAttempts);
    void RecordOperationFailure(DiagnosticsPersistenceOperation operation);
    void RecordLoss(DiagnosticsPersistenceLossReason reason, long count);
}

public sealed record DiagnosticsPersistenceSnapshot(
    DiagnosticsDrainState State,
    long Accepted,
    long QueueDepth,
    long QueueHighWater,
    long CommitRetries,
    long RetentionRetries,
    long CommitFailures,
    long RetentionFailures,
    IReadOnlyDictionary<DiagnosticsPersistenceLossReason, long> Losses);

/// <summary>Thread-safe, pull-only lifecycle and loss counters shared by diagnostics adapters.</summary>
public sealed class DiagnosticsPersistenceCounters : IDiagnosticsPersistenceObserver
{
    private readonly long[] _losses = new long[Enum.GetValues<DiagnosticsPersistenceLossReason>().Length];
    private readonly object _queueDepthGate = new();
    private long _accepted;
    private long _queueDepth;
    private long _queueHighWater;
    private long _queueDepthSequence;
    private long _commitRetries;
    private long _retentionRetries;
    private long _commitFailures;
    private long _retentionFailures;
    private int _state = (int)DiagnosticsDrainState.Created;

    public void RecordState(DiagnosticsDrainState state)
    {
        ValidateDefined(state);
        Volatile.Write(ref _state, (int)state);
    }

    public void RecordAccepted(long count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        Interlocked.Add(ref _accepted, count);
    }

    public void RecordQueueDepth(long depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        lock (_queueDepthGate)
            ApplyQueueDepth(depth);
    }

    public void RecordQueueDepth(long depth, long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        lock (_queueDepthGate)
        {
            if (sequence <= _queueDepthSequence)
                return;
            _queueDepthSequence = sequence;
            ApplyQueueDepth(depth);
        }
    }

    private void ApplyQueueDepth(long depth)
    {
        Volatile.Write(ref _queueDepth, depth);
        while (true)
        {
            var highWater = Volatile.Read(ref _queueHighWater);
            if (depth <= highWater || Interlocked.CompareExchange(ref _queueHighWater, depth, highWater) == highWater)
                return;
        }
    }

    public void RecordRetry(DiagnosticsPersistenceOperation operation, int attempt, int maxAttempts)
    {
        ValidateDefined(operation);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(attempt, maxAttempts);
        switch (operation)
        {
            case DiagnosticsPersistenceOperation.Commit:
                Interlocked.Increment(ref _commitRetries);
                break;
            case DiagnosticsPersistenceOperation.Retention:
                Interlocked.Increment(ref _retentionRetries);
                break;
        }
    }

    public void RecordOperationFailure(DiagnosticsPersistenceOperation operation)
    {
        ValidateDefined(operation);
        switch (operation)
        {
            case DiagnosticsPersistenceOperation.Commit:
                Interlocked.Increment(ref _commitFailures);
                break;
            case DiagnosticsPersistenceOperation.Retention:
                Interlocked.Increment(ref _retentionFailures);
                break;
        }
    }

    public void RecordLoss(DiagnosticsPersistenceLossReason reason, long count)
    {
        ValidateDefined(reason);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        Interlocked.Add(ref _losses[(int)reason], count);
    }

    public DiagnosticsPersistenceSnapshot Snapshot()
    {
        var losses = Enum.GetValues<DiagnosticsPersistenceLossReason>()
            .ToDictionary(reason => reason, reason => Interlocked.Read(ref _losses[(int)reason]));
        return new(
            (DiagnosticsDrainState)Volatile.Read(ref _state),
            Interlocked.Read(ref _accepted),
            Interlocked.Read(ref _queueDepth),
            Interlocked.Read(ref _queueHighWater),
            Interlocked.Read(ref _commitRetries),
            Interlocked.Read(ref _retentionRetries),
            Interlocked.Read(ref _commitFailures),
            Interlocked.Read(ref _retentionFailures),
            losses);
    }

    private static void ValidateDefined<TEnum>(TEnum value, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? parameterName = null)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(parameterName, value, $"Unsupported {typeof(TEnum).Name} value.");
    }
}

internal sealed class NullDiagnosticsPersistenceObserver : IDiagnosticsPersistenceObserver
{
    public static NullDiagnosticsPersistenceObserver Instance { get; } = new();
    public void RecordState(DiagnosticsDrainState state) { }
    public void RecordAccepted(long count) { }
    public void RecordQueueDepth(long depth) { }
    public void RecordQueueDepth(long depth, long sequence) { }
    public void RecordRetry(DiagnosticsPersistenceOperation operation, int attempt, int maxAttempts) { }
    public void RecordOperationFailure(DiagnosticsPersistenceOperation operation) { }
    public void RecordLoss(DiagnosticsPersistenceLossReason reason, long count) { }
}
