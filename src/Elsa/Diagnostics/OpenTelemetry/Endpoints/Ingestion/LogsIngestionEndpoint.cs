using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Ingestion.HttpProtobuf;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints.Ingestion;

/// <summary>OTLP/HTTP protobuf collector endpoint for log records: <c>POST {base}/logs</c>.</summary>
internal sealed class LogsIngestionEndpoint(
    IOpenTelemetryIngestor ingestor,
    IOtlpRequestAuthenticator authenticator,
    IOptions<OpenTelemetryDiagnosticsOptions> options) : OtlpIngestionEndpointBase(ingestor, authenticator, options)
{
    protected override string Signal => "logs";

    protected override OpenTelemetryBatch Parse(ReadOnlySpan<byte> payload) => OtlpHttpProtobufParser.ParseLogs(payload);
}
