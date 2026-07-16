using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Http.Services;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Verifies request-owned delivery of the typed response result after an inline workflow drain. The live
/// <see cref="HttpContext"/> never enters the transient activity activation.
/// </summary>
public sealed class WriteHttpResponseLiveWriteTests
{
    [Fact]
    public async Task NoCommittedInstruction_LeavesResponseUntouched()
    {
        var store = new InMemoryActivityExecutionStateStore();
        var delivery = new HttpResponseInstructionDelivery(store, []);
        var httpContext = NewHttpContext();

        var delivered = await delivery.TryDeliverAsync(httpContext.Response, ["wf-1"]);

        Assert.False(delivered);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task CommittedInstruction_WritesStatusHeadersAndBody_ViaMatchingFactory()
    {
        var store = await StoreWith(new HttpResponseInstruction(
            201,
            new Dictionary<string, string[]> { ["X-Custom"] = ["v1"] },
            """{"id":42}""",
            "application/json"));
        var delivery = new HttpResponseInstructionDelivery(store, [new StubJsonContentFactory()]);
        var httpContext = NewHttpContext();

        var delivered = await delivery.TryDeliverAsync(httpContext.Response, ["wf-1"]);

        Assert.True(delivered);
        Assert.Equal(201, httpContext.Response.StatusCode);
        Assert.Equal("v1", httpContext.Response.Headers["X-Custom"]);
        Assert.StartsWith("application/json", httpContext.Response.ContentType);
        Assert.Equal("""{"id":42}""", await ReadBody(httpContext));
    }

    [Fact]
    public async Task UnknownContentType_FallsBackToRawWrite()
    {
        var store = await StoreWith(new HttpResponseInstruction(
            200,
            new Dictionary<string, string[]>(),
            "<x/>",
            "application/vnd.custom+xml"));
        var delivery = new HttpResponseInstructionDelivery(store, [new StubJsonContentFactory()]);
        var httpContext = NewHttpContext();

        Assert.True(await delivery.TryDeliverAsync(httpContext.Response, ["wf-1"]));
        Assert.Equal("application/vnd.custom+xml", httpContext.Response.ContentType);
        Assert.Equal("<x/>", await ReadBody(httpContext));
    }

    [Fact]
    public async Task AbsentContentType_FallsBackToTextPlain()
    {
        var store = await StoreWith(new HttpResponseInstruction(
            200,
            new Dictionary<string, string[]>(),
            "hello",
            null));
        var delivery = new HttpResponseInstructionDelivery(store, []);
        var httpContext = NewHttpContext();

        Assert.True(await delivery.TryDeliverAsync(httpContext.Response, ["wf-1"]));
        Assert.StartsWith("text/plain", httpContext.Response.ContentType);
        Assert.Equal("hello", await ReadBody(httpContext));
    }

    [Fact]
    public async Task StartedResponse_IsPreservedWithoutReadingState()
    {
        var delivery = new HttpResponseInstructionDelivery(new ThrowingActivityStateStore(), []);
        var httpContext = StartedHttpContext();

        var delivered = await delivery.TryDeliverAsync(httpContext.Response, ["wf-1"]);

        Assert.True(delivered);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
    }

    private static async Task<InMemoryActivityExecutionStateStore> StoreWith(HttpResponseInstruction instruction)
    {
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        var invocationId = "activity-write-response";
        var valueType = new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(HttpResponseInstruction)));
        var result = ValueEnvelope.Inline(
            valueType,
            JsonSerializer.SerializeToElement(instruction, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ValueProtectionPolicy.InstanceInline);
        var completion = new ActivityCompletion(invocationId, "attempt-1", result, "Done", now, "sha256:test");
        var execution = new ActivityExecution(invocationId, "wf-1", "node-write-response", "authored-write-response", "Elsa.WriteHttpResponse", "1.0.0");
        var state = new ActivityExecutionState(
            execution,
            ActivityExecutionStatus.Completed,
            SubStatus: null,
            ScheduledAt: now,
            StartedAt: now,
            CompletedAt: now,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>())
        {
            Completion = completion
        };

        var store = new InMemoryActivityExecutionStateStore();
        await store.SaveAsync(state);
        return store;
    }

    private static DefaultHttpContext NewHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        return httpContext;
    }

    private static async Task<string> ReadBody(HttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static DefaultHttpContext StartedHttpContext()
    {
        var features = new Microsoft.AspNetCore.Http.Features.FeatureCollection();
        features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(new StartedResponseFeature());
        return new DefaultHttpContext(features);
    }

    private sealed class StubJsonContentFactory : IHttpContentFactory
    {
        public IEnumerable<string> SupportedContentTypes => ["application/json", "text/json"];

        public HttpContent CreateHttpContent(object content, string contentType) =>
            new RawStringContent((string)content, Encoding.UTF8, contentType);
    }

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

    private sealed class ThrowingActivityStateStore : Elsa.Workflows.Runtime.Core.Contracts.IActivityExecutionStateStore
    {
        public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<ActivityExecutionState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("State should not be read after the response started.");
        public ValueTask<IReadOnlyCollection<ActivityExecutionState>> ListByParentAsync(string workflowExecutionId, string parentActivityExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
