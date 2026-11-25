using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Elsa.Studio.Tests;

/// <summary>
/// A Studio add-on installs into the running Essentials Studio from the feed, with no rebuild and no restart (#5768). The test
/// packs the real ETL Studio module with <c>dotnet pack</c>, uploads the package the way the Modules page does, enables its
/// feature the way the Features page does, and reads the module and its browser files back from the same process.
/// </summary>
/// <remarks>
/// ETL is a feed-only module of Studio Essentials, so <see cref="EssentialsStudioHostBootTests"/> pins that the app's build output
/// has no copy of it: whatever the app serves for it here came through Nuplane. The browser files are served because the app's
/// <c>appsettings.json</c> turns on <c>Studio:Hosting:Nuplane:EnrollPackageStore</c>, which enrolls the fresh package store on
/// first start (#5778). Studio auth is off for this host, the demo posture, because the module admin API otherwise asks the
/// backend to validate a signed-in session and no backend runs here. The ETL screens themselves stay capability-gated in the
/// browser on <c>elsa.api.etl-*</c>; this test stops at the Studio host.
/// </remarks>
public sealed class EssentialsStudioAddOnTests(EssentialsStudioAddOnTests.Host host) : IClassFixture<EssentialsStudioAddOnTests.Host>
{
    [Fact]
    public async Task An_add_on_installed_from_the_feed_loads_into_the_running_essentials_studio()
    {
        var processId = host.ProcessId;
        var module = await EtlAddOn.InstallAsync(host);

        await EtlAddOn.AssertServedAsync(host, module);
        Assert.Equal(processId, host.ProcessId);
        Assert.False(File.Exists(Path.Join(host.OutputDirectory, EtlAddOn.ModuleId + ".dll")));
    }

    /// <summary>The Essentials Studio app with its shipped configuration and Studio auth off, started once for the class with an empty feed.</summary>
    public sealed class Host() : StudioAppProcess("Elsa.Studio.Essentials.Host", new Dictionary<string, string> { ["Studio__Auth__Enabled"] = "false" });
}

/// <summary>The ETL Studio module, installed into a running Studio app the way the Modules and Features pages do it.</summary>
internal static class EtlAddOn
{
    public const string ModuleId = "Elsa.Studio.Etl";
    private const string FeatureId = "EtlStudio";
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Packs, uploads and enables the module, then returns its client entry and stylesheets once the app lists it.</summary>
    public static async Task<InstalledModule> InstallAsync(StudioAppProcess host)
    {
        Assert.DoesNotContain(ModuleId, await ModuleIdsAsync(host));
        var package = Pack(host);

        // The Modules page: upload the package into the app's directory feed, which schedules a reconcile.
        using (var form = new MultipartFormDataContent())
        {
            var content = new ByteArrayContent(await File.ReadAllBytesAsync(package));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(content, "package", Path.GetFileName(package));
            using var upload = await host.Client.PostAsync("/_elsa/module-management/packages/upload", form);
            Assert.True(upload.IsSuccessStatusCode, $"Upload answered {upload.StatusCode}: {await upload.Content.ReadAsStringAsync()}{host.OutputSection}");
        }
        Assert.True(File.Exists(Path.Join(host.ContentRoot, "packages", Path.GetFileName(package))));

        // The Features page: the add-on's feature appears in the catalog once the package is loaded, and is enabled from there.
        var catalog = await UntilAsync(host, () => CatalogAsync(host), value => FeatureOf(value) is not null, $"{FeatureId} never appeared in the feature catalog");
        Assert.False((bool)FeatureOf(catalog)!["enabled"]!);
        for (var attempt = 1; ; attempt++)
        {
            using var apply = await host.Client.PostAsJsonAsync("/modularity/features/apply", EnableRequest(catalog));
            if (apply.IsSuccessStatusCode)
                break;
            // The package's own shell reload can still be running: the save then conflicts (409), or it lands and the shell
            // replacement fails (503). A user reads the catalog and applies again, and so does this test, at most twice more.
            var retryable = (apply.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.ServiceUnavailable) && attempt < 3;
            Assert.True(retryable, $"Apply answered {apply.StatusCode}: {await apply.Content.ReadAsStringAsync()}{host.OutputSection}");
            await Task.Delay(TimeSpan.FromSeconds(1));
            catalog = await CatalogAsync(host);
        }

        return await ListedAsync(host);
    }

    /// <summary>The module as <c>/_elsa/studio/modules</c> lists it, with its client entry and stylesheets from the installed package.</summary>
    public static async Task<InstalledModule> ListedAsync(StudioAppProcess host)
    {
        var modules = await UntilAsync(host, () => ModulesAsync(host), value => ModuleOf(value) is not null, $"{ModuleId} never appeared in /_elsa/studio/modules");
        var entry = ModuleOf(modules)!["entry"]!.GetValue<string>();
        Assert.StartsWith($"/_content/{ModuleId}/studio/modules/etl/module.js", entry);
        var styles = ModuleOf(modules)!["styles"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray();
        Assert.NotEmpty(styles);
        Assert.All(styles, style => Assert.StartsWith($"/_content/{ModuleId}/studio/modules/etl/", style));
        return new(entry, styles);
    }

    /// <summary>The browser loads the module: its entry answers JavaScript and each stylesheet CSS, from Nuplane's admitted reader.</summary>
    public static async Task AssertServedAsync(StudioAppProcess host, InstalledModule module)
    {
        foreach (var (asset, mediaType) in module.Styles.Select(style => (style, "text/css")).Prepend((module.Entry, "text/javascript")))
        {
            using var response = await host.Client.GetAsync(asset);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{asset} answered {response.StatusCode}: {body}{host.OutputSection}");
            Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
            Assert.NotEmpty(body);
        }
    }

    /// <summary>Nuplane refuses the installed bytes of an unenrolled store, so the browser cannot load the module.</summary>
    public static async Task AssertRefusedAsync(StudioAppProcess host, InstalledModule module)
    {
        foreach (var asset in module.Styles.Prepend(module.Entry))
        {
            using var response = await host.Client.GetAsync(asset);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.ServiceUnavailable, $"{asset} answered {response.StatusCode}: {body}{host.OutputSection}");
            Assert.Equal("UnknownAuthority", (string?)JsonNode.Parse(body)!["reason"]);
        }
    }

    /// <summary>
    /// The ETL Studio module's own <c>.nupkg</c>, packed from its existing build (the test project's build builds it) into a
    /// directory of this host's own, so nothing is written beside the project.
    /// </summary>
    private static string Pack(StudioAppProcess host)
    {
        var project = Path.Join(StudioAppProcess.StudioRoot, "..", "extensions", "Etl", "studio", ModuleId, ModuleId + ".csproj");
        var output = Path.Join(host.ScratchDirectory, "feed");
        var nuspec = Path.Join(host.ScratchDirectory, "nuspec");
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            ["pack", Path.GetFullPath(project), "-c", StudioAppProcess.Configuration, "--no-build", "--no-restore", "-nologo", "-v", "q",
                "-o", output, "-p:NuspecOutputPath=" + nuspec + Path.DirectorySeparatorChar])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        // dotnet test leaves MSBuild variables pointing at its own build context; a spawned build must resolve its own.
        foreach (var variable in new[] { "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildSDKsPath" })
            startInfo.Environment.Remove(variable);
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(2)), "dotnet pack did not finish within two minutes.");
        Assert.True(process.ExitCode == 0, $"dotnet pack exited with {process.ExitCode}:{Environment.NewLine}{stdout.Result}{stderr.Result}");
        return Assert.Single(Directory.GetFiles(output, ModuleId + ".*.nupkg"));
    }

    /// <summary>
    /// The catalog as the Features page reads it. While a reconciliation cycle holds the enrolled store, the catalog answers
    /// 503 with Retry-After, and the read waits that long and asks again.
    /// </summary>
    private static async Task<JsonObject> CatalogAsync(StudioAppProcess host)
    {
        var deadline = DateTimeOffset.UtcNow + SettleTimeout;
        while (true)
        {
            using var response = await host.Client.GetAsync("/modularity/features");
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable && response.Headers.RetryAfter?.Delta is { } delay && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(delay);
                continue;
            }
            Assert.True(response.IsSuccessStatusCode, $"/modularity/features answered {response.StatusCode}: {await response.Content.ReadAsStringAsync()}{host.OutputSection}");
            return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        }
    }

    private static async Task<JsonObject> ModulesAsync(StudioAppProcess host)
    {
        var (status, _, body) = await host.GetAsync("/_elsa/studio/modules", "application/json");
        Assert.True(status == HttpStatusCode.OK, $"/_elsa/studio/modules answered {status}: {body}{host.OutputSection}");
        return JsonNode.Parse(body)!.AsObject();
    }

    private static async Task<string[]> ModuleIdsAsync(StudioAppProcess host) =>
        [.. (await ModulesAsync(host))["modules"]!.AsArray().Select(module => module!["id"]!.GetValue<string>())];

    private static JsonNode? FeatureOf(JsonObject catalog) =>
        catalog["features"]!.AsArray().SingleOrDefault(feature => (string?)feature!["id"] == FeatureId);

    private static JsonNode? ModuleOf(JsonObject modules) =>
        modules["modules"]!.AsArray().SingleOrDefault(module => (string?)module!["id"] == ModuleId);

    /// <summary>
    /// The catalog as it stands with <see cref="FeatureId"/> switched on, shaped as the Features page sends it
    /// (<c>createApplyPayload</c> in FeatureManagement's <c>featureModel.ts</c>): manifest errors left out, and a disabled
    /// feature's configuration empty.
    /// </summary>
    private static JsonObject EnableRequest(JsonObject catalog) => new()
    {
        ["revision"] = catalog["revision"]!.DeepClone(),
        ["features"] = new JsonArray(catalog["features"]!.AsArray()
            .Where(feature => (string?)feature!["sourceKind"] != "manifest-error")
            .Select(feature =>
            {
                var enabled = (string?)feature!["id"] == FeatureId || (bool)feature["enabled"]!;
                return (JsonNode)new JsonObject
                {
                    ["id"] = feature["id"]!.DeepClone(),
                    ["enabled"] = enabled,
                    ["configuration"] = enabled ? feature["configuration"]?.DeepClone() ?? new JsonObject() : new JsonObject()
                };
            }).ToArray())
    };

    private static async Task<JsonObject> UntilAsync(StudioAppProcess host, Func<Task<JsonObject>> read, Func<JsonObject, bool> done, string failure)
    {
        var deadline = DateTimeOffset.UtcNow + SettleTimeout;
        while (true)
        {
            var value = await read();
            if (done(value))
                return value;
            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException($"{failure} within {SettleTimeout}. Last read: {value.ToJsonString()}{host.OutputSection}");
            await Task.Delay(250);
        }
    }
}

/// <summary>An installed module's client entry and stylesheets, as <c>/_elsa/studio/modules</c> lists them.</summary>
internal sealed record InstalledModule(string Entry, IReadOnlyList<string> Styles);
