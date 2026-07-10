using Elsa.Diagnostics.OpenTelemetry.Core.Models;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Tests;

/// <summary>Factory methods for telemetry model instances shared by the persistence test classes.</summary>
internal static class TestModels
{
    public static DateTimeOffset Now { get; } = new(2026, 6, 22, 10, 0, 0, TimeSpan.Zero);

    public static TelemetryResource Resource(string id, string serviceName, DateTimeOffset? lastSeen = null, TelemetryResourceStatus status = TelemetryResourceStatus.Active) =>
        new(id, serviceName, $"{id}-instance", "dotnet", new Dictionary<string, string?> { ["deployment.environment"] = "test" }, lastSeen ?? Now, status);

    public static TelemetryTrace Trace(string traceId, string resourceId, DateTimeOffset? startTime = null, SpanStatus status = SpanStatus.Ok, params string[] workflowInstanceIds)
    {
        var start = startTime ?? Now;
        return new(traceId, $"{traceId}-root", $"trace-{traceId}", start, start.AddMilliseconds(25), TimeSpan.FromMilliseconds(25), status, [resourceId], workflowInstanceIds, 2);
    }

    public static TelemetrySpan Span(string id, string traceId, string spanId, string resourceId, DateTimeOffset? startTime = null)
    {
        var start = startTime ?? Now;
        return new(
            id,
            traceId,
            spanId,
            null,
            resourceId,
            $"span-{spanId}",
            "internal",
            start,
            start.AddMilliseconds(10),
            SpanStatus.Ok,
            null,
            new Dictionary<string, string?> { ["http.method"] = "GET" },
            [new TelemetrySpanEvent("event-a", start.AddMilliseconds(1), new Dictionary<string, string?> { ["event.attr"] = "value" })],
            [new TelemetrySpanLink("linked-trace", "linked-span", new Dictionary<string, string?> { ["link.attr"] = "value" })]);
    }

    public static MetricInstrument Instrument(string id, string resourceId, string name) =>
        new(id, resourceId, name, "ms", "duration", MetricKind.Gauge, new Dictionary<string, string?> { ["instrument.attr"] = "value" });

    public static MetricPoint Point(string id, string instrumentId, string resourceId, DateTimeOffset? timestamp = null, string? traceId = null, string? spanId = null) =>
        new(id, instrumentId, instrumentId, resourceId, timestamp ?? Now, 42, null, null, new Dictionary<string, string?> { ["point.attr"] = "value" }, traceId, spanId);

    public static OtlpLogRecord Log(string id, string resourceId, string traceId, string severity = "Information", string body = "message") =>
        new(id, resourceId, Now, severity, null, body, traceId, $"{traceId}-span", new Dictionary<string, string?> { ["log.attr"] = "value" });
}
