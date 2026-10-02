using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    private readonly ThemeConfiguration _themeConfig = new();
    private HttpClient _client = null!;

    [Fact]
    public async Task BuiltInThemeCanBeDisabledAndReEnabled()
    {
        var disabledStore = await SetVisibilityAsync("drift", enabled: false);
        Assert.Contains("drift", disabledStore.DisabledBuiltInThemeIds ?? []);

        var enabledStore = await SetVisibilityAsync("drift", enabled: true);
        Assert.DoesNotContain("drift", enabledStore.DisabledBuiltInThemeIds ?? []);
    }

    [Fact]
    public async Task DefaultBuiltInThemeCannotBeDisabled()
    {
        var defaultResponse = await _client.PutAsJsonAsync("/_elsa/theme-store/default", new { ThemeId = "drift" });
        defaultResponse.EnsureSuccessStatusCode();

        var disableResponse = await _client.PutAsJsonAsync("/_elsa/theme-store/themes/drift/visibility", new { Enabled = false });

        Assert.Equal(HttpStatusCode.BadRequest, disableResponse.StatusCode);
    }

    [Theory]
    [InlineData("some-custom-theme")]
    [InlineData("hot-pink")] // A retired built-in.
    [MemberData(nameof(RetiredThemeIds))]
    public async Task VisibilityEndpointRejectsNonBuiltInThemeIds(string themeId)
    {
        var response = await _client.PutAsJsonAsync($"/_elsa/theme-store/themes/{themeId}/visibility", new { Enabled = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(RetiredThemeIds))]
    public async Task RetiredBuiltInThemeCannotBecomeTheDefault(string themeId)
    {
        var response = await _client.PutAsJsonAsync("/_elsa/theme-store/default", new { ThemeId = themeId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StoreThatStillNamesRetiredThemesLoadsAndSaves()
    {
        await File.WriteAllTextAsync(
            Path.Join(_contentRoot, "studio-theme-store.json"),
            """{ "themes": [], "defaultThemeId": "black-glass", "assets": [], "disabledBuiltInThemeIds": ["stone", "drift", "brass-instrument"] }""");

        var loaded = await _client.GetFromJsonAsync<ThemeStoreResponse>("/_elsa/theme-store");

        // Retired ids drop out of the hidden list; the client resolves the unknown default to the first built-in.
        Assert.Equal(["drift"], loaded?.DisabledBuiltInThemeIds ?? []);

        var saved = await SetVisibilityAsync("signal", enabled: false);
        Assert.Equal(["drift", "signal"], saved.DisabledBuiltInThemeIds ?? []);

        var defaultResponse = await _client.PutAsJsonAsync("/_elsa/theme-store/default", new { ThemeId = "atelier" });
        defaultResponse.EnsureSuccessStatusCode();
        Assert.Equal("atelier", (await defaultResponse.Content.ReadFromJsonAsync<ThemeStoreResponse>())?.DefaultThemeId);
    }

    [Fact]
    public async Task DisabledBuiltInThemeCannotBecomeTheDefault()
    {
        await SetVisibilityAsync("drift", enabled: false);

        var defaultResponse = await _client.PutAsJsonAsync("/_elsa/theme-store/default", new { ThemeId = "drift" });

        Assert.Equal(HttpStatusCode.BadRequest, defaultResponse.StatusCode);
    }

    [Theory]
    [InlineData("meridian")]
    [InlineData("drift")]
    [InlineData("schematic")]
    [InlineData("atelier")]
    [InlineData("elsa-cloud")]
    [InlineData("signal")]
    [InlineData("dusk")]
    public async Task EverySignatureThemeCanBeSetAsTheDefaultBuiltInTheme(string themeId)
    {
        var response = await _client.PutAsJsonAsync("/_elsa/theme-store/default", new { ThemeId = themeId });
        response.EnsureSuccessStatusCode();
        var store = await response.Content.ReadFromJsonAsync<ThemeStoreResponse>();

        Assert.Equal(themeId, store?.DefaultThemeId);
    }

    [Fact]
    public async Task CustomThemeRoundTripsDimHighContrastTypographyAndShape()
    {
        var theme = CustomTheme() with
        {
            Modes = new StudioThemeModes(Palette(), Palette(), Dim: Palette(), HighContrast: Palette()),
            SupportedModes = ["light", "dark", "dim", "high-contrast"],
            Typography = new StudioThemeTypography("\"Geist Variable\", system-ui, sans-serif", null, "\"Instrument Serif\", serif"),
            // Incidental whitespace is accepted, matching the client-side validator.
            Shape = new StudioThemeShape("4px", " 6px ", null, null, "0.75rem"),
            Layout = "editorial"
        };

        var response = await _client.PutAsJsonAsync($"/_elsa/theme-store/themes/{theme.Id}", theme);
        response.EnsureSuccessStatusCode();
        var saved = (await response.Content.ReadFromJsonAsync<ThemeStoreResponse>())?.Themes.Single(x => x.Id == theme.Id);

        Assert.NotNull(saved?.Modes.Dim);
        Assert.NotNull(saved?.Modes.HighContrast);
        Assert.Equal(["light", "dark", "dim", "high-contrast"], saved?.SupportedModes ?? []);
        Assert.Equal("\"Instrument Serif\", serif", saved?.Typography?.Display);
        Assert.Equal("0.75rem", saved?.Shape?.RadiusXl);
        Assert.Equal("editorial", saved?.Layout);
    }

    [Theory]
    [InlineData("supported-mode-without-palette")]
    [InlineData("font-stack-breakout")]
    [InlineData("radius-with-expression")]
    [InlineData("unknown-layout")]
    public async Task CustomThemeRejectsUnsafeOrInconsistentAppearance(string defect)
    {
        var theme = defect switch
        {
            "supported-mode-without-palette" => CustomTheme() with { SupportedModes = ["light", "dark", "high-contrast"] },
            "font-stack-breakout" => CustomTheme() with { Typography = new StudioThemeTypography("x; background: url(https://evil.test)", null, null) },
            "radius-with-expression" => CustomTheme() with { Shape = new StudioThemeShape(null, "calc(100vw)", null, null, null) },
            _ => CustomTheme() with { Layout = "sidebar-on-the-right" }
        };

        var response = await _client.PutAsJsonAsync($"/_elsa/theme-store/themes/{theme.Id}", theme);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static TheoryData<string> RetiredThemeIds => ["black-glass", "stone", "blueprint", "brass-instrument"];

    private async Task<ThemeStoreResponse> SetVisibilityAsync(string themeId, bool enabled)
    {
        var response = await _client.PutAsJsonAsync($"/_elsa/theme-store/themes/{themeId}/visibility", new { Enabled = enabled });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ThemeStoreResponse>())!;
    }

    [Theory]
    [MemberData(nameof(RetiredThemeIds))]
    public async Task StoredRetiredDefaultIsReportedAsUnset(string themeId)
    {
        await File.WriteAllTextAsync(Path.Combine(_contentRoot, "studio-theme-store.json"), $$"""{"themes":[],"defaultThemeId":"{{themeId}}","assets":[]}""");

        Assert.Equal("", (await GetStoreAsync()).DefaultThemeId);
    }

    [Theory]
    [MemberData(nameof(RetiredThemeIds))]
    public async Task ConfiguredRetiredDefaultIsReportedAsUnset(string themeId)
    {
        _themeConfig.DefaultThemeId = themeId;

        Assert.Equal("", (await GetStoreAsync()).DefaultThemeId);
    }

    [Fact]
    public async Task ConfiguredBuiltInDefaultIsKept()
    {
        _themeConfig.DefaultThemeId = "meridian";

        Assert.Equal("meridian", (await GetStoreAsync()).DefaultThemeId);
    }

    [Fact]
    public async Task StoredCustomThemeDefaultIsKept()
    {
        var theme = CustomTheme();
        (await _client.PutAsJsonAsync($"/_elsa/theme-store/themes/{theme.Id}", theme)).EnsureSuccessStatusCode();
        (await _client.PutAsJsonAsync("/_elsa/theme-store/default", new { ThemeId = theme.Id })).EnsureSuccessStatusCode();

        Assert.Equal(theme.Id, (await GetStoreAsync()).DefaultThemeId);
    }

    [Fact]
    public async Task ConfiguredDirectoryThemeDefaultIsKept()
    {
        var theme = CustomTheme();
        var themesFile = Path.Combine(_contentRoot, "themes.json");
        await File.WriteAllTextAsync(themesFile, JsonSerializer.Serialize(new { themes = new[] { theme } }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        _themeConfig.ThemesDirectory = themesFile;
        _themeConfig.DefaultThemeId = theme.Id;

        Assert.Equal(theme.Id, (await GetStoreAsync()).DefaultThemeId);
    }

    private async Task<ThemeStoreResponse> GetStoreAsync() =>
        (await _client.GetFromJsonAsync<ThemeStoreResponse>("/_elsa/theme-store"))!;

    private static StudioThemeDefinition CustomTheme() =>
        new("custom-appearance", "Custom Appearance", null, "custom", 1, true, true, new StudioThemeModes(Palette(), Palette()), null);

    private static StudioThemeModeDefinition Palette()
    {
        const string color = "oklch(0.5 0.1 250)";
        return new(color, color, color, color, color, color, color, color, color, color, color, color, color, color, color, color,
            color, color, color, color, color, color, color, color, color, [color, color, color, color, color], null);
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
        builder.Configuration.GetSection("Themes").Bind(_themeConfig);
        builder.Services.AddSingleton(_themeConfig);
        builder.Services.AddSingleton<ThemeConfigurationService>();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapElsaThemeStoreCoreApi(StudioBridgeAuth.PolicyName);
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
