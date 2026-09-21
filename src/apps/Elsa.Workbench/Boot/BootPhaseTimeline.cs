using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Elsa.Workbench.Boot;

/// <summary>
/// Opt-in boot / first-request phase-timing recorder (spec 129, First-Request/Cold-Start Readiness).
///
/// This is a diagnostic-only instrument. It is created only when <c>Elsa:Boot:PhaseTiming:Enabled</c> is set,
/// so a production host that leaves the switch off constructs nothing and pays nothing. When enabled it records
/// wall marks against a single monotonic <see cref="Stopwatch"/> started at process entry and, at first-request
/// completion, prints a phase table. Each recorded span is also emitted on the dedicated <c>Elsa.Boot</c>
/// <see cref="ActivitySource"/> — deliberately separate from the runtime hot-path tracing source so subscribing
/// to boot spans never perturbs steady-state workflow tracing.
/// </summary>
public sealed class BootPhaseTimeline
{
    /// <summary>Name of the opt-in boot ActivitySource. Kept distinct from the runtime execution tracing source.</summary>
    public const string ActivitySourceName = "Elsa.Boot";

    /// <summary>Host configuration switch that turns the instrument on. Absent or false means the instrument is inert.</summary>
    public const string EnabledConfigurationKey = "Elsa:Boot:PhaseTiming:Enabled";

    private static readonly ActivitySource Source = new(ActivitySourceName);

    private readonly Stopwatch _stopwatch;
    private readonly ConcurrentQueue<BootPhaseRecord> _records = new();
    private int _tableEmitted;

    private BootPhaseTimeline(Stopwatch stopwatch) => _stopwatch = stopwatch;

    /// <summary>Milliseconds elapsed since the timeline's stopwatch was started (process entry).</summary>
    public double ElapsedMs => _stopwatch.Elapsed.TotalMilliseconds;

    /// <summary>
    /// Creates the timeline only when the switch is enabled; returns <c>null</c> otherwise so callers can guard
    /// every recording site with <c>timeline?.</c> and leave the off-path allocation- and behavior-free.
    /// </summary>
    public static BootPhaseTimeline? CreateIfEnabled(IConfiguration configuration, Stopwatch stopwatch)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(stopwatch);
        return IsEnabled(configuration) ? new BootPhaseTimeline(stopwatch) : null;
    }

    /// <summary>Reads the opt-in switch. Parses loosely so "true"/"1" both enable it.</summary>
    public static bool IsEnabled(IConfiguration configuration)
    {
        var value = configuration[EnabledConfigurationKey];
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (bool.TryParse(value, out var parsed))
            return parsed;
        return value.Trim() == "1";
    }

    /// <summary>Records a zero-width point event (e.g. Kestrel-ready) at the current elapsed time.</summary>
    public void Mark(string phase, string? detail = null)
    {
        var at = ElapsedMs;
        Record(new BootPhaseRecord(phase, at, 0d, detail));
    }

    /// <summary>Records a span that started at <paramref name="startMs"/> and ends now, emitting a boot Activity.</summary>
    public void Measure(string phase, double startMs, string? detail = null)
    {
        var end = ElapsedMs;
        var duration = Math.Max(0d, end - startMs);
        EmitActivity(phase, startMs, duration, detail);
        Record(new BootPhaseRecord(phase, startMs, duration, detail));
    }

    /// <summary>Opens a timed scope; disposing it records the span and emits a boot Activity.</summary>
    public IDisposable Begin(string phase, string? detail = null) => new PhaseScope(this, phase, detail, ElapsedMs);

    private void EmitActivity(string phase, double startMs, double durationMs, string? detail)
    {
        // Only materializes a real Activity when a listener (e.g. OTel) is subscribed to the boot source.
        using var activity = Source.StartActivity($"boot.{phase}", ActivityKind.Internal);
        if (activity is null)
            return;
        activity.SetTag("boot.phase", phase);
        activity.SetTag("boot.start_ms", startMs.ToString("F1", CultureInfo.InvariantCulture));
        activity.SetTag("boot.duration_ms", durationMs.ToString("F1", CultureInfo.InvariantCulture));
        if (detail is not null)
            activity.SetTag("boot.detail", detail);
    }

    private void Record(BootPhaseRecord record) => _records.Enqueue(record);

    /// <summary>
    /// Emits the phase table exactly once (the first caller wins). Intended to fire at first-request completion,
    /// when lazy shell activation and the first authenticated round-trip have already been recorded.
    /// </summary>
    public void EmitTableOnce(ILogger logger, string reason)
    {
        if (Interlocked.Exchange(ref _tableEmitted, 1) != 0)
            return;

        var ordered = _records.OrderBy(record => record.StartMs).ToArray();
        var table = BootPhaseTableFormatter.Format(ordered, ElapsedMs, reason);
        logger.LogInformation("Elsa boot phase timing ({Reason}):\n{PhaseTable}", reason, table);
    }

    private sealed class PhaseScope(BootPhaseTimeline timeline, string phase, string? detail, double startMs) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            timeline.Measure(phase, startMs, detail);
        }
    }
}

/// <summary>One recorded boot phase: its name, when it started, how long it took, and optional detail.</summary>
public readonly record struct BootPhaseRecord(string Phase, double StartMs, double DurationMs, string? Detail);
