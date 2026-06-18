using System.Buffers;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints.Ingestion;

/// <summary>
/// Base for the OTLP/HTTP protobuf collector endpoints (<c>traces</c>, <c>metrics</c>, <c>logs</c>). Reads
/// the raw protobuf body (bounded by <see cref="OpenTelemetryDiagnosticsOptions.MaxHttpRequestBodySize"/>),
/// authenticates the request with <see cref="OtlpIngestionSecurity"/> (API-key header or loopback), parses
/// it with the signal-specific parser, then hands the batch to <see cref="IOpenTelemetryIngestor"/>.
/// These endpoints are intentionally anonymous to the FastEndpoints permission model; ingestion auth is the
/// API key, not a studio principal.
/// </summary>
internal abstract class OtlpIngestionEndpointBase(
    IOpenTelemetryIngestor ingestor,
    IOptions<OpenTelemetryDiagnosticsOptions> options) : ElsaEndpointWithoutRequest
{
    /// <summary>The route suffix appended to <see cref="OpenTelemetryDiagnosticsOptions.HttpEndpointPath"/>.</summary>
    protected abstract string Signal { get; }

    /// <summary>Parses the raw OTLP/protobuf payload into a normalized batch.</summary>
    protected abstract OpenTelemetryBatch Parse(ReadOnlySpan<byte> payload);

    public override void Configure()
    {
        var basePath = (string.IsNullOrWhiteSpace(options.Value.HttpEndpointPath) ? "/elsa/otlp/v1" : options.Value.HttpEndpointPath).TrimEnd('/');
        Post($"{basePath}/{Signal}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var settings = options.Value;

        if (!OtlpIngestionSecurity.IsAuthorized(HttpContext, settings))
        {
            await Send.StringAsync(string.Empty, StatusCodes.Status401Unauthorized, cancellation: ct);
            return;
        }

        ReadOnlyMemory<byte> payload;
        try
        {
            payload = await ReadBodyAsync(HttpContext, settings.MaxHttpRequestBodySize, ct);
        }
        catch (RequestBodyTooLargeException)
        {
            await Send.StringAsync(string.Empty, StatusCodes.Status413PayloadTooLarge, cancellation: ct);
            return;
        }

        OpenTelemetryBatch batch;
        try
        {
            batch = Parse(payload.Span);
        }
        catch (InvalidDataException)
        {
            await Send.StringAsync(string.Empty, StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await ingestor.IngestAsync(batch, ct);
        await Send.StringAsync(string.Empty, StatusCodes.Status200OK, cancellation: ct);
    }

    private static async Task<ReadOnlyMemory<byte>> ReadBodyAsync(HttpContext httpContext, long maxBodySize, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        var totalBytes = 0L;

        try
        {
            int read;
            while ((read = await httpContext.Request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                totalBytes += read;
                if (totalBytes > maxBodySize)
                    throw new RequestBodyTooLargeException();

                stream.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return stream.ToArray();
    }

    private sealed class RequestBodyTooLargeException : Exception;
}
