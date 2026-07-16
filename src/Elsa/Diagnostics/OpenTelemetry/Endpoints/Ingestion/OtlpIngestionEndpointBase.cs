using System.Buffers;
using System.IO.Compression;
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
/// authenticates the request with <see cref="IOtlpRequestAuthenticator"/>, parses
/// it with the signal-specific parser, then hands the batch to <see cref="IOpenTelemetryIngestor"/>.
/// These endpoints are intentionally anonymous to the FastEndpoints permission model; ingestion auth is the
/// request authentication, not a studio principal.
/// </summary>
internal abstract class OtlpIngestionEndpointBase(
    IOpenTelemetryIngestor ingestor,
    IOtlpRequestAuthenticator authenticator,
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

        var authentication = await authenticator.AuthenticateAsync(HttpContext, ct);
        if (!authentication.Accepted)
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
        catch (InvalidDataException)
        {
            // A declared Content-Encoding whose bytes do not actually decompress is a client error.
            await Send.StringAsync(string.Empty, StatusCodes.Status400BadRequest, cancellation: ct);
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

        await ingestor.IngestAsync(batch, authentication.Context, ct);
        await Send.StringAsync(string.Empty, StatusCodes.Status200OK, cancellation: ct);
    }

    private static async Task<ReadOnlyMemory<byte>> ReadBodyAsync(HttpContext httpContext, long maxBodySize, CancellationToken cancellationToken)
    {
        // OTLP/HTTP exporters may gzip (or otherwise compress) the protobuf body; honor Content-Encoding so
        // valid compressed telemetry is not rejected as malformed protobuf. The size cap is applied to the
        // decompressed bytes, which also bounds decompression-bomb expansion.
        var decompressor = CreateDecompressor(httpContext.Request);
        var source = decompressor ?? httpContext.Request.Body;
        using var stream = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        var totalBytes = 0L;

        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
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
            if (decompressor != null)
                await decompressor.DisposeAsync();
        }

        return stream.ToArray();
    }

    private static Stream? CreateDecompressor(HttpRequest request)
    {
        var encoding = request.Headers.ContentEncoding.ToString();

        if (string.IsNullOrWhiteSpace(encoding))
            return null;

        if (encoding.Contains("gzip", StringComparison.OrdinalIgnoreCase) || encoding.Contains("x-gzip", StringComparison.OrdinalIgnoreCase))
            return new GZipStream(request.Body, CompressionMode.Decompress, leaveOpen: true);

        if (encoding.Contains("deflate", StringComparison.OrdinalIgnoreCase))
            return new DeflateStream(request.Body, CompressionMode.Decompress, leaveOpen: true);

        if (encoding.Contains("br", StringComparison.OrdinalIgnoreCase))
            return new BrotliStream(request.Body, CompressionMode.Decompress, leaveOpen: true);

        return null;
    }

    private sealed class RequestBodyTooLargeException : Exception;
}
