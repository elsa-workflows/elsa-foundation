using System.Text;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Http.Services;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Models;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Focused unit coverage for the <see cref="WriteHttpResponse"/> live-write branch (spec 089 sub-unit E, E-D2/E-D3).
/// The activity always records the durable <see cref="HttpResponseInstruction"/> (proven end-to-end by
/// <see cref="WriteHttpResponseExecutionTests"/>); here it additionally writes the live response when — and only
/// when — the request-scoped <see cref="SyncHttpResponseSink"/> is populated with a live <see cref="HttpContext"/>.
/// These tests drive the activity through the real <see cref="SimpleActivityExecutionContext"/> with a controlled
/// service provider so the sink/content-factory seams are exercised exactly as in production.
/// </summary>
public sealed class WriteHttpResponseLiveWriteTests
{
    [Fact]
    public async Task ArtifactOnly_WhenNoSinkRegistered()
    {
        // Absent sink registration ⇒ artifact-only, NO fault (the resolve is null-safe).
        var (activity, context) = Build(services => { }, status: 201, body: "created", contentType: "application/json");

        await ((IActivity)activity).ExecuteAsync(context);

        AssertArtifactRecorded(context, status: 201, body: "created", contentType: "application/json");
    }

    [Fact]
    public async Task ArtifactOnly_WhenSinkPresentButUnpopulated_AsyncModeSafety()
    {
        // The async-mode-safety pin: a fresh scope's sink exists but is unpopulated (the middleware populates it
        // only for sync-mode dispatches). Even though an HttpContext technically exists on the flow, the
        // unpopulated sink keeps the run artifact-only — the discriminator is the sink, never IHttpContextAccessor.
        var (activity, context) = Build(
            services => services.AddScoped<SyncHttpResponseSink>(),
            status: 200, body: "ok", contentType: "text/plain");

        await ((IActivity)activity).ExecuteAsync(context);

        AssertArtifactRecorded(context, status: 200, body: "ok", contentType: "text/plain");
    }

    [Fact]
    public async Task LiveWrite_WritesStatusHeadersAndBody_ViaMatchingFactory()
    {
        var httpContext = NewHttpContext();
        var sink = PopulatedSink(httpContext);
        var (activity, context) = Build(
            services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton<IHttpContentFactory>(new StubJsonContentFactory());
            },
            status: 201, body: """{"id":42}""", contentType: "application/json",
            headers: new Dictionary<string, string[]> { ["X-Custom"] = ["v1"] });

        await ((IActivity)activity).ExecuteAsync(context);

        Assert.Equal(201, httpContext.Response.StatusCode);
        Assert.Equal("v1", httpContext.Response.Headers["X-Custom"]);
        Assert.StartsWith("application/json", httpContext.Response.ContentType);
        Assert.Equal("""{"id":42}""", await ReadBody(httpContext));
        Assert.True(sink.ResponseWritten);
        // The durable artifact is still recorded alongside the live write.
        AssertArtifactRecorded(context, status: 201, body: """{"id":42}""", contentType: "application/json");
    }

    [Fact]
    public async Task LiveWrite_UnknownContentType_FallsBackToRawWrite()
    {
        // No factory matches the authored content type ⇒ the body is written verbatim with the authored type.
        var httpContext = NewHttpContext();
        var sink = PopulatedSink(httpContext);
        var (activity, context) = Build(
            services =>
            {
                services.AddSingleton(sink);
                services.AddSingleton<IHttpContentFactory>(new StubJsonContentFactory()); // only matches application/json
            },
            status: 200, body: "<x/>", contentType: "application/vnd.custom+xml");

        await ((IActivity)activity).ExecuteAsync(context);

        Assert.Equal("application/vnd.custom+xml", httpContext.Response.ContentType);
        Assert.Equal("<x/>", await ReadBody(httpContext));
        Assert.True(sink.ResponseWritten);
    }

    [Fact]
    public async Task LiveWrite_AbsentContentType_FallsBackToTextPlain()
    {
        var httpContext = NewHttpContext();
        var sink = PopulatedSink(httpContext);
        var (activity, context) = Build(
            services => services.AddSingleton(sink),
            status: 200, body: "hello", contentType: null);

        await ((IActivity)activity).ExecuteAsync(context);

        Assert.StartsWith("text/plain", httpContext.Response.ContentType);
        Assert.Equal("hello", await ReadBody(httpContext));
        Assert.True(sink.ResponseWritten);
    }

    [Fact]
    public async Task LiveWrite_SkippedWhenResponseHasStarted()
    {
        // A second WriteHttpResponse in one run finds Response.HasStarted == true and skips the live write; the
        // artifact is still recorded and the sink is NOT (re-)marked by this activity.
        var httpContext = StartedHttpContext();
        var sink = PopulatedSink(httpContext);
        var (activity, context) = Build(
            services => services.AddSingleton(sink),
            status: 200, body: "second", contentType: "text/plain");

        await ((IActivity)activity).ExecuteAsync(context);

        Assert.True(httpContext.Response.HasStarted); // guard: the precondition really held
        Assert.False(sink.ResponseWritten);
        Assert.Equal(200, httpContext.Response.StatusCode); // untouched (default), no live write attempted
        AssertArtifactRecorded(context, status: 200, body: "second", contentType: "text/plain");
    }

    // ---- helpers ----

    /// <summary>
    /// Builds the activity with its authored inputs and a <see cref="SimpleActivityExecutionContext"/> whose memory
    /// register is seeded with those input values, over a provider configured by <paramref name="configure"/>.
    /// </summary>
    private static (WriteHttpResponse Activity, SimpleActivityExecutionContext Context) Build(
        Action<IServiceCollection> configure, int? status, string? body, string? contentType, IDictionary<string, string[]>? headers = null)
    {
        var activity = new WriteHttpResponse();
        var presets = new List<(IMemoryBlockReference Reference, object? Value)>();

        if (status is { } s)
        {
            activity.StatusCode = new(new MemoryBlockReference("StatusCode"));
            presets.Add((activity.StatusCode.MemoryBlockReference(), s));
        }

        if (body is not null)
        {
            activity.Body = new(new MemoryBlockReference("Body"));
            presets.Add((activity.Body.MemoryBlockReference(), body));
        }

        if (contentType is not null)
        {
            activity.ContentType = new(new MemoryBlockReference("ContentType"));
            presets.Add((activity.ContentType.MemoryBlockReference(), contentType));
        }

        if (headers is not null)
        {
            activity.Headers = new(new MemoryBlockReference("Headers"));
            presets.Add((activity.Headers.MemoryBlockReference(), headers));
        }

        var services = new ServiceCollection();
        configure(services);
        var context = new SimpleActivityExecutionContext(services.BuildServiceProvider(), activity, CancellationToken.None, workflowExecutionId: "wf-1");
        foreach (var (reference, value) in presets)
            context.Set(reference, value);

        return (activity, context);
    }

    private static DefaultHttpContext NewHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        return httpContext;
    }

    private static SyncHttpResponseSink PopulatedSink(HttpContext httpContext)
    {
        var sink = new SyncHttpResponseSink();
        sink.Populate(httpContext);
        return sink;
    }

    private static async Task<string> ReadBody(HttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static void AssertArtifactRecorded(SimpleActivityExecutionContext context, int status, string? body, string? contentType)
    {
        Assert.True(context.WorkflowOutputAssignmentRequested);
        var instruction = Assert.IsType<HttpResponseInstruction>(context.RequestedWorkflowOutputs[HttpResponseInstruction.OutputName]);
        Assert.Equal(status, instruction.StatusCode);
        Assert.Equal(body, instruction.Body);
        Assert.Equal(contentType, instruction.ContentType);
    }

    /// <summary>Builds a <see cref="DefaultHttpContext"/> whose response reports <c>HasStarted == true</c>.</summary>
    private static DefaultHttpContext StartedHttpContext()
    {
        var features = new Microsoft.AspNetCore.Http.Features.FeatureCollection();
        features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(new StartedResponseFeature());
        return new DefaultHttpContext(features);
    }

    /// <summary>A minimal public <see cref="IHttpContentFactory"/> mimicking the JSON factory (the real one is internal).</summary>
    private sealed class StubJsonContentFactory : IHttpContentFactory
    {
        public IEnumerable<string> SupportedContentTypes => ["application/json", "text/json"];

        public HttpContent CreateHttpContent(object content, string contentType) =>
            new RawStringContent((string)content, Encoding.UTF8, contentType);
    }

    /// <summary>An <see cref="Microsoft.AspNetCore.Http.Features.IHttpResponseFeature"/> that reports the response has already started.</summary>
    private sealed class StartedResponseFeature : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
