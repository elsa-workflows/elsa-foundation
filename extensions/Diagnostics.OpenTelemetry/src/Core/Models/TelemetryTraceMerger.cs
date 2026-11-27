namespace Elsa.Diagnostics.OpenTelemetry.Core.Models;

/// <summary>
/// Folds every stored <see cref="TelemetryTrace"/> record that shares a trace id into one query-time summary. A trace
/// legitimately arrives across multiple batches — the OTLP receiver derives one record per received export request, and
/// the engine tracing bridge publishes one batch per synchronous drain cycle plus a trailing batch for the quiescence
/// flush commit — so stores must merge the records instead of surfacing only the last one, which would misreport the
/// trace's duration and span count and hide an earlier batch's error status.
/// </summary>
public static class TelemetryTraceMerger
{
    /// <summary>Merges records sharing one trace id; a single record passes through unchanged.</summary>
    public static TelemetryTrace Merge(IReadOnlyList<TelemetryTrace> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
            throw new ArgumentException("At least one trace record is required.", nameof(records));
        if (records.Count == 1)
            return records[0];

        // The earliest record names the trace: its root span started the trace, so its name/root id stay the summary's.
        var first = records.MinBy(x => x.StartTime)!;
        var start = records.Min(x => x.StartTime);
        var end = records.Max(x => x.EndTime);
        var status = records.Any(x => x.Status == SpanStatus.Error)
            ? SpanStatus.Error
            : records.Any(x => x.Status == SpanStatus.Ok)
                ? SpanStatus.Ok
                : SpanStatus.Unset;

        return new TelemetryTrace(
            first.TraceId,
            first.RootSpanId,
            first.Name,
            start,
            end,
            end - start,
            status,
            records.SelectMany(x => x.ResourceIds).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            records.SelectMany(x => x.WorkflowInstanceIds).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            records.Sum(x => x.SpanCount));
    }
}
