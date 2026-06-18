using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Ingestion.HttpProtobuf;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints.Ingestion;

/// <summary>OTLP/HTTP protobuf collector endpoint for metrics: <c>POST {base}/metrics</c>.</summary>
internal sealed class MetricsIngestionEndpoint(
    IOpenTelemetryIngestor ingestor,
    IOptions<OpenTelemetryDiagnosticsOptions> options) : OtlpIngestionEndpointBase(ingestor, options)
{
    protected override string Signal => "metrics";

    protected override OpenTelemetryBatch Parse(ReadOnlySpan<byte> payload) => OtlpHttpProtobufParser.ParseMetrics(payload);
}
