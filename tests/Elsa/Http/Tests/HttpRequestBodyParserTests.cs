using System.Text;
using System.Text.Json;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Http.Services;
using Xunit;

namespace Elsa.Http.Tests;

public class HttpRequestBodyParserTests
{
    private readonly IHttpRequestBodyParser parser = new HttpRequestBodyParser();

    [Fact]
    public void Parse_JsonObject_ReturnsParsedObjectElement()
    {
        var result = parser.Parse("application/json", """{"name":"Alice","age":42}""");

        Assert.NotNull(result);
        var element = result!.Value;
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        Assert.Equal("Alice", element.GetProperty("name").GetString());
        Assert.Equal(42, element.GetProperty("age").GetInt32());
    }

    [Fact]
    public void Parse_JsonArray_ReturnsParsedArrayElement()
    {
        var result = parser.Parse("application/json", "[1,2,3]");

        Assert.NotNull(result);
        var element = result!.Value;
        Assert.Equal(JsonValueKind.Array, element.ValueKind);
        Assert.Equal(3, element.GetArrayLength());
    }

    [Fact]
    public void Parse_JsonStructuredSuffix_IsTreatedAsJson()
    {
        var result = parser.Parse("application/problem+json", """{"title":"Bad Request","status":400}""");

        Assert.NotNull(result);
        Assert.Equal(JsonValueKind.Object, result!.Value.ValueKind);
        Assert.Equal(400, result.Value.GetProperty("status").GetInt32());
    }

    [Fact]
    public void Parse_TextJson_IsTreatedAsJson()
    {
        var result = parser.Parse("text/json", """{"ok":true}""");

        Assert.NotNull(result);
        Assert.True(result!.Value.GetProperty("ok").GetBoolean());
    }

    [Theory]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("application/json;charset=utf-8")]
    [InlineData("application/json ; charset=utf-8")]
    public void Parse_JsonWithCharsetParameter_IsTolerated(string contentType)
    {
        var result = parser.Parse(contentType, """{"n":1}""");

        Assert.NotNull(result);
        Assert.Equal(1, result!.Value.GetProperty("n").GetInt32());
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsNullWithoutThrowing()
    {
        var result = parser.Parse("application/json", "{ this is not json");

        Assert.Null(result);
    }

    [Fact]
    public void Parse_TextPlain_ReturnsStringElement()
    {
        var result = parser.Parse("text/plain", "hello world");

        Assert.NotNull(result);
        Assert.Equal(JsonValueKind.String, result!.Value.ValueKind);
        Assert.Equal("hello world", result.Value.GetString());
    }

    [Fact]
    public void Parse_TextHtml_ReturnsStringElement()
    {
        var result = parser.Parse("text/html", "<p>hi</p>");

        Assert.NotNull(result);
        Assert.Equal(JsonValueKind.String, result!.Value.ValueKind);
        Assert.Equal("<p>hi</p>", result.Value.GetString());
    }

    [Fact]
    public void Parse_UnknownContentType_ReturnsNull()
    {
        var result = parser.Parse("application/octet-stream", "");

        Assert.Null(result);
    }

    [Fact]
    public void Parse_NullContentType_ReturnsNull()
    {
        var result = parser.Parse(null, """{"n":1}""");

        Assert.Null(result);
    }

    [Fact]
    public void Parse_EmptyContentType_ReturnsNull()
    {
        var result = parser.Parse("", """{"n":1}""");

        Assert.Null(result);
    }

    [Fact]
    public void Parse_EmptyBody_ReturnsNull()
    {
        var result = parser.Parse("application/json", "");

        Assert.Null(result);
    }

    /// <summary>
    /// Regression pin: the request-side parser is a separate seam and must not disturb the response-side
    /// <see cref="IHttpContentParser"/> path used by SendHttpRequest. This pins that the response-side
    /// contract is unchanged — the (public) <see cref="XmlHttpContentParser"/> keeps its stream-shaped
    /// <c>HttpResponseParserContext</c> signature and still parses a response body verbatim into a string.
    /// If T005 had reused/reshaped the response parsers, this signature or behaviour would break.
    /// </summary>
    [Fact]
    public async Task ResponseSideParser_ContractUntouched_StillParsesVerbatim()
    {
        const string xml = "<TestPayload><Name>Bob</Name></TestPayload>";
        var responseParser = new XmlHttpContentParser();
        var context = new HttpResponseParserContext(
            new MemoryStream(Encoding.UTF8.GetBytes(xml)),
            "application/xml",
            typeof(string),
            new Dictionary<string, string[]>(),
            CancellationToken.None);

        Assert.True(responseParser.GetSupportsContentType(context));
        var result = await responseParser.ReadAsync(context);
        Assert.Equal(xml, result);
    }
}
