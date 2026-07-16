using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Ingestion.HttpProtobuf;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints.Ingestion;

/// <summary>OTLP/HTTP protobuf collector endpoint for trace spans: <c>POST {base}/traces</c>.</summary>
internal sealed class TracesIngestionEndpoint(
    IOpenTelemetryIngestor ingestor,
    IOtlpRequestAuthenticator authenticator,
    IOptions<OpenTelemetryDiagnosticsOptions> options) : OtlpIngestionEndpointBase(ingestor, authenticator, options)
{
    protected override string Signal => "traces";

    protected override OpenTelemetryBatch Parse(ReadOnlySpan<byte> payload) => OtlpHttpProtobufParser.ParseTraces(payload);
}
