namespace Elsa.Workflows.Runtime.Core.Diagnostics;

/// <summary>
/// Process-wide, deterministic dispatch/fusion counters (spec 123 FR-008). Independent of wall time, so the A/B
/// hop-count claim (~7 dispatches per ReplaySafe leaf edge → ~1–2 fused) is provable under fleet load. A singleton
/// so it survives the per-command drain scopes; the drainer records one dispatch per
/// <c>WorkflowSchedulerDrainer.DispatchAsync</c>, and the ReplaySafe fusion driver records one fused span per engaged
/// schedule→start→invoke pass. Both are pure diagnostics — reading or omitting them never changes durable behavior.
/// </summary>
public sealed class RuntimeSchedulerDispatchDiagnostics
{
    private long _dispatches;
    private long _fusedSpans;

    /// <summary>Total scheduler-work dispatches observed (one per <c>DispatchAsync</c>).</summary>
    public long Dispatches => Interlocked.Read(ref _dispatches);

    /// <summary>Total ReplaySafe fused spans engaged (one per fused schedule→start→invoke pass).</summary>
    public long FusedSpans => Interlocked.Read(ref _fusedSpans);

    /// <summary>Records a single scheduler-work dispatch.</summary>
    public void RecordDispatch() => Interlocked.Increment(ref _dispatches);

    /// <summary>Records a single engaged ReplaySafe fused span.</summary>
    public void RecordFusedSpan() => Interlocked.Increment(ref _fusedSpans);

    /// <summary>Resets both counters (test/benchmark isolation between runs sharing a provider).</summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _dispatches, 0);
        Interlocked.Exchange(ref _fusedSpans, 0);
    }
}
