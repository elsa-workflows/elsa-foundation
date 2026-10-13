using System.Net;
using System.Text;
using System.Text.Json;
using Elsa.Studio.Preferences.Api;
using Elsa.Studio.Preferences.Core;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Models;
using Elsa.Studio.Preferences.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Studio.Preferences.Tests;

public sealed class StudioHomeLayoutApiTests
{
    [Fact]
    public async Task Get_user_returns_builtin_when_nothing_is_stored()
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();
        using var response = await host.Client.SendAsync(Request(HttpMethod.Get, StudioHomeLayoutApi.UserRoute, "read"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.ETag);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("builtin", document.RootElement.GetProperty("source").GetString());
        Assert.Equal("home-layout", document.RootElement.GetProperty("namespace").GetString());
        Assert.Equal(4, document.RootElement.GetProperty("value").GetProperty("widgets").GetArrayLength());
        Assert.Equal("home.get-started", document.RootElement.GetProperty("value").GetProperty("widgets")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Put_user_then_get_returns_the_user_document()
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();
        using var create = await host.Client.SendAsync(Put(
            StudioHomeLayoutApi.UserRoute,
            "write",
            ifNoneMatch: "*",
            body: """{"schemaVersion":1,"value":{"widgets":[{"id":"home.recent-runs","size":"L"}]}}"""));

        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Equal("\"rev-1\"", create.Headers.ETag?.ToString());
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        Assert.Equal("user", created.RootElement.GetProperty("source").GetString());
        Assert.Equal("full", created.RootElement.GetProperty("value").GetProperty("widgets")[0].GetProperty("size").GetString());

        using var read = await host.Client.SendAsync(Request(HttpMethod.Get, StudioHomeLayoutApi.UserRoute, "read"));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("\"rev-1\"", read.Headers.ETag?.ToString());
        using var document = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal("user", document.RootElement.GetProperty("source").GetString());
        Assert.Equal("home.recent-runs", document.RootElement.GetProperty("value").GetProperty("widgets")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Delete_user_resets_to_admin_default_then_builtin()
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();
        using var defaultCreate = await host.Client.SendAsync(Put(
            StudioHomeLayoutApi.DefaultRoute,
            "host-manage",
            ifNoneMatch: "*",
            body: """{"schemaVersion":1,"value":{"widgets":[{"id":"home.needs-attention","size":"wide"}]}}"""));
        Assert.Equal(HttpStatusCode.OK, defaultCreate.StatusCode);

        using var userCreate = await host.Client.SendAsync(Put(
            StudioHomeLayoutApi.UserRoute,
            "write",
            ifNoneMatch: "*",
            body: """{"schemaVersion":1,"value":{"widgets":[{"id":"home.recent-workflows","size":"small"}]}}"""));
        Assert.Equal(HttpStatusCode.OK, userCreate.StatusCode);

        using var reset = await host.Client.SendAsync(Request(HttpMethod.Delete, StudioHomeLayoutApi.UserRoute, "write"));
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        using var afterReset = await host.Client.SendAsync(Request(HttpMethod.Get, StudioHomeLayoutApi.UserRoute, "read"));
        using var afterDocument = JsonDocument.Parse(await afterReset.Content.ReadAsStringAsync());
        Assert.Equal("default", afterDocument.RootElement.GetProperty("source").GetString());
        Assert.Equal("home.needs-attention", afterDocument.RootElement.GetProperty("value").GetProperty("widgets")[0].GetProperty("id").GetString());

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IStudioPreferenceStore>();
            var defaultKey = new StudioPreferenceKey(
                StudioHomeLayout.DefaultSubjectId,
                StudioPreferencesCanaryHost.TenantId,
                StudioPreferencesCanaryHost.HostId,
                StudioPreferenceNamespaces.HomeLayout);
            Assert.NotNull(await store.FindAsync(defaultKey));
            Assert.True(await store.DeleteAsync(defaultKey));
        }

        using var builtin = await host.Client.SendAsync(Request(HttpMethod.Get, StudioHomeLayoutApi.UserRoute, "read"));
        using var builtinDocument = JsonDocument.Parse(await builtin.Content.ReadAsStringAsync());
        Assert.Equal("builtin", builtinDocument.RootElement.GetProperty("source").GetString());
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("denied", 403)]
    [InlineData("read", 200)]
    [InlineData("write", 200)]
    [InlineData("wildcard", 200)]
    [InlineData("host-read", 403)]
    [InlineData("host-manage", 403)]
    public async Task Get_user_uses_preferences_read(string? identity, int expectedStatus)
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();
        using var response = await host.Client.SendAsync(Request(HttpMethod.Get, StudioHomeLayoutApi.UserRoute, identity));
        Assert.Equal(expectedStatus, (int)response.StatusCode);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("denied", 403)]
    [InlineData("read", 403)]
    [InlineData("write", 200)]
    [InlineData("wildcard", 200)]
    [InlineData("host-manage", 403)]
    public async Task Put_user_uses_preferences_write(string? identity, int expectedStatus)
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();
        using var response = await host.Client.SendAsync(Put(
            StudioHomeLayoutApi.UserRoute,
            identity,
            ifNoneMatch: "*",
            body: """{"schemaVersion":1,"value":{"widgets":[{"id":"home.recent-runs","size":"full"}]}}"""));
        Assert.Equal(expectedStatus, (int)response.StatusCode);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("denied", 403)]
    [InlineData("read", 403)]
    [InlineData("host-read", 200)]
    [InlineData("host-manage", 200)]
    [InlineData("wildcard", 200)]
    [InlineData("write", 403)]
    public async Task Get_default_uses_host_control_read(string? identity, int expectedStatus)
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();
        using var response = await host.Client.SendAsync(Request(HttpMethod.Get, StudioHomeLayoutApi.DefaultRoute, identity));
        Assert.Equal(expectedStatus, (int)response.StatusCode);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("denied", 403)]
    [InlineData("host-read", 403)]
    [InlineData("host-manage", 200)]
    [InlineData("wildcard", 200)]
    [InlineData("write", 403)]
    public async Task Put_default_uses_host_control_manage(string? identity, int expectedStatus)
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();
        using var response = await host.Client.SendAsync(Put(
            StudioHomeLayoutApi.DefaultRoute,
            identity,
            ifNoneMatch: "*",
            body: """{"schemaVersion":1,"value":{"widgets":[{"id":"home.get-started","size":"full"}]}}"""));
        Assert.Equal(expectedStatus, (int)response.StatusCode);
    }

    [Fact]
    public async Task Put_rejects_invalid_home_layout_shape()
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();
        using var response = await host.Client.SendAsync(Put(
            StudioHomeLayoutApi.UserRoute,
            "write",
            ifNoneMatch: "*",
            body: """{"schemaVersion":1,"value":{"widgets":[{"id":"dup","size":"small"},{"id":"dup","size":"medium"}]}}"""));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? identity)
    {
        var request = new HttpRequestMessage(method, path);
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(StudioPreferencesCanaryHost.IdentityHeader, identity);
        request.Headers.TryAddWithoutValidation("X-Elsa-Studio-Host-Id", StudioPreferencesCanaryHost.HostId);
        return request;
    }

    private static HttpRequestMessage Put(string path, string? identity, string? ifMatch = null, string? ifNoneMatch = null, string body = "{}")
    {
        var request = Request(HttpMethod.Put, path, identity);
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (ifNoneMatch is not null)
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }
}
