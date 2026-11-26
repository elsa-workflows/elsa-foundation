using System.Net;
using System.Text.Json.Nodes;

namespace Elsa.Studio.Tests;

/// <summary>
/// The built <c>Elsa.Studio.Foundation.Host</c> as a child process (<see cref="StudioAppProcess"/>). One host serves every test
/// here (#5768).
/// </summary>
public sealed class FoundationStudioHostBootTests(FoundationStudioHostBootTests.Host host) : IClassFixture<FoundationStudioHostBootTests.Host>
{
    [Fact]
    public async Task The_foundation_studio_serves_its_index_page()
    {
        var (status, mediaType, body) = await host.GetAsync("/", "text/html");

        Assert.True(status == HttpStatusCode.OK, $"/ answered {status}: {body}{host.OutputSection}");
        Assert.Equal("text/html", mediaType);
        Assert.Contains("<title>Elsa Studio</title>", body);
    }

    /// <summary>
    /// The shell owns Home: <c>/</c> serves the shell, whose bundle registers the Home routes and loads the Home page, empty
    /// state included, from the shell's own assets. The Dashboard module is not compiled in.
    /// </summary>
    [Fact]
    public async Task The_foundation_studio_serves_home_from_the_shell_without_the_dashboard_module()
    {
        var (status, _, index) = await host.GetAsync("/", "text/html");
        Assert.True(status == HttpStatusCode.OK, $"/ answered {status}: {index}{host.OutputSection}");
        Assert.Contains("src=\"/studio/assets/index.js\"", index);

        var (shellStatus, _, shell) = await host.GetAsync("/studio/assets/index.js", "text/javascript");
        Assert.True(shellStatus == HttpStatusCode.OK, $"/studio/assets/index.js answered {shellStatus}{host.OutputSection}");
        Assert.Contains("home:", shell);
        Assert.Contains("./HomePage.js", shell);

        var (homeStatus, _, home) = await host.GetAsync("/studio/assets/HomePage.js", "text/javascript");
        Assert.True(homeStatus == HttpStatusCode.OK, $"/studio/assets/HomePage.js answered {homeStatus}{host.OutputSection}");
        Assert.Contains("No installed module adds content to Home yet.", home);

        var (modulesStatus, _, modules) = await host.GetAsync("/_elsa/studio/modules", "application/json");
        Assert.True(modulesStatus == HttpStatusCode.OK, $"/_elsa/studio/modules answered {modulesStatus}: {modules}{host.OutputSection}");
        Assert.DoesNotContain("Elsa.Studio.Dashboard", modules);
    }

    /// <summary>
    /// <c>/modules</c> uploads add-on packages to the feed; <c>/features</c> (FeatureManagement) enables their features. These are
    /// client routes the SPA fallback serves; the API test below proves the administration endpoints behind them are mapped.
    /// </summary>
    [Fact]
    public async Task The_foundation_studio_serves_the_module_admin_routes()
    {
        foreach (var route in new[] { "/modules", "/features" })
        {
            var (status, mediaType, body) = await host.GetAsync(route, "text/html");

            Assert.True(status == HttpStatusCode.OK, $"{route} answered {status}: {body}{host.OutputSection}");
            Assert.Equal("text/html", mediaType);
            Assert.Contains("<title>Elsa Studio</title>", body);
        }
    }

    /// <summary>
    /// The add-on administration API is mapped. The SPA fallback answers any HTML path, and <c>/_elsa/{**path}</c> answers
    /// unmapped API paths with 404, so only a mapped, authorization-protected endpoint answers 401 here.
    /// </summary>
    [Fact]
    public async Task The_foundation_studio_maps_the_add_on_administration_api()
    {
        var (status, _, body) = await host.GetAsync("/_elsa/module-management/registry", "application/json");

        Assert.True(status == HttpStatusCode.Unauthorized, $"/_elsa/module-management/registry answered {status}: {body}{host.OutputSection}");
    }

    /// <summary>The bare shell lists only module administration and the backend profiles; every other module comes from the feed.</summary>
    [Fact]
    public async Task The_foundation_studio_compiles_in_only_the_bare_shell_modules()
    {
        var (status, _, body) = await host.GetAsync("/_elsa/studio/modules", "application/json");

        Assert.True(status == HttpStatusCode.OK, $"/_elsa/studio/modules answered {status}: {body}{host.OutputSection}");
        var modules = JsonNode.Parse(body)!["modules"]!.AsArray().Select(module => module!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal);
        Assert.Equal(["Elsa.Studio.Backends", "Elsa.Studio.FeatureManagement"], modules);
    }

    /// <summary>Every feature <c>shells.json</c> enables is compiled in, so the shell activates without a warning.</summary>
    [Fact]
    public async Task The_foundation_studio_activates_its_shell_without_warnings()
    {
        var (status, _, body) = await host.GetAsync("/_elsa/studio/modules", "application/json");

        Assert.True(status == HttpStatusCode.OK, $"/_elsa/studio/modules answered {status}: {body}{host.OutputSection}");
        Assert.DoesNotContain("warn:", host.OutputSection);
        Assert.DoesNotContain("fail:", host.OutputSection);
    }

    /// <summary>The Foundation Studio app, started once for the class from its committed Development configuration.</summary>
    public sealed class Host() : StudioAppProcess("Elsa.Studio.Foundation.Host");
}
