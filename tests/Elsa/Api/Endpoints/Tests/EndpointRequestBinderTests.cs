using Elsa.Api.AspNetCore;
using Elsa.Api.Endpoints.Tests.Support;
using Microsoft.AspNetCore.Http;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Elsa.Api.Endpoints.Tests;

/// <summary>
/// Direct branch coverage for <see cref="EndpointRequestBinder"/>: the five body modes, the
/// per-parameter route → supplied-body → query → default precedence, strict typed parsing, and the
/// deliberate narrowness guarantees.
/// </summary>
public sealed class EndpointRequestBinderTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static DefaultHttpContext Context(
        string? body = null,
        string? contentType = null,
        string? query = null,
        params (string Key, string Value)[] route)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body ?? string.Empty));
        if (contentType is not null)
            context.Request.ContentType = contentType;
        if (query is not null)
            context.Request.QueryString = new QueryString(query);
        foreach (var (key, value) in route)
            context.Request.RouteValues[key] = value;
        return context;
    }

    private static async Task<EndpointBindingResult<T>> BindAsync<T>(
        DefaultHttpContext context,
        EndpointBodyMode mode,
        bool strict = false) =>
        await EndpointRequestBinder.BindAsync<T>(context, Web, mode, strict);

    // ---------- Body modes ----------

    [Fact]
    public async Task None_ignores_a_body_and_binds_route_and_query()
    {
        var context = Context(body: """{"id":"from-body"}""", contentType: "application/json",
            query: "?name=q-name", route: ("id", "route-1"));

        var result = await BindAsync<SampleBody>(context, EndpointBodyMode.None);

        Assert.True(result.Succeeded);
        Assert.Equal("route-1", result.Value!.Id);
        Assert.Equal("q-name", result.Value.Name);
    }

    [Fact]
    public async Task Required_with_wrong_content_type_is_unsupported_media()
    {
        var context = Context(body: "id=x", contentType: "application/x-www-form-urlencoded");

        var result = await BindAsync<SampleBody>(context, EndpointBodyMode.Required);

        Assert.Equal(EndpointBindingFailure.UnsupportedMediaType, result.Failure);
        Assert.Equal("The request content type must be application/json.", result.Message);
    }

    [Fact]
    public async Task Required_with_absent_content_type_still_reads_the_body()
    {
        var context = Context(body: """{"id":"typed"}""");

        var result = await BindAsync<SampleBody>(context, EndpointBodyMode.Required);

        Assert.True(result.Succeeded);
        Assert.Equal("typed", result.Value!.Id);
    }

    [Fact]
    public async Task RequiredWithContentType_treats_an_absent_content_type_as_unsupported_media()
    {
        var context = Context(body: """{"id":"typed"}""");

        var result = await BindAsync<SampleBody>(context, EndpointBodyMode.RequiredWithContentType);

        Assert.Equal(EndpointBindingFailure.UnsupportedMediaType, result.Failure);
    }

    [Theory]
    [InlineData(EndpointBodyMode.Required)]
    [InlineData(EndpointBodyMode.RequiredWithContentType)]
    public async Task Required_modes_reject_a_literal_null_body(EndpointBodyMode mode)
    {
        var context = Context(body: "null", contentType: "application/json");

        var result = await BindAsync<SampleBody>(context, mode);

        Assert.Equal(EndpointBindingFailure.MissingBody, result.Failure);
        Assert.Equal("A request body is required.", result.Message);
    }

    [Fact]
    public async Task OptionalWithContentType_binds_route_and_query_when_the_body_is_literal_null()
    {
        var context = Context(body: "null", contentType: "application/json",
            query: "?name=q-name", route: ("id", "route-1"));

        var result = await BindAsync<SampleBody>(context, EndpointBodyMode.OptionalWithContentType);

        Assert.True(result.Succeeded);
        Assert.Equal("route-1", result.Value!.Id);
        Assert.Equal("q-name", result.Value.Name);
    }

    [Fact]
    public async Task RequiredWithContentTypeAndPayload_rejects_a_literal_null_body_as_unsupported_media()
    {
        var context = Context(body: "null", contentType: "application/json");

        var result = await BindAsync<SampleBody>(context, EndpointBodyMode.RequiredWithContentTypeAndPayload);

        Assert.Equal(EndpointBindingFailure.UnsupportedMediaType, result.Failure);
    }

    [Fact]
    public async Task RequiredWithContentTypeAndPayload_keeps_the_content_type_gate_and_the_body_read()
    {
        var absent = Context(body: """{"id":"typed"}""");
        Assert.Equal(EndpointBindingFailure.UnsupportedMediaType,
            (await BindAsync<SampleBody>(absent, EndpointBodyMode.RequiredWithContentTypeAndPayload)).Failure);

        var supplied = Context(body: """{"id":"typed"}""", contentType: "application/json");
        var result = await BindAsync<SampleBody>(supplied, EndpointBodyMode.RequiredWithContentTypeAndPayload);
        Assert.True(result.Succeeded);
        Assert.Equal("typed", result.Value!.Id);
    }

    [Fact]
    public async Task Optional_with_a_non_json_content_type_falls_through_to_route_and_query()
    {
        var context = Context(body: "not json", contentType: "text/plain", route: ("id", "route-1"));

        var result = await BindAsync<SampleBody>(context, EndpointBodyMode.Optional);

        Assert.True(result.Succeeded);
        Assert.Equal("route-1", result.Value!.Id);
    }

    [Fact]
    public async Task Malformed_json_reports_the_serializer_message_without_the_root_path_suffix()
    {
        var context = Context(body: "{", contentType: "application/json");

        var result = await BindAsync<SampleBody>(context, EndpointBodyMode.Required);

        Assert.Equal(EndpointBindingFailure.MalformedBody, result.Failure);
        Assert.DoesNotContain(" Path: $ |", result.Message);
        Assert.Contains("JSON", result.Message);
    }

    // ---------- Per-parameter precedence ----------

    [Fact]
    public async Task Route_wins_over_a_conflicting_body_identifier()
    {
        var context = Context(body: """{"id":"from-body"}""", contentType: "application/json",
            route: ("id", "route-1"));

        var result = await BindAsync<SampleBody>(context, EndpointBodyMode.Required);

        Assert.Equal("route-1", result.Value!.Id);
    }

    [Fact]
    public async Task A_supplied_body_property_wins_over_the_query_string()
    {
        var context = Context(body: """{"id":"b","name":"from-body"}""", contentType: "application/json",
            query: "?name=from-query");

        var result = await BindAsync<SampleBody>(context, EndpointBodyMode.Required);

        Assert.Equal("from-body", result.Value!.Name);
    }

    [Fact]
    public async Task A_property_the_body_omitted_binds_from_the_query_string()
    {
        var context = Context(body: """{"id":"b"}""", contentType: "application/json",
            query: "?name=from-query&count=7");

        var result = await BindAsync<SampleBody>(context, EndpointBodyMode.Required);

        Assert.Equal("from-query", result.Value!.Name);
        Assert.Equal(7, result.Value.Count);
    }

    [Fact]
    public async Task A_property_the_body_supplied_as_null_stays_null()
    {
        var context = Context(body: """{"id":"b","name":null}""", contentType: "application/json",
            query: "?name=from-query");

        var result = await BindAsync<SampleBody>(context, EndpointBodyMode.Required);

        Assert.Null(result.Value!.Name);
    }

    [Fact]
    public async Task A_parameter_absent_everywhere_takes_its_constructor_default()
    {
        var context = Context(body: """{"id":"b"}""", contentType: "application/json");

        var result = await BindAsync<SampleBody>(context, EndpointBodyMode.Required);

        Assert.Equal(5, result.Value!.Count);
        Assert.Null(result.Value.Note);
    }

    [Fact]
    public async Task Query_defaults_apply_per_parameter()
    {
        var context = Context(query: "?id=q-1&cursor=abc");

        var result = await BindAsync<SampleQuery>(context, EndpointBodyMode.None);

        Assert.Equal("q-1", result.Value!.Id);
        Assert.Equal(25, result.Value.Limit);
        Assert.Equal("name-asc", result.Value.Sort);
        Assert.Null(result.Value.Transitive);
        Assert.Equal(SampleColor.Red, result.Value.Color);
        Assert.Equal("abc", result.Value.Cursor);
    }

    [Fact]
    public async Task Query_keys_match_case_insensitively()
    {
        var context = Context(query: "?ID=q-1&LIMIT=9");

        var result = await BindAsync<SampleQuery>(context, EndpointBodyMode.None);

        Assert.Equal("q-1", result.Value!.Id);
        Assert.Equal(9, result.Value.Limit);
    }

    // ---------- Property-bound contracts ----------

    [Fact]
    public async Task Init_only_contracts_bind_route_over_body_and_query_for_omitted_properties()
    {
        var context = Context(body: """{"id":"from-body","limit":9}""", contentType: "application/json",
            query: "?name=from-query", route: ("id", "route-1"));

        var result = await BindAsync<SamplePropertyContract>(context, EndpointBodyMode.Required);

        Assert.Equal("route-1", result.Value!.Id);
        Assert.Equal(9, result.Value.Limit);
        Assert.Equal("from-query", result.Value.Name);
    }

    // ---------- Lenient vs strict typed parsing ----------

    [Fact]
    public async Task Lenient_parsing_falls_back_to_defaults_on_an_unparseable_typed_value()
    {
        var context = Context(query: "?id=q-1&limit=invalid&transitive=invalid&color=invalid");

        var result = await BindAsync<SampleQuery>(context, EndpointBodyMode.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Value!.Limit);
        Assert.Null(result.Value.Transitive);
        Assert.Equal(SampleColor.Red, result.Value.Color);
    }

    [Theory]
    [InlineData("?id=q-1&limit=invalid", "limit", "Int32", "invalid")]
    [InlineData("?id=q-1&transitive=invalid", "transitive", "Boolean", "invalid")]
    [InlineData("?id=q-1&color=invalid", "color", "SampleColor", "invalid")]
    [InlineData("?id=q-1&limit=", "limit", "Int32", "")]
    public async Task Strict_parsing_rejects_an_unparseable_typed_value_naming_the_wire_parameter(
        string query, string expectedKey, string typeName, string raw)
    {
        var context = Context(query: query);

        var result = await BindAsync<SampleQuery>(context, EndpointBodyMode.None, strict: true);

        Assert.Equal(EndpointBindingFailure.InvalidTypedValue, result.Failure);
        Assert.Equal(expectedKey, result.Key);
        Assert.Equal($"Value [{raw}] is not valid for a [{typeName}] property!", result.Message);
    }

    [Fact]
    public async Task Strict_parsing_accepts_valid_typed_values_and_case_insensitive_enums()
    {
        var context = Context(query: "?id=q-1&limit=50&transitive=true&color=green");

        var result = await BindAsync<SampleQuery>(context, EndpointBodyMode.None, strict: true);

        Assert.True(result.Succeeded);
        Assert.Equal(50, result.Value!.Limit);
        Assert.True(result.Value.Transitive);
        Assert.Equal(SampleColor.Green, result.Value.Color);
    }

    [Fact]
    public async Task Strict_parsing_leaves_blank_string_values_untouched()
    {
        var context = Context(query: "?id=q-1&cursor=");

        var result = await BindAsync<SampleQuery>(context, EndpointBodyMode.None, strict: true);

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.Value!.Cursor);
    }

    // ---------- Deliberate narrowness ----------

    [Fact]
    public async Task An_unsupported_parameter_type_throws_instead_of_misbinding()
    {
        var context = Context(query: "?id=q-1&window=00:01:00");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await BindAsync<WithUnsupported>(context, EndpointBodyMode.None));

        Assert.Contains("unsupported type", exception.Message);
        Assert.Contains("EndpointRequestBinder", exception.Message);
    }

    [Fact]
    public async Task A_contract_with_two_public_constructors_is_rejected()
    {
        var context = Context(query: "?id=q-1");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await BindAsync<TwoConstructors>(context, EndpointBodyMode.None));

        Assert.Contains("exactly one public constructor", exception.Message);
    }
}
