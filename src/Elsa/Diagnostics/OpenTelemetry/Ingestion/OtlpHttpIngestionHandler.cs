using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Ingestion.HttpProtobuf;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.IO.Compression;

namespace Elsa.Diagnostics.OpenTelemetry.Ingestion;

/// <summary>
/// Handles one OTLP/HTTP protobuf request. Authentication always completes before the request body is read;
/// accepted requests are decompressed, bounded by their decompressed size, parsed, then ingested with the
/// server-authoritative authentication context.
/// </summary>
public sealed class OtlpHttpIngestionHandler(
    IOpenTelemetryIngestor ingestor,
    IOtlpRequestAuthenticator authenticator,
    IOptions<OpenTelemetryDiagnosticsOptions> options)
{
    public async Task HandleAsync(
        HttpContext httpContext,
        OtlpSignal signal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var authentication = await authenticator.AuthenticateAsync(httpContext, cancellationToken);
        if (!authentication.Accepted)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        ReadOnlyMemory<byte> payload;
        try
        {
            payload = await ReadBodyAsync(httpContext.Request, options.Value.MaxHttpRequestBodySize, cancellationToken);
        }
        catch (RequestBodyTooLargeException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }
        catch (InvalidDataException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        OpenTelemetryBatch batch;
        try
        {
            batch = Parse(signal, payload.Span);
        }
        catch (InvalidDataException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        await ingestor.IngestAsync(batch, authentication.Context, cancellationToken);
        httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static OpenTelemetryBatch Parse(OtlpSignal signal, ReadOnlySpan<byte> payload) => signal switch
    {
        OtlpSignal.Traces => OtlpHttpProtobufParser.ParseTraces(payload),
        OtlpSignal.Metrics => OtlpHttpProtobufParser.ParseMetrics(payload),
        OtlpSignal.Logs => OtlpHttpProtobufParser.ParseLogs(payload),
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, "Unsupported OTLP signal.")
    };

    private static async Task<ReadOnlyMemory<byte>> ReadBodyAsync(
        HttpRequest request,
        long maxBodySize,
        CancellationToken cancellationToken)
    {
        var decompressor = CreateDecompressor(request);
        var source = decompressor ?? request.Body;
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
