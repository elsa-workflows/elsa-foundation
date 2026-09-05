using Elsa.Diagnostics.OpenTelemetry.Core.Models;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

internal static class OpenTelemetryDurabilityProbe
{
    public static string? SelectTraceId(OpenTelemetryBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Traces.Count == 0)
            return null;

        // Stream counts establish full retention. This point read only proves that an admitted trace
        // is publicly visible, so avoid duplicating a deliberately high-fanout trace-detail workload
        // before that route gets its own correctness and native-plan evidence.
        var referencedTraceIds = batch.Spans.Select(span => span.TraceId)
            .Concat(batch.Logs.Select(log => log.TraceId))
            .Where(traceId => !string.IsNullOrWhiteSpace(traceId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (batch.Traces.LastOrDefault(trace => !referencedTraceIds.Contains(trace.TraceId))
            ?? batch.Traces.Last()).TraceId;
    }
}
