using System.Net;
using System.Net.Http.Json;
using Elsa.Studio.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Tests;

public sealed class ElsaThemeStoreApiTests : IAsyncLifetime
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), $"elsa-theme-store-{Guid.NewGuid():N}");
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    [Fact]
    public async Task MaterialDesignCanBeSetAsTheDefaultBuiltInTheme()
    {
        var response = await _client.PutAsJsonAsync("/_elsa/theme-store/default", new { ThemeId = "material-design" });
        response.EnsureSuccessStatusCode();
        var store = await response.Content.ReadFromJsonAsync<ThemeStoreResponse>();

        Assert.NotNull(store);
        Assert.Equal("material-design", store.DefaultThemeId);
    }

    [Fact]
    public async Task BuiltInThemeCanBeDisabledAndReEnabled()
    {
        var disableResponse = await _client.PutAsJsonAsync("/_elsa/theme-store/themes/hot-pink/visibility", new { Enabled = false });
        disableResponse.EnsureSuccessStatusCode();
        var disabledStore = await disableResponse.Content.ReadFromJsonAsync<ThemeStoreResponse>();

        Assert.NotNull(disabledStore);
        Assert.Contains("hot-pink", disabledStore.DisabledBuiltInThemeIds ?? []);

        var enableResponse = await _client.PutAsJsonAsync("/_elsa/theme-store/themes/hot-pink/visibility", new { Enabled = true });
        enableResponse.EnsureSuccessStatusCode();
        var enabledStore = await enableResponse.Content.ReadFromJsonAsync<ThemeStoreResponse>();

        Assert.NotNull(enabledStore);
        Assert.DoesNotContain("hot-pink", enabledStore.DisabledBuiltInThemeIds ?? []);
    }

    [Fact]
    public async Task DefaultBuiltInThemeCannotBeDisabled()
    {
        var defaultResponse = await _client.PutAsJsonAsync("/_elsa/theme-store/default", new { ThemeId = "material-design" });
        defaultResponse.EnsureSuccessStatusCode();

        var disableResponse = await _client.PutAsJsonAsync("/_elsa/theme-store/themes/material-design/visibility", new { Enabled = false });

        Assert.Equal(HttpStatusCode.BadRequest, disableResponse.StatusCode);
    }

    [Fact]
    public async Task VisibilityEndpointRejectsNonBuiltInThemeIds()
    {
        var response = await _client.PutAsJsonAsync("/_elsa/theme-store/themes/some-custom-theme/visibility", new { Enabled = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DisabledBuiltInThemeCannotBecomeTheDefault()
    {
        var disableResponse = await _client.PutAsJsonAsync("/_elsa/theme-store/themes/hot-pink/visibility", new { Enabled = false });
        disableResponse.EnsureSuccessStatusCode();

        var defaultResponse = await _client.PutAsJsonAsync("/_elsa/theme-store/default", new { ThemeId = "hot-pink" });

        Assert.Equal(HttpStatusCode.BadRequest, defaultResponse.StatusCode);
    }

    public async Task InitializeAsync()
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
        // The theme-management endpoints resolve ThemeConfigurationService per request; register it and its bound
        // ThemeConfiguration exactly as Program.cs does (no "Themes" section here, so it binds to defaults).
        var themeConfig = new ThemeConfiguration();
        builder.Configuration.GetSection("Themes").Bind(themeConfig);
        builder.Services.AddSingleton(themeConfig);
        builder.Services.AddSingleton<ThemeConfigurationService>();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapElsaThemeManagementApi(StudioBridgeAuth.PolicyName);

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();

        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
    }
}
