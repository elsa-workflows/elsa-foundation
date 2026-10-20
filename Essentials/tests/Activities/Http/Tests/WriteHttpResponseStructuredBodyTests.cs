using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Http.Services;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.Http;
using Xunit;
using static Elsa.Activities.Http.Tests.HttpResponseInstructionFixture;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Spec 256 (#3050): a response body is any JSON value. Text is sent as is; any other value is sent as compact JSON.
/// With no content type, delivery infers one as Elsa 3 did: <c>text/plain</c> for text, <c>application/json</c> otherwise.
/// </summary>
public sealed class WriteHttpResponseStructuredBodyTests
{
    // ── Structured bodies (US1) ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{ "a": 1, "B": [true, null] }""", """{"a":1,"B":[true,null]}""")]
    [InlineData("""[1, "two"]""", """[1,"two"]""")]
    [InlineData("42", "42")]
    [InlineData("1.5", "1.5")]
    [InlineData("true", "true")]
    [InlineData("{}", "{}")]
    [InlineData("[]", "[]")]
    public async Task A_structured_body_without_a_content_type_is_sent_as_compact_JSON(string body, string expected)
    {
        var (status, contentType, text) = await DeliverAsync(Json(body), contentType: null);

        Assert.Equal(200, status);
        Assert.Equal("application/json", contentType);
        Assert.Equal(expected, text);
    }

    [Fact]
    public async Task A_structured_body_keeps_its_keys_and_characters_as_authored()
    {
        var (_, _, text) = await DeliverAsync(Json("""{"FirstName":"Zoë","html":"<b>&'"}"""), contentType: " ");

        Assert.Equal("""{"FirstName":"Zoë","html":"<b>&'"}""", text);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/json")]
    [InlineData("Application/JSON")]
    public async Task A_structured_body_with_a_JSON_content_type_goes_through_the_JSON_writer(string contentType)
    {
        var json = new RecordingFactory(["application/json", "text/json"]);

        var (_, sent, text) = await DeliverAsync(Json("""{"a":1}"""), contentType, json);

        Assert.Equal("""{"a":1}""", text);
        Assert.Equal(contentType, sent);
        Assert.Equal("""{"a":1}""", Assert.Single(json.Received));
    }

    [Fact]
    public async Task An_inferred_content_type_goes_through_the_matching_writer()
    {
        var json = new RecordingFactory(["application/json"]);

        var (_, contentType, _) = await DeliverAsync(Json("""{"a":1}"""), contentType: null, json);

        Assert.Equal("application/json", contentType);
        Assert.Single(json.Received);
    }

    [Fact]
    public async Task A_structured_body_with_an_unmatched_content_type_is_written_as_JSON_text_with_that_type()
    {
        var (_, contentType, text) = await DeliverAsync(Json("""{"a":1}"""), "application/vnd.custom+json");

        Assert.Equal("application/vnd.custom+json", contentType);
        Assert.Equal("""{"a":1}""", text);
    }

    [Fact]
    public async Task A_JSON_null_body_is_no_body()
    {
        var (_, contentType, text) = await DeliverAsync(Json("null"), contentType: null);

        Assert.Null(contentType);
        Assert.Equal(string.Empty, text);
    }

    [Theory]
    [InlineData("""{"a":1}""", null)]
    [InlineData("\"text\"", "text/plain")]
    public async Task A_204_response_writes_no_body(string body, string? contentType)
    {
        var (status, sentContentType, text) = await DeliverAsync(Json(body), contentType, status: StatusCodes.Status204NoContent);

        Assert.Equal(204, status);
        Assert.Equal(contentType, sentContentType);
        Assert.Equal(string.Empty, text);
    }

    // ── Text bodies keep their behaviour (US2) ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_text_body_that_looks_like_JSON_is_sent_verbatim_as_text()
    {
        var (_, contentType, text) = await DeliverAsync(Text("""{ "message": "Hello" }"""), contentType: null);

        Assert.Equal("text/plain", contentType);
        Assert.Equal("""{ "message": "Hello" }""", text);
    }

    [Fact]
    public async Task A_text_body_with_a_JSON_content_type_is_sent_unquoted()
    {
        var (_, contentType, text) = await DeliverAsync(Text("""{"a":1}"""), "application/json");

        Assert.Equal("application/json", contentType);
        Assert.Equal("""{"a":1}""", text);
    }

    [Fact]
    public async Task An_empty_text_body_is_no_body()
    {
        var (_, contentType, text) = await DeliverAsync(Text(string.Empty), contentType: null);

        Assert.Null(contentType);
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public async Task A_committed_instruction_with_a_text_body_still_delivers_it()
    {
        var instruction = JsonSerializer.Deserialize<HttpResponseInstruction>(
            """{"statusCode":200,"headers":{},"body":"stored text","contentType":null}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(JsonValueKind.String, instruction.Body!.Value.ValueKind);
        var (_, contentType, text) = await DeliverAsync(instruction.Body, instruction.ContentType);

        Assert.Equal("text/plain", contentType);
        Assert.Equal("stored text", text);
    }

    private static async Task<(int Status, string? ContentType, string Body)> DeliverAsync(
        JsonElement? body,
        string? contentType,
        IHttpContentFactory? factory = null,
        int status = StatusCodes.Status200OK)
    {
        var store = await StoreWith(new HttpResponseInstruction(status, new Dictionary<string, string[]>(), body, contentType));
        var delivery = new HttpResponseInstructionDelivery(store, factory is null ? [] : [factory]);
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        Assert.True(await delivery.TryDeliverAsync(httpContext.Response, ["wf-1"]));

        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body, Encoding.UTF8);
        return (httpContext.Response.StatusCode, httpContext.Response.ContentType, await reader.ReadToEndAsync());
    }

    private sealed class RecordingFactory(string[] supported) : IHttpContentFactory
    {
        public List<string> Received { get; } = [];

        public IEnumerable<string> SupportedContentTypes => supported;

        public HttpContent CreateHttpContent(object content, string contentType)
        {
            Received.Add((string)content);
            return new RawStringContent((string)content, Encoding.UTF8, contentType);
        }
    }
}
