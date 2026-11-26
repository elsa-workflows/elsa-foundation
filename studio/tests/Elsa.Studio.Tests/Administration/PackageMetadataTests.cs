using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nuplane.Admin;
using Nuplane.Reconciliation;
using Nuplane.Sources.Directory.Builder;

namespace Elsa.Studio.Tests.Administration;

public sealed class PackageMetadataTests
{
    private const string Manifest = """
        {"package":{"id":"Acme.StudioAdmin","version":"1.0.0"},"features":[
          {"id":"PackagedFeature","displayName":"Packaged feature","dependencies":[{"featureId":"Acme.StudioAdmin.Dependency"}],
           "settings":[{"name":"Credential","jsonType":"string","secret":true,"defaultValue":"private-manifest-default-sentinel"}]}]}
        """;

    [Theory]
    [InlineData("elsa-package.json")]
    [InlineData("build/elsa-package.json")]
    public async Task Protected_manifests_use_shared_mapping_and_secret_masking(string path)
    {
        await using var host = await AdministrationHost.StartAsync(nativeStore: true);
        PackageAssetTests.Pack(host, "1.0.0", "asset", manifest: Manifest, manifestPath: path);
        await PackageAssetTests.Reconcile(host);
        var catalog = await Catalog(host);
        var feature = Assert.Single(catalog["features"]!.AsArray(), item => (string?)item!["id"] == "PackagedFeature")!;
        Assert.Equal("manifest", (string?)feature["sourceKind"]);
        Assert.Equal(path, (string?)feature["manifestPath"]);
        Assert.Equal(64, ((string)feature["manifestHash"]!).Length);
        Assert.Equal("Dependency", (string?)feature["dependencies"]![0]!["id"]);
        Assert.True((bool)feature["settings"]![0]!["secret"]!);
        Assert.DoesNotContain("private-manifest-default-sentinel", catalog.ToJsonString());
    }

    [Theory]
    [InlineData("oversized", "1048576-byte")]
    [InlineData("invalid", "not valid JSON")]
    [InlineData("missing", "not found")]
    [InlineData("ordinary", "unavailable")]
    public async Task Unsupported_or_bad_metadata_is_diagnostic_without_an_unprotected_fallback(string kind, string reason)
    {
        await using var host = await AdministrationHost.StartAsync(nativeStore: kind != "ordinary", configureNuplane: nuplane =>
        {
            if (kind == "ordinary") nuplane.AddDirectoryFeed("ordinary", "feed", feed => { feed.Watch = false; feed.IncludeAll(); });
        });
        var manifest = kind switch { "oversized" => new string(' ', 1024 * 1024 + 1), "invalid" => "{invalid", "missing" => null, _ => Manifest };
        PackageAssetTests.Pack(host, "1.0.0", "asset", manifest: manifest);
        await PackageAssetTests.Reconcile(host);
        var catalog = await Catalog(host);
        var diagnostic = Assert.Single(catalog["features"]!.AsArray(), item => (string?)item!["sourceKind"] == "manifest-error")!;
        Assert.Equal(PackageAssetTests.PackageId + ":1.0.0", (string?)diagnostic["id"]);
        Assert.Contains(reason, (string)diagnostic["readError"]!);
        Assert.DoesNotContain("private-manifest-default-sentinel", catalog.ToJsonString());
        Assert.DoesNotContain(catalog["features"]!.AsArray(), item => (string?)item!["id"] == "PackagedFeature");
    }

    [Fact]
    public async Task Metadata_is_read_under_native_admission_and_released_after_detachment()
    {
        using var logs = new ContentLog { PausePreflight = true };
        await using var host = await AdministrationHost.StartAsync(nativeStore: true, afterStudio: builder =>
        {
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Debug);
            builder.Logging.AddProvider(logs);
        });
        PackageAssetTests.Pack(host, "1.0.0", "asset", manifest: Manifest);
        await PackageAssetTests.Reconcile(host);
        var pending = Catalog(host);
        try
        {
            await logs.PreflightEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var outcome = await host.App.Services.GetRequiredService<INuplaneAdminOperations>().TriggerReconcileAsync(CancellationToken.None);
            Assert.Equal(ManualReconcileOutcomeCode.Unavailable, outcome.OutcomeCode);
        }
        finally { logs.ReleasePreflight(); }
        Assert.Contains((await pending)["features"]!.AsArray(), item => (string?)item!["id"] == "PackagedFeature");
        await PackageAssetTests.Reconcile(host);
    }

    [Fact]
    public async Task Linked_manifest_is_not_parsed_or_exposed()
    {
        await using var host = await AdministrationHost.StartAsync(nativeStore: true);
        PackageAssetTests.Pack(host, "1.0.0", "asset");
        await PackageAssetTests.Reconcile(host);
        var package = Assert.Single((await host.App.Services.GetRequiredService<INuplaneAdminOperations>().GetPackagesAsync(CancellationToken.None)).Packages);
        var outside = Path.Combine(host.Root, "outside-manifest.json");
        await File.WriteAllTextAsync(outside, Manifest);
        File.CreateSymbolicLink(Path.Combine(package.InstallPath, "elsa-package.json"), outside);
        var catalog = await Catalog(host);
        Assert.Contains(catalog["features"]!.AsArray(), item => (string?)item!["sourceKind"] == "manifest-error");
        Assert.DoesNotContain(catalog["features"]!.AsArray(), item => (string?)item!["id"] == "PackagedFeature");
        Assert.DoesNotContain("private-manifest-default-sentinel", catalog.ToJsonString());
        Assert.Equal(Manifest, await File.ReadAllTextAsync(outside));
    }

    private static async Task<JsonObject> Catalog(AdministrationHost host)
    {
        using var response = await host.Client.GetAsync("/modularity/features");
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
    }
}
