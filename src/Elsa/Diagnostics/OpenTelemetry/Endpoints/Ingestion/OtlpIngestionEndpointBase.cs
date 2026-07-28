using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Extensions;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints.Ingestion;

/// <summary>
/// Base for the OTLP/HTTP protobuf collector endpoints (<c>traces</c>, <c>metrics</c>, <c>logs</c>). Reads
/// the raw protobuf body (bounded by <see cref="OpenTelemetryDiagnosticsOptions.MaxHttpRequestBodySize"/>),
/// authenticates the request with <see cref="IOtlpRequestAuthenticator"/>, parses
/// it with the signal-specific parser, then hands the batch to <see cref="IOpenTelemetryIngestor"/>.
/// These endpoints are intentionally anonymous to the FastEndpoints permission model; ingestion auth is the
/// request authentication, not a studio principal.
/// </summary>
internal abstract class OtlpIngestionEndpointBase(
    OtlpHttpIngestionHandler handler,
    IOptions<OpenTelemetryDiagnosticsOptions> options) : ElsaEndpointWithoutRequest
{
    protected abstract OtlpSignal Signal { get; }

    public override void Configure()
    {
        var basePath = OpenTelemetryEndpointRouteBuilderExtensions.NormalizeBasePath(options.Value.HttpEndpointPath);
        Post($"{basePath}/{OpenTelemetryEndpointRouteBuilderExtensions.GetRouteSegment(Signal)}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await handler.HandleAsync(HttpContext, Signal, ct);
    }
}
