using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Elsa.Studio.Api.Authorization;
using Elsa.Studio.Hosting;
using Elsa.Studio.Hosting.Themes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Studio.Tests.Hosting;

public sealed class ThemeCompositionTests
{
    [Theory]
    [InlineData("", false, false, false)]
    [InlineData("ThemeStoreCore", true, false, false)]
    [InlineData("ThemePicker", true, true, false)]
    [InlineData("ThemeManagement", true, false, true)]
    [InlineData("ThemePicker,ThemeManagement", true, true, true)]
    public async Task Public_composition_preserves_independent_features_and_core_dependencies(string selection, bool core, bool picker, bool management)
    {
        await using var host = await ThemeHost.StartAsync(selection);
        foreach (var (name, enabled) in new[] { ("core", core), ("picker", picker), ("management", management) })
        {
            using var response = await host.Client.GetAsync("/_elsa/theme-store/capabilities/" + name);
            Assert.Equal(enabled ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
        }
        using var store = await host.Client.GetAsync("/_elsa/theme-store");
        Assert.Equal(core ? HttpStatusCode.OK : HttpStatusCode.NotFound, store.StatusCode);
        using var write = await host.Client.PutAsJsonAsync("/_elsa/theme-store/themes/classic/visibility", new { enabled = true });
        Assert.Equal(management ? HttpStatusCode.OK : HttpStatusCode.NotFound, write.StatusCode);
    }

    [Fact]
    public async Task Base_host_has_no_theme_routes_or_options_registration()
    {
        await using var host = await ThemeHost.StartAsync(addThemes: false);
        using var response = await host.Client.GetAsync("/_elsa/theme-store");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(host.App.Services.GetServices<IValidateOptions<StudioThemeStoreOptions>>(), validator => validator.GetType().Assembly == typeof(StudioThemeStoreOptions).Assembly);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    [InlineData(true, HttpStatusCode.OK)]
    public async Task Theme_endpoints_use_the_existing_host_authorization_policy(bool customizePolicy, HttpStatusCode expected)
    {
        await using var host = await ThemeHost.StartAsync("ThemeManagement", auth: true, afterComposition: builder =>
        {
            if (customizePolicy) builder.Services.AddAuthorization(options => options.AddPolicy(StudioBridgeAuthorization.PolicyName,
                policy => policy.RequireAssertion(_ => true)));
        });
        using var read = await host.Client.GetAsync("/_elsa/theme-store");
        using var write = await host.Client.PutAsJsonAsync("/_elsa/theme-store/themes/classic/visibility", new { enabled = true });
        Assert.Equal(expected, read.StatusCode);
        Assert.Equal(expected, write.StatusCode);
    }

    [Fact]
    public async Task Content_root_paths_callback_precedence_and_copied_installed_choices_are_applied()
    {
        StudioThemeStoreOptions retained = null!;
        await using var host = await ThemeHost.StartAsync("ThemeManagement", configure: (options, root) =>
        {
            retained = options;
            options.StoreFile = "state/managed.json";
            options.AssetsDirectory = "state/materials";
            options.ThemesDirectory = "sources/collection.json";
            options.ThemesMode = " directoryonly ";
            Directory.CreateDirectory(Path.Combine(root, "sources"));
            File.WriteAllText(Path.Combine(root, "sources", "collection.json"), """{"themes":[]}""");
        }, beforeComposition: builder => builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Themes:StoreFile"] = "wrong.json", ["Themes:ThemesMode"] = "BuiltInsOnly"
        }));
        retained.StoreFile = "redirected.json";
        retained.AssetsDirectory = "redirected-assets";
        retained.ThemesMode = "BuiltInsOnly";
        using var read = await host.Client.GetAsync("/_elsa/theme-store");
        read.EnsureSuccessStatusCode();
        Assert.Equal("DirectoryOnly", (string?)JsonNode.Parse(await read.Content.ReadAsStringAsync())!["themesMode"]);
        using var write = await host.Client.PutAsJsonAsync("/_elsa/theme-store/themes/classic/visibility", new { enabled = true });
        write.EnsureSuccessStatusCode();
        Assert.True(File.Exists(Path.Combine(host.Root, "state", "managed.json")));
        Assert.False(File.Exists(Path.Combine(host.Root, "redirected.json")));
        Assert.False(File.Exists(Path.Combine(host.Root, "wrong.json")));
        using var form = new MultipartFormDataContent();
        using var bytes = new ByteArrayContent([1, 2, 3]);
        bytes.Headers.ContentType = new("image/png");
        form.Add(bytes, "asset", "material.png");
        using var upload = await host.Client.PostAsync("/_elsa/theme-store/assets", form);
        upload.EnsureSuccessStatusCode();
        Assert.Single(Directory.EnumerateFiles(Path.Combine(host.Root, "state", "materials")));
        Assert.False(Directory.Exists(Path.Combine(host.Root, "redirected-assets")));
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("blank-mode")]
    [InlineData("blank-store")]
    [InlineData("blank-assets")]
    [InlineData("store-directory")]
    [InlineData("assets-file")]
    [InlineData("source-store")]
    public async Task Invalid_typed_theme_choices_fail_during_startup(string kind)
    {
        var error = await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var host = await ThemeHost.StartAsync(configure: (options, root) =>
            {
                if (kind == "mode") options.ThemesMode = "everything";
                if (kind == "blank-mode") options.ThemesMode = "";
                if (kind == "blank-store") options.StoreFile = " ";
                if (kind == "blank-assets") options.AssetsDirectory = " ";
                if (kind == "store-directory") options.StoreFile = root;
                if (kind == "assets-file") { File.WriteAllText(Path.Combine(root, "asset-file"), "owned"); options.AssetsDirectory = "asset-file"; }
                if (kind == "source-store") options.ThemesDirectory = options.StoreFile;
            });
        });
        Assert.Contains("Themes:", error.Message);
    }

    [Fact]
    public async Task Composition_rejects_duplicates_and_registration_after_build()
    {
        var builder = WebApplication.CreateSlimBuilder();
        var studio = builder.AddElsaStudio(options => options.ShellsFile = null);
        Assert.Same(studio, studio.AddElsaStudioThemes());
        Assert.Throws<InvalidOperationException>(() => studio.AddElsaStudioThemes());
        await using var app = builder.Build();
        Assert.Throws<InvalidOperationException>(() => studio.AddElsaStudioThemes());
    }
}

internal sealed class ThemeHost : IAsyncDisposable
{
    private bool _started;
    public string Root { get; } = Directory.CreateTempSubdirectory("studio-themes-").FullName;
    public WebApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public static async Task<ThemeHost> StartAsync(string selection = "ThemeStoreCore", bool addThemes = true, bool auth = false,
        Action<StudioThemeStoreOptions, string>? configure = null, Action<WebApplicationBuilder>? beforeComposition = null, Action<WebApplicationBuilder>? afterComposition = null)
    {
        var host = new ThemeHost();
        try
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = host.Root, EnvironmentName = Environments.Production });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.Sources.Clear();
            var values = new Dictionary<string, string?> { ["CShells:Shells:Authoring:Features:StudioApi"] = "true", ["Studio:Auth:Enabled"] = auth.ToString() };
            foreach (var feature in selection.Split(',', StringSplitOptions.RemoveEmptyEntries)) values[$"CShells:Shells:Authoring:Features:{feature}"] = "true";
            builder.Configuration.AddInMemoryCollection(values);
            beforeComposition?.Invoke(builder);
            var studio = builder.AddElsaStudio(options => { options.ShellName = "Authoring"; options.ShellsFile = null; });
            if (addThemes) studio.AddElsaStudioThemes(options => configure?.Invoke(options, host.Root));
            afterComposition?.Invoke(builder);
            host.App = builder.Build();
            host.App.UseElsaStudio();
            host.App.MapElsaStudio();
            await host.App.StartAsync();
            host._started = true;
            host.Client = host.App.GetTestClient();
            return host;
        }
        catch { await host.DisposeAsync(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (App is not null)
        {
            try { if (_started) await App.StopAsync(); }
            finally { await App.DisposeAsync(); }
        }
        Directory.Delete(Root, recursive: true);
    }
}
