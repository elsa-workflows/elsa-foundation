using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Storage;
using Elsa.Diagnostics.OpenTelemetry.Services;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Tests;

internal sealed class OpenTelemetryPersistenceTestContext : IDisposable
{
    private readonly OpenTelemetryTestHost _host;

    public OpenTelemetryPersistenceTestContext(OpenTelemetryDiagnosticsOptions? options = null, int pruneInterval = 500)
    {
        _host = OpenTelemetryTestHost.Create();
        Options = options ?? new OpenTelemetryDiagnosticsOptions();
        SourceRegistry = new OpenTelemetrySourceRegistry(Microsoft.Extensions.Options.Options.Create(Options));
        Store = new EfCoreOpenTelemetryStore(_host, Microsoft.Extensions.Options.Options.Create(Options), SourceRegistry, pruneInterval);
        Store.StartDraining();
    }

    public OpenTelemetryDiagnosticsOptions Options { get; }
    public OpenTelemetrySourceRegistry SourceRegistry { get; }
    public EfCoreOpenTelemetryStore Store { get; }
    public DateTimeOffset Now => TestModels.Now;

    public TelemetryResource Resource(string id, string serviceName, DateTimeOffset? lastSeen = null, TelemetryResourceStatus status = TelemetryResourceStatus.Active) =>
        TestModels.Resource(id, serviceName, lastSeen, status);

    public TelemetryTrace Trace(string traceId, string resourceId, DateTimeOffset? startTime = null, SpanStatus status = SpanStatus.Ok, params string[] workflowInstanceIds) =>
        TestModels.Trace(traceId, resourceId, startTime, status, workflowInstanceIds);

    public TelemetrySpan Span(string id, string traceId, string spanId, string resourceId, DateTimeOffset? startTime = null) =>
        TestModels.Span(id, traceId, spanId, resourceId, startTime);

    public MetricInstrument Instrument(string id, string resourceId, string name) =>
        TestModels.Instrument(id, resourceId, name);

    public MetricPoint Point(string id, string instrumentId, string resourceId, DateTimeOffset? timestamp = null, string? traceId = null, string? spanId = null) =>
        TestModels.Point(id, instrumentId, resourceId, timestamp, traceId, spanId);

    public OtlpLogRecord Log(string id, string resourceId, string traceId, string severity = "Information", string body = "message") =>
        TestModels.Log(id, resourceId, traceId, severity, body);

    public void Dispose()
    {
        Store.Dispose();
        _host.Dispose();
    }
}
