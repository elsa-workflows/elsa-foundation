using Elsa.Studio.Preferences.Tests.Support;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Elsa.Studio.Preferences.Tests;

public sealed class StudioPreferencesApiReadContractTests
{
    [Fact]
    public async Task Get_returns_the_seeded_document_and_quoted_etag()
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();
        using var response = await host.Client.SendAsync(GetRequest("read", StudioPreferencesCanaryHost.HostId, "dashboard"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.ETag is not null);
        Assert.Equal("\"rev-1\"", response.Headers.ETag!.ToString());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("dashboard", document.RootElement.GetProperty("namespace").GetString());
        Assert.Equal("wide", document.RootElement.GetProperty("value").GetProperty("layout").GetString());
    }

    [Fact]
    public async Task Get_returns_not_found_for_missing_and_unknown_namespaces()
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();

        using var missing = await host.Client.SendAsync(GetRequest("read", StudioPreferencesCanaryHost.HostId, "attention"));
        using var unknown = await host.Client.SendAsync(GetRequest("read", StudioPreferencesCanaryHost.HostId, "missing"));

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Get_rejects_missing_and_malformed_host_ids_with_bad_request()
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();

        using var missing = await host.Client.SendAsync(GetRequest("read", null, "dashboard"));
        using var malformed = await host.Client.SendAsync(GetRequest("read", "host/segment", "dashboard"));

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    private static HttpRequestMessage GetRequest(string? identity, string? hostId, string @namespace)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/_elsa/studio/preferences/{@namespace}");
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(StudioPreferencesCanaryHost.IdentityHeader, identity);
        if (hostId is not null)
            request.Headers.TryAddWithoutValidation("X-Elsa-Studio-Host-Id", hostId);
        return request;
    }

}
