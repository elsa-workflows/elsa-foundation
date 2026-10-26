using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nuplane.Admin;
using CShells.Lifecycle;
using Nuplane.Loading;
using Nuplane.Loading.Hosting.Builder;

namespace Elsa.Studio.Tests.Administration;

public sealed class PackageMaintenanceTests
{
    private const string PruneUrl = "/_elsa/module-management/prune";
    private const string RetentionUrl = "/_elsa/module-management/retention-policy";

    [Fact]
    public async Task Confirmed_execution_recovers_stale_content_uses_before_replanning_deletion()
    {
        await using var host = await AdministrationHost.StartAsync(nativeStore: true);
        var (old, active) = await Versions(host);
        await SavePolicy(host, 0);
        var preview = await Prune(host, new JsonObject());
        Assert.Equal("Preview", (string?)preview["status"]);
        Assert.Equal("Unknown", (string?)preview["protectionKnowledge"]);
        Assert.True((bool)preview["inventoryComplete"]!);
        Assert.Contains(preview["diagnostics"]!.AsArray(), diagnostic => (string?)diagnostic!["code"] == "StaleGraphUseRequiresRecovery");
        Assert.All(preview["candidates"]!.AsArray(), candidate => Assert.Equal("NotAttempted", (string?)candidate!["attempt"]));
        Assert.True(Directory.Exists(old));
        var execute = await Prune(host, Execute(preview));
        Assert.True((string?)execute["status"] == "Completed", execute.ToJsonString());
        Assert.True((string?)execute["protectionKnowledge"] == "Known", execute.ToJsonString());
        Assert.Contains(execute["candidates"]!.AsArray(), candidate => (string?)candidate!["physicalOutcome"] == "Deleted");
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(active));
        using var asset = await host.Client.GetAsync($"/_content/{PackageAssetTests.PackageId}/probe.txt");
        asset.EnsureSuccessStatusCode();
        Assert.Equal("version two", await asset.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Public_addon_deletes_obsolete_content_and_preserves_an_actual_retained_shell_generation()
    {
        string root;
        string obsolete;
        await using (var installer = await AdministrationHost.StartAsync(nativeStore: true, keepFiles: true))
        {
            root = installer.Root;
            PackageAssetTests.Pack(installer, "0.5.0", "obsolete");
            await PackageAssetTests.Reconcile(installer);
            obsolete = Assert.Single((await installer.App.Services.GetRequiredService<INuplaneAdminOperations>().GetPackagesAsync(CancellationToken.None)).Packages).InstallPath;
        }
        File.Delete(Path.Combine(root, "feed", PackageAssetTests.PackageId + ".0.5.0.nupkg"));
        await using var host = await AdministrationHost.StartAsync(root: root, nativeStore: true, loadPackages: true,
            configureNuplane: nuplane => nuplane.AutoloadPackages(loading => loading.WithDefaultLoadMode(PackageLoadMode.Collectible)
                .WithDeactivationTimeout(TimeSpan.FromMilliseconds(100))),
            beforeStudio: builder => builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CShells:Shells:Authoring:Features:StudioReuseProbe"] = "true"
            }));
        PackageAssetTests.Pack(host, "1.0.0", "first", assembly: "First");
        await PackageAssetTests.Reconcile(host);
        var registry = host.App.Services.GetRequiredService<IShellRegistry>();
        var first = await registry.GetOrActivateAsync("Authoring");
        await using var retained = first.BeginScope();
        Assert.Equal("first", retained.ServiceProvider.GetRequiredService<string>());
        var keepAllPreview = await Prune(host, new JsonObject());
        Assert.True((string?)keepAllPreview["protectionKnowledge"] == "Known", keepAllPreview.ToJsonString());
        Assert.Null(keepAllPreview["retention"]);
        var keepAll = await Prune(host, Execute(keepAllPreview));
        Assert.Equal("Completed", (string?)keepAll["status"]);
        Assert.DoesNotContain(keepAll["candidates"]!.AsArray(), candidate => (string?)candidate!["physicalOutcome"] == "Deleted");
        Assert.True(Directory.Exists(obsolete));
        await SavePolicy(host, 0);
        var preview = await Prune(host, new JsonObject());
        Assert.True((string?)preview["protectionKnowledge"] == "Known", preview.ToJsonString());
        var firstPrune = await Prune(host, Execute(preview));
        Assert.Equal("Completed", (string?)firstPrune["status"]);
        Assert.Contains(firstPrune["candidates"]!.AsArray(), candidate => (string?)candidate!["physicalOutcome"] == "Deleted");
        Assert.False(Directory.Exists(obsolete));

        var admin = host.App.Services.GetRequiredService<INuplaneAdminOperations>();
        var firstPath = Assert.Single((await admin.GetPackagesAsync(CancellationToken.None)).Packages).InstallPath;
        File.Delete(Path.Combine(root, "feed", PackageAssetTests.PackageId + ".1.0.0.nupkg"));
        PackageAssetTests.Pack(host, "2.0.0", "second", assembly: "Second");
        await PackageAssetTests.Reconcile(host);
        await using (var current = registry.GetActive("Authoring")!.BeginScope())
            Assert.Equal("second", current.ServiceProvider.GetRequiredService<string>());
        var replacedPreview = await Prune(host, new JsonObject());
        var replaced = await Prune(host, Execute(replacedPreview));
        Assert.Equal("Completed", (string?)replaced["status"]);
        Assert.Contains(replaced["candidates"]!.AsArray(), candidate => (string?)candidate!["install"]!["version"] == "1.0.0"
            && (string?)candidate["physicalOutcome"] == "Retained" && candidate["reasons"]!.AsArray().Any(reason => (string?)reason == "ProtectedLiveUse"));
        Assert.True(Directory.Exists(firstPath));
        Assert.Equal("first", retained.ServiceProvider.GetRequiredService<string>());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("epoch")]
    [InlineData("label")]
    [InlineData("policy")]
    public async Task Missing_or_stale_confirmation_is_returned_as_refusal_without_deletion(string kind)
    {
        await using var host = await AdministrationHost.StartAsync(nativeStore: true);
        var (old, active) = await Versions(host);
        await SavePolicy(host, 0);
        var preview = await Prune(host, new JsonObject());
        var request = Execute(preview);
        var confirmation = request["confirmation"]!.AsObject();
        if (kind == "missing") request.Remove("confirmation");
        if (kind == "epoch") confirmation["enrollmentEpoch"] = (long)confirmation["enrollmentEpoch"]! + 1;
        if (kind == "label") confirmation["rootLabel"] = "other-root";
        if (kind == "policy") await SavePolicy(host, 1);
        if (kind is "missing" or "label" or "policy")
        {
            using var invalid = await host.Client.PostAsJsonAsync(PruneUrl, request);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Contains("Execute requires explicit confirmation", await invalid.Content.ReadAsStringAsync());
        }
        else
        {
            var result = await Prune(host, request);
            Assert.Equal("Refused", (string?)result["status"]);
            Assert.NotEmpty(result["diagnostics"]!.AsArray());
            Assert.All(result["candidates"]!.AsArray(), candidate => Assert.Equal("NotAttempted", (string?)candidate!["attempt"]));
        }
        Assert.True(Directory.Exists(old));
        Assert.True(Directory.Exists(active));
    }

    [Fact]
    public async Task A_confirmation_from_another_physical_root_cannot_authorize_deletion()
    {
        await using var host = await AdministrationHost.StartAsync(nativeStore: true);
        await using var other = await AdministrationHost.StartAsync(nativeStore: true);
        var (old, _) = await Versions(host);
        await SavePolicy(host, 0);
        var preview = await Prune(host, new JsonObject());
        var otherPreview = await Prune(other, new JsonObject());
        var request = Execute(preview);
        request["confirmation"]!["rootIdentity"] = otherPreview["rootIdentity"]!.DeepClone();
        var result = await Prune(host, request);
        Assert.Equal("Refused", (string?)result["status"]);
        Assert.True(Directory.Exists(old));
    }

    [Fact]
    public async Task Null_retention_preserves_every_install_after_confirmed_use_recovery()
    {
        await using var host = await AdministrationHost.StartAsync(nativeStore: true);
        var (old, active) = await Versions(host);
        var preview = await Prune(host, new JsonObject());
        Assert.Null(preview["retention"]);
        Assert.Equal("Unknown", (string?)preview["protectionKnowledge"]);
        var result = await Prune(host, Execute(preview));
        Assert.True((string?)result["status"] == "Completed", result.ToJsonString());
        Assert.True((string?)result["protectionKnowledge"] == "Known", result.ToJsonString());
        Assert.DoesNotContain(result["candidates"]!.AsArray(), candidate => (string?)candidate!["attempt"] == "Completed");
        Assert.DoesNotContain(result["candidates"]!.AsArray(), candidate => (string?)candidate!["physicalOutcome"] == "Deleted");
        Assert.True(Directory.Exists(old));
        Assert.True(Directory.Exists(active));
        Assert.Equal("version one", await File.ReadAllTextAsync(Path.Combine(old, "staticwebassets", "probe.txt")));
        Assert.Equal("version two", await File.ReadAllTextAsync(Path.Combine(active, "staticwebassets", "probe.txt")));
    }

    [Fact]
    public async Task Ordinary_store_preview_reports_unknown_protection_and_does_not_enroll()
    {
        await using var host = await AdministrationHost.StartAsync();
        var result = await Prune(host, new JsonObject());
        Assert.Equal("Unknown", (string?)result["protectionKnowledge"]);
        Assert.False((bool)result["inventoryComplete"]!);
        Assert.Contains(result["diagnostics"]!.AsArray(), diagnostic => (string?)diagnostic!["code"] == "EnrollmentRequired");
        Assert.Empty(Directory.EnumerateFiles(host.Root, "*.ledger*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Legacy_policy_requires_deliberate_selection_and_is_not_silently_translated()
    {
        await using var host = await AdministrationHost.StartAsync(nativeStore: true, beforeStudio: builder => builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Nuplane:CleanupPolicy:RetainLastNVersions"] = "3", ["Nuplane:CleanupPolicy:RetainYoungerThanDays"] = "30" }));
        using var refused = await host.Client.PostAsJsonAsync(PruneUrl, new { });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("legacy-retention-unsupported", await refused.Content.ReadAsStringAsync());
        await SavePolicy(host, null);
        var preview = await Prune(host, new JsonObject());
        Assert.Equal("Preview", (string?)preview["status"]);
        Assert.Null(preview["retention"]);
        Assert.Equal("3", host.App.Configuration["Nuplane:CleanupPolicy:RetainLastNVersions"]);
        var document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(host.Root, "nuplane-management.json")))!;
        Assert.True(document["Studio"]!["PackageManagement"]!["RetentionPolicy"]!.AsObject().ContainsKey("KeepNewestInactiveVersionsPerPackage"));
    }

    [Fact]
    public async Task Negative_retention_does_not_create_the_management_file()
    {
        await using var host = await AdministrationHost.StartAsync();
        using var result = await host.Client.PutAsJsonAsync(RetentionUrl, new { keepNewestInactiveVersionsPerPackage = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.False(File.Exists(Path.Combine(host.Root, "nuplane-management.json")));
    }

    [Fact]
    public async Task An_overlaid_policy_source_is_read_only_even_when_its_value_matches_the_file()
    {
        await using var host = await AdministrationHost.StartAsync(afterStudio: builder => builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Studio:PackageManagement:RetentionPolicy:KeepNewestInactiveVersionsPerPackage"] = "0" }));
        var file = Path.Combine(host.Root, "nuplane-management.json");
        await File.WriteAllTextAsync(file, """{"preserve":"unrelated","Studio":{"PackageManagement":{"RetentionPolicy":{"KeepNewestInactiveVersionsPerPackage":0}}}}""");
        ((IConfigurationRoot)host.App.Configuration).Reload();
        var before = await File.ReadAllBytesAsync(file);
        using var result = await host.Client.PutAsJsonAsync(RetentionUrl, new { keepNewestInactiveVersionsPerPackage = 1 });
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal(before, await File.ReadAllBytesAsync(file));
    }

    private static async Task SavePolicy(AdministrationHost host, int? count)
    {
        using var result = await host.Client.PutAsJsonAsync(RetentionUrl, new { keepNewestInactiveVersionsPerPackage = count });
        result.EnsureSuccessStatusCode();
    }

    private static async Task<(string Old, string Active)> Versions(AdministrationHost host)
    {
        PackageAssetTests.Pack(host, "1.0.0", "version one");
        await PackageAssetTests.Reconcile(host);
        var admin = host.App.Services.GetRequiredService<INuplaneAdminOperations>();
        var old = Assert.Single((await admin.GetPackagesAsync(CancellationToken.None)).Packages).InstallPath;
        PackageAssetTests.Pack(host, "2.0.0", "version two");
        await PackageAssetTests.Reconcile(host);
        var active = Assert.Single((await admin.GetPackagesAsync(CancellationToken.None)).Packages).InstallPath;
        Assert.NotEqual(old, active);
        return (old, active);
    }

    private static async Task<JsonObject> Prune(AdministrationHost host, JsonObject request)
    {
        using var response = await host.Client.PostAsJsonAsync(PruneUrl, request);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
    }

    private static JsonObject Execute(JsonObject preview) => new()
    {
        ["dryRun"] = false,
        ["rootLabel"] = preview["rootLabel"]!.DeepClone(),
        ["confirmation"] = new JsonObject
        {
            ["rootLabel"] = preview["rootLabel"]!.DeepClone(),
            ["rootIdentity"] = preview["rootIdentity"]!.DeepClone(),
            ["enrollmentEpoch"] = preview["enrollmentEpoch"]!.DeepClone(),
            ["retention"] = preview["retention"]?.DeepClone()
        }
    };
}
