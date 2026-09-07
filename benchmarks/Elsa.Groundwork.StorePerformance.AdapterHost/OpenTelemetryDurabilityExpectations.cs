using System.Collections.Concurrent;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

internal sealed class OpenTelemetryDurabilityExpectations
{
    private readonly ConcurrentDictionary<string, byte> resources = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> instruments = new(StringComparer.Ordinal);
    private int traces;
    private int spans;
    private int points;
    private int logs;
    private string? traceProbeId;

    public void Record(OpenTelemetryBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        Interlocked.Add(ref traces, batch.Traces.Count);
        Interlocked.Add(ref spans, batch.Spans.Count);
        Interlocked.Add(ref points, batch.MetricPoints.Count);
        Interlocked.Add(ref logs, batch.Logs.Count);
        foreach (var resource in batch.Resources)
            resources.TryAdd(resource.Id, 0);
        foreach (var instrument in batch.Instruments)
            instruments.TryAdd(instrument.Id, 0);
        if (OpenTelemetryDurabilityProbe.SelectTraceId(batch) is { } selectedTraceId)
            Volatile.Write(ref traceProbeId, selectedTraceId);
    }

    public OpenTelemetryDurabilityTarget Read() => new(
        Volatile.Read(ref traces),
        Volatile.Read(ref spans),
        Volatile.Read(ref points),
        Volatile.Read(ref logs),
        resources.Count,
        instruments.Count,
        Volatile.Read(ref traceProbeId));
}

internal readonly record struct OpenTelemetryDurabilityTarget(
    int Traces,
    int Spans,
    int Points,
    int Logs,
    int Resources,
    int Instruments,
    string? TraceProbeId);
