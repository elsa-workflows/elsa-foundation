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
