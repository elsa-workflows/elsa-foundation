using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Http.Services;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using static Elsa.Activities.Http.Tests.HttpResponseInstructionFixture;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Verifies that the HTTP transport writes the selected response instruction.
/// </summary>
public sealed class WriteHttpResponseLiveWriteTests
{
    [Fact]
    public async Task CommittedInstruction_WritesStatusHeadersAndBody_ViaMatchingFactory()
    {
        var instruction = new HttpResponseInstruction(
            201,
            new Dictionary<string, string[]> { ["X-Custom"] = ["v1"] },
            Text("""{"id":42}"""),
            "application/json");
        var httpContext = NewHttpContext();

        await HttpResponseInstructionDelivery.WriteAsync(httpContext.Response, instruction, "wf-1", [new StubJsonContentFactory()], CancellationToken.None);

        Assert.Equal(201, httpContext.Response.StatusCode);
        Assert.Equal("v1", httpContext.Response.Headers["X-Custom"]);
        Assert.StartsWith("application/json", httpContext.Response.ContentType);
        Assert.Equal("""{"id":42}""", await ReadBody(httpContext));
    }

    [Fact]
    public async Task UnknownContentType_FallsBackToRawWrite()
    {
        var instruction = new HttpResponseInstruction(
            200,
            new Dictionary<string, string[]>(),
            Text("<x/>"),
            "application/vnd.custom+xml");
        var httpContext = NewHttpContext();

        await HttpResponseInstructionDelivery.WriteAsync(httpContext.Response, instruction, "wf-1", [], CancellationToken.None);
        Assert.Equal("application/vnd.custom+xml", httpContext.Response.ContentType);
        Assert.Equal("<x/>", await ReadBody(httpContext));
    }

    [Fact]
    public async Task AbsentContentType_FallsBackToTextPlain()
    {
        var instruction = new HttpResponseInstruction(
            200,
            new Dictionary<string, string[]>(),
            Text("hello"),
            null);
        var httpContext = NewHttpContext();

        await HttpResponseInstructionDelivery.WriteAsync(httpContext.Response, instruction, "wf-1", [], CancellationToken.None);
        Assert.StartsWith("text/plain", httpContext.Response.ContentType);
        Assert.Equal("hello", await ReadBody(httpContext));
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

    private sealed class StubJsonContentFactory : IHttpContentFactory
    {
        public IEnumerable<string> SupportedContentTypes => ["application/json", "text/json"];

        public HttpContent CreateHttpContent(object content, string contentType) =>
            new RawStringContent((string)content, Encoding.UTF8, contentType);
    }
}
