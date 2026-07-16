using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints.Ingestion;

/// <summary>OTLP/HTTP protobuf collector endpoint for trace spans: <c>POST {base}/traces</c>.</summary>
internal sealed class TracesIngestionEndpoint(
    OtlpHttpIngestionHandler handler,
    IOptions<OpenTelemetryDiagnosticsOptions> options) : OtlpIngestionEndpointBase(handler, options)
{
    protected override OtlpSignal Signal => OtlpSignal.Traces;
}
