using System.Net.Http.Json;
using Elsa.Studio.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Tests;

public sealed class ElsaThemeStoreApiTests : IAsyncDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), $"elsa-theme-store-{Guid.NewGuid():N}");
    private WebApplication? _app;

    [Fact]
    public async Task MaterialDesignCanBeSetAsTheDefaultBuiltInTheme()
    {
        var client = await StartThemeStoreHostAsync();

        var response = await client.PutAsJsonAsync("/_elsa/theme-store/default", new { ThemeId = "material-design" });
        response.EnsureSuccessStatusCode();
        var store = await response.Content.ReadFromJsonAsync<ThemeStoreResponse>();

        Assert.NotNull(store);
        Assert.Equal("material-design", store.DefaultThemeId);
    }

    [Fact]
    public async Task BuiltInThemeCanBeDisabledAndReEnabled()
    {
        var client = await StartThemeStoreHostAsync();

        var disableResponse = await client.PutAsJsonAsync("/_elsa/theme-store/themes/hot-pink/visibility", new { Enabled = false });
        disableResponse.EnsureSuccessStatusCode();
        var disabledStore = await disableResponse.Content.ReadFromJsonAsync<ThemeStoreResponse>();

        Assert.NotNull(disabledStore);
        Assert.Contains("hot-pink", disabledStore.DisabledBuiltInThemeIds ?? []);

        var enableResponse = await client.PutAsJsonAsync("/_elsa/theme-store/themes/hot-pink/visibility", new { Enabled = true });
        enableResponse.EnsureSuccessStatusCode();
        var enabledStore = await enableResponse.Content.ReadFromJsonAsync<ThemeStoreResponse>();

        Assert.NotNull(enabledStore);
        Assert.DoesNotContain("hot-pink", enabledStore.DisabledBuiltInThemeIds ?? []);
    }

    [Fact]
    public async Task DefaultBuiltInThemeCannotBeDisabled()
    {
        var client = await StartThemeStoreHostAsync();

        var defaultResponse = await client.PutAsJsonAsync("/_elsa/theme-store/default", new { ThemeId = "material-design" });
        defaultResponse.EnsureSuccessStatusCode();

        var disableResponse = await client.PutAsJsonAsync("/_elsa/theme-store/themes/material-design/visibility", new { Enabled = false });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, disableResponse.StatusCode);
    }

    [Fact]
    public async Task VisibilityEndpointRejectsNonBuiltInThemeIds()
    {
        var client = await StartThemeStoreHostAsync();

        var response = await client.PutAsJsonAsync("/_elsa/theme-store/themes/some-custom-theme/visibility", new { Enabled = false });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DisabledBuiltInThemeCannotBecomeTheDefault()
    {
        var client = await StartThemeStoreHostAsync();

        var disableResponse = await client.PutAsJsonAsync("/_elsa/theme-store/themes/hot-pink/visibility", new { Enabled = false });
        disableResponse.EnsureSuccessStatusCode();

        var defaultResponse = await client.PutAsJsonAsync("/_elsa/theme-store/default", new { ThemeId = "hot-pink" });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, defaultResponse.StatusCode);
    }

    private async Task<HttpClient> StartThemeStoreHostAsync()
    {
        Directory.CreateDirectory(_contentRoot);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = _contentRoot
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Studio:Auth:Enabled"] = "false"
        });
        builder.Services.AddStudioBridgeAuth(builder.Configuration);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapElsaThemeManagementApi(StudioBridgeAuth.PolicyName);

        await app.StartAsync();
        _app = app;
        return app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();

        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
    }
}
