using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using System.Text.Json;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints;

/// <summary>
/// Serializes OpenTelemetry live-stream payloads to the wire JSON shape used by the SSE <c>data</c> lines.
/// Kept as a standalone unit so the JSON contract can be branch-tested without spinning up an HTTP host.
/// </summary>
public sealed class OpenTelemetryStreamItemSerializer
{
    /// <summary>Serializes a telemetry resource to its JSON object form.</summary>
    public string Serialize(TelemetryResource resource) => JsonSerializer.Serialize(resource, OpenTelemetryJsonContext.WireContext.TelemetryResource);

    /// <summary>Serializes a telemetry trace to its JSON object form.</summary>
    public string Serialize(TelemetryTrace trace) => JsonSerializer.Serialize(trace, OpenTelemetryJsonContext.WireContext.TelemetryTrace);

    /// <summary>Serializes a metric point to its JSON object form.</summary>
    public string Serialize(MetricPoint point) => JsonSerializer.Serialize(point, OpenTelemetryJsonContext.WireContext.MetricPoint);

    /// <summary>Serializes an OTLP log record to its JSON object form.</summary>
    public string Serialize(OtlpLogRecord log) => JsonSerializer.Serialize(log, OpenTelemetryJsonContext.WireContext.OtlpLogRecord);

    /// <summary>Serializes a dropped-item summary to its JSON object form.</summary>
    public string Serialize(OpenTelemetryDroppedItemSummary dropped) => JsonSerializer.Serialize(dropped, OpenTelemetryJsonContext.WireContext.OpenTelemetryDroppedItemSummary);
}
