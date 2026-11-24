using System.Text;
using System.Text.Json;
using Elsa.Http.Core.Models;
using Elsa.Http.Core.Contracts;
using Elsa.Serialization.Core;
using CShells;
using CShells.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Http.Tests;

public sealed class JsonHttpContentParserTests
{
    [Theory]
    [InlineData("{ \"UpperKey\":\"Ren\\u00e9<+>\", \"n\":1.0, \"wide\":12345678901234567890123456789012345, \"x\":null }")]
    [InlineData("[ 1e0, null, { \"CaseKey\":\"\\u0061\" } ]")]
    [InlineData("null")]
    [InlineData("\"Ren\\u00e9\"")]
    public async Task Any_JSON_response_keeps_its_tokens_through_the_registered_outgoing_body_writer(string raw)
    {
        var services = new ServiceCollection();
        new HttpFeature(new ShellFeatureContext(new ShellSettings { Id = new ShellId("json-forwarding-tests") }, [])).ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var parser = provider.GetServices<IHttpContentParser>().Single(value => value.Priority == 0 && value.GetSupportsContentType(Context(raw, typeof(object))));
        var factory = provider.GetServices<IHttpContentFactory>().Single(value => value.SupportedContentTypes.Contains("application/json"));

        var value = await parser.ReadAsync(Context(raw, typeof(object)));

        Assert.Equal(raw, Assert.IsType<JsonElement>(value).GetRawText());
        Assert.Equal(raw, JsonConversionContract.SerializeToElement(value).GetRawText());
        using var forwarded = factory.CreateHttpContent(value, "application/json");
        Assert.Equal(raw, await forwarded.ReadAsStringAsync());
        Assert.Equal("application/json", forwarded.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Transport_JSON_is_parsed_before_strict_scalar_conversion()
    {
        var parser = Parser();
        Assert.Equal(42, await parser.ReadAsync(Context("42", typeof(int))));
        Assert.Equal(true, await parser.ReadAsync(Context("true", typeof(bool))));
        await Assert.ThrowsAnyAsync<JsonException>(() => parser.ReadAsync(Context("\"42\"", typeof(int))));
        await Assert.ThrowsAnyAsync<JsonException>(() => parser.ReadAsync(Context("5.0", typeof(int))));
    }

    [Fact]
    public async Task A_typed_response_records_case_mismatches_and_a_raw_response_preserves_its_text()
    {
        const string raw = "{ \"name\":\"Ada\", \"NAME\":\"other\", \"n\":1.0 }";
        var notes = new List<JsonConversionDiagnostic>();
        var parser = Parser();
        var order = Assert.IsType<Order>(await parser.ReadAsync(Context(raw, typeof(Order)) with { ReportJsonDiagnostic = notes.Add }));
        Assert.Equal("Ada", order.Name);
        Assert.Contains(notes, note => note.Code == "json-unmatched-member" && note.Path == "$[\"NAME\"]");
        Assert.Equal(raw, Assert.IsType<JsonElement>(await parser.ReadAsync(Context(raw, typeof(JsonElement)))).GetRawText());
        Assert.Equal(raw, await parser.ReadAsync(Context(raw, typeof(string))));
    }

    [Fact]
    public async Task Typed_date_responses_require_an_explicit_instant()
    {
        var parser = Parser();
        var error = await Assert.ThrowsAsync<JsonConversionException>(() => parser.ReadAsync(Context("\"2026-07-15T09:15:00\"", typeof(DateTimeOffset))));
        Assert.Equal("json-datetime-offset-missing", error.Code);
        Assert.Equal(TimeSpan.FromHours(2), Assert.IsType<DateTimeOffset>(await parser.ReadAsync(Context("\"2026-07-15T09:15:00+02:00\"", typeof(DateTimeOffset)))).Offset);
    }

    private static HttpResponseParserContext Context(string json, Type type) => new(new MemoryStream(Encoding.UTF8.GetBytes(json)),
        "application/json", type, new Dictionary<string, string[]>(), CancellationToken.None);
    private static IHttpContentParser Parser()
    {
        var services = new ServiceCollection();
        new HttpFeature(new ShellFeatureContext(new ShellSettings { Id = new ShellId("json-response-tests") }, [])).ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        return provider.GetServices<IHttpContentParser>().Single(parser => parser.Priority == 0 && parser.GetSupportsContentType(Context("{}", typeof(object))));
    }
    private sealed record Order(string? Name);
}
