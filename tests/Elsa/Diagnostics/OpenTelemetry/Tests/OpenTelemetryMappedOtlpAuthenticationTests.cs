using System.Net;
using Elsa.Diagnostics.OpenTelemetry;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Extensions;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Diagnostics.OpenTelemetry.Tests;

public sealed class OpenTelemetryMappedOtlpAuthenticationTests
{
    [Fact]
    public async Task Feature_lifecycle_maps_default_authenticator_and_enforces_credentials_before_body_read()
    {
        var feature = new OpenTelemetryFeature { ApiKey = "secret", AllowUnauthenticatedLoopback = true };
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddSingleton<IOpenTelemetryIngestor, RecordingIngestor>();
        feature.ConfigureServices(builder.Services);
        await using var app = builder.Build();
        feature.MapEndpoints(app, null);
        await app.StartAsync();

        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        Assert.Equal(11, routes.Length);
        var otlpRoutes = routes.Where(route => route.RoutePattern.RawText?.StartsWith("/elsa/otlp/v1/", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(3, otlpRoutes.Length);
        Assert.All(otlpRoutes, route =>
        {
            Assert.NotNull(route.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.Equal(
                Elsa.Api.AspNetCore.EndpointSecurityDispositionKind.HostCredential,
                route.Metadata.GetMetadata<Elsa.Api.AspNetCore.EndpointSecurityDispositionMetadata>()?.Kind);
        });
        Assert.IsType<DefaultOtlpRequestAuthenticator>(app.Services.GetRequiredService<IOtlpRequestAuthenticator>());

        var client = app.GetTestClient();
        using var validRequest = new HttpRequestMessage(HttpMethod.Post, "/elsa/otlp/v1/logs")
        {
            Content = new ByteArrayContent([])
        };
        validRequest.Headers.Add("x-otlp-api-key", "secret");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(validRequest)).StatusCode);

        using var invalidRequest = new HttpRequestMessage(HttpMethod.Post, "/elsa/otlp/v1/logs")
        {
            Content = new ByteArrayContent([])
        };
        invalidRequest.Headers.Add("x-otlp-api-key", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(invalidRequest)).StatusCode);

        var loopback = await ExecuteMappedAsync(
            app,
            "/elsa/otlp/v1/logs",
            IPAddress.Loopback,
            new MemoryStream(),
            null);
        Assert.Equal(StatusCodes.Status401Unauthorized, loopback.StatusCode);

        var external = await ExecuteMappedAsync(
            app,
            "/elsa/otlp/v1/logs",
            IPAddress.Parse("192.0.2.10"),
            new MemoryStream(),
            null);
        Assert.Equal(StatusCodes.Status401Unauthorized, external.StatusCode);

        var noApiKeyFeature = new OpenTelemetryFeature { AllowUnauthenticatedLoopback = true };
        var loopbackBuilder = WebApplication.CreateBuilder();
        loopbackBuilder.WebHost.UseTestServer();
        loopbackBuilder.Services.AddRouting();
        loopbackBuilder.Services.AddSingleton<IOpenTelemetryIngestor, RecordingIngestor>();
        noApiKeyFeature.ConfigureServices(loopbackBuilder.Services);
        await using var loopbackApp = loopbackBuilder.Build();
        noApiKeyFeature.MapEndpoints(loopbackApp, null);
        await loopbackApp.StartAsync();
        var acceptedLoopback = await ExecuteMappedAsync(
            loopbackApp,
            "/elsa/otlp/v1/logs",
            IPAddress.Loopback,
            new MemoryStream(),
            null);
        Assert.Equal(StatusCodes.Status200OK, acceptedLoopback.StatusCode);

        var rejectedBody = new ThrowOnReadStream();
        var rejected = await ExecuteMappedAsync(
            app,
            "/elsa/otlp/v1/logs",
            IPAddress.Parse("192.0.2.10"),
            rejectedBody,
            "wrong");
        Assert.Equal(StatusCodes.Status401Unauthorized, rejected.StatusCode);
        Assert.Equal(0, rejectedBody.ReadCount);
    }

    private static async Task<ExecutionResult> ExecuteMappedAsync(
        WebApplication app,
        string path,
        IPAddress remoteAddress,
        Stream body,
        string? apiKey)
    {
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(route => string.Equals(route.RoutePattern.RawText, path, StringComparison.Ordinal));
        await using var scope = app.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Connection.RemoteIpAddress = remoteAddress;
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;
        context.Request.Body = body;
        context.Response.Body = new MemoryStream();
        if (apiKey is not null)
            context.Request.Headers["x-otlp-api-key"] = apiKey;
        var requestDelegate = endpoint.RequestDelegate ?? throw new InvalidOperationException($"Route '{path}' has no request delegate.");
        await requestDelegate(context);
        return new ExecutionResult(context.Response.StatusCode);
    }

    private sealed record ExecutionResult(int StatusCode);

    private sealed class RecordingIngestor : IOpenTelemetryIngestor
    {
        public ValueTask IngestAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask IngestAsync(OpenTelemetryBatch batch, OpenTelemetryIngestionContext ingestionContext, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class ThrowOnReadStream : Stream
    {
        public int ReadCount { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) { ReadCount++; throw new InvalidOperationException("Rejected authentication read the body."); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { ReadCount++; throw new InvalidOperationException("Rejected authentication read the body."); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
