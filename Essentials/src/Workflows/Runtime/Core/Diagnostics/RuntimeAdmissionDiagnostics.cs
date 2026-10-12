namespace Elsa.Workflows.Runtime.Core.Diagnostics;

/// <summary>
/// Process-wide, deterministic admission counters (RB1, #1235), the sibling of
/// <see cref="RuntimeSchedulerDispatchDiagnostics"/>. Independent of wall time, so "the limiter shed N commands" is
/// provable under fleet load instead of inferred from a throughput curve. A shed decision that is not countable is
/// not observable, and a visible refusal is the point of the whole mechanism.
///
/// <para>A singleton so it survives the per-command drain scopes. Purely diagnostic: reading or omitting these
/// counters never changes an admission decision or any durable state.</para>
/// </summary>
public sealed class RuntimeAdmissionDiagnostics
{
    private long _admitted;
    private long _shed;
    private long _limitIncreases;
    private long _limitDecreases;

    /// <summary>Commands admitted to run now.</summary>
    public long Admitted => Interlocked.Read(ref _admitted);

    /// <summary>Commands refused because the in-flight dispatch load was at or above the limit.</summary>
    public long Shed => Interlocked.Read(ref _shed);

    /// <summary>Additive increases the adaptive limit has taken.</summary>
    public long LimitIncreases => Interlocked.Read(ref _limitIncreases);

    /// <summary>Multiplicative decreases the adaptive limit has taken.</summary>
    public long LimitDecreases => Interlocked.Read(ref _limitDecreases);

    /// <summary>Records one admitted command.</summary>
    public void RecordAdmitted() => Interlocked.Increment(ref _admitted);

    /// <summary>Records one shed command.</summary>
    public void RecordShed() => Interlocked.Increment(ref _shed);

    /// <summary>Records one additive increase of the adaptive limit.</summary>
    public void RecordLimitIncrease() => Interlocked.Increment(ref _limitIncreases);

    /// <summary>Records one multiplicative decrease of the adaptive limit.</summary>
    public void RecordLimitDecrease() => Interlocked.Increment(ref _limitDecreases);

    /// <summary>Resets all counters (test/benchmark isolation between runs sharing a host).</summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _admitted, 0);
        Interlocked.Exchange(ref _shed, 0);
        Interlocked.Exchange(ref _limitIncreases, 0);
        Interlocked.Exchange(ref _limitDecreases, 0);
    }
}
