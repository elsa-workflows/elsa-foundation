using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints.Ingestion;

/// <summary>OTLP/HTTP protobuf collector endpoint for metrics: <c>POST {base}/metrics</c>.</summary>
internal sealed class MetricsIngestionEndpoint(
    OtlpHttpIngestionHandler handler,
    IOptions<OpenTelemetryDiagnosticsOptions> options) : OtlpIngestionEndpointBase(handler, options)
{
    protected override OtlpSignal Signal => OtlpSignal.Metrics;
}
