using System.Net;
using System.Text.Json;
using Elsa.Diagnostics.StructuredLogs.Tests.Support;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogsApiQueryTests
{
    [Theory]
    [InlineData("?minLevel=&category=%20%20&source=&take=", HttpStatusCode.OK)]
    [InlineData("?minLevel=wArNiNg&take=2", HttpStatusCode.OK)]
    [InlineData("?take=0", HttpStatusCode.OK)]
    [InlineData("?take=-1", HttpStatusCode.BadRequest)]
    [InlineData("?take=1%2C000", HttpStatusCode.BadRequest)]
    // StringValues preserves the legacy comma-joined enum parsing behavior for repeated minLevel values.
    [InlineData("?minLevel=Warning&minLevel=Error", HttpStatusCode.OK)]
    public async Task Recent_route_preserves_raw_query_binding(string query, HttpStatusCode expectedStatus)
    {
        await using var host = await StructuredLogsApiHost.StartReplacementAsync();
        using var request = AuthorizedGet(StructuredLogsCompatibilityCases.RecentPath + query);
        using var response = await host.Client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(
            expectedStatus == HttpStatusCode.OK ? "application/json" : "text/plain; charset=utf-8",
            response.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task Recent_route_clamps_large_take_and_preserves_store_order()
    {
        await using var host = await StructuredLogsApiHost.StartReplacementAsync();
        using var request = AuthorizedGet(StructuredLogsCompatibilityCases.RecentPath + "?take=999");
        using var response = await host.Client.SendAsync(request);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([1L, 2L, 3L], json.RootElement.EnumerateArray().Select(x => x.GetProperty("sequence").GetInt64()));
    }

    [Fact]
    public async Task Recent_route_returns_the_exact_empty_json_array()
    {
        await using var host = await StructuredLogsApiHost.StartReplacementAsync(seed: false);
        using var request = AuthorizedGet(StructuredLogsCompatibilityCases.RecentPath);
        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sources_route_is_stable_and_custom_paths_do_not_leave_the_default_route_mapped()
    {
        await using var defaultHost = await StructuredLogsApiHost.StartReplacementAsync();
        using var firstRequest = AuthorizedGet(StructuredLogsCompatibilityCases.SourcesPath);
        using var firstResponse = await defaultHost.Client.SendAsync(firstRequest);
        var first = await firstResponse.Content.ReadAsStringAsync();

        using var secondRequest = AuthorizedGet(StructuredLogsCompatibilityCases.SourcesPath);
        using var secondResponse = await defaultHost.Client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(first, await secondResponse.Content.ReadAsStringAsync());

        await using var customHost = await StructuredLogsApiHost.StartReplacementAsync(customPaths: true);
        using var customRequest = AuthorizedGet(StructuredLogsCompatibilityCases.CustomSourcesPath);
        using var customResponse = await customHost.Client.SendAsync(customRequest);
        using var defaultRequest = AuthorizedGet(StructuredLogsCompatibilityCases.SourcesPath);
        using var absentDefaultResponse = await customHost.Client.SendAsync(defaultRequest);

        Assert.Equal(HttpStatusCode.OK, customResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absentDefaultResponse.StatusCode);
    }

    private static HttpRequestMessage AuthorizedGet(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation(StructuredLogsApiHost.IdentityHeader, StructuredLogsApiHost.ExactIdentity);
        return request;
    }
}
