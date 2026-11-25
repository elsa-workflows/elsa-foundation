using System.Net;
using System.Text.Json.Nodes;

namespace Elsa.Studio.Tests;

/// <summary>
/// The built <c>Elsa.Studio.Essentials.Host</c> as a child process (<see cref="StudioAppProcess"/>). One host serves every test
/// here (#5768).
/// </summary>
public sealed class EssentialsStudioHostBootTests(EssentialsStudioHostBootTests.Host host) : IClassFixture<EssentialsStudioHostBootTests.Host>
{
    /// <summary>The modules the Essentials server serves, as <c>/_elsa/studio/modules</c> lists them.</summary>
    private static readonly string[] InSetModules =
    [
        "Elsa.Studio.Attention",
        "Elsa.Studio.Backends",
        "Elsa.Studio.ExpressionEditors.JavaScript",
        "Elsa.Studio.ExpressionEditors.Liquid",
        "Elsa.Studio.FeatureManagement",
        "Elsa.Studio.Secrets",
        "Elsa.Studio.UserTasks",
        "Elsa.Studio.Workflows",
        "Elsa.Studio.Workflows.Dashboard"
    ];

    /// <summary>The Studio module assemblies that reach Studio Essentials only as feed add-ons.</summary>
    public static TheoryData<string> FeedOnlyAssemblies =>
    [
        "Elsa.Studio.Agent",
        "Elsa.Studio.Connections",
        "Elsa.Studio.ConsoleStream",
        "Elsa.Studio.Dashboard",
        "Elsa.Studio.Decisions",
        "Elsa.Studio.Diagnostics.OpenTelemetry",
        "Elsa.Studio.Diagnostics.StructuredLogs",
        "Elsa.Studio.Etl",
        "Elsa.Studio.ExtensionBuilder",
        "Elsa.Studio.Identity.ExternalAuthentication",
        "Elsa.Studio.Samples.WeatherForecast",
        "Elsa.Studio.Weaver.Core",
        "Elsa.Studio.Weaver.Workflows"
    ];

    [Fact]
    public async Task The_essentials_studio_serves_its_index_page()
    {
        var (status, mediaType, body) = await host.GetAsync("/", "text/html");

        Assert.True(status == HttpStatusCode.OK, $"/ answered {status}: {body}{host.OutputSection}");
        Assert.Equal("text/html", mediaType);
        Assert.Contains("<title>Elsa Studio</title>", body);
    }

    /// <summary>
    /// <c>/modules</c> uploads add-on packages to the feed; <c>/features</c> (FeatureManagement) enables their features. These are
    /// client routes the SPA fallback serves; the API test below proves the administration endpoints behind them are mapped.
    /// </summary>
    [Fact]
    public async Task The_essentials_studio_serves_the_module_admin_routes()
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
    public async Task The_essentials_studio_maps_the_add_on_administration_api()
    {
        var (status, _, body) = await host.GetAsync("/_elsa/module-management/registry", "application/json");

        Assert.True(status == HttpStatusCode.Unauthorized, $"/_elsa/module-management/registry answered {status}: {body}{host.OutputSection}");
    }

    [Fact]
    public async Task The_essentials_studio_loads_exactly_the_modules_the_essentials_server_serves()
    {
        var (status, _, body) = await host.GetAsync("/_elsa/studio/modules", "application/json");

        Assert.True(status == HttpStatusCode.OK, $"/_elsa/studio/modules answered {status}: {body}{host.OutputSection}");
        var modules = JsonNode.Parse(body)!["modules"]!.AsArray().Select(module => module!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal);
        Assert.Equal(InSetModules, modules);
    }

    /// <summary>A feed-only module is not compiled in: its assembly is absent from the app's build output.</summary>
    [Theory]
    [MemberData(nameof(FeedOnlyAssemblies))]
    public void A_feed_only_module_is_not_compiled_into_the_essentials_studio(string assembly)
    {
        Assert.False(File.Exists(Path.Join(host.OutputDirectory, assembly + ".dll")), $"{assembly}.dll is in {host.OutputDirectory}.");
    }

    /// <summary>The theory above passes trivially against an empty or wrong directory, so this pins that it reads the real output.</summary>
    [Fact]
    public void The_build_output_check_reads_the_real_essentials_studio_output()
    {
        foreach (var module in InSetModules)
            Assert.True(File.Exists(Path.Join(host.OutputDirectory, module + ".dll")), $"{module}.dll is missing from {host.OutputDirectory}.");
    }

    /// <summary>Every feature <c>shells.json</c> enables is compiled in, so the shell activates without a warning.</summary>
    [Fact]
    public async Task The_essentials_studio_activates_its_shell_without_warnings()
    {
        var (status, _, body) = await host.GetAsync("/_elsa/studio/modules", "application/json");

        Assert.True(status == HttpStatusCode.OK, $"/_elsa/studio/modules answered {status}: {body}{host.OutputSection}");
        Assert.DoesNotContain("warn:", host.OutputSection);
        Assert.DoesNotContain("fail:", host.OutputSection);
    }

    /// <summary>The Essentials Studio app, started once for the class from its committed Development configuration.</summary>
    public sealed class Host() : StudioAppProcess("Elsa.Studio.Essentials.Host");
}
