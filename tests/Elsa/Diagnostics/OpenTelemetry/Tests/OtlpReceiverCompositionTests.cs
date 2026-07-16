using System.IO.Compression;
using System.Net;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Extensions;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Tests.Ingestion;

public sealed class OtlpReceiverCompositionTests
{
    [Fact]
    public async Task RouteMapperMapsOnlyThreeOtlpPostRoutesAndPropagatesAuthenticatedContext()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var ingestor = new RecordingIngestor();
        var context = OpenTelemetryIngestionContext.Authenticated(
            "source-1",
            new Dictionary<string, string> { ["workspace"] = "workspace-1" });
        builder.Services.AddSingleton<IOpenTelemetryIngestor>(ingestor);
        builder.Services.AddSingleton<IOtlpRequestAuthenticator>(new StaticAuthenticator(OtlpRequestAuthenticationResult.Accept(context)));
        builder.Services.AddOpenTelemetryDiagnosticsServices(options => options.HttpEndpointPath = "/custom/otlp/");
        await using var app = builder.Build();

        app.MapOpenTelemetryOtlpReceiver();
        await app.StartAsync();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => (
                Pattern: endpoint.RoutePattern.RawText,
                Methods: endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods,
                AllowsAnonymous: endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null))
            .ToArray();
        Assert.Equal(
            ["/custom/otlp/logs", "/custom/otlp/metrics", "/custom/otlp/traces"],
            routes.Select(route => route.Pattern).Order(StringComparer.Ordinal));
        Assert.All(routes, route => Assert.Equal([HttpMethods.Post], route.Methods));
        Assert.All(routes, route => Assert.True(route.AllowsAnonymous));

        var client = app.GetTestClient();
        var response = await client.PostAsync("/custom/otlp/logs", new ByteArrayContent([]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("workspace-1", Assert.Single(ingestor.Contexts).Claims["workspace"]);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/custom/otlp/unknown", new ByteArrayContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync("/custom/otlp/logs")).StatusCode);
    }

    [Fact]
    public async Task HandlerAuthenticatesBeforeReadingOrParsingBody()
    {
        var ingestor = new RecordingIngestor();
        var handler = CreateHandler(ingestor, OtlpRequestAuthenticationResult.Rejected);
        var httpContext = CreateHttpContext(new ThrowOnReadStream());

        await handler.HandleAsync(httpContext, OtlpSignal.Logs);

        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
        Assert.Equal(0, ingestor.IngestCount);
    }

    [Fact]
    public async Task HandlerReturnsBadRequestForMalformedProtobuf()
    {
        var ingestor = new RecordingIngestor();
        var handler = CreateHandler(ingestor, AcceptedContext());
        var httpContext = CreateHttpContext(new MemoryStream([0x0A, 0x05, 0x01]));

        await handler.HandleAsync(httpContext, OtlpSignal.Logs);

        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.Equal(0, ingestor.IngestCount);
    }

    [Fact]
    public async Task HandlerDecompressesGzipBeforeParsing()
    {
        var ingestor = new RecordingIngestor();
        var handler = CreateHandler(ingestor, AcceptedContext());
        var httpContext = CreateHttpContext(new MemoryStream(Compress([])));
        httpContext.Request.Headers.ContentEncoding = "gzip";

        await handler.HandleAsync(httpContext, OtlpSignal.Traces);

        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Equal(1, ingestor.IngestCount);
    }

    [Fact]
    public async Task HandlerReturnsBadRequestForMalformedCompressedBody()
    {
        var ingestor = new RecordingIngestor();
        var handler = CreateHandler(ingestor, AcceptedContext());
        var httpContext = CreateHttpContext(new MemoryStream([0x01, 0x02, 0x03]));
        httpContext.Request.Headers.ContentEncoding = "gzip";

        await handler.HandleAsync(httpContext, OtlpSignal.Logs);

        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.Equal(0, ingestor.IngestCount);
    }

    [Fact]
    public async Task HandlerAppliesSizeLimitToDecompressedBody()
    {
        var ingestor = new RecordingIngestor();
        var handler = CreateHandler(ingestor, AcceptedContext(), maxBodySize: 4);
        var httpContext = CreateHttpContext(new MemoryStream(Compress(new byte[32])));
        httpContext.Request.Headers.ContentEncoding = "gzip";

        await handler.HandleAsync(httpContext, OtlpSignal.Metrics);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, httpContext.Response.StatusCode);
        Assert.Equal(0, ingestor.IngestCount);
    }

    private static OtlpHttpIngestionHandler CreateHandler(
        IOpenTelemetryIngestor ingestor,
        OtlpRequestAuthenticationResult authentication,
        long maxBodySize = 1024) =>
        new(
            ingestor,
            new StaticAuthenticator(authentication),
            Options.Create(new OpenTelemetryDiagnosticsOptions { MaxHttpRequestBodySize = maxBodySize }));

    private static OtlpRequestAuthenticationResult AcceptedContext() =>
        OtlpRequestAuthenticationResult.Accept(OpenTelemetryIngestionContext.Authenticated("source-1"));

    private static DefaultHttpContext CreateHttpContext(Stream body)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = body;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static byte[] Compress(byte[] payload)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(payload);
        return output.ToArray();
    }

    private sealed class StaticAuthenticator(OtlpRequestAuthenticationResult result) : IOtlpRequestAuthenticator
    {
        public ValueTask<OtlpRequestAuthenticationResult> AuthenticateAsync(
            HttpContext httpContext,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(result);
    }

    private sealed class RecordingIngestor : IOpenTelemetryIngestor
    {
        public int IngestCount { get; private set; }
        public List<OpenTelemetryIngestionContext> Contexts { get; } = [];

        public ValueTask IngestAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default)
        {
            IngestCount++;
            Contexts.Add(OpenTelemetryIngestionContext.Untrusted);
            return ValueTask.CompletedTask;
        }

        public ValueTask IngestAsync(
            OpenTelemetryBatch batch,
            OpenTelemetryIngestionContext ingestionContext,
            CancellationToken cancellationToken = default)
        {
            IngestCount++;
            Contexts.Add(ingestionContext);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowOnReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Body was read before authentication.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Body was read before authentication.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
