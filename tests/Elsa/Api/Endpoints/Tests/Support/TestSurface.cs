using Elsa.Api.AspNetCore;
using Elsa.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Api.Endpoints.Tests.Support;

// ---------- Contracts ----------

public enum SampleColor
{
    Red,
    Green,
    Blue
}

/// <summary>A query-bound read contract covering defaults, typed values, and nullables.</summary>
public sealed record SampleQuery(
    string Id,
    int Limit = 25,
    string Sort = "name-asc",
    bool? Transitive = null,
    SampleColor Color = SampleColor.Red,
    string? Cursor = null);

/// <summary>A body-bound contract whose identifier can also arrive by route.</summary>
public sealed record SampleBody(string Id, string? Name = null, int Count = 5, string? Note = null);

/// <summary>An init-only contract bound by property assignment rather than a positional constructor.</summary>
public sealed class SamplePropertyContract
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public int Limit { get; set; } = 25;
}

public sealed record SampleResponse(string Value);

public sealed record WithUnsupported(string Id, TimeSpan Window = default);

public sealed class TwoConstructors
{
    public TwoConstructors() { }
    public TwoConstructors(string id) => Id = id;
    public string? Id { get; }
}

// ---------- Source-generated wire metadata ----------

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(SampleQuery))]
[JsonSerializable(typeof(SampleBody))]
[JsonSerializable(typeof(SamplePropertyContract))]
[JsonSerializable(typeof(SampleResponse))]
public sealed partial class TestJsonContext : JsonSerializerContext
{
}

// ---------- Domain exceptions and failure services ----------

public sealed class SampleDomainException(string code) : Exception($"domain failure: {code}")
{
    public string Code { get; } = code;
}

public sealed class RenderedDomainException : Exception;

/// <summary>Writes problems as a flat JSON shape the tests can assert on.</summary>
public sealed class TestProblemWriter(string marker) : IEndpointProblemWriter
{
    public TestProblemWriter() : this("unkeyed") { }

    public Task WriteAsync(HttpContext context, EndpointProblem problem)
    {
        context.Response.StatusCode = problem.StatusCode;
        context.Response.ContentType = "application/json";
        var payload = new Dictionary<string, object>
        {
            ["writer"] = marker,
            ["status"] = problem.StatusCode,
            ["errors"] = problem.Errors.ToDictionary(entry => entry.Key, entry => entry.Value)
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}

public sealed class TestExceptionTranslator(string marker) : IEndpointExceptionTranslator
{
    public TestExceptionTranslator() : this("unkeyed") { }

    public EndpointProblem? Translate(Exception exception) =>
        exception is SampleDomainException domain
            ? EndpointProblem.General(StatusCodes.Status409Conflict, $"{marker}:{domain.Code}", "domainErrors")
            : null;
}

public sealed class TestFaultRenderer(string marker) : IEndpointFaultRenderer
{
    public TestFaultRenderer() : this("unkeyed") { }

    public async ValueTask<bool> TryWriteAsync(HttpContext context, Exception exception)
    {
        if (exception is not RenderedDomainException)
            return false;

        context.Response.StatusCode = StatusCodes.Status418ImATeapot;
        await context.Response.WriteAsync($"rendered:{marker}");
        return true;
    }
}

// ---------- Endpoint classes, one per shape ----------

public static class ShapeEndpoints
{
    [Post("/items/{id}")]
    public sealed class BodyShape : ApiEndpoint<SampleBody, SampleResponse>
    {
        public override void Configure(ApiEndpointOptions options)
        {
            options.Operation = "BodyShape";
            options.Accepts = ["application/json"];
        }

        public override Task<SampleResponse> HandleAsync(SampleBody request, CancellationToken cancellationToken) =>
            Task.FromResult(new SampleResponse($"{request.Id}|{request.Name}|{request.Count}|{request.Note}"));
    }

    [Delete("/items/{id}")]
    public sealed class NoContentShape : ApiEndpoint<SampleBody>
    {
        public override void Configure(ApiEndpointOptions options) => options.Operation = "NoContentShape";

        public override Task HandleAsync(SampleBody request, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    [Get("/status")]
    public sealed class UnboundShape : ApiEndpointWithoutRequest<SampleResponse>
    {
        public override void Configure(ApiEndpointOptions options) => options.Operation = "UnboundShape";

        public override Task<SampleResponse> HandleAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SampleResponse("unbound"));
    }

    [Post("/items/{id}/outcomes")]
    public sealed class ResultShape : ApiEndpointWithResult<SampleBody, SampleResponse>
    {
        public override void Configure(ApiEndpointOptions options)
        {
            options.Operation = "ResultShape";
            options.SuccessStatus = StatusCodes.Status200OK;
        }

        public override Task<EndpointResult<SampleResponse>> HandleAsync(SampleBody request, CancellationToken cancellationToken) =>
            Task.FromResult(new EndpointResult<SampleResponse>(
                request.Name is null ? StatusCodes.Status202Accepted : StatusCodes.Status201Created,
                new SampleResponse(request.Id)));
    }

    [Get("/queries")]
    public sealed class QueryShape : ApiEndpoint<SampleQuery, SampleResponse>
    {
        public override void Configure(ApiEndpointOptions options)
        {
            options.Operation = "QueryShape";
            options.StrictTypedParsing = true;
        }

        public override Task<SampleResponse> HandleAsync(SampleQuery request, CancellationToken cancellationToken) =>
            Task.FromResult(new SampleResponse($"{request.Id}|{request.Limit}|{request.Sort}|{request.Transitive}|{request.Color}|{request.Cursor}"));
    }

    [Get("/faulting/{kind}")]
    public sealed class FaultingShape : ApiEndpointWithoutRequest<SampleResponse>
    {
        public override void Configure(ApiEndpointOptions options) => options.Operation = "FaultingShape";

        public override Task<SampleResponse> HandleAsync(CancellationToken cancellationToken) =>
            (HttpContext.Request.RouteValues["kind"] as string) switch
            {
                "domain" => throw new SampleDomainException("promotion-conflict"),
                "rendered" => throw new RenderedDomainException(),
                "canceled" => throw new OperationCanceledException("canceled by test"),
                _ => throw new InvalidOperationException("secret-internal-details")
            };
    }
}

/// <summary>An attribute-contributed convention, proving the host-layer extension point.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TestMarkerAttribute : Attribute, IEndpointConventionAttribute
{
    public void Apply(IEndpointConventionBuilder builder) => builder.WithMetadata(new TestMarkerMetadata());
}

public sealed class TestMarkerMetadata;

[Get("/marked")]
[TestMarker]
public sealed class MarkedEndpoint : ApiEndpointWithoutRequest<SampleResponse>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "MarkedEndpoint";
        options.Convention(builder => builder.WithMetadata(new TestMarkerMetadata()));
    }

    public override Task<SampleResponse> HandleAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new SampleResponse("marked"));
}

/// <summary>Proves Configure runs on an uninitialized instance: the constructor throws.</summary>
[Get("/uninitialized")]
public sealed class ThrowingConstructorEndpoint : ApiEndpointWithoutRequest<SampleResponse>
{
    public ThrowingConstructorEndpoint() => throw new InvalidOperationException("constructor must not run at map time");

    public override void Configure(ApiEndpointOptions options) => options.Operation = "ThrowingConstructorEndpoint";

    public override Task<SampleResponse> HandleAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new SampleResponse("never"));
}

// ---------- Host factory ----------

public sealed class PipelineHost(IHost host) : IAsyncDisposable
{
    public HttpClient Client { get; } = host.GetTestClient();
    public IHost Host { get; } = host;

    public static async Task<PipelineHost> StartAsync(
        Action<ModuleEndpointGroup> map,
        Action<IServiceCollection>? configureServices = null,
        string ownerId = "Test.Owner",
        string jsonContentType = "application/json")
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddSingleton<IEndpointProblemWriter, TestProblemWriter>();
                    configureServices?.Invoke(services);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => map(endpoints.MapModuleEndpoints(
                        ownerId,
                        new TestJsonContext(new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                        jsonContentType)));
                });
            })
            .Build();

        await host.StartAsync();
        return new PipelineHost(host);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Host.StopAsync();
        Host.Dispose();
    }
}
