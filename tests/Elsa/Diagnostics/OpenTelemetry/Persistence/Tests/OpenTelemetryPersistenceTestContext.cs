using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Storage;
using Elsa.Diagnostics.OpenTelemetry.Services;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Tests;

internal sealed class OpenTelemetryPersistenceTestContext : IDisposable
{
    private readonly OpenTelemetryTestHost _host;

    public OpenTelemetryPersistenceTestContext(OpenTelemetryDiagnosticsOptions? options = null, int pruneInterval = 500, bool startDraining = true)
    {
        _host = OpenTelemetryTestHost.Create();
        Options = options ?? new OpenTelemetryDiagnosticsOptions();
        SourceRegistry = new OpenTelemetrySourceRegistry(Microsoft.Extensions.Options.Options.Create(Options));
        Store = new EfCoreOpenTelemetryStore(_host, Microsoft.Extensions.Options.Options.Create(Options), SourceRegistry, pruneInterval);
        if (startDraining)
            Store.StartDraining();
    }

    public OpenTelemetryDiagnosticsOptions Options { get; }
    public OpenTelemetrySourceRegistry SourceRegistry { get; }
    public EfCoreOpenTelemetryStore Store { get; }
    public DateTimeOffset Now { get; } = new(2026, 6, 22, 10, 0, 0, TimeSpan.Zero);

    public TelemetryResource Resource(string id, string serviceName, DateTimeOffset? lastSeen = null, TelemetryResourceStatus status = TelemetryResourceStatus.Active) =>
        new(id, serviceName, $"{id}-instance", "dotnet", new Dictionary<string, string?> { ["deployment.environment"] = "test" }, lastSeen ?? Now, status);

    public TelemetryTrace Trace(string traceId, string resourceId, DateTimeOffset? startTime = null, SpanStatus status = SpanStatus.Ok, params string[] workflowInstanceIds)
    {
        var start = startTime ?? Now;
        return new(traceId, $"{traceId}-root", $"trace-{traceId}", start, start.AddMilliseconds(25), TimeSpan.FromMilliseconds(25), status, [resourceId], workflowInstanceIds, 2);
    }

    public TelemetrySpan Span(string id, string traceId, string spanId, string resourceId, DateTimeOffset? startTime = null)
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

    public MetricInstrument Instrument(string id, string resourceId, string name) =>
        new(id, resourceId, name, "ms", "duration", MetricKind.Gauge, new Dictionary<string, string?> { ["instrument.attr"] = "value" });

    public MetricPoint Point(string id, string instrumentId, string resourceId, DateTimeOffset? timestamp = null, string? traceId = null, string? spanId = null) =>
        new(id, instrumentId, instrumentId, resourceId, timestamp ?? Now, 42, null, null, new Dictionary<string, string?> { ["point.attr"] = "value" }, traceId, spanId);

    public OtlpLogRecord Log(string id, string resourceId, string traceId, string severity = "Information", string body = "message") =>
        new(id, resourceId, Now, severity, null, body, traceId, $"{traceId}-span", new Dictionary<string, string?> { ["log.attr"] = "value" });

    public void Dispose()
    {
        Store.Dispose();
        _host.Dispose();
    }
}
