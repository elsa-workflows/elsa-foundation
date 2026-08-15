using System.Net;
using System.Text;
using Elsa.Studio.Preferences.Tests.Support;
using Xunit;

namespace Elsa.Studio.Preferences.Tests;

public sealed class StudioPreferencesApiAuthorizationTests
{
    [Theory]
    [InlineData(null, 401)]
    [InlineData("denied", 403)]
    [InlineData("read", 200)]
    [InlineData("write", 200)]
    [InlineData("wildcard", 200)]
    [InlineData("untrusted", 401)]
    [InlineData("resource-denied", 403)]
    public async Task Get_uses_the_shared_normalized_permission_and_resource_evaluator(string? identity, int expectedStatus)
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();
        using var response = await host.Client.SendAsync(GetRequest(identity));

        Assert.Equal(expectedStatus, (int)response.StatusCode);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("denied", 403)]
    [InlineData("read", 403)]
    [InlineData("write", 200)]
    [InlineData("wildcard", 200)]
    [InlineData("untrusted", 401)]
    [InlineData("resource-denied", 403)]
    public async Task Put_uses_the_shared_write_permission_and_resource_evaluator(string? identity, int expectedStatus)
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();
        var before = await host.FindDashboardAsync();

        using var response = await host.Client.SendAsync(PutRequest(identity));

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        if (expectedStatus != 200)
            Assert.Equal(before, await host.FindDashboardAsync());
    }

    private static HttpRequestMessage GetRequest(string? identity)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/_elsa/studio/preferences/dashboard");
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(StudioPreferencesCanaryHost.IdentityHeader, identity);
        request.Headers.TryAddWithoutValidation("X-Elsa-Studio-Host-Id", StudioPreferencesCanaryHost.HostId);
        return request;
    }

    private static HttpRequestMessage PutRequest(string? identity)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/_elsa/studio/preferences/dashboard");
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(StudioPreferencesCanaryHost.IdentityHeader, identity);
        request.Headers.TryAddWithoutValidation("X-Elsa-Studio-Host-Id", StudioPreferencesCanaryHost.HostId);
        request.Headers.TryAddWithoutValidation("If-Match", "\"rev-1\"");
        request.Content = new StringContent(
            "{\"schemaVersion\":1,\"value\":{\"layout\":\"authorization\"}}",
            Encoding.UTF8,
            "application/json");
        return request;
    }
}
