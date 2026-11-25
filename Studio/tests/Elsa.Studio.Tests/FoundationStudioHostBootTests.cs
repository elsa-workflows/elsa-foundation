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

    /// <summary><c>/modules</c> uploads add-on packages to the feed; <c>/features</c> (FeatureManagement) enables their features.</summary>
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
